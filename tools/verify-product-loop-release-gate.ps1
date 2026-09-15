[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$ExecutablePath = '',
    [string]$SeedDllPath = '',
    [string]$DatabasePath = '',
    [string]$GodotExecutablePath = '',
    [ValidateSet('codex', 'opencode')]
    [string]$WorkerProvider = 'codex',
    [string]$WorkerModel = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$demoArgs = @(
    '-NoLogo',
    '-NoProfile',
    '-File',
    (Join-Path $repo 'tools\validate-product-loop-demo.ps1'),
    '-RequireGodot'
)
if (-not [string]::IsNullOrWhiteSpace($GodotExecutablePath)) {
    $demoArgs += @('-GodotExecutablePath', $GodotExecutablePath)
}

& pwsh @demoArgs
if ($LASTEXITCODE -ne 0) {
    throw "Godot product loop fixture validation failed with exit code $LASTEXITCODE."
}

$liveArgs = @(
    '-NoLogo',
    '-NoProfile',
    '-File',
    (Join-Path $repo 'tools\verify-product-loop-ui-real-codex.ps1')
)
foreach ($pair in @(
    @('-ProjectPath', $ProjectPath),
    @('-ExecutablePath', $ExecutablePath),
    @('-SeedDllPath', $SeedDllPath),
    @('-DatabasePath', $DatabasePath),
    @('-WorkerProvider', $WorkerProvider),
    @('-WorkerModel', $WorkerModel)
)) {
    if (-not [string]::IsNullOrWhiteSpace($pair[1])) {
        $liveArgs += $pair
    }
}

& pwsh @liveArgs
if ($LASTEXITCODE -ne 0) {
    throw "Real Codex desktop product loop failed with exit code $LASTEXITCODE."
}

Write-Output 'ProductLoopReleaseGate=Passed'
Write-Output 'KernelFreeze=Acceptance Spine, Authority, AcceptedProjectState, Recovery'
Write-Output "CertifiedPath=Godot fixture + $WorkerProvider Worker + UI Accept + process restart"
