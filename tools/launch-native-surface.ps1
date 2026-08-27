$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$runtimeDirectory = Join-Path $repo 'src\Workbench.App\bin\Release\net10.0'
$executable = Join-Path $runtimeDirectory 'Workbench.App.exe'

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Workbench executable not found: $executable"
}

$running = @(Get-Process -Name 'Workbench.App' -ErrorAction SilentlyContinue | Where-Object {
    try {
        $_.Path -and $_.Path.Equals($executable, [StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        $false
    }
})
if ($running.Count -gt 0) {
    exit 0
}

$env:WORKBENCH_NATIVE_AGENT_SURFACE = '1'
Start-Process -FilePath $executable -WorkingDirectory $runtimeDirectory
