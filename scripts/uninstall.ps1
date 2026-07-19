[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$productName = "Pitchify"
$helperProcessName = "Pitchify.Helper"
$installDirectory = Join-Path $env:LOCALAPPDATA $productName
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Get-Process -Name $helperProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path $runKey -Name $productName -ErrorAction SilentlyContinue

$spicetify = Get-Command spicetify -ErrorAction SilentlyContinue
if ($spicetify) {
    $spicetifyConfig = (& spicetify -c).Trim()
    if ($spicetifyConfig) {
        $spicetifyRoot = Split-Path -Parent $spicetifyConfig
        $installedExtension = Join-Path $spicetifyRoot "Extensions\pitchify.js"
        if (Test-Path -LiteralPath $installedExtension) {
            Remove-Item -LiteralPath $installedExtension -Force
        }

        & spicetify config extensions "pitchify.js-"
        & spicetify apply
    }
}

if (Test-Path -LiteralPath $installDirectory) {
    $resolvedInstall = [System.IO.Path]::GetFullPath($installDirectory)
    $resolvedLocalAppData = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
    if (-not $resolvedInstall.StartsWith($resolvedLocalAppData, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a Pitchify path outside LOCALAPPDATA."
    }
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}

Write-Host "Pitchify was removed." -ForegroundColor Green
Write-Host "VB-CABLE was left installed. Restore Spotify's output to Default in Windows Volume mixer if needed."

