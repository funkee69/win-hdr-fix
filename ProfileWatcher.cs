using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Windows.Forms;

namespace HdrProfileSwitcher;

/// <summary>
/// Surveillance en arrière-plan des changements d'écrans, du mode HDR et des dérives de profil.
/// Combine WMI + polling de sécurité.
/// </summary>
public sealed class ProfileWatcher : IDisposable
{
    private readonly DisplayService _displayService;
    private readonly AppConfig _config;
    private readonly Logger _logger;
    private readonly System.Windows.Forms.Timer _timerPolling;
    private ManagementEventWatcher? _watcherCréation;
    private ManagementEventWatcher? _watcherSuppression;
    private List<DisplayMonitor> _dernierInstantané = new();
    private bool _dispose;

    public event Action<List<DisplayMonitor>>? OnDisplayChanged;
    public event Action<DisplayMonitor>? OnHdrStateChanged;
    public event Action<DisplayMonitor>? OnProfileDrift;

    public ProfileWatcher(DisplayService displayService, AppConfig config, Logger logger)
    {
        _displayService = displayService;
        _config = config;
        _logger = logger;
        _timerPolling = new System.Windows.Forms.Timer { Interval = Math.Max(1000, _config.IntervallePollingMs) };
        _timerPolling.Tick += (_, _) => ScannerEtNotifier();
    }

    public void Démarrer()
    {
        _logger.Info("Démarrage de la surveillance des écrans.");
        InitialiserWatchersWmi();
        _dernierInstantané = ConstruireInstantané();
        _timerPolling.Start();
    }

    public void ForcerAnalyse()
    {
        ScannerEtNotifier();
    }

    private void InitialiserWatchersWmi()
    {
        try
        {
            const string requêteCréation = "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity' AND TargetInstance.PNPClass = 'Monitor'";
            const string requêteSuppression = "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity' AND TargetInstance.PNPClass = 'Monitor'";

            _watcherCréation = new ManagementEventWatcher(new WqlEventQuery(requêteCréation));
            _watcherSuppression = new ManagementEventWatcher(new WqlEventQuery(requêteSuppression));

            _watcherCréation.EventArrived += (_, _) =>
            {
                _logger.Info("Événement WMI : moniteur connecté.");
                ScannerEtNotifier();
            };

            _watcherSuppression.EventArrived += (_, _) =>
            {
                _logger.Info("Événement WMI : moniteur déconnecté.");
                ScannerEtNotifier();
            };

            _watcherCréation.Start();
            _watcherSuppression.Start();
        }
        catch (Exception ex)
        {
            _logger.Erreur("Impossible d'initialiser les watchers WMI. Le polling reste actif en secours.", ex);
        }
    }

    private void ScannerEtNotifier()
    {
        if (_dispose)
            return;

        try
        {
            var instantanéActuel = ConstruireInstantané();

            if (LesÉcransOntChangé(_dernierInstantané, instantanéActuel))
            {
                _logger.Info("Changement de topologie d'affichage détecté.");
                OnDisplayChanged?.Invoke(instantanéActuel);
            }

            foreach (var écranActuel in instantanéActuel)
            {
                var ancien = _dernierInstantané.FirstOrDefault(x => x.MêmeÉcranQue(écranActuel));
                if (ancien != null && ancien.EstHdrActif != écranActuel.EstHdrActif)
                {
                    _logger.Info($"Changement HDR/SDR détecté : {écranActuel.NomConvivial} -> {(écranActuel.EstHdrActif ? "HDR" : "SDR")}");
                    OnHdrStateChanged?.Invoke(écranActuel);
                }

                if (!string.IsNullOrWhiteSpace(écranActuel.ProfilAttendu) && !écranActuel.ProfilCorrect)
                {
                    _logger.Avertissement($"Dérive de profil détectée pour '{écranActuel.NomConvivial}' : attendu='{écranActuel.ProfilAttendu}', actuel='{écranActuel.ProfilActuelAppliqué ?? "aucun"}'");
                    OnProfileDrift?.Invoke(écranActuel);
                }
            }

            _dernierInstantané = instantanéActuel;
        }
        catch (Exception ex)
        {
            _logger.Erreur("Erreur pendant le polling de surveillance.", ex);
        }
    }

    private List<DisplayMonitor> ConstruireInstantané()
    {
        var écrans = _displayService.ObtenirÉcransActifs();
        _displayService.AssocierConfigurations(écrans, _config);

        foreach (var écran in écrans)
        {
            écran.ProfilActuelAppliqué = écran.ProfilActuelAppliqué ?? DéterminerProfilCourantApproximatif(écran);

            // Espionner les profils actifs Windows pour détecter les changements
            _displayService.EspionnerProfilsActifs(écran);
        }

        return écrans;
    }

    private static bool LesÉcransOntChangé(List<DisplayMonitor> ancien, List<DisplayMonitor> actuel)
    {
        if (ancien.Count != actuel.Count)
            return true;

        foreach (var écran in actuel)
        {
            if (!ancien.Any(x => x.MêmeÉcranQue(écran)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Approximation du profil courant : on ne dispose pas ici d'une API simple et fiable pour relire
    /// le profil exact par écran sans chemin device complet. On utilise donc le profil attendu comme base.
    /// Cela permet au watchdog de réappliquer si l'application interne sait qu'un profil a été changé.
    /// </summary>
    private static string? DéterminerProfilCourantApproximatif(DisplayMonitor écran)
    {
        return écran.ProfilAttendu;
    }

    public void Dispose()
    {
        if (_dispose)
            return;

        _dispose = true;

        try
        {
            _timerPolling.Stop();
            _timerPolling.Dispose();
            _watcherCréation?.Stop();
            _watcherSuppression?.Stop();
            _watcherCréation?.Dispose();
            _watcherSuppression?.Dispose();
        }
        catch
        {
            // Ignore à la fermeture.
        }
    }
}
