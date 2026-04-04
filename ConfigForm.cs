using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HdrProfileSwitcher;

/// <summary>
/// Fenêtre de configuration WinForms.
/// </summary>
public sealed class ConfigForm : Form
{
    private readonly AppConfig _config;
    private readonly DisplayService _displayService;
    private readonly Logger _logger;
    private readonly List<DisplayMonitor> _écransDétectés;
    private readonly List<string> _profilsDisponibles;
    private readonly TableLayoutPanel _table;
    private readonly CheckBox _caseDémarrageAuto;
    private readonly Button _boutonEnregistrer;
    private readonly Button _boutonAnnuler;
    private readonly List<LigneConfigurationÉcran> _lignes = new();

    public ConfigForm(AppConfig config, DisplayService displayService, Logger logger, List<DisplayMonitor> écransDétectés)
    {
        _config = config;
        _displayService = displayService;
        _logger = logger;
        _écransDétectés = écransDétectés;
        _profilsDisponibles = _displayService.ListerProfilsInstallés();

        Text = "Configuration — HDR Profile Switcher";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 960;
        Height = 540;
        MinimumSize = new Size(860, 420);
        Font = new Font("Segoe UI", 9f);

        var conteneur = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        conteneur.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        conteneur.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        conteneur.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(conteneur);

        var labelIntro = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Associez chaque écran détecté à un profil SDR et/ou HDR. Les profils proviennent de C:\\Windows\\System32\\spool\\drivers\\color\\.",
            Margin = new Padding(3, 3, 3, 12)
        };
        conteneur.Controls.Add(labelIntro, 0, 0);

