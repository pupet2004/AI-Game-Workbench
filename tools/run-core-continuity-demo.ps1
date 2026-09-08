param(
    [string]$BaselineDirectory = $null,
    [string]$RunDirectory = $null,
    [switch]$Resume,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $BaselineDirectory) { $BaselineDirectory = Join-Path $repo 'artifacts\demo-baseline' }
$baseline = [IO.Path]::GetFullPath($BaselineDirectory)
$runsRoot = Join-Path $repo 'artifacts\demo-runs'
if (-not $RunDirectory) { $RunDirectory = Join-Path $runsRoot ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$run = [IO.Path]::GetFullPath($RunDirectory)
$repoPrefix = $repo.TrimEnd('\') + '\'
if (-not $run.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Run directory must remain under the repository: $run" }
if (-not (Test-Path -LiteralPath (Join-Path $baseline 'baseline.json'))) {
    throw "Demo baseline is missing. Run prepare-core-continuity-demo-baseline.ps1 first."
}
$running = @(Get-Process -Name Workbench.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count -gt 0) { throw 'Workbench.App is already running from this repository. Exit it before starting the demo.' }

if (-not $Resume) {
    if (Test-Path -LiteralPath $run) { Remove-Item -LiteralPath $run -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $run | Out-Null
    $project = Join-Path $run '零刻'
    New-Item -ItemType Directory -Force -Path $project | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $baseline '零刻') -Force | Copy-Item -Destination $project -Recurse -Force
    Get-ChildItem -LiteralPath $baseline -Filter 'workbench.db*' -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $run $_.Name) -Force
    }
} else {
    if (-not (Test-Path -LiteralPath $run)) { throw "Run directory not found for resume: $run" }
}

$db = Join-Path $run 'workbench.db'
$project = Join-Path $run '零刻'
$projectFile = Join-Path $repo 'src\Workbench.App\Workbench.App.csproj'
$runtime = Join-Path $repo ('artifacts\local\demo-run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (-not $NoBuild) {
    New-Item -ItemType Directory -Force -Path $runtime | Out-Null
    & dotnet build $projectFile --configuration Debug --nologo --output $runtime
    if ($LASTEXITCODE -ne 0) { throw "Workbench build failed: $LASTEXITCODE" }
}
$exe = Join-Path $runtime 'Workbench.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { $exe = Join-Path $repo 'src\Workbench.App\bin\Debug\net10.0-windows\Workbench.App.exe' }
if (-not (Test-Path -LiteralPath $exe)) { throw "Workbench executable not found: $exe" }

$oldDb = $env:WORKBENCH_DATABASE_PATH
$oldProject = $env:WORKBENCH_OPEN_PROJECT_PATH
$oldDemo = $env:WORKBENCH_DEMO_RUN_DIRECTORY
try {
    $env:WORKBENCH_DATABASE_PATH = $db
    $env:WORKBENCH_OPEN_PROJECT_PATH = $project
    $env:WORKBENCH_DEMO_RUN_DIRECTORY = $run
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
} finally {
    $env:WORKBENCH_DATABASE_PATH = $oldDb
    $env:WORKBENCH_OPEN_PROJECT_PATH = $oldProject
    $env:WORKBENCH_DEMO_RUN_DIRECTORY = $oldDemo
}

$manifest = [ordered]@{
    schema = 'workbench.core-continuity-demo-run/v1'
    startedAt = [DateTimeOffset]::UtcNow.ToString('O')
    baselineDirectory = $baseline
    runDirectory = $run
    databasePath = $db
    projectPath = $project
    resume = [bool]$Resume
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'run.json') -Encoding UTF8
Write-Output "Demo started: $run"
Write-Output "Fresh run:  .\tools\run-core-continuity-demo.ps1 -RunDirectory `"$run`""
Write-Output "Resume run: .\tools\run-core-continuity-demo.ps1 -RunDirectory `"$run`" -Resume"
