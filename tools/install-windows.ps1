[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs\AI Game Workbench'),
    [switch]$NoDesktopShortcut,
    [switch]$NoShortcuts
)

$ErrorActionPreference = 'Stop'

function Resolve-ExistingPath([string]$Path, [string]$Description) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -eq $resolved) {
        throw "$Description was not found: $Path"
    }
    return $resolved.Path
}

$packageRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$sourceExecutable = Resolve-ExistingPath (Join-Path $packageRoot 'Workbench.App.exe') 'Workbench package'
$installRoot = [IO.Path]::GetFullPath($InstallRoot)
$parent = [IO.Path]::GetDirectoryName($installRoot)
if ([string]::IsNullOrWhiteSpace($parent)) {
    throw "Install path has no parent directory: $installRoot"
}

if (([IO.Path]::GetPathRoot($installRoot)) -eq $installRoot) {
    throw "Refusing to install directly at a drive root: $installRoot"
}

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
Get-ChildItem -LiteralPath $packageRoot -Force |
    Where-Object { $_.Name -notin @('install-windows.ps1', 'uninstall-windows.ps1') } |
    Copy-Item -Destination $installRoot -Recurse -Force

$uninstaller = Join-Path $installRoot 'uninstall-windows.ps1'
@'
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$installRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$shortcutPaths = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'AI Game Workbench.lnk'),
    (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\AI Game Workbench.lnk')
)
foreach ($shortcut in $shortcutPaths) {
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
}
$running = @(Get-Process -Name 'Workbench.App' -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -and $_.Path.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase) }
    catch { $false }
})
if ($running.Count -gt 0) {
    throw 'AI Game Workbench is running. Exit it from the system tray before uninstalling.'
}
Remove-Item -LiteralPath $installRoot -Recurse -Force
'AI Game Workbench was uninstalled. Project data in LocalAppData was preserved.'
'@ | Set-Content -LiteralPath $uninstaller -Encoding UTF8

$startMenu = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'
if (-not $NoShortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
    foreach ($shortcut in @(
        (Join-Path $startMenu 'AI Game Workbench.lnk'),
        $(if (-not $NoDesktopShortcut) { Join-Path ([Environment]::GetFolderPath('Desktop')) 'AI Game Workbench.lnk' })
    )) {
        if ([string]::IsNullOrWhiteSpace($shortcut)) { continue }
        $link = $shell.CreateShortcut($shortcut)
        $link.TargetPath = $sourceExecutable.Replace($packageRoot, $installRoot)
        $link.WorkingDirectory = $installRoot
        $link.Description = 'AI Game Workbench'
        $link.Save()
    }
}

Write-Output "Installed AI Game Workbench to $installRoot"
if (-not $NoShortcuts) {
    Write-Output "Start menu shortcut: $(Join-Path $startMenu 'AI Game Workbench.lnk')"
    if (-not $NoDesktopShortcut) {
        Write-Output "Desktop shortcut: $(Join-Path ([Environment]::GetFolderPath('Desktop')) 'AI Game Workbench.lnk')"
    }
}