        _table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 5,
            RowCount = 1,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23f));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23f));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8f));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12f));

        AjouterEntêtes();
        ConstruireLignes();
        conteneur.Controls.Add(_table, 0, 1);

        var panneauBas = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 12, 0, 0)
        };

        _boutonEnregistrer = new Button { Text = "Enregistrer", AutoSize = true, MinimumSize = new Size(110, 32) };
        _boutonAnnuler = new Button { Text = "Annuler", AutoSize = true, MinimumSize = new Size(110, 32) };
        _caseDémarrageAuto = new CheckBox { Text = "Démarrer avec Windows", AutoSize = true, Checked = _config.DémarrageAuto, Margin = new Padding(0, 7, 24, 0) };

        _boutonEnregistrer.Click += (_, _) => Enregistrer();
        _boutonAnnuler.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        panneauBas.Controls.Add(_boutonEnregistrer);
        panneauBas.Controls.Add(_boutonAnnuler);
        panneauBas.Controls.Add(_caseDémarrageAuto);

        conteneur.Controls.Add(panneauBas, 0, 2);
    }

    private void AjouterEntêtes()
    {
        AjouterCelluleEntête("Écran détecté", 0);
        AjouterCelluleEntête("Profil SDR", 1);
        AjouterCelluleEntête("Profil HDR", 2);
        AjouterCelluleEntête("Lettre", 3);
        AjouterCelluleEntête("Motif de recherche", 4);
    }

    private void AjouterCelluleEntête(string texte, int colonne)
    {
        var label = new Label
        {
            Text = texte,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(6)
        };
        _table.Controls.Add(label, colonne, 0);
    }

    private void ConstruireLignes()
    {
        var sources = _écransDétectés.Count > 0
            ? _écransDétectés
            : _config.Écrans.Select(cfg => new DisplayMonitor { NomConvivial = cfg.Nom, ConfigAssociée = cfg }).ToList();

        int ligneIndex = 1;
        foreach (var écran in sources)
        {
            _table.RowCount++;
            _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var config = écran.ConfigAssociée ?? _config.TrouverCorrespondance(écran) ?? new ConfigurationÉcran
            {
                Nom = écran.NomConvivial,
                Lettre = écran.NomConvivial.Length > 0 ? écran.NomConvivial[0].ToString().ToUpperInvariant() : "?",
                MotifRecherche = écran.NomConvivial
            };

            var labelÉcran = new Label
            {
                Text = écran.NomConvivial,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(6)
            };

            var comboSdr = CréerComboProfils(config.ProfilSdr);
            var comboHdr = CréerComboProfils(config.ProfilHdr);
            var texteLettre = new TextBox { Text = config.Lettre, MaxLength = 1, Dock = DockStyle.Fill, Margin = new Padding(6) };
            var texteMotif = new TextBox { Text = config.MotifRecherche, Dock = DockStyle.Fill, Margin = new Padding(6) };

            _table.Controls.Add(labelÉcran, 0, ligneIndex);
            _table.Controls.Add(comboSdr, 1, ligneIndex);
            _table.Controls.Add(comboHdr, 2, ligneIndex);
            _table.Controls.Add(texteLettre, 3, ligneIndex);
            _table.Controls.Add(texteMotif, 4, ligneIndex);

            _lignes.Add(new LigneConfigurationÉcran
            {
                NomÉcran = écran.NomConvivial,
                ComboSdr = comboSdr,
                ComboHdr = comboHdr,
                TexteLettre = texteLettre,
                TexteMotif = texteMotif
            });

            ligneIndex++;
        }
    }

    private ComboBox CréerComboProfils(string? valeur)
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(6)
        };

        combo.Items.Add("Aucun");
        foreach (var profil in _profilsDisponibles)
        {
            combo.Items.Add(profil);
        }

        var cible = string.IsNullOrWhiteSpace(valeur) ? "Aucun" : valeur;
        combo.SelectedItem = combo.Items.Cast<object>().FirstOrDefault(x => string.Equals(x?.ToString(), cible, StringComparison.OrdinalIgnoreCase)) ?? "Aucun";
        return combo;
    }

    private void Enregistrer()
    {
        try
        {
            var nouvellesLignes = new List<ConfigurationÉcran>();

            foreach (var ligne in _lignes)
            {
                var motif = ligne.TexteMotif.Text.Trim();
                if (string.IsNullOrWhiteSpace(motif))
                {
                    MessageBox.Show(
                        $"Le motif de recherche est obligatoire pour l'écran '{ligne.NomÉcran}'.",
                        "Validation",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var lettre = string.IsNullOrWhiteSpace(ligne.TexteLettre.Text) ? "?" : ligne.TexteLettre.Text.Trim()[0].ToString().ToUpperInvariant();
                var profilSdr = ConvertirSélectionProfil(ligne.ComboSdr.SelectedItem?.ToString());
                var profilHdr = ConvertirSélectionProfil(ligne.ComboHdr.SelectedItem?.ToString());

                nouvellesLignes.Add(new ConfigurationÉcran
                {
                    Nom = ligne.NomÉcran,
                    Lettre = lettre,
                    MotifRecherche = motif,
                    ProfilSdr = profilSdr,
                    ProfilHdr = profilHdr
                });
            }

            _config.Écrans = nouvellesLignes;
            _config.DémarrageAuto = _caseDémarrageAuto.Checked;
            _config.Sauvegarder(_logger);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _logger.Erreur("Erreur lors de l'enregistrement de la configuration depuis le formulaire.", ex);
            MessageBox.Show(
                $"Impossible d'enregistrer la configuration :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string? ConvertirSélectionProfil(string? valeur)
    {
        return string.Equals(valeur, "Aucun", StringComparison.OrdinalIgnoreCase) ? null : valeur;
    }

    private sealed class LigneConfigurationÉcran
    {
        public string NomÉcran { get; set; } = string.Empty;
        public ComboBox ComboSdr { get; set; } = null!;
        public ComboBox ComboHdr { get; set; } = null!;
        public TextBox TexteLettre { get; set; } = null!;
        public TextBox TexteMotif { get; set; } = null!;
    }
}
