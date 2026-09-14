[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectPath = '',

    [Parameter(Mandatory = $false)]
    [string]$ExecutablePath = '',

    [switch]$KeepSecondProcess
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repo 'demos\product-loop-godot-counter'
}
$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedProject 'project.godot') -PathType Leaf)) {
    throw "The product loop project does not contain project.godot: $resolvedProject"
}

$publishDirectory = $null
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $publishDirectory = Join-Path $repo ('artifacts\local\product-loop-restart-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
    & dotnet build (Join-Path $repo 'src\Workbench.App\Workbench.App.csproj') `
        --configuration Debug `
        --nologo `
        --output $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Workbench build failed with exit code $LASTEXITCODE."
    }
    $ExecutablePath = Join-Path $publishDirectory 'Workbench.App.exe'
}

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$executableDirectory = Split-Path -Parent $resolvedExecutable
$repoPrefix = $repo.TrimEnd('\') + '\'

function Start-WorkbenchProcess {
    param(
        [string]$Executable,
        [string]$WorkingDirectory,
        [string]$TargetProject
    )

    $process = Start-Process `
        -FilePath $Executable `
        -WorkingDirectory $WorkingDirectory `
        -ArgumentList @('--project', $TargetProject) `
        -PassThru

    $deadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) {
            throw "Workbench exited during startup with code $($process.ExitCode)."
        }
    } while ((Get-Date) -lt $deadline)

    return $process
}

function Stop-VerifiedProcess {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ExpectedExecutable,
        [string]$RepositoryRoot
    )

    $Process.Refresh()
    if ($Process.HasExited) {
        return
    }

    $processPath = $Process.Path
    if ([string]::IsNullOrWhiteSpace($processPath)) {
        throw "Cannot verify the Workbench process path before stopping PID $($Process.Id)."
    }

    $resolvedProcessPath = (Resolve-Path -LiteralPath $processPath).Path
    $resolvedExpectedPath = (Resolve-Path -LiteralPath $ExpectedExecutable).Path
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($resolvedProcessPath, $resolvedExpectedPath)) {
        throw "Refusing to stop unexpected process path: $resolvedProcessPath"
    }
    if (-not $resolvedProcessPath.StartsWith($RepositoryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to stop a process outside the Workbench repository: $resolvedProcessPath"
    }

    Stop-Process -Id $Process.Id -Force
    $Process.WaitForExit(10000) | Out-Null
}

$first = $null
$second = $null
try {
    $first = Start-WorkbenchProcess $resolvedExecutable $executableDirectory $resolvedProject
    Write-Output "First Workbench process started: PID $($first.Id)"

    Stop-VerifiedProcess $first $resolvedExecutable $repo
    Write-Output 'First Workbench process stopped.'

    $second = Start-WorkbenchProcess $resolvedExecutable $executableDirectory $resolvedProject
    Write-Output "Second Workbench process started with the same project: PID $($second.Id)"
}
finally {
    if ($null -ne $first) {
        Stop-VerifiedProcess $first $resolvedExecutable $repo
    }
    if ($null -ne $second -and -not $KeepSecondProcess) {
        Stop-VerifiedProcess $second $resolvedExecutable $repo
    }
}

Write-Output 'Product loop process restart harness passed.'
