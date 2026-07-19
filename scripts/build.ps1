[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $repoRoot ".tools"
$localDotnet = Join-Path $toolsDirectory "dotnet\dotnet.exe"
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$hasSdk = $false

if ($dotnetCommand) {
    $hasSdk = [bool](& $dotnetCommand.Source --list-sdks)
}

if (-not $hasSdk) {
    if (-not (Test-Path -LiteralPath $localDotnet)) {
        New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
        $installer = Join-Path $toolsDirectory "dotnet-install.ps1"
        Invoke-WebRequest `
            -UseBasicParsing `
            -Uri "https://dot.net/v1/dotnet-install.ps1" `
            -OutFile $installer
        & $installer `
            -Channel "8.0" `
            -Quality "GA" `
            -InstallDir (Join-Path $toolsDirectory "dotnet") `
            -NoPath
        if ($LASTEXITCODE -ne 0) {
            throw "The workspace-local .NET SDK installation failed."
        }
    }

    $dotnet = $localDotnet
}
else {
    $dotnet = $dotnetCommand.Source
}

Push-Location $repoRoot
try {
    if (Test-Path -LiteralPath (Join-Path $repoRoot "package-lock.json")) {
        npm.cmd ci
    }
    else {
        npm.cmd install
    }
    if ($LASTEXITCODE -ne 0) {
        throw "npm dependency installation failed."
    }

    npm.cmd run build
    if ($LASTEXITCODE -ne 0) {
        throw "The Spicetify extension build failed."
    }

    & $dotnet restore "helper\Pitchify.Helper\Pitchify.Helper.csproj"
    if ($LASTEXITCODE -ne 0) {
        throw "Helper dependency restore failed."
    }
    & $dotnet restore "helper\Pitchify.Helper.Tests\Pitchify.Helper.Tests.csproj"
    if ($LASTEXITCODE -ne 0) {
        throw "Test dependency restore failed."
    }

    if (-not $SkipTests) {
        npm.cmd test
        if ($LASTEXITCODE -ne 0) {
            throw "Extension tests failed."
        }
        & $dotnet test `
            "helper\Pitchify.Helper.Tests\Pitchify.Helper.Tests.csproj" `
            --configuration $Configuration `
            --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "Helper tests failed."
        }
    }

    $publishDirectory = Join-Path $repoRoot "dist\helper"
    if (Test-Path -LiteralPath $publishDirectory) {
        $resolvedPublish = [System.IO.Path]::GetFullPath($publishDirectory)
        $resolvedRoot = [System.IO.Path]::GetFullPath($repoRoot)
        if (-not $resolvedPublish.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear a publish path outside the repository."
        }
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }

    & $dotnet publish `
        "helper\Pitchify.Helper\Pitchify.Helper.csproj" `
        --configuration $Configuration `
        --self-contained false `
        --no-restore `
        --output $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Helper publish failed."
    }

    Write-Host "Build completed successfully." -ForegroundColor Green
}
finally {
    Pop-Location
}
