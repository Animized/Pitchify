# Changelog

All notable user-facing changes are documented here.

## 1.2.5 - 2026-07-19

### Fixed

- Retry release creation, asset upload, and publication when GitHub's release
  service is temporarily unavailable.

## 1.2.4 - 2026-07-19

### Fixed

- Keep automatic updates from closing or relaunching Spotify when the existing
  extension is already installed.
- Always attempt to restart Pitchify Helper if an installer step fails after
  the running helper has been stopped.
- Fall back to the previously installed helper if a new helper executable is
  unavailable after a failed update.

## 1.2.3 - 2026-07-19

### Fixed

- Automatically rebuild the audio pipeline after Windows invalidates an output
  device instead of requiring a manual output toggle.
- Retry failed audio recovery with bounded backoff and retry immediately when
  Windows reports another device change.

### Changed

- Increased the pitch shifter's anti-alias filter from 64 to 128 taps for
  cleaner high-frequency audio during larger pitch shifts.

## 1.2.2 - 2026-07-19

### Fixed

- Use an explicit extension test path so Windows GitHub Actions on Node 20 can
  run the test suite without shell wildcard expansion.

## 1.2.1 - 2026-07-19

### Fixed

- Detect Spotify updates during installation and automatically rebuild the
  Spicetify backup before applying Marketplace and Pitchify.
- Prevent Spotify from reopening early and loading an unpatched `xpui.spa`
  archive instead of Spicetify's generated interface.

### Changed

- Centralized helper runtime version reporting for safer update releases.

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
