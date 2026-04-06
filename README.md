# HDR Profile Switcher

A Windows tray utility that automatically applies the correct ICC color profile (SDR/HDR) to each connected display.

## The Problem

Windows 11 doesn't properly switch ICC color profiles when you toggle HDR, connect a TV, or change displays. You have to manually go to Settings → Display → Color Profile every time.

## The Solution

HDR Profile Switcher runs silently in the system tray and:
- **Detects** connected displays and their HDR/SDR state in real-time
- **Applies** the configured ICC profile (SDR or HDR) per display automatically
- **Monitors** display changes (new display connected, HDR toggled) every 5 seconds
- **Provides** a GUI to configure profiles per display (right-click tray → Settings)

## System Requirements

- Windows 10 version 2004 (20H1) or later
- GPU with WDDM 2.6+ driver:
  - NVIDIA GeForce GTX 10xx (Pascal) or later
  - AMD Radeon RX 400/500 Series or later
  - Intel 10th Gen (Ice Lake) or later
- ICC profiles installed in `C:\Windows\System32\spool\drivers\color\`

## Installation

1. Download the latest release from [Releases](https://github.com/funkee69/win-hdr-fix/releases)
2. Extract the zip to a folder of your choice
3. Run `HdrProfileSwitcher.exe`
4. On first launch, open **Settings** and assign SDR/HDR profiles to your displays
5. (Optional) Enable "Start with Windows" in Settings

No ICC profiles are bundled with the application. You must assign your own installed profiles.
A generic `config.example.json` file is included for reference.

## Usage

The tray icon displays:
- A letter identifying the primary display
- Orange/gold = HDR active, Blue/gray = SDR active

Right-click the tray icon for:
- **Settings** — assign SDR and HDR profiles per display
- **Open log** — view actions and debug info
- **About** — version information
- **Quit** — close the application

## How It Works

The application uses the Windows `ColorProfileAddDisplayAssociation` API with the correct adapter LUID obtained from `QueryDisplayConfig`. This is the official Microsoft mechanism for programmatic ICC profile management.

The key discovery: profiles must be **removed** then **re-added as default** in sequence for the switch to take effect. Simply calling the API with `setAsDefault=true` alone returns success but doesn't actually switch the profile.

### Technical Flow
```
1. QueryDisplayConfig → adapter LUID + source ID
2. Remove conflicting profile associations
3. Remove the target profile association
4. Add the target profile as default (setAsDefault=true, advancedColor=true/false)
5. Re-add the other profiles as non-default
```

## Supported Profile Types

- **HDR profiles** with MHC2 tags (Microsoft Hardware Calibration v2)
- **SDR profiles** — standard ICC (.icc/.icm) files
- Calibration profiles from DisplayCAL, rtings, SpectraCal, etc.

## Language

The application automatically detects your Windows language:
- **French** Windows → French UI
- **Other** languages → English UI

Logs are always in English for universal debugging.

## License

MIT
