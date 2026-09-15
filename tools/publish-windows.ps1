[CmdletBinding()]
param(
    [string]$Version = "alpha",
    [switch]$KeepPublishDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $root 'artifacts\release'
$publishDirectory = Join-Path $releaseRoot "AI.Game.Workbench-$Version-win-x64"
$archivePath = Join-Path $releaseRoot "AI.Game.Workbench-$Version-win-x64.zip"

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

dotnet publish (Join-Path $root 'src\Workbench.App\Workbench.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -File | Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-windows.ps1') -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall-windows.ps1') -Destination $publishDirectory
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath
if (-not $KeepPublishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

Write-Output "Created $archivePath"
