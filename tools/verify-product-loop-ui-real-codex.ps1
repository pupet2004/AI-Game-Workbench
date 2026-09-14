[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$ExecutablePath = '',
    [string]$SeedDllPath = '',
    [string]$DatabasePath = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$createdProject = $false

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $root = Join-Path $repo ('artifacts\local\product-loop-real-codex-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    $ProjectPath = Join-Path $root 'counter'
    New-Item -ItemType Directory -Force -Path $ProjectPath | Out-Null
    $fixture = Join-Path $repo 'demos\product-loop-godot-counter'
    Get-ChildItem -LiteralPath $fixture -Force | Copy-Item -Destination $ProjectPath -Recurse -Force
    $createdProject = $true
}

$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolvedProject 'project.godot') -PathType Leaf)) {
    throw "The product loop project does not contain project.godot: $resolvedProject"
}

if ($createdProject) {
    & git -C $resolvedProject init -b main | Out-Null
    & git -C $resolvedProject config user.name 'Workbench Real Codex Certification'
    & git -C $resolvedProject config user.email 'workbench-real-codex@local.invalid'
    & git -C $resolvedProject add .
    & git -C $resolvedProject commit -m 'initial Godot counter' | Out-Null
}

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $publishDirectory = Join-Path $repo ('artifacts\local\product-loop-real-codex-app-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    & dotnet build (Join-Path $repo 'src\Workbench.App\Workbench.App.csproj') `
        --configuration Debug `
        --nologo `
        --output $publishDirectory `
        /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "Workbench build failed with exit code $LASTEXITCODE."
    }
    $ExecutablePath = Join-Path $publishDirectory 'Workbench.App.exe'
}

if ([string]::IsNullOrWhiteSpace($SeedDllPath)) {
    $seedOutput = Join-Path $repo 'artifacts\local\product-loop-ui-seed'
    & dotnet build (Join-Path $repo 'tools\ProductLoopUiSeed\ProductLoopUiSeed.csproj') `
        --configuration Debug `
        --nologo `
        --output $seedOutput `
        /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "ProductLoopUiSeed build failed with exit code $LASTEXITCODE."
    }
    $SeedDllPath = Join-Path $seedOutput 'ProductLoopUiSeed.dll'
}

if ([string]::IsNullOrWhiteSpace($DatabasePath)) {
    $DatabasePath = Join-Path $repo ('artifacts\local\product-loop-real-codex-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.db')
}

$resolvedDatabase = [System.IO.Path]::GetFullPath($DatabasePath)
$resolvedSeed = (Resolve-Path -LiteralPath $SeedDllPath).Path
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Wait-Element {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [int]$TimeoutSeconds = 180
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $element = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for UI element: $Name"
}

function Wait-TextContaining {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Text,
        [int]$TimeoutSeconds = 180
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $items = $Root.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($item in $items) {
                if ($item.Current.Name -and $item.Current.Name.Contains($Text, [StringComparison]::Ordinal)) {
                    return $item
                }
            }
        }
        catch {
            # UIA can briefly invalidate the tree while Avalonia swaps views.
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for UI text containing: $Text"
}

function Find-Element {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Invoke-UiElement {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Name
    )

    try {
        $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
    }
    catch {
        throw "Could not invoke UI element '$Name': $($_.Exception.Message)"
    }
}

function Set-UiValue {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Value
    )

    try {
        $pattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $pattern.SetValue($Value)
    }
    catch {
        throw "Could not set UI value: $($_.Exception.Message)"
    }
}

function Wait-EditElements {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [int]$MinimumCount = 3,
        [int]$TimeoutSeconds = 60
    )

    $editCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $edits = $Root.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                $editCondition)
            if ($edits.Count -ge $MinimumCount) {
                return $edits
            }
        }
        catch {
            # UIA can briefly invalidate the tree while Avalonia swaps views.
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for $MinimumCount edit controls."
}

function Start-Workbench {
    param(
        [string]$Executable,
        [string]$Project,
        [string]$Database
    )

    $arguments = @(
        '--project', ('"' + $Project + '"'),
        '--database', ('"' + $Database + '"'))
    $process = Start-Process `
        -FilePath $Executable `
        -WorkingDirectory (Split-Path -Parent $Executable) `
        -ArgumentList $arguments `
        -PassThru
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) {
            throw "Workbench exited during startup with code $($process.ExitCode)."
        }
    } while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
    if ($process.MainWindowHandle -eq 0) {
        throw 'Workbench did not create a main window.'
    }
    return $process
}

function Wait-ForReview {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
    [int]$TimeoutSeconds = 600
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        foreach ($approvalName in @('Approve once', 'Approve for session')) {
            $approvalCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                $approvalName)
            $approval = $Root.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $approvalCondition)
            if ($null -ne $approval -and $approval.Current.IsEnabled) {
                $approval.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep -Milliseconds 500
            }
        }

        try {
            foreach ($reviewText in @('REVIEW WORK RESULT', 'Preview what will change')) {
                $items = $Root.FindAll(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)
                foreach ($item in $items) {
                    if ($item.Current.Name -and $item.Current.Name.Contains($reviewText, [StringComparison]::Ordinal)) {
                        return
                    }
                }
            }
        }
        catch {
            # UIA can briefly invalidate the tree while Avalonia swaps views.
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw 'Timed out waiting for the real Codex Worker review surface.'
}

function Stop-VerifiedProcess {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ExpectedExecutable
    )

    if ($null -eq $Process) {
        return
    }
    $Process.Refresh()
    if ($Process.HasExited) {
        return
    }
    $processPath = $Process.Path
    $resolvedProcessPath = (Resolve-Path -LiteralPath $processPath).Path
    $resolvedExpectedPath = (Resolve-Path -LiteralPath $ExpectedExecutable).Path
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($resolvedProcessPath, $resolvedExpectedPath)) {
        throw "Refusing to stop unexpected process path: $resolvedProcessPath"
    }
    Stop-Process -Id $Process.Id -Force
    $Process.WaitForExit(10000) | Out-Null
}

function Get-RoundConfiguration {
    param([int]$Round)

    switch ($Round) {
        1 {
            return [pscustomobject]@{
                Statement = 'The score button now adds 2 per click.'
                Successor = 'Add a Reset button to the counter.'
                Patterns = @(
                    'CLICK_INCREMENT: int = 2'
                )
            }
        }
        2 {
            return [pscustomobject]@{
                Statement = 'The counter has a Reset button.'
                Successor = 'Add high score tracking to the counter.'
                Patterns = @(
                    'ResetButton',
                    '_on_reset_button_pressed',
                    'score = 0'
                )
            }
        }
        3 {
            return [pscustomobject]@{
                Statement = 'The counter records the highest score.'
                Successor = ''
                Patterns = @(
                    'HighScore',
                    'high_score'
                )
            }
        }
        default {
            throw "Unsupported product loop round: $Round"
        }
    }
}

$first = $null
$second = $null
$oldNativeSurface = $env:WORKBENCH_NATIVE_AGENT_SURFACE
$env:WORKBENCH_NATIVE_AGENT_SURFACE = '0'
try {
    for ($round = 1; $round -le 3; $round++) {
        $configuration = Get-RoundConfiguration $round
        & dotnet $resolvedSeed `
            --database $resolvedDatabase `
            --project $resolvedProject `
            --round $round `
            --real-worker
        if ($LASTEXITCODE -ne 0) {
            throw "Real Codex seed failed for round $round with exit code $LASTEXITCODE."
        }

        $first = Start-Workbench $resolvedExecutable $resolvedProject $resolvedDatabase
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($first.MainWindowHandle)
        [void](Wait-Element $root 'PROJECT OVERVIEW')
        $openWorkspace = Find-Element $root 'Open workspace'
        if ($null -ne $openWorkspace -and $openWorkspace.Current.IsEnabled) {
            Invoke-UiElement $openWorkspace 'Open workspace'
        }

        Invoke-UiElement (Wait-Element $root 'Confirm / Start Worker' 60) 'Confirm / Start Worker'
        Write-Output "Round ${round}: desktop UI started the real Codex Worker."

        Wait-ForReview $root 900
        if (-not [string]::IsNullOrWhiteSpace($configuration.Successor)) {
            $edits = Wait-EditElements $root 3 60
            Set-UiValue $edits[$edits.Count - 1] $configuration.Successor
        }
        Invoke-UiElement (Wait-Element $root 'Preview what will change' 60) 'Preview what will change'
        Invoke-UiElement (Wait-Element $root 'Confirm Decision' 60) 'Confirm Decision'
        Start-Sleep -Seconds 2

        foreach ($pattern in $configuration.Patterns) {
            if (-not (Select-String -LiteralPath (Join-Path $resolvedProject 'scripts\main.gd') -Pattern $pattern -Quiet) -and
                -not (Select-String -LiteralPath (Join-Path $resolvedProject 'scenes\main.tscn') -Pattern $pattern -Quiet)) {
                throw "Round $round did not produce the expected project change: $pattern"
            }
        }

        Stop-VerifiedProcess $first $resolvedExecutable
        $first = $null
        $second = Start-Workbench $resolvedExecutable $resolvedProject $resolvedDatabase
        $secondRoot = [System.Windows.Automation.AutomationElement]::FromHandle($second.MainWindowHandle)
        [void](Wait-Element $secondRoot 'PROJECT OVERVIEW')
        [void](Wait-TextContaining $secondRoot "$round accepted statement(s).")
        [void](Wait-TextContaining $secondRoot $configuration.Statement)
        [void](Wait-TextContaining $secondRoot 'No handoffs are awaiting review.')
        if (-not [string]::IsNullOrWhiteSpace($configuration.Successor)) {
            [void](Wait-TextContaining $secondRoot 'ACTIVE WORK')
        }
        Write-Output "Round ${round}: accepted state recovered after process restart."
        Stop-VerifiedProcess $second $resolvedExecutable
        $second = $null
    }
    Write-Output 'Real Codex desktop Worker three-round product loop passed.'
}
finally {
    Stop-VerifiedProcess $first $resolvedExecutable
    Stop-VerifiedProcess $second $resolvedExecutable
    if ($null -eq $oldNativeSurface) {
        Remove-Item Env:WORKBENCH_NATIVE_AGENT_SURFACE -ErrorAction SilentlyContinue
    }
    else {
        $env:WORKBENCH_NATIVE_AGENT_SURFACE = $oldNativeSurface
    }
}

Write-Output "ProjectPath=$resolvedProject"
Write-Output "DatabasePath=$resolvedDatabase"
