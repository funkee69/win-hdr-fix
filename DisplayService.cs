using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HdrProfileSwitcher;

/// <summary>
/// Service principal de gestion des écrans.
/// Énumère les moniteurs actifs, détecte le mode HDR et applique les profils ICC.
///
/// v4.1 — Mécanisme validé (05/04/2026, 800 nits confirmé NVIDIA app) :
/// 1. Méthode primaire : Remove + Add(default) + Re-add via ColorProfileAddDisplayAssociation (LUID struct)
/// 2. Méthode fallback : écriture directe registre ICM (REG_MULTI_SZ, premier élément = profil actif)
/// </summary>
public class DisplayService
{
    private readonly Logger _logger;

    /// <summary>
    /// Répertoire Windows contenant les profils ICC/SICC installés.
    /// </summary>
    public static readonly string RépertoireProfils =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                     "spool", "drivers", "color");

    public DisplayService(Logger logger)
    {
        _logger = logger;
    }

    // =========================================================================
    // ÉNUMÉRATION DES ÉCRANS
    // =========================================================================

    /// <summary>
    /// Énumère tous les écrans actifs via QueryDisplayConfig.
    /// Retourne une liste de DisplayMonitor avec toutes les informations disponibles.
    /// </summary>
    public List<DisplayMonitor> ObtenirÉcransActifs()
    {
        var moniteurs = new List<DisplayMonitor>();

        try
        {
            // Étape 1 : Déterminer la taille des buffers nécessaires
            int résultat = NativeApis.GetDisplayConfigBufferSizes(
                NativeApis.QDC_ONLY_ACTIVE_PATHS,
                out uint nbChemins,
                out uint nbModes
            );

            if (!NativeApis.EstSuccès(résultat))
            {
                _logger.Erreur($"GetDisplayConfigBufferSizes a échoué avec le code : {résultat}");
                return moniteurs;
            }

            // Étape 2 : Allouer les buffers et récupérer la configuration
            var chemins = new NativeApis.DISPLAYCONFIG_PATH_INFO[nbChemins];
            var modes = new NativeApis.DISPLAYCONFIG_MODE_INFO[nbModes];

            résultat = NativeApis.QueryDisplayConfig(
                NativeApis.QDC_ONLY_ACTIVE_PATHS,
                ref nbChemins,
                chemins,
                ref nbModes,
                modes,
                IntPtr.Zero
            );

            if (!NativeApis.EstSuccès(résultat))
            {
                _logger.Erreur($"QueryDisplayConfig a échoué avec le code : {résultat}");
                return moniteurs;
            }

            // Étape 3 : Traiter chaque chemin d'affichage actif
            for (int i = 0; i < nbChemins; i++)
            {
                var chemin = chemins[i];
                var moniteur = new DisplayMonitor
                {
                    AdapterId = chemin.sourceInfo.adapterId,
                    SourceId = chemin.sourceInfo.id,
                    TargetId = chemin.targetInfo.id,
                    DernièreMiseÀJour = DateTime.Now
                };

                // Récupérer le nom convivial du moniteur
                RécupérerNomMoniteur(chemin, moniteur);

                // Détecter l'état HDR
                DétecterÉtatHdr(chemin, moniteur);

                moniteurs.Add(moniteur);
            }
        }
        catch (Exception ex)
        {
            _logger.Erreur("Erreur lors de l'énumération des écrans", ex);
        }

        // Log uniquement au premier cycle ou si la liste/état des écrans a changé
        string fingerprint = string.Join("|", moniteurs.Select(m => $"{m.NomConvivial}:{m.EstHdrActif}"));
        if (fingerprint != _derniersÉcransFingerprint)
        {
            foreach (var m in moniteurs)
                _logger.Debug($"Écran détecté : {m}");
            _logger.Info($"Écrans actifs trouvés : {moniteurs.Count}");
            _derniersÉcransFingerprint = fingerprint;
        }
        return moniteurs;
    }

    /// <summary>
    /// Récupère le nom convivial (EDID) d'un moniteur via DisplayConfigGetDeviceInfo.
    /// </summary>
    private void RécupérerNomMoniteur(
        NativeApis.DISPLAYCONFIG_PATH_INFO chemin,
        DisplayMonitor moniteur)
    {
        try
        {
            var requêteNom = new NativeApis.DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new NativeApis.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = NativeApis.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)Marshal.SizeOf<NativeApis.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                    adapterId = chemin.targetInfo.adapterId,
                    id = chemin.targetInfo.id
                },
                monitorFriendlyDeviceName = string.Empty,
                monitorDevicePath = string.Empty
            };

            int résultat = NativeApis.DisplayConfigGetDeviceInfo(ref requêteNom);

            if (NativeApis.EstSuccès(résultat))
            {
                moniteur.NomConvivial = requêteNom.monitorFriendlyDeviceName ?? string.Empty;
                moniteur.CheminPériphérique = requêteNom.monitorDevicePath ?? string.Empty;
            }
            else
            {
                _logger.Avertissement($"Impossible de récupérer le nom du moniteur (code: {résultat})");
                moniteur.NomConvivial = $"Moniteur inconnu (target={chemin.targetInfo.id})";
            }
        }
        catch (Exception ex)
        {
            _logger.Erreur("Erreur lors de la récupération du nom du moniteur", ex);
            moniteur.NomConvivial = "Moniteur inconnu";
        }
    }

    /// <summary>
    /// Cache pour éviter de retenter HDR v2 toutes les 5 secondes
    /// quand on sait déjà que ça ne marche pas sur ce système.
    /// </summary>
    private bool _hdrV2Tenté = false;
    private bool _hdrV2Disponible = false;

    // =========================================================================
    // TRACKING POLLING — Éviter les logs redondants à chaque cycle (5 sec)
    // =========================================================================
    private string _derniersÉcransFingerprint = "";
    private readonly Dictionary<uint, string> _hdrV1DernierLog = new();
    private readonly Dictionary<string, string?> _dernièresAssociations = new();

    /// <summary>
    /// Détecte l'état HDR d'un écran en essayant d'abord la version v2 (Windows 24H2+),
    /// puis en retombant sur la version v1 si nécessaire.
    /// </summary>
    private void DétecterÉtatHdr(
        NativeApis.DISPLAYCONFIG_PATH_INFO chemin,
        DisplayMonitor moniteur)
    {
        // Essai version 2 (Windows 11 24H2+, avec ACM) — une seule fois
        if (!_hdrV2Tenté)
        {
            _hdrV2Tenté = true;
            _hdrV2Disponible = DétecterHdrV2(chemin, moniteur);
            if (_hdrV2Disponible) return;
        }
        else if (_hdrV2Disponible)
        {
            if (DétecterHdrV2(chemin, moniteur)) return;
        }

        // Fallback version 1 (Windows 10 1903+ jusqu'à Windows 11 23H2)
        DétecterHdrV1(chemin, moniteur);
    }

    /// <summary>
    /// Détection HDR via DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 (Windows 24H2+).
    /// Utilise un buffer raw pour éviter les problèmes de taille de struct.
    /// Distingue correctement SDR / WCG / HDR / Auto.
    /// </summary>
    private bool DétecterHdrV2(
        NativeApis.DISPLAYCONFIG_PATH_INFO chemin,
        DisplayMonitor moniteur)
    {
        try
        {
            int structSize = Marshal.SizeOf<NativeApis.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>();
            _logger.Debug($"HDR v2 : tentative avec taille struct = {structSize} octets");

            var requête = new NativeApis.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
            {
                header = new NativeApis.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = NativeApis.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2,
                    size = (uint)structSize,
                    adapterId = chemin.targetInfo.adapterId,
                    id = chemin.targetInfo.id
                }
            };

            int résultat = NativeApis.DisplayConfigGetDeviceInfo(ref requête);

            if (NativeApis.EstSuccès(résultat))
            {
                moniteur.HdrSupporté = requête.HighDynamicRangeSupported;
                moniteur.EstHdrActif = requête.IsHdrActif;
                _logger.Debug($"HDR v2 pour '{moniteur.NomConvivial}' : supporté={moniteur.HdrSupporté}, actif={moniteur.EstHdrActif}, mode={requête.activeColorMode}");
                return true;
            }

            _logger.Debug($"HDR v2 struct échoué (code: {résultat}), tentative raw buffer...");
            return DétecterHdrV2Raw(chemin, moniteur);
        }
        catch (Exception ex)
        {
            _logger.Debug($"Exception lors de la détection HDR v2 : {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Détection HDR v2 via buffer raw (IntPtr) pour contourner les problèmes de taille.
    /// Essaie plusieurs tailles de buffer.
    /// </summary>
    private bool DétecterHdrV2Raw(
        NativeApis.DISPLAYCONFIG_PATH_INFO chemin,
        DisplayMonitor moniteur)
    {
        uint[] tailles = { 32, 36, 40, 48, 64, 128 };

        foreach (var taille in tailles)
        {
            IntPtr buffer = Marshal.AllocHGlobal((int)taille);
            try
            {
                for (int i = 0; i < (int)taille; i++)
                    Marshal.WriteByte(buffer, i, 0);

                // Header (offset 0-19)
                Marshal.WriteInt32(buffer, 0, (int)NativeApis.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2);
                Marshal.WriteInt32(buffer, 4, (int)taille);
                Marshal.WriteInt32(buffer, 8, (int)chemin.targetInfo.adapterId.LowPart);
                Marshal.WriteInt32(buffer, 12, chemin.targetInfo.adapterId.HighPart);
                Marshal.WriteInt32(buffer, 16, (int)chemin.targetInfo.id);

                int résultat = NativeApis.DisplayConfigGetDeviceInfoRaw(buffer);

                if (NativeApis.EstSuccès(résultat))
                {
                    uint supportedColorInfo = (uint)Marshal.ReadInt32(buffer, 20);
                    uint activeColorMode = (uint)Marshal.ReadInt32(buffer, 24);

                    moniteur.HdrSupporté = (supportedColorInfo & 0x1) != 0;
                    moniteur.EstHdrActif = (activeColorMode == NativeApis.DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR);

                    _logger.Info($"✅ HDR v2 RAW (taille={taille}) pour '{moniteur.NomConvivial}' : " +
                        $"supportedColorInfo=0x{supportedColorInfo:X8}, activeColorMode={activeColorMode}, " +
                        $"supporté={moniteur.HdrSupporté}, actif={moniteur.EstHdrActif}");
                    return true;
                }
                else
                {
                    _logger.Debug($"HDR v2 raw taille={taille} échoué (code: {résultat})");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        _logger.Debug($"HDR v2 non disponible pour '{moniteur.NomConvivial}' (toutes les tailles échouées), tentative v1...");
        return false;
    }

    /// <summary>
    /// Détection HDR via DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO (pré-Windows 24H2).
    /// </summary>
    private bool DétecterHdrV1(
        NativeApis.DISPLAYCONFIG_PATH_INFO chemin,
        DisplayMonitor moniteur)
    {
        try
        {
            var requête = new NativeApis.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
            {
                header = new NativeApis.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = NativeApis.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                    size = (uint)Marshal.SizeOf<NativeApis.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                    adapterId = chemin.targetInfo.adapterId,
                    id = chemin.targetInfo.id
                }
            };

            int résultat = NativeApis.DisplayConfigGetDeviceInfo(ref requête);

            if (NativeApis.EstSuccès(résultat))
            {
                moniteur.HdrSupporté = requête.AdvancedColorSupported;
                moniteur.EstHdrActif = requête.AdvancedColorEnabled;
                // Log uniquement au premier appel ou si l'état HDR v1 change
                string hdrV1Info = $"valueInfo=0x{requête.valueInfo:X8},supporté={moniteur.HdrSupporté},actif={moniteur.EstHdrActif}";
                if (!_hdrV1DernierLog.TryGetValue(moniteur.TargetId, out string? dernierLog) || dernierLog != hdrV1Info)
                {
                    _logger.Debug($"HDR v1 pour '{moniteur.NomConvivial}' : valueInfo=0x{requête.valueInfo:X8}, " +
                        $"supporté={moniteur.HdrSupporté}, actif={moniteur.EstHdrActif}, " +
                        $"wideColor={requête.WideColorEnforced}, forceDisabled={requête.AdvancedColorForceDisabled}, " +
                        $"colorEncoding={requête.colorEncoding}, bitsPerChannel={requête.bitsPerColorChannel}");
                    _hdrV1DernierLog[moniteur.TargetId] = hdrV1Info;
                }
                return true;
            }

            _logger.Avertissement($"Détection HDR v1 échouée pour '{moniteur.NomConvivial}' (code: {résultat})");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Erreur($"Exception lors de la détection HDR v1 pour '{moniteur.NomConvivial}'", ex);
            return false;
        }
    }

    // =========================================================================
    // GESTION DES PROFILS ICC
    // =========================================================================

    /// <summary>
    /// Liste tous les profils ICC et SICC installés dans le répertoire système Windows.
    /// </summary>
    public List<string> ListerProfilsInstallés()
    {
        var profils = new List<string>();

        try
        {
            if (!Directory.Exists(RépertoireProfils))
            {
                _logger.Avertissement($"Répertoire de profils introuvable : {RépertoireProfils}");
                return profils;
            }

            foreach (var extension in new[] { "*.icc", "*.icm" })
            {
                var fichiers = Directory.GetFiles(RépertoireProfils, extension, SearchOption.TopDirectoryOnly);
                foreach (var fichier in fichiers)
                {
                    profils.Add(Path.GetFileName(fichier));
                }
            }

            profils.Sort(StringComparer.OrdinalIgnoreCase);
            _logger.Info($"Profils installés trouvés : {profils.Count}");
        }
        catch (Exception ex)
        {
            _logger.Erreur("Erreur lors de la liste des profils installés", ex);
        }

        return profils;
    }

    /// <summary>
    /// Applique un profil ICC ou SICC à un écran spécifique.
    ///
    /// Mécanisme v4.1 (validé 05/04/2026) :
    /// a) Méthode primaire : Remove + Add(default) + Re-add via ColorProfileAddDisplayAssociation avec LUID
    /// b) Méthode fallback : écriture registre ICM (REG_MULTI_SZ, premier = actif)
    /// </summary>
    /// <param name="moniteur">L'écran cible.</param>
    /// <param name="nomProfil">Nom du fichier profil (ex: "HDR PEAK 1000.icc").</param>
    /// <param name="estProfilHdr">true pour HDR/SICC (associateAsAdvancedColor=true).</param>
    /// <returns>true si l'application a réussi.</returns>
    public bool AppliquerProfil(DisplayMonitor moniteur, string nomProfil, bool estProfilHdr)
    {
        if (string.IsNullOrWhiteSpace(nomProfil))
        {
            _logger.Avertissement($"Profil vide pour '{moniteur.NomConvivial}', application ignorée.");
            return false;
        }

        _logger.Info($"Application du profil '{nomProfil}' sur '{moniteur.NomConvivial}' " +
                     $"(HDR={estProfilHdr}, adapterId=0x{moniteur.AdapterId.LowPart:X8}:{moniteur.AdapterId.HighPart:X8}, sourceId={moniteur.SourceId})");

        uint profileSubType = estProfilHdr
            ? NativeApis.CPST_EXTENDED_DISPLAY_COLOR_MODE
            : NativeApis.CPST_STANDARD_DISPLAY_COLOR_MODE;

        // ============================================================
        // MÉTHODE PRIMAIRE : API ColorProfile* (mscms.dll)
        // Flow validé le 05/04/2026 (800 nits confirmé par NVIDIA app) :
        //   1. Remove(ancien profil)
        //   2. Add(nouveau profil, setAsDefault=true, advColor=estHdr)
        //   3. Re-add(ancien profil, setAsDefault=false)
        // Le LUID est un struct de 8 bytes obtenu via QueryDisplayConfig.
        // ============================================================
        bool succèsApi = false;
        try
        {
            // Étape 1 : Retirer TOUS les autres profils du même type (nécessaire pour forcer le switch)
            // On retire tous les profils HDR ou SDR connus sauf celui qu'on veut appliquer.
            // Cela garantit que le switch fonctionne même au premier lancement (ProfilActuelAppliqué vide).
            var profilsConnus = ListerProfilsInstallés();
            foreach (var autreProfile in profilsConnus)
            {
                if (autreProfile.Equals(nomProfil, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    int hrRemove = NativeApis.ColorProfileRemoveDisplayAssociation(
                        scope: NativeApis.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER,
                        profileName: autreProfile,
                        targetAdapterID: moniteur.AdapterId,
                        sourceID: moniteur.SourceId,
                        dissociateAdvancedColor: estProfilHdr
                    );
                    if (hrRemove == 0)
                        _logger.Debug($"Remove '{autreProfile}' : OK");
                }
                catch { /* ignorer les profils non associés */ }
            }

            // Étape 2 : Ajouter le nouveau profil comme défaut
            int hrAdd = NativeApis.ColorProfileAddDisplayAssociation(
                scope: NativeApis.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER,
                profileName: nomProfil,
                targetAdapterID: moniteur.AdapterId,
                sourceID: moniteur.SourceId,
                setAsDefault: true,
                associateAsAdvancedColor: estProfilHdr
            );
            _logger.Debug($"Add '{nomProfil}' (default=true, advColor={estProfilHdr}) : HR=0x{hrAdd:X8}");

            succèsApi = (hrAdd == 0);

            // Étape 3 : Re-ajouter les autres profils en non-défaut
            if (succèsApi)
            {
                foreach (var autreProfile in profilsConnus)
                {
                    if (autreProfile.Equals(nomProfil, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        NativeApis.ColorProfileAddDisplayAssociation(
                            scope: NativeApis.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER,
                            profileName: autreProfile,
                            targetAdapterID: moniteur.AdapterId,
                            sourceID: moniteur.SourceId,
                            setAsDefault: false,
                            associateAsAdvancedColor: estProfilHdr
                        );
                    }
                    catch { /* ignorer */ }
                }
            }

            if (succèsApi)
                _logger.Info($"✅ Profil appliqué via API : '{nomProfil}' {(estProfilHdr ? "HDR" : "SDR")} sur '{moniteur.NomConvivial}'");
            else
                _logger.Avertissement($"API Add a échoué HR=0x{hrAdd:X8}, passage au fallback registre.");
        }
        catch (DllNotFoundException)
        {
            _logger.Avertissement("mscms.dll indisponible, passage au fallback registre.");
        }
        catch (Exception ex)
        {
            _logger.Avertissement($"API primaire exception : {ex.Message}, passage au fallback registre.");
        }

        // ============================================================
        // MÉTHODE FALLBACK : Registre ICM direct
        // HKCU\...\ICM\ProfileAssociations\Display\{GUID}\XXXX
        //   ICMProfileAC (HDR) ou ICMProfile (SDR)
        // Windows lit le 1er élément du REG_MULTI_SZ comme profil actif.
        // ============================================================
        bool succèsRegistre = succèsApi; // si API ok, pas besoin du registre en plus
        if (!succèsApi)
        {
            string valeurReg = estProfilHdr ? "ICMProfileAC" : "ICMProfile";
            succèsRegistre = RéordonnerProfilRegistre(nomProfil, valeurReg);

            if (succèsRegistre)
                _logger.Info($"✅ Fallback registre réussi : '{nomProfil}' en première position de '{valeurReg}'");
            else
            {
                _logger.Erreur($"❌ Échec registre pour '{nomProfil}' sur '{moniteur.NomConvivial}'");
                var cheminComplet = Path.Combine(RépertoireProfils, nomProfil);
                if (!File.Exists(cheminComplet))
                    _logger.Avertissement($"Fichier profil '{nomProfil}' absent de {RépertoireProfils}");
            }
        }

        if (succèsApi || succèsRegistre)
        {
            moniteur.ProfilActuelAppliqué = nomProfil;
        }

        return succèsApi || succèsRegistre;
    }

    /// <summary>
    /// Applique automatiquement le bon profil à un écran selon son état HDR/SDR.
    /// </summary>
    public bool AppliquerProfilAutomatique(DisplayMonitor moniteur)
    {
        if (moniteur.ConfigAssociée == null)
        {
            _logger.Debug($"Aucune configuration pour '{moniteur.NomConvivial}', profil ignoré.");
            return false;
        }

        string? profilAttendu = moniteur.EstHdrActif
            ? moniteur.ConfigAssociée.ProfilHdr
            : moniteur.ConfigAssociée.ProfilSdr;

        if (string.IsNullOrEmpty(profilAttendu))
        {
            _logger.Debug($"Aucun profil {(moniteur.EstHdrActif ? "HDR" : "SDR")} configuré pour '{moniteur.NomConvivial}'.");
            return false;
        }

        return AppliquerProfil(moniteur, profilAttendu, moniteur.EstHdrActif);
    }

    // =========================================================================
    // REGISTRE — Réordonnement du profil actif (fallback)
    // =========================================================================

    /// <summary>
    /// Clé registre contenant les associations de profils par écran.
    /// Chaque sous-clé (0000, 0001, ...) correspond à un device index.
    /// </summary>
    private const string CLÉ_REGISTRE_ICM =
        @"Software\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations\Display\{4d36e96e-e325-11ce-bfc1-08002be10318}";

    /// <summary>
    /// Parcourt toutes les sous-clés registre ICM et réordonne le REG_MULTI_SZ
    /// pour mettre le profil demandé en premier (= actif pour Windows).
    ///
    /// ICMProfileAC = liste profils HDR (premier = actif)
    /// ICMProfile   = liste profils SDR (premier = actif)
    /// </summary>
    private bool RéordonnerProfilRegistre(string nomProfil, string nomValeur)
    {
        int modifiées = 0;
        try
        {
            using var cléParent = Registry.CurrentUser.OpenSubKey(CLÉ_REGISTRE_ICM);
            if (cléParent == null)
            {
                _logger.Erreur($"Clé registre ICM introuvable : HKCU\\{CLÉ_REGISTRE_ICM}");
                return false;
            }

            foreach (string nomSousClé in cléParent.GetSubKeyNames())
            {
                try
                {
                    using var sousClé = Registry.CurrentUser.OpenSubKey(
                        $"{CLÉ_REGISTRE_ICM}\\{nomSousClé}", writable: true);
                    if (sousClé == null) continue;

                    var valeur = sousClé.GetValue(nomValeur);
                    if (valeur is not string[] listeActuelle || listeActuelle.Length == 0)
                        continue;

                    int index = Array.FindIndex(listeActuelle,
                        p => p.Equals(nomProfil, StringComparison.OrdinalIgnoreCase));

                    if (index < 0) continue;

                    if (index == 0)
                    {
                        _logger.Debug($"Registre [{nomSousClé}] {nomValeur} : '{nomProfil}' déjà en premier.");
                        modifiées++;
                        continue;
                    }

                    var nouvelleListe = new List<string> { listeActuelle[index] };
                    for (int i = 0; i < listeActuelle.Length; i++)
                    {
                        if (i != index)
                            nouvelleListe.Add(listeActuelle[i]);
                    }

                    sousClé.SetValue(nomValeur, nouvelleListe.ToArray(), RegistryValueKind.MultiString);
                    modifiées++;

                    _logger.Info($"✅ Registre [{nomSousClé}] {nomValeur} : '{nomProfil}' déplacé en 1ère position " +
                                 $"(ancien ordre: {string.Join(" → ", listeActuelle.Select(p => $"'{p}'"))})");
                }
                catch (Exception exSousClé)
                {
                    _logger.Debug($"Registre [{nomSousClé}] ignoré : {exSousClé.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Erreur($"Erreur accès registre ICM : {ex.Message}");
            return false;
        }

        if (modifiées > 0)
            _logger.Info($"Registre : {modifiées} sous-clé(s) mises à jour pour '{nomProfil}' dans '{nomValeur}'.");
        else
            _logger.Avertissement($"Registre : profil '{nomProfil}' introuvable dans aucune sous-clé de '{nomValeur}'.");

        return modifiées > 0;
    }

    // =========================================================================
    // ESPION DE PROFILS — Détecte les changements via Windows
    // =========================================================================

    /// <summary>
    /// Dictionnaire des derniers profils connus par subtype pour chaque écran.
    /// Clé = "sourceId:subType", Valeur = nom du profil.
    /// </summary>
    private readonly Dictionary<string, string> _derniersProfilsConnus = new();

    /// <summary>
    /// Interroge Windows pour connaître le profil par défaut actuel d'un écran
    /// pour les subtypes SDR (7) et HDR (8). Log les changements détectés.
    /// </summary>
    public void EspionnerProfilsActifs(DisplayMonitor moniteur)
    {
        // DÉSACTIVÉ v4.1 — ColorProfileGetDisplayDefault retourne des données corrompues
        // (caractères coréens/chinois dans le StringBuilder). Non nécessaire :
        // la détection HDR/SDR via DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO fonctionne parfaitement.
        return;

        // Subtypes pertinents : 7 = SDR, 8 = HDR (confirmé par espionnage v1.9)
        uint[] subtypesATester = { 7, 8 };

        foreach (uint subType in subtypesATester)
        {
            try
            {
                uint bufferSize = 512;
                var sb = new System.Text.StringBuilder((int)bufferSize);
                int hr = NativeApis.ColorProfileGetDisplayDefault(
                    NativeApis.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER,
                    moniteur.AdapterId,
                    moniteur.SourceId,
                    NativeApis.CPT_ICC,
                    subType,
                    sb,
                    ref bufferSize
                );

                string clé = $"{moniteur.SourceId}:{subType}";
                string profilActuel = (hr == 0 && sb.Length > 0) ? sb.ToString() : "";
                string label = subType == 7 ? "SDR" : "HDR";

                if (_derniersProfilsConnus.TryGetValue(clé, out string? ancien))
                {
                    if (ancien != profilActuel)
                    {
                        _logger.Info($"\U0001f50d ESPION [{label}] : Changement détecté ! " +
                                     $"'{ancien}' → '{profilActuel}' " +
                                     $"(HR=0x{hr:X8}, écran={moniteur.NomConvivial})");
                        _derniersProfilsConnus[clé] = profilActuel;
                    }
                }
                else
                {
                    _derniersProfilsConnus[clé] = profilActuel;
                    _logger.Info($"\U0001f50d ESPION INIT [{label}] : '{profilActuel}' " +
                                 $"(HR=0x{hr:X8}, écran={moniteur.NomConvivial})");
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Espion subType={subType} exception : {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Associe chaque moniteur détecté à sa configuration dans config.json via le motif de recherche.
    /// </summary>
    public void AssocierConfigurations(List<DisplayMonitor> moniteurs, AppConfig config)
    {
        foreach (var moniteur in moniteurs)
        {
            moniteur.ConfigAssociée = null;

            foreach (var configÉcran in config.Écrans)
            {
                if (string.IsNullOrEmpty(configÉcran.MotifRecherche))
                    continue;

                if (moniteur.NomConvivial.Contains(
                    configÉcran.MotifRecherche,
                    StringComparison.OrdinalIgnoreCase))
                {
                    moniteur.ConfigAssociée = configÉcran;
                    // Log uniquement si l'association a changé (évite le spam de polling)
                    if (!_dernièresAssociations.TryGetValue(moniteur.NomConvivial, out string? dernièreAssoc) || dernièreAssoc != configÉcran.Nom)
                    {
                        _logger.Info($"Configuration associée : '{moniteur.NomConvivial}' → '{configÉcran.Nom}' (lettre: {configÉcran.Lettre})");
                        _dernièresAssociations[moniteur.NomConvivial] = configÉcran.Nom;
                    }
                    break;
                }
            }

            if (moniteur.ConfigAssociée == null)
            {
                _logger.Debug($"Aucune configuration trouvée pour '{moniteur.NomConvivial}'");
            }
        }
    }
}
