[CmdletBinding()]
param(
    [switch]$RequireGodot,
    [string]$GodotExecutablePath = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$demo = Join-Path $repo 'demos\product-loop-godot-counter'
$requiredFiles = @(
    'project.godot',
    'scenes\main.tscn',
    'scripts\main.gd',
    'README.md'
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $demo $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Product loop demo file is missing: $path"
    }
}

$projectText = Get-Content -LiteralPath (Join-Path $demo 'project.godot') -Raw
if ($projectText -notmatch 'run/main_scene="res://scenes/main\.tscn"') {
    throw 'Product loop demo does not point to the expected main scene.'
}

$sceneText = Get-Content -LiteralPath (Join-Path $demo 'scenes\main.tscn') -Raw
if ($sceneText -notmatch 'res://scripts/main\.gd') {
    throw 'Product loop demo scene does not reference the main script.'
}

$scriptText = Get-Content -LiteralPath (Join-Path $demo 'scripts\main.gd') -Raw
if ($scriptText -notmatch 'const CLICK_INCREMENT: int = 1') {
    throw 'Product loop demo baseline must start at +1.'
}

$godotPath = $null
if (-not [string]::IsNullOrWhiteSpace($GodotExecutablePath)) {
    $godotPath = (Resolve-Path -LiteralPath $GodotExecutablePath).Path
}
else {
    $godot = Get-Command godot -ErrorAction SilentlyContinue
    if ($null -ne $godot) {
        $godotPath = $godot.Source
    }
}

if ($null -eq $godotPath) {
    if ($RequireGodot) {
        throw 'Godot executable was not found on PATH.'
    }

    Write-Output 'Product loop demo files are valid. Godot executable not found; runtime smoke test skipped.'
    exit 0
}

& $godotPath --headless --path $demo --quit
if ($LASTEXITCODE -ne 0) {
    throw "Godot headless smoke test failed with exit code $LASTEXITCODE."
}

Write-Output 'Product loop Godot demo passed static and headless validation.'
