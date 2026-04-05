using System.Globalization;

namespace HdrProfileSwitcher;

/// <summary>
/// Localisation FR/EN auto-détectée via CultureInfo.CurrentUICulture.
/// </summary>
public static class Strings
{
    private static readonly bool IsFrench = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

    // === Window titles ===
    public static string ConfigTitle => IsFrench ? "Configuration — HDR Profile Switcher" : "Settings — HDR Profile Switcher";
    public static string AboutTitle => IsFrench ? "À propos" : "About";
    public static string ErrorTitle => IsFrench ? "HDR Profile Switcher — Erreur" : "HDR Profile Switcher — Error";
    public static string FatalErrorTitle => IsFrench ? "HDR Profile Switcher — Erreur fatale" : "HDR Profile Switcher — Fatal Error";

    // === Tray menu ===
    public static string TrayState => IsFrench ? "État : initialisation..." : "Status: initializing...";
    public static string TrayConfiguration => IsFrench ? "Configuration..." : "Settings...";
    public static string TrayOpenLog => IsFrench ? "Ouvrir le fichier log" : "Open log file";
    public static string TrayAbout => IsFrench ? "À propos" : "About";
    public static string TrayQuit => IsFrench ? "Quitter" : "Quit";
    public static string TrayNoDisplay => IsFrench ? "Aucun écran actif" : "No active display";
    public static string TrayNoProfile => IsFrench ? "aucun profil" : "no profile";

    // === Tray state format ===
    public static string TrayStateFormat(string display, string mode) =>
        IsFrench ? $"État : {display} / {mode}" : $"Status: {display} / {mode}";

    // === Config form ===
    public static string ConfigIntro => IsFrench
        ? @"Associez chaque écran détecté à un profil SDR et/ou HDR. Les profils proviennent de C:\Windows\System32\spool\drivers\color\."
        : @"Assign an SDR and/or HDR profile to each detected display. Profiles are loaded from C:\Windows\System32\spool\drivers\color\.";
    public static string ConfigStartWithWindows => IsFrench ? "Démarrer avec Windows" : "Start with Windows";
    public static string ConfigDetectedDisplay => IsFrench ? "Écran détecté" : "Detected display";
    public static string ConfigSearchPattern => IsFrench ? "Motif" : "Pattern";
    public static string ConfigSdrProfile => IsFrench ? "Profil SDR" : "SDR Profile";
    public static string ConfigHdrProfile => IsFrench ? "Profil HDR" : "HDR Profile";
    public static string ConfigLetter => IsFrench ? "Lettre" : "Letter";
    public static string ConfigSave => IsFrench ? "Enregistrer" : "Save";
    public static string ConfigCancel => IsFrench ? "Annuler" : "Cancel";
    public static string ConfigNone => IsFrench ? "(aucun)" : "(none)";
    public static string ConfigPatternRequired(string name) => IsFrench
        ? $"Le motif de recherche est obligatoire pour l'écran '{name}'."
        : $"The search pattern is required for display '{name}'.";

    // === About dialog ===
    public static string AboutMessage => IsFrench
        ? "HDR Profile Switcher\n\n" +
          "Bascule automatiquement les profils ICC/HDR selon l'écran actif.\n\n" +
          "Fonctions :\n" +
          "• détection automatique des écrans actifs\n" +
          "• détection HDR / SDR\n" +
          "• application automatique des profils de calibration\n" +
          "• surveillance en continu avec watchdog\n\n"
        : "HDR Profile Switcher\n\n" +
          "Automatically switches ICC/HDR color profiles based on the active display.\n\n" +
          "Features:\n" +
          "• automatic detection of active displays\n" +
          "• HDR / SDR state detection\n" +
          "• automatic calibration profile application\n" +
          "• continuous monitoring with watchdog\n\n";

    // === Error messages ===
    public static string AlreadyRunning => IsFrench
        ? "HDR Profile Switcher est déjà en cours d'exécution.\n\nVérifiez la zone de notification (tray)."
        : "HDR Profile Switcher is already running.\n\nCheck the system tray (notification area).";
    public static string FatalStartup(string msg) => IsFrench
        ? $"Erreur fatale au démarrage :\n\n{msg}\n\nConsultez le fichier log pour les détails."
        : $"Fatal error on startup:\n\n{msg}\n\nCheck the log file for details.";
    public static string LogNotFound(string path) => IsFrench
        ? $"Le fichier log n'existe pas encore :\n{path}"
        : $"The log file does not exist yet:\n{path}";
    public static string ConfigOpenError(string msg) => IsFrench
        ? $"Impossible d'ouvrir la configuration :\n\n{msg}"
        : $"Unable to open settings:\n\n{msg}";
}
