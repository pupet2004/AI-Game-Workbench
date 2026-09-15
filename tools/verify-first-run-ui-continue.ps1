[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [Parameter(Mandatory)][string]$ProjectPath,
    [Parameter(Mandatory)][string]$DatabasePath,
    [Parameter(Mandatory)][string]$ModelDisplayName,
    [Parameter(Mandatory)][string]$GodotExecutablePath,
    [Parameter(Mandatory)][ValidateSet(2, 3)][int]$Round,
    [switch]$ResumeDraft,
    [switch]$ResumeReview
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'first-run-ui-helpers.ps1')
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$database = [IO.Path]::GetFullPath($DatabasePath)
$godot = (Resolve-Path -LiteralPath $GodotExecutablePath).Path
if (-not (Test-Path -LiteralPath $database -PathType Leaf)) { throw 'Continuation requires an existing certified database.' }
if (@(Get-Process -Name Workbench.App -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Workbench is already running. Exit it from the system tray before certification.'
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ContinuationWindow {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
$output = Join-Path (Split-Path $database) ("round-$Round-" + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $output | Out-Null

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
        if ($script:process.HasExited) { throw "Workbench exited during startup ($($script:process.ExitCode))." }
    } while ($script:process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
    if ($script:process.MainWindowHandle -eq 0) { throw 'Workbench window not found.' }
    $script:root = [System.Windows.Automation.AutomationElement]::FromHandle($script:process.MainWindowHandle)
}
function Stop-TestWindow([switch]$FailureCleanup) {
    if ($null -ne $script:process -and -not $script:process.HasExited) {
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals($script:process.Path, $executable)) { throw 'Unexpected process.' }
        if ($FailureCleanup) {
            Write-Warning 'Failure cleanup only: terminating the isolated test process. This is not a certified UI exit.'
            Stop-Process -Id $script:process.Id -Force
            [void]$script:process.WaitForExit(10000)
        } else {
            Exit-WorkbenchThroughTray $script:process $executable $output
        }
    }
    $script:process = $null
}
function Find-Control([string]$Value, [switch]$ById, [int]$Seconds = 30) {
    $property = if ($ById) { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::NameProperty }
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
    $deadline = (Get-Date).AddSeconds(180)
    while (-not $Element.Current.IsEnabled -and (Get-Date) -lt $deadline) {
        Assert-TestWindowAlive
        Start-Sleep -Milliseconds 250
    }
    Assert-TestWindowAlive
    if (-not $Element.Current.IsEnabled) { throw "Disabled UI control: $($Element.Current.Name)" }
    if ($Element.Current.IsOffscreen) {
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($Element)
        while ($null -ne $parent) {
            $scroll = $null
            if ($parent.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$scroll) -and $scroll.Current.VerticallyScrollable) {
                for ($step = 0; $step -lt 30 -and $Element.Current.IsOffscreen; $step++) {
                    $scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount, [System.Windows.Automation.ScrollAmount]::SmallIncrement)
                    Start-Sleep -Milliseconds 150
                }
                break
            }
            $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
        }
    }
    if ($Element.Current.IsOffscreen) { throw "Offscreen UI control: $($Element.Current.Name)" }
    $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 500
}
function Set-Value($Element, [string]$Value) {
    $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Wait-ForReview {
    $deadline = (Get-Date).AddMinutes(12)
    do {
        Assert-TestWindowAlive
        foreach ($text in @('REVIEW QUEUE', 'REVIEW WORK RESULT')) {
            if ($null -ne $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $text))) { return }
        }
        Write-Output "Round $Round waiting for Review: $(Get-Date -Format HH:mm:ss)"
        Start-Sleep -Seconds 10
    } while ((Get-Date) -lt $deadline)
    throw "Round $Round did not reach Review."
}
function Assert-Text([string]$Text) {
    $items = $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $needle = [Regex]::Replace($Text, '\s+', '')
    if (-not @($items | Where-Object { ([Regex]::Replace($_.Current.Name, '\s+', '')).Contains($needle, [StringComparison]::Ordinal) })) {
        throw "Missing UI text: $Text"
    }
}
function Capture([string]$Name) {
    Assert-TestWindowAlive
    [void][ContinuationWindow]::SetForegroundWindow($script:process.MainWindowHandle)
    Start-Sleep -Milliseconds 400
    $rect = $script:root.Current.BoundingRectangle
    $bitmap = [System.Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bitmap.Size)
        $bitmap.Save((Join-Path $output "$Name.png"))
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Get-AcceptedStatements {
    Click-Control (Find-Control 'NavWorld' -ById)
    $statements = @($script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            'WorldAcceptedStatement')) | ForEach-Object { $_.Current.Name.Trim() })
    Click-Control (Find-Control 'NavOverview' -ById)
    $statements
}
function Assert-AcceptedStatements($Expected) {
    $actual = @(Get-AcceptedStatements)
    if (@(Compare-Object @($Expected) $actual).Count -ne 0) {
        throw "Accepted statements differ. Expected: $($Expected | ConvertTo-Json -Compress); Actual: $($actual | ConvertTo-Json -Compress)"
    }
}
function Open-RecentProject {
    $parent = Find-Control $project -Seconds 60
    while ($null -ne $parent -and $parent.Current.ControlType -ne [System.Windows.Automation.ControlType]::Button) {
        $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)
    }
    if ($null -eq $parent) { throw 'Recent project button not found.' }
    Click-Control $parent
    [void](Find-Control 'OverviewState' -ById)
}
function Start-FreshLeader {
    Click-Control (Find-Control 'Change Brain' -Seconds 90)
    [void](Find-Control 'Choose a new agent or model')
    $combos = $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ComboBox))
    $combo = @($combos | Where-Object { $_.Current.IsEnabled -and -not $_.Current.IsOffscreen }) | Select-Object -Last 1
    if ($null -eq $combo) { throw 'Brain picker model selector is unavailable.' }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $ModelDisplayName)
    $selected = $false
    $deadline = (Get-Date).AddSeconds(30)
    do {
        foreach ($option in $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
            $selection = $null
            if ($option.Current.IsEnabled -and -not $option.Current.IsOffscreen -and
                $option.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)) {
                $selection.Select()
                $selected = $true
                break
            }
        }
        if (-not $selected) { Start-Sleep -Milliseconds 300 }
    } while (-not $selected -and (Get-Date) -lt $deadline)
    if (-not $selected) { throw 'Requested model is not selectable in the brain picker.' }
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Click-Control (Find-Control 'Switch Brain')
    [void](Find-Control 'Fresh Leader session started.' -Seconds 120)
}
function Read-LeaderAcceptedState {
    $before = (Get-Content -LiteralPath (Join-Path $project 'scripts/main.gd') -Raw)
    Set-Value (Find-Control 'input' -ById) 'From the accepted Workbench project state supplied to this new session, describe only what changes the user has already accepted. Do not inspect workspace files, run tools, propose tasks, or execute work. Distinguish accepted changes from plans.'
    Click-Control (Find-Control 'action' -ById)
    $deadline = (Get-Date).AddSeconds(180)
    do {
        $conversation = Find-Control 'RootWebArea' -ById
        $items = $conversation.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
        $text = ($items | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text } |
            ForEach-Object { $_.Current.Name }) -join ' '
        $changeBrain = Find-Control 'Change Brain'
        if ($changeBrain.Current.IsEnabled -and $text -match '(?i)accept' -and
            $text -match '(?i)(adds?\s*2|\+\s*2|increment.{0,40}2)') {
            if ($Round -eq 2 -or $text -match '(?i)reset') { break }
        }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    if ((Get-Date) -ge $deadline) { throw 'New Leader did not report expected accepted state.' }
    if ($before -ne (Get-Content -LiteralPath (Join-Path $project 'scripts/main.gd') -Raw)) { throw 'Read-only Leader inquiry changed project source.' }
    Write-Output "FreshLeaderStateText=$text"
    Capture '02-fresh-leader-state'
}

