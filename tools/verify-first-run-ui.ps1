[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [Parameter(Mandatory)][string]$GodotExecutablePath,
    [string]$ModelDisplayName = 'OpenCode · Local OpenCode Account · DeepSeek/DeepSeek V4 Flash',
    [switch]$IncludeContinuation
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'first-run-ui-helpers.ps1')
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$godot = (Resolve-Path -LiteralPath $GodotExecutablePath).Path
if (@(Get-Process -Name Workbench.App -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Workbench is already running. Exit it from the system tray before certification; closing its window only minimizes it.'
}
$output = Join-Path $repo ('artifacts\local\first-run-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$project = Join-Path $output 'counter'
$database = Join-Path $output 'workbench.db'
New-Item -ItemType Directory -Path $project -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repo 'demos\product-loop-godot-counter') -Force |
    Where-Object { $_.Name -notin @('.git', '.godot') } |
    Copy-Item -Destination $project -Recurse

# Prepare only a source fixture, not Workbench state. Keep its Git root isolated.
& git -C $project init -b main
if ($LASTEXITCODE -ne 0) { throw 'Fixture git init failed.' }
& git -C $project -c user.name=Workbench -c user.email=fixture@local.invalid add .
if ($LASTEXITCODE -ne 0) { throw 'Fixture git add failed.' }
& git -C $project -c user.name=Workbench -c user.email=fixture@local.invalid commit -m 'Counter baseline'
if ($LASTEXITCODE -ne 0) { throw 'Fixture git commit failed.' }
if (Test-Path -LiteralPath $database) { throw 'First-run database must not exist.' }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FirstRunWindow {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern IntPtr SendMessage(IntPtr h, uint message, IntPtr w, string text);
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr h);
}
'@

