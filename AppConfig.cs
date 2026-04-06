using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace HdrProfileSwitcher;

/// <summary>
/// Configuration complète de l'application, stockée dans config.json à côté de l'exécutable.
/// </summary>
public class AppConfig
{
    [JsonPropertyName("écrans")]
    public List<ConfigurationÉcran> Écrans { get; set; } = new();

    [JsonPropertyName("intervallePollingMs")]
    public int IntervallePollingMs { get; set; } = 5000;

    [JsonPropertyName("fichierLog")]
    public string FichierLog { get; set; } = "hdr-switcher.log";

    [JsonPropertyName("démarrageAuto")]
    public bool DémarrageAuto { get; set; } = true;

    [JsonIgnore]
    public string CheminFichier => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppConfig Charger(Logger logger)
    {
        var chemin = Path.Combine(AppContext.BaseDirectory, "config.json");

        try
        {
            if (!File.Exists(chemin))
            {
                var configParDéfaut = CréerConfigurationParDéfaut();
                configParDéfaut.Sauvegarder(logger);
                logger.Info("config.json absent : configuration par défaut créée.");
                return configParDéfaut;
            }

            var json = File.ReadAllText(chemin);
            var options = CréerOptionsJson();
            var config = JsonSerializer.Deserialize<AppConfig>(json, options);

            if (config == null)
            {
                logger.Avertissement("config.json vide ou invalide : configuration par défaut recréée.");
                config = CréerConfigurationParDéfaut();
                config.Sauvegarder(logger);
            }

            config.Normaliser();
            config.AppliquerDémarrageAuto(logger);
            return config;
        }
        catch (Exception ex)
        {
            logger.Erreur("Erreur lors du chargement de config.json, utilisation de la configuration par défaut.", ex);
            var config = CréerConfigurationParDéfaut();
            config.Sauvegarder(logger);
            return config;
        }
    }

    public void Sauvegarder(Logger logger)
    {
        try
        {
            Normaliser();
            var json = JsonSerializer.Serialize(this, CréerOptionsJson());
            File.WriteAllText(CheminFichier, json);
            logger.Info($"Configuration sauvegardée : {CheminFichier}");
            AppliquerDémarrageAuto(logger);
        }
        catch (Exception ex)
        {
            logger.Erreur("Impossible de sauvegarder la configuration.", ex);
            throw;
        }
    }

    public ConfigurationÉcran? TrouverCorrespondance(DisplayMonitor moniteur)
    {
        return Écrans.FirstOrDefault(écran =>
            !string.IsNullOrWhiteSpace(écran.MotifRecherche) &&
            moniteur.NomConvivial.Contains(écran.MotifRecherche, StringComparison.OrdinalIgnoreCase));
    }

    public void AppliquerDémarrageAuto(Logger logger)
    {
        try
        {
            using var clé = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);

            if (clé == null)
            {
                logger.Avertissement("Clé de registre Run introuvable : impossible de gérer le démarrage automatique.");
                return;
            }

            const string nomValeur = "HdrProfileSwitcher";
            var cheminExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "HdrProfileSwitcher.exe");

            if (DémarrageAuto)
            {
                clé.SetValue(nomValeur, $"\"{cheminExe}\"");
                logger.Info("Démarrage automatique activé.");
            }
            else if (clé.GetValue(nomValeur) != null)
            {
                clé.DeleteValue(nomValeur, false);
                logger.Info("Démarrage automatique désactivé.");
            }
        }
        catch (Exception ex)
        {
            logger.Erreur("Erreur lors de la mise à jour du démarrage automatique.", ex);
        }
    }

    private void Normaliser()
    {
        IntervallePollingMs = Math.Max(1000, IntervallePollingMs);
        FichierLog = string.IsNullOrWhiteSpace(FichierLog) ? "hdr-switcher.log" : FichierLog.Trim();
        Écrans ??= new List<ConfigurationÉcran>();

        foreach (var écran in Écrans)
        {
            écran.Nom = écran.Nom?.Trim() ?? string.Empty;
            écran.Lettre = string.IsNullOrWhiteSpace(écran.Lettre) ? "?" : écran.Lettre.Trim()[0].ToString().ToUpperInvariant();
            écran.MotifRecherche = écran.MotifRecherche?.Trim() ?? string.Empty;
            écran.ProfilSdr = écran.ProfilSdr?.Trim();
            écran.ProfilHdr = écran.ProfilHdr?.Trim();
        }
    }

    private static AppConfig CréerConfigurationParDéfaut() => new()
    {
        Écrans = new List<ConfigurationÉcran>(),
        IntervallePollingMs = 5000,
        FichierLog = "hdr-switcher.log",
        DémarrageAuto = true
    };

    private static JsonSerializerOptions CréerOptionsJson() => new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null
    };
}

/// <summary>
/// Configuration d'un écran individuel.
/// </summary>
public class ConfigurationÉcran
{
    [JsonPropertyName("nom")]
    public string Nom { get; set; } = string.Empty;

    [JsonPropertyName("lettre")]
    public string Lettre { get; set; } = "?";

    [JsonPropertyName("motifRecherche")]
    public string MotifRecherche { get; set; } = string.Empty;

    [JsonPropertyName("profilSdr")]
    public string? ProfilSdr { get; set; }

    [JsonPropertyName("profilHdr")]
    public string? ProfilHdr { get; set; }
}
