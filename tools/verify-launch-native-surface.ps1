$ErrorActionPreference = 'Stop'
$launcher = Join-Path $PSScriptRoot 'launch-native-surface.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($launcher, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw ($errors | Out-String) }
# Load only the selection function; never launch a desktop or build during this test.
$function = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Get-WorkbenchLaunchCandidate'
}, $true)
. ([scriptblock]::Create($function.Extent.Text))
$root = Join-Path ([IO.Path]::GetTempPath()) ('workbench-launch-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    if ($null -ne (Get-WorkbenchLaunchCandidate $root)) { throw 'Empty directory returned a candidate.' }
    $files = @('Workbench.App.exe', 'Workbench.App.dll', 'Workbench.App.deps.json',
        'Workbench.App.runtimeconfig.json', 'Workbench.Core.dll', 'Workbench.Runtime.dll',
        'Workbench.Storage.dll', 'Workbench.Project.dll', 'Avalonia.Controls.dll')
    foreach ($name in @('dev-complete', 'dev-partial', 'dev-pending', 'dev-failed', 'fixture-newer')) {
        $directory = Join-Path $root $name
        New-Item -ItemType Directory -Path $directory | Out-Null
        foreach ($file in $files) {
            if ($name -eq 'dev-partial' -and $file -eq 'Workbench.App.runtimeconfig.json') { continue }
            New-Item -ItemType File -Path (Join-Path $directory $file) | Out-Null
        }
    }
    New-Item -ItemType File -Path (Join-Path $root 'dev-pending\build.pending') | Out-Null
    New-Item -ItemType File -Path (Join-Path $root 'dev-failed\build.failed') | Out-Null
    $selected = Get-WorkbenchLaunchCandidate $root
    if ($selected.Name -ne 'dev-complete') { throw "Selected incomplete or unrelated build: $($selected.Name)" }
    Copy-Item -LiteralPath (Join-Path $root 'dev-complete') -Destination (Join-Path $root 'dev-newer') -Recurse
    (Get-Item -LiteralPath (Join-Path $root 'dev-newer\Workbench.App.dll')).LastWriteTimeUtc = [DateTime]::UtcNow.AddMinutes(1)
    if ((Get-WorkbenchLaunchCandidate $root).Name -ne 'dev-newer') { throw 'Did not choose the most recent complete build.' }
    Write-Output 'LauncherSelection=Passed (empty, incomplete, pending, failed, unrelated, newest)'
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'workbench-launch-test-*') {
        throw "Refusing to remove unexpected test directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