$script:process = $null
$script:root = $null
function Assert-TestWindowAlive {
    if ($null -eq $script:process) { throw 'Workbench is not running.' }
    $script:process.Refresh()
    if ($script:process.HasExited) { throw "Workbench exited unexpectedly ($($script:process.ExitCode))." }
}
function Start-TestWindow {
    $script:process = Start-Process -FilePath $executable -WorkingDirectory (Split-Path $executable) `
        -ArgumentList @('--database', ('"' + $database + '"')) -PassThru
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        $script:process.Refresh()
        if ($script:process.HasExited) { throw "Workbench exited during startup (exit code $($script:process.ExitCode)). Check the single-instance restriction." }
    } while ($script:process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
    if ($script:process.MainWindowHandle -eq 0) { throw 'Workbench window not found.' }
    $script:root = [System.Windows.Automation.AutomationElement]::FromHandle($script:process.MainWindowHandle)
}

function Stop-TestWindow([switch]$FailureCleanup) {
    if ($null -eq $script:process -or $script:process.HasExited) { return }
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($script:process.Path, $executable)) {
        throw 'Refusing to terminate an unexpected process.'
    }
    if ($FailureCleanup) {
        Write-Warning 'Failure cleanup only: terminating the isolated test process. This is not a certified UI exit.'
        Stop-Process -Id $script:process.Id -Force
        [void]$script:process.WaitForExit(10000)
    } else {
        Exit-WorkbenchThroughTray $script:process $executable $output
    }
    $script:process = $null
}

function Find-Control([string]$Value, [switch]$ById, [int]$Seconds = 30) {
    $property = if ($ById) {
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty
    } else {
        [System.Windows.Automation.AutomationElement]::NameProperty
    }
    $condition = [System.Windows.Automation.PropertyCondition]::new($property, $Value)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        Assert-TestWindowAlive
        $element = $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    throw "Missing UI control: $Value"
}

function Click-Control($Element) {
    $deadline = (Get-Date).AddSeconds(30)
    while (-not $Element.Current.IsEnabled -and (Get-Date) -lt $deadline) {
        Assert-TestWindowAlive
        Start-Sleep -Milliseconds 250
    }
    if (-not $Element.Current.IsEnabled) { throw "UI control is disabled: $($Element.Current.Name)" }
    if ($Element.Current.IsOffscreen) {
        $scrollItem = $null
        if ($Element.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$scrollItem)) {
            $scrollItem.ScrollIntoView()
        } else {
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($Element)
            while ($null -ne $parent) {
                $scroll = $null
                if ($parent.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$scroll) -and
                    $scroll.Current.VerticallyScrollable) {
                    for ($step = 0; $step -lt 20 -and $Element.Current.IsOffscreen; $step++) {
                        $scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount,
                            [System.Windows.Automation.ScrollAmount]::SmallIncrement)
                        Start-Sleep -Milliseconds 150
                    }
                    break
                }
                $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
            }
        }
        Start-Sleep -Milliseconds 200
    }
    if ($Element.Current.IsOffscreen) { throw "UI control remains offscreen: $($Element.Current.Name)" }
    $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
}

function Assert-AcceptedStatement {
    Click-Control (Find-Control 'NavWorld' -ById)
    $texts = $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            'WorldAcceptedStatement'))
    $actual = @($texts | ForEach-Object { $_.Current.Name.Trim() })
    if (@(Compare-Object $script:proposedStatements $actual).Count -ne 0) { throw 'Accepted state differs from the reviewed proposals.' }
    Click-Control (Find-Control 'NavOverview' -ById)
}

function Set-Value($Element, [string]$Value) {
    $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}

function Get-Edits {
    $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Edit))
}

function Capture([string]$Name) {
    [void][FirstRunWindow]::SetForegroundWindow($script:process.MainWindowHandle)
    Start-Sleep -Milliseconds 500
    $bounds = $script:root.Current.BoundingRectangle
    $bitmap = [System.Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $output "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Wait-ForReview {
    $deadline = (Get-Date).AddMinutes(10)
    do {
        Assert-TestWindowAlive
        foreach ($text in @('REVIEW QUEUE', 'REVIEW WORK RESULT')) {
            $match = $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $text))
            if ($null -ne $match) { return }
        }
        Write-Output "Waiting for first Worker review: $(Get-Date -Format HH:mm:ss)"
        Start-Sleep -Seconds 10
    } while ((Get-Date) -lt $deadline)
    throw 'First Worker did not reach Review.'
}

Start-Transcript -Path (Join-Path $output 'walkthrough.log')
try {
    Write-Output "FirstRunExecutable=$executable"
    Write-Output "FirstRunAppSha256=$((Get-FileHash -LiteralPath (Join-Path (Split-Path $executable) 'Workbench.App.dll')).Hash)"
    Write-Output 'FirstRunBoundary=Fresh Workbench database; existing authenticated provider and installed tools; source fixture only'
    Start-TestWindow
    [void](Find-Control 'No projects yet. Open a local project folder to begin.')
    Capture '01-empty-home'
    Click-Control (Find-Control 'Create Project')
    $picker = Find-Control 'Choose Project Folder'
    function Find-PickerControl([string]$Id, [string]$Class) {
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id),
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ClassNameProperty, $Class))
        $deadline = (Get-Date).AddSeconds(10)
        do {
            $control = $picker.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($null -ne $control) { return $control }
            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $deadline)
        throw "Native folder picker control not found: $Id / $Class"
    }
    $folder = Find-PickerControl '1152' 'Edit'
    $select = Find-PickerControl '1' 'Button'
    # Native shell picker may not expose ValuePattern/InvokePattern.
    if ($folder.Current.ClassName -ne 'Edit' -or $select.Current.ClassName -ne 'Button') {
        throw 'Unexpected native folder picker controls.'
    }
    [void][FirstRunWindow]::SendMessage([IntPtr]$folder.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $project)
    [void][FirstRunWindow]::PostMessage([IntPtr]$select.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
    [void](Find-Control 'FirstRunStart' -ById)
    [void](Find-Control $project)
    [void](Find-Control 'Agent is not ready')
    Capture '02-agent-not-configured'

    Click-Control (Find-Control 'FirstRunConfigure' -ById)
    (Find-Control 'Enable OpenCode').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    $edits = Get-Edits
    Set-Value $edits[1] (Join-Path $output 'missing-opencode.cmd')
    Set-Value $edits[2] $godot
    Click-Control (Find-Control 'Save Agent Settings')
    Click-Control (Find-Control 'Back')
    [void](Find-Control 'Agent is not ready')
    Capture '03-invalid-agent-path'
    Click-Control (Find-Control 'FirstRunConfigure' -ById)
    $edits = Get-Edits
    Set-Value $edits[1] ''
    Click-Control (Find-Control 'Save Agent Settings')
    Click-Control (Find-Control 'Back')
    [void](Find-Control 'Models available')
    [void](Find-Control 'No accepted project state yet.')
    Capture '04-agent-ready'
    Write-Output 'FirstRunAgentRecovery=Passed'

    Click-Control (Find-Control 'FirstRunStart' -ById)
    Set-Value (Find-Control 'FirstRunWork' -ById) 'Change the counter score button from +1 to +2.'
    Click-Control (Find-Control 'FirstRunStart' -ById)
    [void](Find-Control 'Confirm and open project')
    Set-Value (Find-Control 'FirstRunWork' -ById) 'Change CLICK_INCREMENT from 1 to 2 in scripts/main.gd and validate in Godot.'
    [void](Find-Control 'Preview what will be recorded')
    Click-Control (Find-Control 'FirstRunStart' -ById)
    Capture '05-setup-preview'
    Click-Control (Find-Control 'FirstRunStart' -ById)
    [void](Find-Control 'OverviewState' -ById)
    [void](Find-Control '0 accepted statement(s).')
    [void](Find-Control 'No Agents running')
    Capture '06-initial-overview'
    Write-Output 'FirstRunSetupAndPreview=Passed'

    Click-Control (Find-Control 'NavWork' -ById)
    $combo = $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ComboBox))
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    (Find-Control $ModelDisplayName).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $workerPrompt = @"
Prepare a bounded Worker task for my confirmation: change the counter from +1 to +2 by changing only CLICK_INCREMENT in scripts/main.gd. Do not edit files or start the Worker yourself. Preserve everything else. The Worker should validate the project using Godot installed at "$godot", and report the proposed change as "The score button now adds 2 per click." Include diff and validation evidence.
"@
    Set-Value (Find-Control 'input' -ById) $workerPrompt
    Click-Control (Find-Control 'action' -ById)
    $confirm = Wait-WorkerDraft $workerPrompt
    if ($confirm.Current.IsOffscreen) { throw 'Worker confirmation is outside the visible window.' }
    if (-not (Select-String -LiteralPath (Join-Path $project 'scripts\main.gd') -Pattern 'CLICK_INCREMENT:\s*int\s*=\s*1' -Quiet)) {
        throw 'Project changed before Worker confirmation.'
    }
    Capture '07-worker-proposal'
    Write-Output 'FirstRunLeaderProposal=Passed'
    Click-Control $confirm
    Wait-ForReview
    Click-Control (Find-Control 'Back to Project Overview')
    [void](Find-Control '0 accepted statement(s).')
    [void](Find-Control '1 change(s) waiting for review.')
    Capture '08-completed-not-accepted'
    Click-Control (Find-Control 'NavReview' -ById)
    Click-Control (Find-Control 'Review change')
    $missingContribution = $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, 'No proposed Project contribution.'))
    if ($null -ne $missingContribution) { throw 'Worker completed without a proposed project contribution.' }
    $script:proposedStatements = @($script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            'ReviewProposedStatement')) | ForEach-Object { $_.Current.Name.Trim() })
    $proposalText = $script:proposedStatements -join ' '
    if ($script:proposedStatements.Count -eq 0 -or $proposalText -notmatch 'score' -or
        $proposalText -notmatch 'click' -or $proposalText -notmatch '2') { throw 'Review proposal does not describe the requested score change.' }
    $script:proposedStatements | ConvertTo-Json -AsArray | Set-Content -LiteralPath (Join-Path $output 'reviewed-proposals.json')
    Write-Output "FirstRunReviewedProposals=$($script:proposedStatements | ConvertTo-Json -Compress)"
    Capture '09-review'
    Click-Control (Find-Control 'Accept')
    Capture '10-accept-preview'
    Click-Control (Find-Control 'Confirm Decision')
    Click-Control (Find-Control 'Back to Project Overview')
    [void](Find-Control "$($script:proposedStatements.Count) accepted statement(s).")
    Assert-AcceptedStatement
    Capture '10b-accepted-overview'
    if (-not (Select-String -LiteralPath (Join-Path $project 'scripts\main.gd') -Pattern 'CLICK_INCREMENT:\s*int\s*=\s*2' -Quiet)) {
        throw 'Worker did not implement +2.'
    }
    Stop-TestWindow
    Start-TestWindow
    $pathControl = Find-Control $project
    $parent = $pathControl
    while ($null -ne $parent -and $parent.Current.ControlType -ne [System.Windows.Automation.ControlType]::Button) {
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
    }
    if ($null -eq $parent) { throw 'Recent project button not found.' }
    Click-Control $parent
    [void](Find-Control 'OverviewState' -ById)
    [void](Find-Control "$($script:proposedStatements.Count) accepted statement(s).")
    Assert-AcceptedStatement
    [void](Find-Control 'No Agents running')
    [void](Find-Control 'No action needed.')
    Capture '11-restarted-overview'
    Stop-TestWindow
    Write-Output 'FirstRunUiLoop=Passed'
}
catch {
    Write-Output "FirstRunUiLoop=Failed: $($_.Exception.Message)"
    Write-Output $_.ScriptStackTrace
    if ($null -ne $script:process -and -not $script:process.HasExited -and $null -ne $script:root) {
        try { Capture 'failure' } catch { Write-Warning $_.Exception.Message }
    }
    throw
}
finally {
    Stop-TestWindow -FailureCleanup
    Write-Output "FirstRunEvidence=$output"
    Stop-Transcript
}

if ($IncludeContinuation) {
    foreach ($round in @(2, 3)) {
        & pwsh -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'verify-first-run-ui-continue.ps1') `
            -ExecutablePath $executable -ProjectPath $project -DatabasePath $database `
            -ModelDisplayName $ModelDisplayName -GodotExecutablePath $godot -Round $round
        if ($LASTEXITCODE -ne 0) { throw "Fresh first-run continuation round $round failed. Evidence: $output" }
    }
    Write-Output "FirstRunUiThreeRounds=Passed; Evidence=$output"
}
