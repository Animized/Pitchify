[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $repoRoot "release\Pitchify"

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration Release
}

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "dist\helper\Pitchify.Helper.exe"))) {
    throw "The helper has not been published. Run scripts\build.ps1 first."
}

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot "dist\pitchify.template.js"))) {
    throw "The extension has not been built. Run scripts\build.ps1 first."
}

if (Test-Path -LiteralPath $releaseRoot) {
    $resolvedRelease = [System.IO.Path]::GetFullPath($releaseRoot)
    $resolvedRoot = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $resolvedRelease.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear a release path outside the repository."
    }
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $releaseRoot "helper") | Out-Null
Copy-Item -Path (Join-Path $repoRoot "dist\helper\*") -Destination (Join-Path $releaseRoot "helper") -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot "dist\pitchify.template.js") -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install.ps1") -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall.ps1") -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $releaseRoot
Copy-Item -LiteralPath (Join-Path $repoRoot "THIRD_PARTY_NOTICES.md") -Destination $releaseRoot

$repository = [string]$env:GITHUB_REPOSITORY
if ($repository -notmatch "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") {
    $repository = "__PITCHIFY_GITHUB_REPOSITORY__"
}
$releaseInfo = [System.IO.File]::ReadAllText(
    (Join-Path $repoRoot "release-info.template.json"))
$releaseInfo = $releaseInfo.Replace(
    "__PITCHIFY_GITHUB_REPOSITORY__",
    $repository)
[System.IO.File]::WriteAllText(
    (Join-Path $releaseRoot "release-info.json"),
    $releaseInfo)

$archive = Join-Path $repoRoot "release\Pitchify-win-x64.zip"
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}
Compress-Archive -Path (Join-Path $releaseRoot "*") -DestinationPath $archive -CompressionLevel Optimal

Write-Host "Release created at $archive" -ForegroundColor Green