$prompt = if ($Round -eq 2) {
    "Prepare a bounded Worker task for my confirmation: add a Reset button to the counter. Preserve the existing +2 score behavior. Edit only scripts/main.gd and scenes/main.tscn, validate with Godot at `"$godot`", and report the proposed change as `"The counter has a Reset button.`" Do not edit files or start the Worker yourself."
} else {
    "Prepare a bounded Worker task for my confirmation: add high score tracking without changing the existing +2 score or Reset behavior. Edit only scripts/main.gd and scenes/main.tscn, validate with Godot at `"$godot`", and report the proposed change as `"The counter records the highest score.`" Do not edit files or start the Worker yourself."
}
$expectedPattern = if ($Round -eq 2) { 'Reset' } else { 'High.?Score|high_score|highest' }

Start-Transcript -Path (Join-Path $output 'walkthrough.log')
try {
    Write-Output "ContinuationRound=$Round; Database=$database"
    Write-Output "AppSha256=$((Get-FileHash -LiteralPath (Join-Path (Split-Path $executable) 'Workbench.App.dll')).Hash)"
    Start-TestWindow
    Open-RecentProject
    Assert-Text 'No Agents running'
    $priorStatements = @(Get-AcceptedStatements)
    if ($priorStatements.Count -eq 0 -or ($Round -eq 3 -and ($priorStatements -join ' ') -notmatch '(?i)reset')) {
        throw 'Prior accepted state is missing the required changes.'
    }
    Assert-Text "$($priorStatements.Count) accepted statement(s)."
    $priorStatements | ConvertTo-Json -AsArray | Set-Content -LiteralPath (Join-Path $output 'prior-accepted.json')
    Capture '01-prior-accepted-state'
    if (-not $ResumeReview) {
    Click-Control (Find-Control 'NavWork' -ById)
    Start-FreshLeader
    Read-LeaderAcceptedState
    $sourceBefore = Get-Content -LiteralPath (Join-Path $project 'scripts/main.gd') -Raw
    $sceneBefore = Get-Content -LiteralPath (Join-Path $project 'scenes/main.tscn') -Raw
    if ($ResumeDraft) {
        Write-Output 'Resuming the persisted draft through UI confirmation.'
    } else {
        Set-Value (Find-Control 'input' -ById) $prompt
        Click-Control (Find-Control 'action' -ById)
    }
    $confirm = if ($ResumeDraft) { Find-Control 'ConfirmWorkerDraft' -ById } else { Wait-WorkerDraft $prompt }
    if ($ResumeDraft) {
        $previewText = ($script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join ' '
        if ($previewText -notmatch $expectedPattern) { throw 'Recovered draft does not match the requested round.' }
        Write-Output 'Recovered draft matches the requested round.'
    }
    if ($sourceBefore -ne (Get-Content -LiteralPath (Join-Path $project 'scripts/main.gd') -Raw) -or
        $sceneBefore -ne (Get-Content -LiteralPath (Join-Path $project 'scenes/main.tscn') -Raw)) { throw 'Leader changed files before confirmation.' }
    Capture '03-worker-proposal'
    Click-Control $confirm
    Wait-ForReview
    Click-Control (Find-Control 'Back to Project Overview')
    }
    Assert-Text "$($priorStatements.Count) accepted statement(s)."
    Assert-Text '1 change(s) waiting for review.'
    Assert-AcceptedStatements $priorStatements
    Capture '04-completed-not-accepted'
    Click-Control (Find-Control 'NavReview' -ById)
    Click-Control (Find-Control 'Review change')
    $proposals = @($script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            'ReviewProposedStatement')) | ForEach-Object { $_.Current.Name.Trim() })
    if ($proposals.Count -eq 0 -or ($proposals -join ' ') -notmatch $expectedPattern) {
        throw "Round $Round proposed contributions do not contain the expected change."
    }
    $proposals | ConvertTo-Json -AsArray | Set-Content -LiteralPath (Join-Path $output 'reviewed-proposals.json')
    Write-Output "ReviewedProposals=$($proposals | ConvertTo-Json -Compress)"
    Capture '05-review'
    Click-Control (Find-Control 'Accept')
    Capture '06-decision-preview'
    Click-Control (Find-Control 'Confirm Decision')
    Click-Control (Find-Control 'Back to Project Overview')
    $expectedStatements = @($priorStatements) + $proposals
    Assert-Text "$($expectedStatements.Count) accepted statement(s)."
    Assert-AcceptedStatements $expectedStatements
    Capture '07-accepted'
    $source = Get-Content -LiteralPath (Join-Path $project 'scripts/main.gd') -Raw
    $scene = Get-Content -LiteralPath (Join-Path $project 'scenes/main.tscn') -Raw
    if ($source -notmatch 'CLICK_INCREMENT:\s*int\s*=\s*2' -or ($source + $scene) -notmatch $expectedPattern) {
        throw 'Expected source changes are absent.'
    }
    Stop-TestWindow
    Start-TestWindow
    Open-RecentProject
    Assert-Text "$($expectedStatements.Count) accepted statement(s)."
    Assert-Text 'No action needed.'
    Assert-Text 'No Agents running'
    Assert-AcceptedStatements $expectedStatements
    Capture '08-restarted'
    Stop-TestWindow
    Write-Output "FirstRunRound${Round}=Passed"
}
catch {
    Write-Output "FirstRunRound${Round}=Failed: $($_.Exception.Message)"
    Write-Output $_.ScriptStackTrace
    if ($null -ne $script:process -and -not $script:process.HasExited) {
        try { Capture 'failure' } catch { Write-Warning $_.Exception.Message }
    }
    throw
}
finally {
    Stop-TestWindow -FailureCleanup
    Write-Output "ContinuationEvidence=$output"
    Stop-Transcript
}
