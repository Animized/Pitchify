# Pitchify

Pitchify adds a semitone slider to Spotify Desktop through Spicetify. It shifts local Spotify audio from **-12 to +12 semitones** while preserving the song's tempo.

Spotify does not expose decoded song audio or pitch control to Spicetify. Pitchify therefore has two cooperating parts:

1. A Spicetify extension that adds the slider to the left side of the player bar.
2. A local Windows helper that captures Spotify through VB-CABLE, processes stereo audio, and sends it to your speakers or headphones.

Pitchify is Windows-only and affects playback on **This computer**. Spotify Connect playback on a phone, television, console, or smart speaker cannot be processed.

## Features

- Inline playbar slider from -12 to +12 semitones.
- Tempo-preserving stereo pitch shifting.
- Persistent pitch and output-device selection.
- One-click, SHA-256-verified updates from GitHub Releases.
- Automatic recovery when Windows audio devices change.

## Requirements

- Windows 10 or Windows 11 x64.
- Spotify Desktop with [Spicetify](https://spicetify.app/) installed.
- [.NET 8 Desktop/ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- [VB-CABLE](https://vb-audio.com/Cable/) installed, followed by a Windows restart.

VB-CABLE is a separate donationware audio driver. It is not bundled with Pitchify.

## Install

1. Install: https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe
and https://aka.ms/dotnet/8.0/aspnetcore-runtime-win-x64.exe
2. Install Spicetify.
3. Install VB-CABLE from its official website and restart Windows.
4. Extract `Pitchify-win-x64.zip`.
5. Open PowerShell in the extracted folder.
<img width="1064" height="248" alt="image" src="https://github.com/user-attachments/assets/fd3f1f1d-4435-4a00-8697-62f90a509702" />

6. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

7. Start Spotify and play a song.
8. Open **Settings > System > Sound > Volume mixer**.
9. Find Spotify and set its output to **CABLE Input (VB-Audio Virtual Cable)**.
10. Restart Spotify.

The Pitchify slider appears beside the now-playing information on the left side of the player bar. Double-click the slider to reset it to `0`. Click the **Pitch** label to open output-device and diagnostic settings.

When a newer GitHub Release is available, an **Update to vX.Y.Z** link appears beside the semitone value. Clicking it downloads the official release asset, verifies GitHub's SHA-256 digest, installs it, and reconnects the helper automatically.

## Audio routing

When Spotify is routed to CABLE Input, the Pitchify helper is responsible for forwarding its audio. Spotify will be silent if the helper is stopped.

To recover, either:

- Restart `%LOCALAPPDATA%\Pitchify\helper\Pitchify.Helper.exe`; or
- Change Spotify's Volume mixer output back to **Default**.

Only Spotify should be routed to CABLE Input. Other Windows applications continue using their normal outputs.

## Build from source

Node.js 20 or newer is required. A system-wide .NET SDK is optional; the build script downloads a workspace-local .NET 8 SDK when no SDK is installed.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\package.ps1 -SkipBuild
```

Build outputs:

- `dist\pitchify.template.js`
- `dist\helper\`
- `release\Pitchify-win-x64.zip`

Run the test suites directly:

```powershell
npm.cmd test
.\.tools\dotnet\dotnet.exe test .\helper\Pitchify.Helper.Tests\Pitchify.Helper.Tests.csproj
```

## Local API

The helper listens only on `http://127.0.0.1:38123`. Every request requires the random bearer token generated during installation.

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/v1/status` | Current pitch, pipeline, devices, and latency |
| `PUT` | `/v1/pitch` | Set `{ "semitones": 0 }` |
| `PUT` | `/v1/output` | Set `{ "deviceId": null }` to follow Windows |
| `POST` | `/v1/restart` | Rebuild the audio pipeline |
| `POST` | `/v1/update/check` | Check the configured GitHub repository |
| `POST` | `/v1/update` | Install the verified available update |

Browser requests are accepted only from Spotify's `https://xpui.app.spotify.com` origin. The installed extension contains its generated token; the repository template does not.

Automatic updates are enabled only in packages built by GitHub Actions, which injects the repository name into `release-info.json`. Update ZIPs are accepted only from the configured public GitHub repository and must match the SHA-256 digest returned by GitHub's Releases API.

## Troubleshooting

- **Pitchify says setup required:** Confirm VB-CABLE is installed and enabled, then click the Pitch label and restart the audio pipeline.
- **No Spotify audio:** Confirm the helper is running and Spotify—not the whole system—is routed to CABLE Input.
- **Wrong output device:** Click the Pitch label and select another output, or choose **Follow Windows default**.
- **Spotify is playing elsewhere:** Transfer playback to **This computer**.
- **Slider is missing:** Run `spicetify apply`, then fully restart Spotify.
- **Logs:** `%LOCALAPPDATA%\Pitchify\logs\pitchify.log`.

When reporting audio glitches, include the log, audio device names, Spotify version, Spicetify version, pitch value, and whether the problem also occurs at `0 st`.

## Uninstall

Run `uninstall.ps1` from the extracted release folder. It removes the helper, startup entry, configuration, and Spicetify extension. It intentionally leaves VB-CABLE installed.

## License

Pitchify is licensed under the [MIT License](LICENSE). Third-party components retain their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
