param(
    [switch]$NoBuild,
    [switch]$SkipLaunchProfile
)

$ErrorActionPreference = 'Stop'

# Resolve the repository from this script so the current directory does not matter.
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Workbench.App\Workbench.App.csproj'

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

$arguments = @('run', '--project', $project)
if ($NoBuild) {
    $arguments += '--no-build'
}
if ($SkipLaunchProfile) {
    $arguments += '--no-launch-profile'
}

Push-Location $repo
try {
    & dotnet @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
