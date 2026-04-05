using System;
using System.Threading;
using System.Windows.Forms;

namespace HdrProfileSwitcher;

/// <summary>
/// Point d'entrée de l'application HDR Profile Switcher.
/// Gère l'instance unique et lance la boucle principale Windows Forms.
/// </summary>
internal static class Program
{
    // Mutex pour garantir une seule instance de l'application
    private static Mutex? _instanceUnique;
    private const string NomMutex = "HdrProfileSwitcher_InstanceUnique_2026";

    [STAThread]
    static void Main(string[] args)
    {
        // Vérification instance unique
        _instanceUnique = new Mutex(true, NomMutex, out bool premiereLancement);

        if (!premiereLancement)
        {
            // Une instance est déjà en cours d'exécution
            MessageBox.Show(
                Strings.AlreadyRunning,
                "HDR Profile Switcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        // Configuration Windows Forms pour une meilleure compatibilité Windows 11
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Gestionnaire d'exceptions non gérées pour éviter les crashs silencieux
        Application.ThreadException += GestionnaireExceptionThread;
        AppDomain.CurrentDomain.UnhandledException += GestionnaireExceptionDomaine;

        // Initialisation du logger avant tout
        var logger = new Logger();
        logger.ÉcrireBannerDémarrage();

        TrayIconManager? trayManager = null;

        try
        {
            // Chargement de la configuration
            var config = AppConfig.Charger(logger);
            logger.Info($"Configuration chargée : {config.Écrans.Count} écran(s) configuré(s)");

            // Démarrage du gestionnaire de tray (lance aussi le watcher en arrière-plan)
            trayManager = new TrayIconManager(config, logger);
            trayManager.Démarrer();

            // Boucle principale Windows Forms sans fenêtre principale visible.
            Application.Run();
        }
        catch (Exception ex)
        {
            logger.Erreur("Erreur fatale au démarrage", ex);
            MessageBox.Show(
                Strings.FatalStartup(ex.Message),
                Strings.FatalErrorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
        finally
        {
            trayManager?.Dispose();

            // Libérer le mutex à la fermeture
            _instanceUnique?.ReleaseMutex();
            _instanceUnique?.Dispose();
        }
    }

    /// <summary>
    /// Gestion des exceptions dans le thread UI Windows Forms.
    /// </summary>
    private static void GestionnaireExceptionThread(object sender, ThreadExceptionEventArgs e)
    {
        var logger = new Logger();
        logger.Erreur("Exception non gérée dans le thread UI", e.Exception);

        var résultat = MessageBox.Show(
            $"Une erreur inattendue s'est produite :\n\n{e.Exception.Message}\n\nContinuer l'application ?",
            Strings.ErrorTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error
        );

        if (résultat == DialogResult.No)
        {
            Application.Exit();
        }
    }

    /// <summary>
    /// Gestion des exceptions non gérées dans les threads non-UI.
    /// </summary>
    private static void GestionnaireExceptionDomaine(object sender, UnhandledExceptionEventArgs e)
    {
        var logger = new Logger();
        var exception = e.ExceptionObject as Exception;
        logger.Erreur("Exception non gérée dans le domaine applicatif", exception);

        if (e.IsTerminating)
        {
            MessageBox.Show(
                $"Erreur fatale, l'application va se fermer :\n\n{exception?.Message ?? "Erreur inconnue"}",
                Strings.FatalErrorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }
}
