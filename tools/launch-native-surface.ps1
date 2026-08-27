$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Workbench.App\Workbench.App.csproj'
$runtimeDirectory = Join-Path $repo 'src\Workbench.App\bin\Debug\net10.0-windows'
$executable = Join-Path $runtimeDirectory 'Workbench.App.exe'

if (-not (Test-Path -LiteralPath $project)) {
    throw "Workbench project not found: $project"
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
Push-Location $repo
try {
    & dotnet build $project --configuration Debug --nologo
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
