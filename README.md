# HDR Profile Switcher for Windows 11

Tray utility that automatically applies the correct SDR/HDR ICC color profile per connected display on Windows 11.

## Why?
Windows 11 25H2 has no public API to programmatically switch ICC color profiles. All documented `mscms.dll` APIs return success without actually changing the active profile. This tool reverse-engineers the internal `DisplayColorManagement.dll` WinRT component to call the same APIs that Windows Settings uses.

## Features
- 🖥️ Auto-detect connected displays (Alienware, LG OLED, SudoVGA/Apollo)
- 🔄 Auto-switch SDR/HDR profiles when HDR state changes
- 🎨 Per-display profile configuration via GUI
- 📊 System tray icon showing current screen + mode
- 📝 Detailed logging for troubleshooting

## Supported Displays
- **Alienware AW3423DWF** — SDR + HDR profile switching
- **LG OLED TV** — HDR-only
- **SudoVGA/Apollo virtual displays** — HDR-only (Steam Deck streaming)

## Technical Details
Uses undocumented Windows internal WinRT APIs:
- `Windows.Internal.Graphics.Display.DisplayColorManagement.DisplayColorManagementServer`
- `Windows.Internal.Graphics.Display.DisplayColorManagement.DisplayColorManagement`
- Raw COM vtable calls to `GetColorManagerForDisplayAsync` and the Apply method

See [PLAN.md](PLAN.md) for full reverse-engineering documentation.

## Build
Requires .NET 10 SDK:
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Status
🚧 **Work in progress** — Core WinRT chain working, profile setter identification in progress.

## Project Structure
```
HdrProfileSwitcher.csproj  — Main tray application
Program.cs                 — Entry point
DisplayService.cs          — Display detection + profile management
NativeApis.cs              — P/Invoke definitions
DisplayMonitor.cs          — Monitor data model
ProfileWatcher.cs          — HDR state change detection
AppConfig.cs               — Configuration management
ConfigForm.cs              — Settings GUI
TrayIconManager.cs         — System tray icon
Logger.cs                  — Logging
TargetProbe/               — WinRT vtable probing tool
DllMetaDump/               — DLL metadata extraction tool
WinmdDump/                 — WinMD metadata scanner
dump*.ps1                  — PowerShell probing scripts
```
