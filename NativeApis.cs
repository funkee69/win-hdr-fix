using System;
using System.Runtime.InteropServices;

namespace HdrProfileSwitcher;

/// <summary>
/// Toutes les définitions P/Invoke, structs et constantes pour les APIs Windows natives.
/// Sources : user32.dll (DisplayConfig), mscms.dll (gestion profils couleur).
/// </summary>
public static class NativeApis
{
    // =========================================================================
    // CONSTANTES DisplayConfig
    // =========================================================================

    /// <summary>Énumérer uniquement les chemins d'affichage actifs.</summary>
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    /// <summary>Type d'info : nom du périphérique cible (moniteur).</summary>
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    /// <summary>Type d'info : informations de couleur avancée (HDR, pré-Windows 11 24H2).</summary>
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

    /// <summary>Type d'info : informations de couleur avancée v2 (Windows 11 24H2+, ACM).</summary>
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 14;

    /// <summary>Type d'info : nom de l'adaptateur (GPU).</summary>
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME = 4;

    /// <summary>Type d'info : activer/désactiver le mode couleur avancée (HDR).</summary>
    public const uint DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;

    // Constantes pour le mode couleur avancé (version 2 — Windows 24H2+)
    public const uint DISPLAYCONFIG_ADVANCED_COLOR_MODE_SDR = 0;
    public const uint DISPLAYCONFIG_ADVANCED_COLOR_MODE_WCG = 1;
    public const uint DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR = 2;
    public const uint DISPLAYCONFIG_ADVANCED_COLOR_MODE_AUTO = 3;

    // =========================================================================
    // CONSTANTES Color Management (mscms.dll)
    // =========================================================================

    /// <summary>Scope utilisateur courant (pas besoin d'admin).</summary>
    public const uint WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER = 0;

    /// <summary>Scope système (nécessite admin).</summary>
    public const uint WCS_PROFILE_MANAGEMENT_SCOPE_SYSTEM_WIDE = 1;

    // =========================================================================
    // STRUCTS DisplayConfig
    // =========================================================================

