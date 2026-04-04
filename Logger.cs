using System;
using System.IO;
using System.Text;

namespace HdrProfileSwitcher;

/// <summary>
/// Logger simple fichier avec rotation basique.
/// Tous les messages sont horodatés et écrits de manière thread-safe.
/// </summary>
public class Logger
{
    private static readonly object Verrou = new();
    private readonly string _cheminLog;
    private const long TailleMaxOctets = 2 * 1024 * 1024; // 2 Mo
    private const int NombreArchivesMax = 3;

    public Logger(string? cheminLog = null)
    {
        _cheminLog = cheminLog ?? RésoudreCheminLogParDéfaut();
        InitialiserRépertoire();
    }

    public void Debug(string message) => Écrire("DEBUG", message);
    public void Info(string message) => Écrire("INFO", message);
    public void Avertissement(string message) => Écrire("WARN", message);
    public void Erreur(string message, Exception? exception = null)
    {
        if (exception == null)
        {
            Écrire("ERROR", message);
            return;
        }

        var détail = new StringBuilder()
            .AppendLine(message)
            .AppendLine($"Type : {exception.GetType().FullName}")
            .AppendLine($"Message : {exception.Message}")
            .AppendLine("StackTrace :")
            .AppendLine(exception.StackTrace ?? "<aucune>")
            .ToString();

        Écrire("ERROR", détail.TrimEnd());
    }

    /// <summary>
    /// Écrit un bloc de démarrage avec les infos système pour faciliter le debug.
    /// </summary>
    public void ÉcrireBannerDémarrage()
    {
        var séparateur = new string('=', 80);
        var sb = new StringBuilder()
            .AppendLine(séparateur)
            .AppendLine("HDR PROFILE SWITCHER — DÉMARRAGE")
            .AppendLine(séparateur)
            .AppendLine($"Version        : {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}")
            .AppendLine($"Date           : {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"OS             : {Environment.OSVersion}")
            .AppendLine($"Runtime        : {Environment.Version}")
            .AppendLine($"Répertoire exe : {AppContext.BaseDirectory}")
            .AppendLine($"Fichier log    : {_cheminLog}")
            .AppendLine(séparateur);
        Écrire("INFO", sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Retourne le chemin complet du fichier log (pour l'afficher à l'utilisateur).
    /// </summary>
    public string CheminLog => _cheminLog;

    private void Écrire(string niveau, string message)
    {
        try
        {
            lock (Verrou)
            {
                EffectuerRotationSiNécessaire();
                var ligne = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{niveau}] {message}{Environment.NewLine}";
                File.AppendAllText(_cheminLog, ligne, Encoding.UTF8);
            }
        }
        catch
        {
            // Ne jamais faire planter l'application à cause du logger.
        }
    }

    private void InitialiserRépertoire()
    {
        var dossier = Path.GetDirectoryName(_cheminLog);
        if (!string.IsNullOrWhiteSpace(dossier) && !Directory.Exists(dossier))
        {
            Directory.CreateDirectory(dossier);
        }
    }

    private void EffectuerRotationSiNécessaire()
    {
        if (!File.Exists(_cheminLog))
            return;

        var info = new FileInfo(_cheminLog);
        if (info.Length < TailleMaxOctets)
            return;

        for (int i = NombreArchivesMax; i >= 1; i--)
        {
            var source = i == 1 ? _cheminLog : $"{_cheminLog}.{i - 1}";
            var destination = $"{_cheminLog}.{i}";

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }
    }

    private static string RésoudreCheminLogParDéfaut()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "hdr-switcher.log");
    }
}
