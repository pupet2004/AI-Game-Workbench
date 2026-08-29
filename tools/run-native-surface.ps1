param(
    [switch]$NoBuild,
    [switch]$SkipLaunchProfile
)

$ErrorActionPreference = 'Stop'

# Resolve the repository from this script so the current directory does not matter.
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Workbench.App\Workbench.App.csproj'
$runtimeDirectory = Join-Path $repo 'artifacts\local\native-surface'
$publishedExecutable = Join-Path $runtimeDirectory 'Workbench.App.exe'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Workbench project not found: $project"
}

$repoPrefix = $repo.TrimEnd('\') + '\'
$running = @(Get-Process -Name 'Workbench.App' -ErrorAction SilentlyContinue | Where-Object {
    try {
        # Native Surface runs from artifacts\local\run-*; Debug runs may
        # still run from src\Workbench.App\bin. Treat both as this repo so
        # a hidden tray instance cannot make a rebuild appear ineffective.
        $_.Path -and $_.Path.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        $false
    }
})
if ($running.Count -gt 0) {
    foreach ($process in $running) {
        Write-Output ("Workbench.App is already running (PID {0}, started {1}, path {2}). Close it from the tray menu (退出 Workbench) before starting a rebuilt Native Surface instance." -f $process.Id, $process.StartTime, $process.Path)
    }
    exit 0
}

$env:WORKBENCH_NATIVE_AGENT_SURFACE = '1'

if (-not $NoBuild) {
    $runtimeDirectory = Join-Path $repo ('artifacts\local\run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    $publishedExecutable = Join-Path $runtimeDirectory 'Workbench.App.exe'
    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    Push-Location $repo
    try {
        & dotnet build $project --configuration Debug --nologo --output $runtimeDirectory
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
