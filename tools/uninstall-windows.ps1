[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\AI Game Workbench')
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = Resolve-Path -LiteralPath $InstallRoot -ErrorAction SilentlyContinue
if ($null -eq $resolvedRoot) {
    Write-Output 'AI Game Workbench is not installed.'
    exit 0
}
$uninstaller = Join-Path $resolvedRoot.Path 'uninstall-windows.ps1'
if (-not (Test-Path -LiteralPath $uninstaller)) {
    throw "Installed uninstaller was not found: $uninstaller"
}
& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $uninstaller
