[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$handoffParent = Join-Path $repoRoot "handoff"
$handoffRoot = Join-Path $handoffParent "Pitchify-GitHub-Ready"
$repositoryRoot = Join-Path $handoffRoot "Pitchify"
$releaseArchive = Join-Path $repoRoot "release\Pitchify-win-x64.zip"

if (-not (Test-Path -LiteralPath $releaseArchive)) {
    throw "The release archive is missing. Run scripts\package.ps1 first."
}

$resolvedRepo = [System.IO.Path]::GetFullPath($repoRoot)
$resolvedHandoff = [System.IO.Path]::GetFullPath($handoffRoot)
$resolvedParent = [System.IO.Path]::GetFullPath($handoffParent)
if (-not $resolvedHandoff.StartsWith(
        $resolvedParent,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to recreate a handoff path outside the repository's handoff directory."
}

if (Test-Path -LiteralPath $handoffRoot) {
    Remove-Item -LiteralPath $handoffRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $repositoryRoot | Out-Null

$rootFiles = @(
    ".editorconfig",
    ".gitignore",
    "CHANGELOG.md",
    "CONTRIBUTING.md",
    "LICENSE",
    "package-lock.json",
    "package.json",
    "README.md",
    "release-info.template.json",
    "SECURITY.md",
    "THIRD_PARTY_NOTICES.md",
    "tsconfig.json"
)

foreach ($relativePath in $rootFiles) {
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot $relativePath) `
        -Destination (Join-Path $repositoryRoot $relativePath)
}

$sourceDirectories = @(".github", "extension", "helper", "scripts")
foreach ($directory in $sourceDirectories) {
    $sourceRoot = Join-Path $repoRoot $directory
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | ForEach-Object {
        $relativePath = $_.FullName.Substring($resolvedRepo.Length).TrimStart(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
        $segments = $relativePath -split "[\\/]"
        if ($segments -contains "bin" -or $segments -contains "obj") {
            return
        }

        $destination = Join-Path $repositoryRoot $relativePath
        New-Item `
            -ItemType Directory `
            -Force `
            -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination
    }
}

Copy-Item -LiteralPath $releaseArchive -Destination $handoffRoot

$sourceArchive = Join-Path $handoffRoot "Pitchify-source.zip"
Compress-Archive `
    -Path (Join-Path $repositoryRoot "*") `
    -DestinationPath $sourceArchive `
    -CompressionLevel Optimal

Write-Host "GitHub-ready handoff created at $handoffRoot" -ForegroundColor Green
