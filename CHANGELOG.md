# Changelog

All notable changes to HDR Profile Switcher are documented here.

## [4.4.1] - 2026-04-07

### Changed
- Replaced remaining display/profile examples in source comments with generic wording

## [4.4.0] - 2026-04-07

### Changed
- Replaced bundled user-specific configuration with a generic first-run configuration
- Updated packaging for public distribution with no personal display/profile data
- Refreshed release tooling to derive the package version from the project file

### Removed
- Internal development notes, reverse-engineering artifacts, and dump scripts from the public repository

## [4.3.9] - 2026-04-07

### Fixed
- More reliable profile switching during display topology changes and HDR/SDR transitions
- Preserved configuration entries for displays that are temporarily disconnected
- Corrected application version metadata in published builds and About dialog

### Changed
- Simplified profile application logic to keep only the validated switching method
- Improved logging clarity around active display/config mapping and applied profiles

### Changed
- Replaced bundled user-specific configuration with a generic first-run configuration
- Updated packaging for public distribution with no personal display/profile data
- Refreshed release tooling to derive the package version from the project file

### Removed
- Internal development notes, reverse-engineering artifacts, and dump scripts from the public repository

## [4.2.0] - 2026-04-05

### Added
- **i18n**: Automatic French/English UI based on Windows language
- Custom application icon (monitor with orange→blue gradient)
- GitHub release with packaged zip

### Fixed
- Profile switch now works reliably (Remove → Add → Re-add sequence)
- Scope constant corrected (current_user = 1, not 0)

## [4.1.0] - 2026-04-05

### Removed
- Calibration Loader trigger (unnecessary, API alone works)
- HDR v2 detection spam (cached after first attempt)
- Broken profile spy (corrupted buffer output)

### Improved
- Reduced polling log verbosity (fingerprint-based change detection)

## [4.0.0] - 2026-04-05

### Changed
- **Complete rewrite of profile switching mechanism**
- Uses `ColorProfileAddDisplayAssociation` with correct LUID struct
- Remove → Add(default) → Re-add(non-default) sequence
- Removed all WinRT/DCM reverse-engineering code
- Removed WM_SETTINGCHANGE broadcasts
- Removed SetDisplayConfig fallback

### Validated
- Profile switch confirmed by NVIDIA app (peak luminance 800 vs 1000 nits)
- Works in-game with correct HDR metadata propagation

## [3.x] - 2026-04-04/05

### Explored (dead ends)
- WinRT DisplayColorManagement.dll reverse-engineering
- COM vtable probing (25 slots)
- Ghidra disassembly
- Registry writes + Calibration Loader
- HDR toggle OFF→ON
- Various mscms.dll API parameter combinations

## [1.0-2.0] - 2026-04-04

### Initial
- Basic tray app with display detection
- HDR/SDR state monitoring
- Configuration GUI
- mscms.dll API attempts (all returned S_OK but no effect)
