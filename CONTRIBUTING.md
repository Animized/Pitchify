# Contributing

Issues and pull requests are welcome.

## Development setup

You need Windows 10/11, Node.js 20+, and either the .NET 8 SDK or network access for the build script to install a workspace-local SDK.

```powershell
npm.cmd ci
npm.cmd run check
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

VB-CABLE and Spotify are required only for live end-to-end testing.

## Pull requests

- Keep the helper loopback-only and preserve bearer-token authentication.
- Do not commit generated tokens, installed extension copies, logs, or build output.
- Add tests for validation, buffering, DSP, or UI logic changes.
- Run both test suites before opening a pull request.
- Describe the Windows audio devices used for live testing.

## Audio changes

Live capture and playback devices use independent hardware clocks. Changes must keep buffers bounded without periodically dropping fresh audio or allowing stale audio to accumulate. Avoid allocations, file I/O, logging, and device enumeration on real-time audio callbacks.
