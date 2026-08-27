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

if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    if ($NoBuild) {
        throw "Native Surface executable not found: $publishedExecutable"
    }

    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    $publishArguments = @(
        'publish', $project,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $runtimeDirectory,
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false'
    )
    Push-Location $repo
    try {
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }
}

Push-Location $runtimeDirectory
try {
    & $publishedExecutable
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
