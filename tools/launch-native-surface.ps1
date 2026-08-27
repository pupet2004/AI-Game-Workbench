$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Workbench.App\Workbench.App.csproj'
$runRoot = Join-Path $repo 'artifacts\local'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Workbench project not found: $project"
}

$running = @(Get-Process -Name 'Workbench.App' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Output 'Workbench.App is already running. Close it before starting a rebuilt instance.'
    exit 0
}

$env:WORKBENCH_NATIVE_AGENT_SURFACE = '1'
$runtimeDirectory = Join-Path $runRoot ('dev-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$executable = Join-Path $runtimeDirectory 'Workbench.App.exe'
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

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Workbench executable not found after build: $executable"
}

Start-Process -FilePath $executable -WorkingDirectory $runtimeDirectory
