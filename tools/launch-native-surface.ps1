[CmdletBinding()]
param([switch]$Rebuild)

$ErrorActionPreference = 'Stop'

function Get-WorkbenchLaunchCandidate {
    param([string]$RunRoot)

    if (-not (Test-Path -LiteralPath $RunRoot -PathType Container)) { return }
    $required = @(
        'Workbench.App.exe', 'Workbench.App.dll', 'Workbench.App.deps.json',
        'Workbench.App.runtimeconfig.json', 'Workbench.Core.dll',
        'Workbench.Runtime.dll', 'Workbench.Storage.dll', 'Workbench.Project.dll',
        'Avalonia.Controls.dll'
    )
    Get-ChildItem -LiteralPath $RunRoot -Directory -Filter 'dev-*' |
        Where-Object {
            $directory = $_.FullName
            $missing = @($required | Where-Object {
                -not (Test-Path -LiteralPath (Join-Path $directory $_) -PathType Leaf)
            })
            $missing.Count -eq 0 -and
                -not (Test-Path -LiteralPath (Join-Path $directory 'build.pending')) -and
                -not (Test-Path -LiteralPath (Join-Path $directory 'build.failed'))
        } |
        Sort-Object { (Get-Item -LiteralPath (Join-Path $_.FullName 'Workbench.App.dll')).LastWriteTimeUtc } -Descending |
        Select-Object -First 1
}

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\Workbench.App\Workbench.App.csproj'
$runRoot = Join-Path $repo 'artifacts\local'

$transcriptStarted = $false
$lock = $null
$lockHeld = $false
try {
    # Serialize repeated shortcut clicks, including while the first build is running.
    $hash = [System.Security.Cryptography.SHA256]::Create()
    try {
        $key = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($repo.ToLowerInvariant()))).Replace('-', '')
    } finally { $hash.Dispose() }
    $lock = [System.Threading.Mutex]::new($false, "Local\WorkbenchLauncher-$key")
    try { $lockHeld = $lock.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $lockHeld = $true }
    if (-not $lockHeld) { return }

    $running = @(Get-Process -Name 'Workbench.App' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($process in $running) {
            if ($process.MainWindowHandle -ne 0 -and $shell.AppActivate($process.Id)) { break }
        }
        return
    }

    New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
    $log = Join-Path $runRoot ('launch-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
    Start-Transcript -Path $log | Out-Null
    $transcriptStarted = $true
    $candidate = Get-WorkbenchLaunchCandidate -RunRoot $runRoot
    if ($Rebuild -or $null -eq $candidate) {
        if (-not (Test-Path -LiteralPath $project)) { throw "Workbench project not found: $project" }
        Add-Type -AssemblyName System.Windows.Forms
        [void][System.Windows.Forms.MessageBox]::Show(
            "Workbench needs to build before opening. This can take several minutes.`n`nBuild log: $log",
            'AI Game Workbench')
        $runtimeDirectory = Join-Path $runRoot ('dev-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
        New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
        $pending = Join-Path $runtimeDirectory 'build.pending'
        New-Item -ItemType File -Path $pending | Out-Null
        Push-Location $repo
        try {
            & dotnet build $project --configuration Debug --nologo --output $runtimeDirectory /p:UseSharedCompilation=false
            if ($LASTEXITCODE -ne 0) { throw "Workbench build failed with exit code $LASTEXITCODE. See $log" }
            Remove-Item -LiteralPath $pending
        } catch {
            New-Item -ItemType File -Path (Join-Path $runtimeDirectory 'build.failed') -Force | Out-Null
            throw
        } finally { Pop-Location }
    } else {
        $runtimeDirectory = $candidate.FullName
    }

    $executable = Join-Path $runtimeDirectory 'Workbench.App.exe'
    if (-not (Test-Path -LiteralPath $executable)) { throw "Workbench executable not found: $executable" }
    Write-Output "Opening built version: $executable"
    $env:WORKBENCH_NATIVE_AGENT_SURFACE = '1'
    Start-Process -FilePath $executable -WorkingDirectory $runtimeDirectory
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Workbench could not open')
    throw
} finally {
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
    if ($lockHeld) { $lock.ReleaseMutex() }
    if ($null -ne $lock) { $lock.Dispose() }
}
