# Changelog

All notable user-facing changes are documented here.

## 1.2.0 - 2026-07-19

### Added

- Inline **Update to vX.Y.Z** link when a newer GitHub Release exists.
- Authenticated local update endpoints.
- Background GitHub Release checks every six hours.
- SHA-256 verification against GitHub's release-asset digest.
- Safe ZIP extraction and silent installer handoff.
- GitHub Actions repository metadata injection.

## 1.1.0 - 2026-07-19

### Added

- Inline pitch slider in Spotify's left now-playing area.
- Device-change notifications instead of recurring endpoint polling.
- High-quality sinc clock-drift resampling.
- Regression tests for buffer overflow and drift correction.
- GitHub Actions build and release workflows.

### Fixed

- Periodic live-audio gaps caused by dropping newly captured audio when the buffer filled.
- PowerShell 5.1 installer compatibility.
- Upgrade failures when Windows briefly retained a lock on an old helper DLL.
- Repeated audio-device enumeration and undisposed endpoint objects.
- Temporary live-input starvation being treated as the permanent end of a file.

### Changed

- Increased SoundTouch anti-alias filter quality.
- Reduced helper status polling to once every 15 seconds.
- Removed the right-side Pitchify playbar icon.

## 1.0.0 - 2026-07-19

- Initial Windows hybrid release.
