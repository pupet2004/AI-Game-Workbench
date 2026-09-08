param(
    [string]$SourceProjectPath = 'C:\Users\pupet\Desktop\零刻',
    [string]$SourceDatabasePath = (Join-Path $env:LOCALAPPDATA 'AI Game Workbench\workbench.db'),
    [string]$BaselineDirectory = $null,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $BaselineDirectory) { $BaselineDirectory = Join-Path $repo 'artifacts\demo-baseline' }
$baseline = [IO.Path]::GetFullPath($BaselineDirectory)
$projectSource = [IO.Path]::GetFullPath($SourceProjectPath)
$databaseSource = [IO.Path]::GetFullPath($SourceDatabasePath)
$projectTarget = Join-Path $baseline '零刻'
$databaseTarget = Join-Path $baseline 'workbench.db'

$repoPrefix = $repo.TrimEnd('\') + '\'
if (-not $baseline.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Baseline directory must remain under the repository artifacts directory: $baseline"
}
if (-not (Test-Path -LiteralPath $projectSource -PathType Container)) { throw "Source project not found: $projectSource" }
if (-not (Test-Path -LiteralPath $databaseSource -PathType Leaf)) { throw "Source database not found: $databaseSource" }
$running = @(Get-Process -Name Workbench.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count -gt 0) {
    throw 'Workbench.App is running from this repository. Exit Workbench before capturing a baseline so SQLite WAL state is complete.'
}

if (Test-Path -LiteralPath $baseline) { Remove-Item -LiteralPath $baseline -Recurse -Force }
New-Item -ItemType Directory -Force -Path $projectTarget | Out-Null
Get-ChildItem -LiteralPath $projectSource -Force | Copy-Item -Destination $projectTarget -Recurse -Force

$databaseDirectory = Split-Path -Parent $databaseSource
Get-ChildItem -LiteralPath $databaseDirectory -Filter 'workbench.db*' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $baseline $_.Name) -Force
}

$project = Join-Path $repo 'src\Workbench.App\Workbench.App.csproj'
$runtime = Join-Path $repo 'artifacts\local\demo-bootstrap'
if (-not $NoBuild) {
    if (Test-Path -LiteralPath $runtime) { Remove-Item -LiteralPath $runtime -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $runtime | Out-Null
    & dotnet build $project --configuration Debug --nologo --output $runtime
    if ($LASTEXITCODE -ne 0) { throw "Workbench build failed: $LASTEXITCODE" }
}
$exe = Join-Path $runtime 'Workbench.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { $exe = Join-Path $repo 'src\Workbench.App\bin\Debug\net10.0-windows\Workbench.App.exe' }
if (-not (Test-Path -LiteralPath $exe)) { throw "Workbench executable not found: $exe" }

& $exe '--relocate-demo-project' $projectTarget $databaseTarget '零刻'
if ($LASTEXITCODE -ne 0) { throw "Baseline relocation failed: $LASTEXITCODE" }

$manifest = [ordered]@{
    schema = 'workbench.core-continuity-demo-baseline/v1'
    capturedAt = [DateTimeOffset]::UtcNow.ToString('O')
    sourceProjectPath = $projectSource
    sourceDatabasePath = $databaseSource
    projectPath = $projectTarget
    databasePath = $databaseTarget
    projectName = '零刻'
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $baseline 'baseline.json') -Encoding UTF8
Write-Output "Demo baseline captured: $baseline"
