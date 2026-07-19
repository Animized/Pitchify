[CmdletBinding()]
param(
    [switch]$Silent,
    [int]$WaitForProcessId = 0
)

$ErrorActionPreference = "Stop"
$productName = "Pitchify"
$productVersion = "1.2.1"
$helperProcessName = "Pitchify.Helper"
$sourceHelper = Join-Path $PSScriptRoot "helper"
$extensionTemplate = Join-Path $PSScriptRoot "pitchify.template.js"
$releaseInfoPath = Join-Path $PSScriptRoot "release-info.json"
$installDirectory = Join-Path $env:LOCALAPPDATA $productName
$configPath = Join-Path $installDirectory "config.json"
$helperDirectory = Join-Path $installDirectory "helper\$productVersion"
$helperExecutable = Join-Path $helperDirectory "Pitchify.Helper.exe"
$apiUrl = "http://127.0.0.1:38123"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

function Assert-Prerequisites {
    if (-not (Get-Command spicetify -ErrorAction SilentlyContinue)) {
        throw "Spicetify was not found on PATH. Install Spicetify before Pitchify."
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET 8 and ASP.NET Core 8 runtimes are required: https://dotnet.microsoft.com/download/dotnet/8.0"
    }

    $runtimes = @(& dotnet --list-runtimes 2>$null)
    $requiredFrameworks = @(
        "Microsoft.NETCore.App",
        "Microsoft.AspNetCore.App"
    )
    foreach ($framework in $requiredFrameworks) {
        if (-not ($runtimes | Where-Object { $_ -match "^$([regex]::Escape($framework)) 8\." })) {
            throw ".NET 8 or ASP.NET Core 8 runtime is missing: https://dotnet.microsoft.com/download/dotnet/8.0"
        }
    }

    if (-not (Test-Path -LiteralPath $sourceHelper) -or
        -not (Test-Path -LiteralPath (Join-Path $sourceHelper "Pitchify.Helper.exe"))) {
        throw "The release helper files are missing. Extract the complete Pitchify release first."
    }

    if (-not (Test-Path -LiteralPath $extensionTemplate)) {
        throw "pitchify.template.js is missing from the release."
    }

    $cable = Get-PnpDevice -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FriendlyName -match "VB-Audio Virtual Cable|CABLE Input|CABLE Output"
        } |
        Select-Object -First 1
    if (-not $cable) {
        throw @"
VB-CABLE is required but was not detected.

Install it from https://vb-audio.com/Cable/, reboot Windows, then run this installer again.
No Pitchify files or settings have been changed.
"@
    }
}

function New-ApiToken {
    $bytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return ([System.BitConverter]::ToString($bytes)).Replace("-", "").ToLowerInvariant()
}

function Test-UpdateRepository([string]$repository) {
    return -not [string]::IsNullOrWhiteSpace($repository) -and
        $repository -match "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$" -and
        $repository -ne "__PITCHIFY_GITHUB_REPOSITORY__"
}

function Stop-PitchifyHelper {
    $runningHelpers = @(Get-Process -Name $helperProcessName -ErrorAction SilentlyContinue)
    foreach ($helper in $runningHelpers) {
        try {
            $helper.Kill()
            if (-not $helper.WaitForExit(10000)) {
                throw "Pitchify Helper process $($helper.Id) did not stop within 10 seconds."
            }
        }
        catch [System.InvalidOperationException] {
            # The process exited between discovery and shutdown.
        }
    }

    $remainingHelpers = @(Get-Process -Name $helperProcessName -ErrorAction SilentlyContinue)
    if ($remainingHelpers.Count -gt 0) {
        throw "Pitchify Helper is still running. End it in Task Manager, then run the installer again."
    }
}

function Stop-SpotifyClient {
    $runningSpotify = @(Get-Process -Name "spotify" -ErrorAction SilentlyContinue)
    foreach ($spotify in $runningSpotify) {
        try {
            $spotify.Kill()
            if (-not $spotify.WaitForExit(10000)) {
                throw "Spotify process $($spotify.Id) did not stop within 10 seconds."
            }
        }
        catch [System.InvalidOperationException] {
            # The process exited between discovery and shutdown.
        }
    }

    if (Get-Process -Name "spotify" -ErrorAction SilentlyContinue) {
        throw "Spotify is still running. Close it and run the Pitchify installer again."
    }
}

