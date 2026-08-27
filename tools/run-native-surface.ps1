param(
    [switch]$NoBuild,
    [switch]$SkipLaunchProfile
)

$ErrorActionPreference = 'Stop'

# Resolve the repository from this script so the current directory does not matter.
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Workbench.App\Workbench.App.csproj'
$runtimeDirectory = Join-Path $repo 'src\Workbench.App\bin\Debug\net10.0-windows'
$publishedExecutable = Join-Path $runtimeDirectory 'Workbench.App.exe'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Workbench project not found: $project"
}

$running = @(Get-Process -Name 'Workbench.App' -ErrorAction SilentlyContinue | Where-Object {
    try {
        $_.Path -and $_.Path.StartsWith((Join-Path $repo 'src\Workbench.App\bin\'), [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        $false
    }
})
if ($running.Count -gt 0) {
    Write-Output 'Workbench.App is already running. Close it before starting a rebuilt Native Surface instance.'
    exit 0
}

$env:WORKBENCH_NATIVE_AGENT_SURFACE = '1'

if (-not $NoBuild) {
    Push-Location $repo
    try {
        & dotnet build $project --configuration Debug --nologo
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Native Surface executable not found: $publishedExecutable"
}

Push-Location $runtimeDirectory
try {
    & $publishedExecutable
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