    /// <summary>Identifiant local unique (LUID) — identifie le GPU/adaptateur.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary>Identifiant de l'adaptateur d'affichage.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    /// <summary>Informations sur la cible d'affichage (moniteur connecté).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;
        public uint statusFlags;
    }

    /// <summary>Ratio de fréquence de rafraîchissement.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    /// <summary>Chemin d'affichage complet (source → cible).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>En-tête commune à toutes les structs DISPLAYCONFIG_MODE_INFO.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        // Union de 64 octets pour les différents types de mode
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] modeInfoData;
    }

    /// <summary>En-tête de base pour les requêtes DeviceInfo.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    /// <summary>
    /// Informations sur le nom du périphérique cible (moniteur).
    /// Le champ monitorFriendlyDeviceName contient le nom EDID du moniteur.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    /// <summary>
    /// Informations de couleur avancée (HDR) — version 1, pré-Windows 24H2.
    /// Compatible avec Windows 10 1903+ et Windows 11 avant 24H2.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        // Champs de couleur avancée (bitfield encodé dans un uint)
        public uint valueInfo;

        public uint colorEncoding;
        public uint bitsPerColorChannel;

        // Propriétés dérivées du champ valueInfo (bits 0-3)
        public bool AdvancedColorSupported => (valueInfo & 0x1) != 0;
        public bool AdvancedColorEnabled => (valueInfo & 0x2) != 0;    // HDR actif
        public bool WideColorEnforced => (valueInfo & 0x4) != 0;
        public bool AdvancedColorForceDisabled => (valueInfo & 0x8) != 0;
    }

    /// <summary>
    /// Informations de couleur avancée v2 — Windows 11 24H2+ (ACM).
    /// Distingue SDR / WCG / HDR / Auto via activeColorMode.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        // Champs de support (bitfield)
        public uint supportedColorInfo;

        // Mode couleur actif
        public uint activeColorMode;

        public uint colorEncoding;
        public uint bitsPerColorChannel;

        // Propriétés dérivées
        public bool HighDynamicRangeSupported => (supportedColorInfo & 0x1) != 0;
        public bool AdvancedColorSupported => (supportedColorInfo & 0x2) != 0;
        public bool DolbyVisionSupported => (supportedColorInfo & 0x4) != 0;
        public bool IsHdrActif => activeColorMode == DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR;
    }

    // =========================================================================
    // P/INVOKE — user32.dll (DisplayConfig)
    // =========================================================================

    /// <summary>
    /// Détermine le nombre de chemins et modes nécessaires pour QueryDisplayConfig.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements
    );

    /// <summary>
    /// Récupère la topologie d'affichage complète (écrans, modes, connexions).
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId
    );

    /// <summary>
    /// Récupère des informations spécifiques sur un périphérique d'affichage.
    /// Utilisé pour le nom du moniteur et les infos HDR.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket
    );

    /// <summary>Surcharge pour DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO (v1).</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket
    );

    /// <summary>Surcharge pour DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 (v2, Windows 24H2+).</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 requestPacket
    );

    /// <summary>Surcharge raw avec IntPtr pour contourner les problèmes de taille de struct.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false, EntryPoint = "DisplayConfigGetDeviceInfo")]
    public static extern int DisplayConfigGetDeviceInfoRaw(IntPtr requestPacket);

    /// <summary>Applique une modification de configuration d'affichage (ex: toggle HDR).</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int DisplayConfigSetDeviceInfo(IntPtr setPacket);

    /// <summary>Applique une configuration d'affichage. Avec SDC_APPLY | SDC_USE_DATABASE_CURRENT,
    /// force Windows à re-appliquer la config actuelle depuis sa base interne.</summary>
    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        IntPtr pathArray,
        uint numModeInfoArrayElements,
        IntPtr modeInfoArray,
        uint flags
    );

    // SetDisplayConfig flags
    public const uint SDC_APPLY = 0x00000080;
    public const uint SDC_USE_DATABASE_CURRENT = 0x0000000F;
    public const uint SDC_NO_OPTIMIZATION = 0x00000100;
    public const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    public const uint SDC_ALLOW_CHANGES = 0x00000400;
    public const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
    public const uint SDC_TOPOLOGY_CLONE = 0x00000002;
    public const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    public const uint SDC_TOPOLOGY_EXTERNAL = 0x00000008;
    public const uint SDC_TOPOLOGY_SUPPLIED = 0x00000010;
    public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    public const uint SDC_VALIDATE = 0x00000040;
    public const uint SDC_PATH_PERSIST_IF_REQUIRED = 0x00000800;
    public const uint SDC_FORCE_MODE_ENUMERATION = 0x00001000;
    public const uint SDC_ALLOW_PATH_ORDER_CHANGES = 0x00002000;
    public const uint SDC_VIRTUAL_MODE_AWARE = 0x00008000;
    public const uint SDC_VIRTUAL_REFRESH_RATE_AWARE = 0x00020000;

    // =========================================================================
    // P/INVOKE — mscms.dll (Color Management System)
    // =========================================================================

    /// <summary>
    /// Associe un profil ICC/SICC à un affichage spécifique.
    /// Requiert Windows 10 Build 20348+ (Windows Server 2022) ou Windows 11.
    /// </summary>
    /// <param name="scope">Portée : utilisateur courant ou système.</param>
    /// <param name="profileName">Nom du fichier profil (ex: "HDR PEAK 1000.icc").</param>
    /// <param name="targetAdapterID">LUID de l'adaptateur GPU.</param>
    /// <param name="sourceID">Identifiant de la source d'affichage.</param>
    /// <param name="setAsDefault">Définir comme profil par défaut.</param>
    /// <param name="associateAsAdvancedColor">true pour HDR/SICC, false pour SDR/ICC.</param>
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int ColorProfileAddDisplayAssociation(
        uint scope,
        [MarshalAs(UnmanagedType.LPWStr)] string profileName,
        LUID targetAdapterID,
        uint sourceID,
        [MarshalAs(UnmanagedType.Bool)] bool setAsDefault,
        [MarshalAs(UnmanagedType.Bool)] bool associateAsAdvancedColor
    );

    /// <summary>
    /// Supprime l'association d'un profil ICC/SICC d'un affichage.
    /// </summary>
    // Constantes COLORPROFILETYPE et COLORPROFILESUBTYPE
    public const uint CPT_ICC = 1;
    // Enum séquentiel confirmé par espionnage v1.9 (04/04/2026)
    // 0=PERCEPTUAL, 1=REL_COLORIMETRIC, 2=SATURATION, 3=ABS_COLORIMETRIC,
    // 4=NONE, 5=RGB_WORKING_SPACE, 6=CUSTOM_WORKING_SPACE,
    // 7=STANDARD_DISPLAY_COLOR_MODE, 8=EXTENDED_DISPLAY_COLOR_MODE
    public const uint CPST_STANDARD_DISPLAY_COLOR_MODE = 7;     // SDR
    public const uint CPST_EXTENDED_DISPLAY_COLOR_MODE = 8;     // HDR / Advanced Color

    /// <summary>
    /// Définit un profil installé comme profil PAR DÉFAUT pour un écran.
    /// C'est cette API qui force réellement le changement (pas juste l'ajout à la liste).
    /// </summary>
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int ColorProfileSetDisplayDefaultAssociation(
        uint scope,
        [MarshalAs(UnmanagedType.LPWStr)] string profileName,
        uint profileType,
        uint profileSubType,
        LUID targetAdapterID,
        uint sourceID
    );

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int ColorProfileRemoveDisplayAssociation(
        uint scope,
        [MarshalAs(UnmanagedType.LPWStr)] string profileName,
        LUID targetAdapterID,
        uint sourceID,
        [MarshalAs(UnmanagedType.Bool)] bool dissociateAdvancedColor
    );

    /// <summary>
    /// Récupère le profil couleur par défaut associé à un périphérique d'affichage.
    /// </summary>
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WcsGetDefaultColorProfileSize(
        uint scope,
        [MarshalAs(UnmanagedType.LPWStr)] string? pDeviceName,
        uint dwProfileType,
        uint dwProfileID,
        uint dwProfileMode,
        out uint pcbProfileName
    );

    /// <summary>
    /// Récupère le nom du profil par défaut pour un écran et un subtype donné.
    /// C'est la lecture inverse de SetDisplayDefaultAssociation.
    /// </summary>
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int ColorProfileGetDisplayDefault(
        uint scope,
        LUID targetAdapterID,
        uint sourceID,
        uint profileType,
        uint profileSubType,
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder? profileName,
        ref uint profileNameSize
    );

    /// <summary>
    /// Récupère la liste des profils associés à un écran.
    /// </summary>
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int ColorProfileGetDisplayList(
        uint scope,
        LUID targetAdapterID,
        uint sourceID,
        [Out] IntPtr profileList,
        ref uint profileCount
    );

    // =========================================================================
    // NOTIFICATION — Forcer Windows à relire le registre ICM
    // =========================================================================

    public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        string? lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam
    );

    /// <summary>
    /// Ancienne API WCS : définit le profil par défaut via device name (\\.\DISPLAY1).
    /// Peut déclencher le rafraîchissement interne que les nouvelles API ne font pas.
    /// </summary>
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WcsSetDefaultColorProfile(
        uint scope,
        [MarshalAs(UnmanagedType.LPWStr)] string? pDeviceName,
        uint cptColorProfileType,
        uint cpstColorProfileSubType,
        uint dwProfileID,
        [MarshalAs(UnmanagedType.LPWStr)] string pProfileName
    );

    // WcsSetDefaultColorProfile constants
    public const uint COLORPROFILETYPE_ICC = 1;  // ICC profile
    public const uint COLORPROFILESUBTYPE_NONE = 4;  // CPST_NONE

    // =========================================================================
    // CODES D'ERREUR Windows
    // =========================================================================

    /// <summary>Opération réussie.</summary>
    public const int ERROR_SUCCESS = 0;

    /// <summary>Tampon insuffisant, réessayer avec la taille demandée.</summary>
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>
    /// Vérifie si un code de retour indique un succès.
    /// </summary>
    public static bool EstSuccès(int code) => code == ERROR_SUCCESS;
}
