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
                _logger.Debug($"Écran détecté : {moniteur}");
            }
        }
        catch (Exception ex)
        {
            _logger.Erreur("Erreur lors de l'énumération des écrans", ex);
        }

        _logger.Info($"Écrans actifs trouvés : {moniteurs.Count}");
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
    /// Détecte l'état HDR d'un écran en essayant d'abord la version v2 (Windows 24H2+),
    /// puis en retombant sur la version v1 si nécessaire.
    /// </summary>
    private void DétecterÉtatHdr(
        NativeApis.DISPLAYCONFIG_PATH_INFO chemin,
        DisplayMonitor moniteur)
    {
        // Essai version 2 (Windows 11 24H2+, avec ACM)
        if (DétecterHdrV2(chemin, moniteur))
            return;

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
            // Tailles à essayer : la v2 peut varier selon la version exacte du SDK/Windows
            // On essaie d'abord notre struct, puis un buffer plus grand si ça échoue
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

            // Tentative avec buffer raw plus grand (128 octets, largement suffisant)
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
        // Tailles à essayer : 32, 36, 40, 48, 64, 128 octets
        uint[] tailles = { 32, 36, 40, 48, 64, 128 };

        foreach (var taille in tailles)
        {
            IntPtr buffer = Marshal.AllocHGlobal((int)taille);
            try
            {
                // Remplir de zéros
                for (int i = 0; i < (int)taille; i++)
                    Marshal.WriteByte(buffer, i, 0);

                // Écrire le header manuellement
                // header.type (offset 0, 4 octets)
                Marshal.WriteInt32(buffer, 0, (int)NativeApis.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2);
                // header.size (offset 4, 4 octets)
                Marshal.WriteInt32(buffer, 4, (int)taille);
                // header.adapterId.LowPart (offset 8, 4 octets)
                Marshal.WriteInt32(buffer, 8, (int)chemin.targetInfo.adapterId.LowPart);
                // header.adapterId.HighPart (offset 12, 4 octets)
                Marshal.WriteInt32(buffer, 12, chemin.targetInfo.adapterId.HighPart);
                // header.id (offset 16, 4 octets)
                Marshal.WriteInt32(buffer, 16, (int)chemin.targetInfo.id);

                int résultat = NativeApis.DisplayConfigGetDeviceInfoRaw(buffer);

                if (NativeApis.EstSuccès(résultat))
                {
                    // Lire les champs après le header (offset 20)
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
    /// Utilise le champ advancedColorEnabled du bitfield.
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
                _logger.Debug($"HDR v1 pour '{moniteur.NomConvivial}' : valueInfo=0x{requête.valueInfo:X8}, " +
                    $"supporté={moniteur.HdrSupporté}, actif={moniteur.EstHdrActif}, " +
                    $"wideColor={requête.WideColorEnforced}, forceDisabled={requête.AdvancedColorForceDisabled}, " +
                    $"colorEncoding={requête.colorEncoding}, bitsPerChannel={requête.bitsPerColorChannel}");
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

            // Récupérer tous les fichiers .icc et .icm
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
    /// Utilise ColorProfileAddDisplayAssociation (mscms.dll).
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

        try
        {
            _logger.Info($"Application du profil '{nomProfil}' sur '{moniteur.NomConvivial}' " +
                         $"(HDR={estProfilHdr}, sourceId={moniteur.SourceId})");

            // Retirer l'ancien profil du même type (HDR ou SDR) s'il y en avait un
            if (!string.IsNullOrEmpty(moniteur.ProfilActuelAppliqué) && moniteur.ProfilActuelAppliqué != nomProfil)
            {
                try
                {
                    int hrRetrait = NativeApis.ColorProfileRemoveDisplayAssociation(
                        scope: NativeApis.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER,
                        profileName: moniteur.ProfilActuelAppliqué,
                        targetAdapterID: moniteur.AdapterId,
                        sourceID: moniteur.SourceId,
                        dissociateAdvancedColor: estProfilHdr
                    );
                    _logger.Debug($"Retrait ancien profil '{moniteur.ProfilActuelAppliqué}' : HRESULT=0x{hrRetrait:X8}");
                }
                catch (Exception exRetrait)
                {
                    _logger.Debug($"Retrait ancien profil ignoré : {exRetrait.Message}");
                }
            }

            // ============================================================
            // MÉTHODE REGISTRE DIRECTE (Plan C — confirmé par diff reg 04/04/2026)
            // L'UI Windows Settings stocke le profil actif comme 1er élément
            // d'un REG_MULTI_SZ dans :
            //   HKCU\...\ICM\ProfileAssociations\Display\{GUID}\XXXX
            //   → ICMProfileAC (HDR) ou ICMProfile (SDR)
            // ============================================================
            string valeurReg = estProfilHdr ? "ICMProfileAC" : "ICMProfile";
            bool succèsRegistre = RéordonnerProfilRegistre(nomProfil, valeurReg);

            // Aussi appeler les API mscms en backup (elles ne font rien de visible
            // mais maintiennent la cohérence interne du WCS)
            try
            {
                NativeApis.ColorProfileAddDisplayAssociation(
                    scope: NativeApis.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER,
                    profileName: nomProfil,
                    targetAdapterID: moniteur.AdapterId,
                    sourceID: moniteur.SourceId,
                    setAsDefault: true,
                    associateAsAdvancedColor: estProfilHdr
                );
                NativeApis.ColorProfileSetDisplayDefaultAssociation(
                    scope: NativeApis.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER,
                    profileName: nomProfil,
                    profileType: NativeApis.CPT_ICC,
                    profileSubType: estProfilHdr
                        ? NativeApis.CPST_EXTENDED_DISPLAY_COLOR_MODE
                        : NativeApis.CPST_STANDARD_DISPLAY_COLOR_MODE,
                    targetAdapterID: moniteur.AdapterId,
                    sourceID: moniteur.SourceId
                );
            }
            catch (Exception exApi)
            {
                _logger.Debug($"API mscms backup ignorée : {exApi.Message}");
            }

            if (succèsRegistre)
            {
                moniteur.ProfilActuelAppliqué = nomProfil;
                _logger.Info($"✅ Profil '{nomProfil}' défini comme défaut {(estProfilHdr ? "HDR" : "SDR")} " +
                             $"sur '{moniteur.NomConvivial}' (via registre)");

                // ============================================================
                // NOTIFICATION — Forcer Windows à relire le registre ICM
                // (en arrière-plan pour ne pas bloquer le thread UI)
                // ============================================================
                System.Threading.Tasks.Task.Run(() => NotifierChangementProfil(moniteur, nomProfil));
            }
            else
            {
                _logger.Erreur($"❌ Échec registre pour '{nomProfil}' sur '{moniteur.NomConvivial}'");

                var cheminComplet = Path.Combine(RépertoireProfils, nomProfil);
                if (!File.Exists(cheminComplet))
                    _logger.Avertissement($"Le fichier profil '{nomProfil}' n'existe pas dans {RépertoireProfils}");
            }

            return succèsRegistre;
        }
        catch (DllNotFoundException)
        {
            _logger.Erreur("mscms.dll introuvable — ColorProfileAddDisplayAssociation non disponible. " +
                           "Nécessite Windows 10 Build 20348+ ou Windows 11.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Erreur($"Exception lors de l'application du profil '{nomProfil}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Applique automatiquement le bon profil à un écran selon son état HDR/SDR.
    /// Utilise la configuration associée à cet écran.
    /// </summary>
    /// <returns>true si un profil a été appliqué avec succès.</returns>
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
    // NOTIFICATION — Forcer Windows à appliquer le changement de profil
    // =========================================================================

    // =========================================================================
    // WINRT COM ACTIVATION — Appel direct à DisplayColorManagement.dll
    // =========================================================================

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoGetActivationFactory(
        IntPtr activatableClassId,
        [In] ref Guid iid,
        out IntPtr factory
    );

    [DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring
    );

    [DllImport("combase.dll")]
    private static extern void WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int type);

    /// <summary>
    /// Après l'écriture dans le registre, notifie le pipeline couleur Windows.
    /// Stratégie multi-couches :
    /// 1. Tente d'activer la factory WinRT DisplayColorManagement (comme fait Settings)
    /// 2. En fallback, utilise WcsSetDefaultColorProfile + broadcasts
    /// </summary>
    private void NotifierChangementProfil(DisplayMonitor moniteur, string nomProfil)
    {
        try
        {
            // Initialiser WinRT (MTA)
            RoInitialize(1); // RO_INIT_MULTITHREADED

            // 1) Tenter d'activer le serveur DisplayColorManagement
            //    C'est ce que fait SystemSettings.exe quand il charge la DLL
            string className = "Windows.Internal.Graphics.Display.DisplayColorManagement.DisplayColorManagementServer";
            IntPtr hClassName = IntPtr.Zero;
            try
            {
                WindowsCreateString(className, (uint)className.Length, out hClassName);

                // IActivationFactory GUID = {00000035-0000-0000-C000-000000000046}
                Guid iidActivationFactory = new Guid("00000035-0000-0000-C000-000000000046");

                try
                {
                    RoGetActivationFactory(hClassName, ref iidActivationFactory, out IntPtr factory);
                    _logger.Info($"✅ WinRT DisplayColorManagementServer activé ! (factory=0x{factory:X})");

                    // Le simple fait d'activer la factory peut déclencher
                    // le rechargement des profils depuis le registre.
                    // On libère proprement
                    if (factory != IntPtr.Zero)
                        Marshal.Release(factory);
                }
                catch (Exception exWinRT)
                {
                    _logger.Debug($"WinRT activation échouée (attendu si pas admin) : {exWinRT.Message}");
                }
            }
            finally
            {
                if (hClassName != IntPtr.Zero)
                    WindowsDeleteString(hClassName);
            }

            // 2) Aussi essayer DisplayColorManagement (pas Server)
            string className2 = "Windows.Internal.Graphics.Display.DisplayColorManagement.DisplayColorManagement";
            IntPtr hClassName2 = IntPtr.Zero;
            try
            {
                WindowsCreateString(className2, (uint)className2.Length, out hClassName2);
                Guid iidActivationFactory = new Guid("00000035-0000-0000-C000-000000000046");

                try
                {
                    RoGetActivationFactory(hClassName2, ref iidActivationFactory, out IntPtr factory2);
                    _logger.Info($"✅ WinRT DisplayColorManagement activé ! (factory=0x{factory2:X})");
                    if (factory2 != IntPtr.Zero)
                        Marshal.Release(factory2);
                }
                catch (Exception exWinRT2)
                {
                    _logger.Debug($"WinRT DCM activation échouée : {exWinRT2.Message}");
                }
            }
            finally
            {
                if (hClassName2 != IntPtr.Zero)
                    WindowsDeleteString(hClassName2);
            }

            // 3) SetDisplayConfig — force Windows à re-appliquer la config
            //    depuis sa base interne (qui inclut les profils du registre)
            _logger.Debug("Tentative SetDisplayConfig(SDC_APPLY | SDC_USE_DATABASE_CURRENT)...");
            int sdc = NativeApis.SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero,
                NativeApis.SDC_APPLY | NativeApis.SDC_USE_DATABASE_CURRENT);
            _logger.Info($"SetDisplayConfig : code retour = {sdc} (0=succès)");

            // 4) Fallback : WcsSetDefaultColorProfile + broadcasts
            string cheminProfil = Path.Combine(RépertoireProfils, nomProfil);
            string deviceName = $"\\\\.\\DISPLAY{moniteur.SourceId + 1}";

            bool wcsResult = NativeApis.WcsSetDefaultColorProfile(
                scope: NativeApis.WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER,
                pDeviceName: deviceName,
                cptColorProfileType: NativeApis.COLORPROFILETYPE_ICC,
                cpstColorProfileSubType: NativeApis.COLORPROFILESUBTYPE_NONE,
                dwProfileID: 0,
                pProfileName: cheminProfil
            );
            _logger.Debug($"WcsSetDefaultColorProfile('{deviceName}') : résultat={wcsResult}");

            // Broadcasts
            NativeApis.SendMessageTimeout(
                NativeApis.HWND_BROADCAST,
                NativeApis.WM_SETTINGCHANGE,
                IntPtr.Zero,
                "ImmersiveColorSet",
                NativeApis.SMTO_ABORTIFHUNG,
                2000,
                out _
            );
            _logger.Debug("Broadcast 'ImmersiveColorSet' envoyé.");

            NativeApis.SendMessageTimeout(
                NativeApis.HWND_BROADCAST,
                NativeApis.WM_SETTINGCHANGE,
                IntPtr.Zero,
                "WindowsColorSystem",
                NativeApis.SMTO_ABORTIFHUNG,
                2000,
                out _
            );
            _logger.Debug("Broadcast 'WindowsColorSystem' envoyé.");
        }
        catch (Exception ex)
        {
            _logger.Erreur($"Erreur notification changement profil : {ex.Message}");
        }
    }

    // =========================================================================
    // REGISTRE — Réordonnement du profil actif (Plan C)
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
    /// ProcMon a confirmé que Windows Settings écrit UNIQUEMENT dans la sous-clé
    /// correspondant à l'écran actif. On écrit dans TOUTES les sous-clés qui
    /// contiennent le profil, comme avant.
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
    /// pour TOUS les subtypes possibles (0 à 20 + bitflags).
    /// Log les changements détectés.
    /// </summary>
    public void EspionnerProfilsActifs(DisplayMonitor moniteur)
    {
        // Seuls les subtypes pertinents : 7 = SDR, 8 = HDR (confirmé par espionnage v1.9)
        uint[] subtypesATester = { 7, 8 };

        foreach (uint subType in subtypesATester)
        {
            try
            {
                // Allouer un buffer large (512 chars = 1024 octets) d'un coup
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
            moniteur.ConfigAssociée = null; // Réinitialiser

            foreach (var configÉcran in config.Écrans)
            {
                if (string.IsNullOrEmpty(configÉcran.MotifRecherche))
                    continue;

                if (moniteur.NomConvivial.Contains(
                    configÉcran.MotifRecherche,
                    StringComparison.OrdinalIgnoreCase))
                {
                    moniteur.ConfigAssociée = configÉcran;
                    _logger.Info($"Configuration associée : '{moniteur.NomConvivial}' → '{configÉcran.Nom}' (lettre: {configÉcran.Lettre})");
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
