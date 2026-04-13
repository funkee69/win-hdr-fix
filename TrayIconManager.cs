using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace HdrProfileSwitcher;

/// <summary>
/// Gère l'icône de tray, le menu contextuel et l'orchestration globale.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly AppConfig _config;
    private readonly Logger _logger;
    private readonly DisplayService _displayService;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly List<ToolStripItem> _itemsÉtat = new();
    private readonly ToolStripMenuItem _itemConfiguration;
    private readonly ToolStripMenuItem _itemLog;
    private readonly ToolStripMenuItem _itemÀPropos;
    private readonly ToolStripMenuItem _itemQuitter;
    private ProfileWatcher? _watcher;
    private List<DisplayMonitor> _écransActifs = new();
    private DisplayMonitor? _écranPrincipal;
    private bool _dispose;

    public TrayIconManager(AppConfig config, Logger logger)
    {
        _config = config;
        _logger = logger;
        _displayService = new DisplayService(logger);

        _menu = new ContextMenuStrip();
        _itemConfiguration = new ToolStripMenuItem(Strings.TrayConfiguration);
        _itemLog = new ToolStripMenuItem(Strings.TrayOpenLog);
        _itemÀPropos = new ToolStripMenuItem(Strings.TrayAbout);
        _itemQuitter = new ToolStripMenuItem(Strings.TrayQuit);

        _itemConfiguration.Click += (_, _) => OuvrirConfiguration();
        _itemLog.Click += (_, _) => OuvrirFichierLog();
        _itemÀPropos.Click += (_, _) => AfficherÀPropos();
        _itemQuitter.Click += (_, _) => Quitter();

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _itemConfiguration,
            _itemLog,
            new ToolStripSeparator(),
            _itemÀPropos,
            new ToolStripSeparator(),
            _itemQuitter
        });

        _notifyIcon = new NotifyIcon
        {
            Visible = false,
            Text = "HDR Profile Switcher",
            ContextMenuStrip = _menu
        };

        _notifyIcon.DoubleClick += (_, _) => OuvrirConfiguration();
    }

    public void Démarrer()
    {
        _logger.Info("Initialisation du tray icon.");
        RafraîchirÉtatComplet();

        _watcher = new ProfileWatcher(_displayService, _config, _logger);
        _watcher.OnDisplayChanged += écrans =>
        {
            _logger.Info("Traitement du changement d'écran.");
            _écransActifs = écrans;
            _displayService.AssocierConfigurations(_écransActifs, _config);
            AppliquerProfilsSurTousLesÉcrans();
            MajIconeTooltipSafe();
        };
        _watcher.OnHdrStateChanged += écran =>
        {
            _logger.Info($"Changement HDR détecté pour '{écran.NomConvivial}' : HDR={écran.EstHdrActif}");
            _displayService.AppliquerProfilAutomatique(écran);
            // Important : recharger l'état complet, sinon le tray garde un ancien snapshot SDR.
            RafraîchirÉtatComplet();
        };
        _watcher.OnProfileDrift += écran =>
        {
            _displayService.AppliquerProfilAutomatique(écran);
            // Re-synchroniser l'état interne après réapplication.
            RafraîchirÉtatComplet();
        };
        _watcher.Démarrer();

        _notifyIcon.Visible = true;
        _logger.Info("HDR Profile Switcher prêt.");
    }

    private void RafraîchirÉtatComplet()
    {
        _écransActifs = _displayService.ObtenirÉcransActifs();
        _displayService.AssocierConfigurations(_écransActifs, _config);
        AppliquerProfilsSurTousLesÉcrans();
        _écranPrincipal = DéterminerÉcranPrincipal(_écransActifs);
        MajIconeTooltip();
    }

    private void AppliquerProfilsSurTousLesÉcrans()
    {
        foreach (var écran in _écransActifs)
        {
            _displayService.AppliquerProfilAutomatique(écran);
        }
    }

    /// <summary>
    /// Version thread-safe pour appel depuis les callbacks du watcher.
    /// </summary>
    private void MajIconeTooltipSafe()
    {
        if (_notifyIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _notifyIcon.ContextMenuStrip.BeginInvoke(new Action(MajIconeTooltip));
        }
        else
        {
            MajIconeTooltip();
        }
    }

    private void MajIconeTooltip()
    {
        _écranPrincipal = DéterminerÉcranPrincipal(_écransActifs);
        var lettre = _écranPrincipal?.Lettre ?? "?";
        var hdr = _écranPrincipal?.EstHdrActif ?? false;

        _logger.Debug($"Mise à jour tray : {_écranPrincipal?.NomConvivial ?? "?"} / {(hdr ? "HDR" : "SDR")}");

        // Icône = écran principal
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Icon = GénérerIcône(lettre, hdr);

        // Tooltip = une ligne par écran connecté
        var lignes = _écransActifs
            .OrderByDescending(e => e.EstPrincipal)
            .Select(e => $"[{e.Lettre}] {e.NomConvivial} — {(e.EstHdrActif ? "HDR" : "SDR")} — {e.ProfilActuelAppliqué ?? Strings.TrayNoProfile}")
            .ToList();
        var tooltip = lignes.Count > 0
            ? string.Join("\n", lignes)
            : Strings.TrayNoDisplay;
        _notifyIcon.Text = LimiterTooltip(tooltip);

        // Menu = lignes d'état par écran
        MajMenuÉtat();
    }

    /// <summary>
    /// Reconstruit les lignes d'état (une par écran) en haut du menu contextuel.
    /// </summary>
    private void MajMenuÉtat()
    {
        // Retirer les anciennes lignes
        foreach (var item in _itemsÉtat)
        {
            _menu.Items.Remove(item);
            item.Dispose();
        }
        _itemsÉtat.Clear();

        // Insérer une ligne par écran connecté
        int idx = 0;
        foreach (var écran in _écransActifs.OrderByDescending(e => e.EstPrincipal))
        {
            var mode = écran.EstHdrActif ? "HDR" : "SDR";
            var item = new ToolStripMenuItem($"[{écran.Lettre}] {écran.NomConvivial} — {mode}") { Enabled = false };
            _menu.Items.Insert(idx, item);
            _itemsÉtat.Add(item);
            idx++;
        }

        // Séparateur après les lignes d'état
        if (_itemsÉtat.Count > 0)
        {
            var sep = new ToolStripSeparator();
            _menu.Items.Insert(idx, sep);
            _itemsÉtat.Add(sep);
        }
    }

    private static DisplayMonitor? DéterminerÉcranPrincipal(List<DisplayMonitor> écrans)
    {
        if (écrans.Count == 0)
            return null;

        // Préférer l'écran principal Windows, sinon fallback sur le premier configuré
        return écrans.FirstOrDefault(x => x.EstPrincipal)
            ?? écrans.OrderByDescending(x => x.EstConfiguré).ThenBy(x => x.NomConvivial).FirstOrDefault();
    }

    private void OuvrirConfiguration()
    {
        try
        {
            using var formulaire = new ConfigForm(_config, _displayService, _logger, _écransActifs);
            if (formulaire.ShowDialog() == DialogResult.OK)
            {
                _config.Sauvegarder(_logger);
                RafraîchirÉtatComplet();
                _watcher?.ForcerAnalyse();
            }
        }
        catch (Exception ex)
        {
            _logger.Erreur("Erreur lors de l'ouverture de la configuration.", ex);
            MessageBox.Show(
                Strings.ConfigOpenError(ex.Message),
                "HDR Profile Switcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OuvrirFichierLog()
    {
        try
        {
            var chemin = _logger.CheminLog;
            if (System.IO.File.Exists(chemin))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = chemin,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show(
                    Strings.LogNotFound(chemin),
                    "HDR Profile Switcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.Erreur("Erreur lors de l'ouverture du fichier log.", ex);
        }
    }

    private void AfficherÀPropos()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "4.5.0";
        var message = Strings.AboutMessage + $"Version : {version}";

        MessageBox.Show(message, Strings.AboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Quitter()
    {
        _logger.Info("Fermeture demandée par l'utilisateur.");
        Dispose();
        Application.Exit();
    }

    private static string LimiterTooltip(string texte)
    {
        // Windows Vista+ supporte 127 caractères pour NotifyIcon.Text
        return texte.Length <= 127 ? texte : texte[..124] + "...";
    }

    private static Icon GénérerIcône(string lettre, bool hdr)
    {
        // Taille 64x64 pour un rendu net sur les écrans haute résolution
        const int taille = 64;
        using var bitmap = new Bitmap(taille, taille);
        using var graphique = Graphics.FromImage(bitmap);
        graphique.SmoothingMode = SmoothingMode.HighQuality;
        graphique.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphique.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphique.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphique.Clear(Color.Transparent);

        // Fond : cercle avec dégradé subtil
        var couleurFond = hdr
            ? Color.FromArgb(230, 155, 40)   // Doré/orange HDR
            : Color.FromArgb(65, 95, 135);    // Bleu-gris SDR
        var couleurFondClair = hdr
            ? Color.FromArgb(255, 195, 80)
            : Color.FromArgb(100, 140, 180);

        using (var degrade = new LinearGradientBrush(
            new Rectangle(0, 0, taille, taille),
            couleurFondClair, couleurFond,
            LinearGradientMode.ForwardDiagonal))
        {
            graphique.FillEllipse(degrade, 1, 1, taille - 2, taille - 2);
        }

        // Bordure fine
        using (var stylo = new Pen(Color.FromArgb(40, 0, 0, 0), 1.5f))
        {
            graphique.DrawEllipse(stylo, 1, 1, taille - 3, taille - 3);
        }

        // Lettre centrée
        var texte = string.IsNullOrWhiteSpace(lettre) ? "?" : lettre[..1].ToUpperInvariant();
        using (var police = new Font("Segoe UI", 32f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var pinceauTexte = new SolidBrush(Color.White))
        using (var pinceauOmbre = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
        using (var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        })
        {
            // Ombre légère
            graphique.DrawString(texte, police, pinceauOmbre, new RectangleF(1, 2, taille, taille), format);
            // Texte principal
            graphique.DrawString(texte, police, pinceauTexte, new RectangleF(0, 0, taille, taille), format);
        }

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        if (_dispose)
            return;

        _dispose = true;
        _watcher?.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