function Apply-Spicetify {
    Stop-SpotifyClient

    & spicetify -n apply
    if ($LASTEXITCODE -ne 0) {
        Write-Host (
            "Spotify changed since the last Spicetify backup; rebuilding the backup."
        ) -ForegroundColor Yellow
        & spicetify -n backup apply
        if ($LASTEXITCODE -ne 0) {
            throw "Spicetify could not patch the installed Spotify version."
        }
    }

    $spotifyPath = (& spicetify config spotify_path).Trim()
    $spotifyExecutable = Join-Path $spotifyPath "Spotify.exe"
    $patchedWrapper = Join-Path $spotifyPath "Apps\xpui\helper\spicetifyWrapper.js"
    $staleArchive = Join-Path $spotifyPath "Apps\xpui.spa"
    if (-not (Test-Path -LiteralPath $patchedWrapper) -or
        (Test-Path -LiteralPath $staleArchive)) {
        throw @"
Spicetify did not finish patching Spotify.

Close Spotify, run 'spicetify backup apply', then run the Pitchify installer again.
"@
    }

    Start-Process -FilePath $spotifyExecutable
}

function Copy-HelperWithRetry {
    New-Item -ItemType Directory -Force -Path $helperDirectory | Out-Null
    $maximumAttempts = 20
    for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
        try {
            Copy-Item `
                -Path (Join-Path $sourceHelper "*") `
                -Destination $helperDirectory `
                -Recurse `
                -Force
            return
        }
        catch {
            if ($attempt -eq $maximumAttempts) {
                throw
            }

            Start-Sleep -Milliseconds 250
        }
    }
}

if ($WaitForProcessId -gt 0) {
    Wait-Process `
        -Id $WaitForProcessId `
        -Timeout 20 `
        -ErrorAction SilentlyContinue
}

Assert-Prerequisites

$existingConfig = $null
if (Test-Path -LiteralPath $configPath) {
    try {
        $existingConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    }
    catch {
        $existingConfig = $null
    }
}

$token = if ($existingConfig.apiToken) { [string]$existingConfig.apiToken } else { New-ApiToken }
$semitones = if ($null -ne $existingConfig.semitones) { [int]$existingConfig.semitones } else { 0 }
$outputDeviceId = if ($existingConfig.outputDeviceId) { [string]$existingConfig.outputDeviceId } else { $null }
$updateRepository = if ($existingConfig.updateRepository) {
    [string]$existingConfig.updateRepository
}
else {
    $null
}
if (Test-Path -LiteralPath $releaseInfoPath) {
    try {
        $releaseInfo = Get-Content -LiteralPath $releaseInfoPath -Raw |
            ConvertFrom-Json
        if (Test-UpdateRepository ([string]$releaseInfo.repository)) {
            $updateRepository = [string]$releaseInfo.repository
        }
    }
    catch {
        # A malformed optional update manifest must not block installation.
    }
}

Stop-PitchifyHelper
Copy-HelperWithRetry

$config = [ordered]@{
    apiToken = $token
    semitones = [Math]::Max(-12, [Math]::Min(12, $semitones))
    outputDeviceId = $outputDeviceId
    updateRepository = $updateRepository
}
$configJson = $config | ConvertTo-Json
[System.IO.File]::WriteAllText($configPath, $configJson)

$spicetifyConfig = (& spicetify -c).Trim()
if (-not $spicetifyConfig) {
    throw "Spicetify did not return its configuration path."
}
$spicetifyRoot = Split-Path -Parent $spicetifyConfig
$extensionsDirectory = Join-Path $spicetifyRoot "Extensions"
$installedExtension = Join-Path $extensionsDirectory "pitchify.js"
New-Item -ItemType Directory -Force -Path $extensionsDirectory | Out-Null

$extension = [System.IO.File]::ReadAllText($extensionTemplate)
$extension = $extension.Replace("__PITCHIFY_BASE_URL__", $apiUrl)
$extension = $extension.Replace("__PITCHIFY_TOKEN__", $token)
[System.IO.File]::WriteAllText($installedExtension, $extension)

New-Item -Path $runKey -Force | Out-Null
$runCommand = '"' + $helperExecutable + '"'
New-ItemProperty -Path $runKey -Name $productName -Value $runCommand -PropertyType String -Force | Out-Null

& spicetify config extensions "pitchify.js"
Apply-Spicetify

Start-Process -FilePath $helperExecutable -WorkingDirectory $helperDirectory -WindowStyle Hidden

if (-not $Silent) {
    Write-Host ""
    Write-Host "Pitchify installed successfully." -ForegroundColor Green
    Write-Host ""
    Write-Host "One manual routing step remains:" -ForegroundColor Yellow
    Write-Host "1. Start Spotify and play a song."
    Write-Host "2. Open Settings > System > Sound > Volume mixer."
    Write-Host "3. Set Spotify's output device to CABLE Input (VB-Audio Virtual Cable)."
    Write-Host "4. Restart Spotify. The Pitchify slider appears on the left side of the player bar."
}
