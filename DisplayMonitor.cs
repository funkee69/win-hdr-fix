using System;

namespace HdrProfileSwitcher;

/// <summary>
/// Représente un écran physique ou virtuel détecté par Windows.
/// Contient toutes les informations nécessaires pour l'identification et l'application des profils.
/// </summary>
public class DisplayMonitor
{
    // =========================================================================
    // IDENTIFICATION
    // =========================================================================

    /// <summary>Nom convivial de l'écran (depuis l'EDID, ex: "Alienware AW3423DWF").</summary>
    public string NomConvivial { get; set; } = string.Empty;

    /// <summary>Chemin du périphérique Windows (ex: "\\?\DISPLAY1\...").</summary>
    public string CheminPériphérique { get; set; } = string.Empty;

    /// <summary>LUID de l'adaptateur GPU auquel cet écran est connecté.</summary>
    public NativeApis.LUID AdapterId { get; set; }

    /// <summary>Identifiant source de l'affichage (sourceId dans DisplayConfig).</summary>
    public uint SourceId { get; set; }

    /// <summary>Identifiant cible de l'affichage (targetId dans DisplayConfig).</summary>
    public uint TargetId { get; set; }

    // =========================================================================
    // ÉTAT COURANT
    // =========================================================================

    /// <summary>Indique si le mode HDR est actuellement actif sur cet écran.</summary>
    public bool EstHdrActif { get; set; }

    /// <summary>Indique si le HDR est supporté par le matériel de cet écran.</summary>
    public bool HdrSupporté { get; set; }

    /// <summary>Profil couleur actuellement appliqué (SDR ou HDR selon l'état).</summary>
    public string? ProfilActuelAppliqué { get; set; }

    /// <summary>Horodatage de la dernière mise à jour des informations.</summary>
    public DateTime DernièreMiseÀJour { get; set; } = DateTime.Now;

    // =========================================================================
    // CONFIGURATION ASSOCIÉE
    // =========================================================================

    /// <summary>Configuration trouvée dans config.json correspondant à cet écran. Null si non configuré.</summary>
    public ConfigurationÉcran? ConfigAssociée { get; set; }

    // =========================================================================
    // PROPRIÉTÉS CALCULÉES
    // =========================================================================

    /// <summary>Indique si cet écran a une configuration dans config.json.</summary>
    public bool EstConfiguré => ConfigAssociée != null;

    /// <summary>Lettre d'identification pour le tray icon (ex: "A" pour Alienware).</summary>
    public string Lettre => !string.IsNullOrWhiteSpace(ConfigAssociée?.Lettre)
        ? ConfigAssociée!.Lettre
        : NomConvivial.Length > 0
            ? NomConvivial[0].ToString().ToUpper()
            : "?";

    /// <summary>Profil attendu selon le mode HDR/SDR actuel et la configuration.</summary>
    public string? ProfilAttendu
    {
        get
        {
            if (ConfigAssociée == null) return null;

            return EstHdrActif
                ? ConfigAssociée.ProfilHdr
                : ConfigAssociée.ProfilSdr;
        }
    }

    /// <summary>
    /// Indique si le profil actuellement appliqué est correct (correspond à l'attendu).
    /// Si aucun profil attendu n'est défini, on considère que c'est OK.
    /// </summary>
    public bool ProfilCorrect
    {
        get
        {
            if (string.IsNullOrEmpty(ProfilAttendu)) return true;
            if (string.IsNullOrEmpty(ProfilActuelAppliqué)) return false;

            return string.Equals(
                ProfilActuelAppliqué,
                ProfilAttendu,
                StringComparison.OrdinalIgnoreCase
            );
        }
    }

    /// <summary>Description courte pour les logs et le tooltip.</summary>
    public string Description =>
        $"{NomConvivial} | {(EstHdrActif ? "HDR" : "SDR")} | Profil : {ProfilActuelAppliqué ?? "aucun"}";

    /// <summary>
    /// Représentation textuelle de l'écran pour les logs.
    /// </summary>
    public override string ToString() =>
        $"[{Lettre}] {NomConvivial} (adapterId={AdapterId.LowPart}, sourceId={SourceId}, targetId={TargetId}, HDR={EstHdrActif})";

    /// <summary>
    /// Vérifie si deux moniteurs représentent le même écran physique
    /// (comparaison par LUID + targetId).
    /// </summary>
    public bool MêmeÉcranQue(DisplayMonitor autre)
    {
        return AdapterId.LowPart == autre.AdapterId.LowPart
            && AdapterId.HighPart == autre.AdapterId.HighPart
            && TargetId == autre.TargetId;
    }
}
