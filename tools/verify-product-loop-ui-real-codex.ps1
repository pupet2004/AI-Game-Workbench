[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$ExecutablePath = '',
    [string]$SeedDllPath = '',
    [string]$DatabasePath = '',
    [ValidateSet('codex', 'opencode')]
    [string]$WorkerProvider = 'codex',
    [string]$WorkerModel = ''
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
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReviewCaptureWindow {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@

$screenshotDirectory = [System.IO.Path]::ChangeExtension($resolvedDatabase, 'screenshots')
New-Item -ItemType Directory -Force -Path $screenshotDirectory | Out-Null

function Save-ReviewScreenshot {
    param(
        [System.Diagnostics.Process]$Process,
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name
    )
    [void][ReviewCaptureWindow]::SetForegroundWindow($Process.MainWindowHandle)
    Start-Sleep -Milliseconds 600
    $bounds = $Root.Current.BoundingRectangle
    $bitmap = [System.Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.X, [int]$bounds.Y, 0, 0, $bitmap.Size)
        $bitmap.Save((Join-Path $screenshotDirectory $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Wait-Element {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [int]$TimeoutSeconds = 180,
        [switch]$ById
    )

    $property = if ($ById) { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::NameProperty }
    $condition = [System.Windows.Automation.PropertyCondition]::new($property, $Name)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $element = $Root.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $condition)
            if ($null -ne $element) {
                return $element
            }
        }
        catch {
            # A navigation can invalidate the UIA tree for one poll.
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
                $actual = if ($item.Current.Name) {
                    [Regex]::Replace($item.Current.Name, '\s+', '')
                } else {
                    ''
                }
                $expected = [Regex]::Replace($Text, '\s+', '')
                if ($actual.Contains($expected, [StringComparison]::Ordinal)) {
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

function Expand-UiElement {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Name
    )

    try {
        $pattern = $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $pattern.Expand()
        return
    }
    catch {
        try {
            $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $pattern.Invoke()
            return
        }
        catch {
            $bounds = $Element.Current.BoundingRectangle
            if ($bounds.Width -le 0 -or $bounds.Height -le 0) {
                throw "Could not expand UI element '$Name': the element has no visible bounds."
            }
            [void][ReviewCaptureWindow]::SetCursorPos(
                [int]($bounds.X + ($bounds.Width / 2)),
                [int]($bounds.Y + ($bounds.Height / 2)))
            [ReviewCaptureWindow]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
            [ReviewCaptureWindow]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 500
        }
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
            foreach ($reviewText in @('REVIEW QUEUE', 'REVIEW WORK RESULT')) {
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

    throw 'Timed out waiting for the configured Worker review surface.'
}

function Wait-ForAgentRuntime {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [int]$TimeoutSeconds = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $retry = Find-Element $Root 'Retry'
            if ($null -ne $retry -and $retry.Current.IsEnabled -and -not $retry.Current.IsOffscreen) {
                Invoke-UiElement $retry 'Retry runtime'
                Start-Sleep -Seconds 2
            }

            $error = Wait-TextContaining $Root 'Worker runtime is not connected.' 1
            if ($null -eq $error) {
                return
            }
        }
        catch {
            # The runtime surface is still settling or the transient error is gone.
            if ($_.Exception.Message -like '*Timed out waiting for UI text*') {
                return
            }
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw 'Timed out waiting for the configured Agent runtime.'
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
            --real-worker `
            --provider $WorkerProvider `
            $(if ([string]::IsNullOrWhiteSpace($WorkerModel)) { @() } else { @('--model', $WorkerModel) })
        if ($LASTEXITCODE -ne 0) {
            throw "Real Codex seed failed for round $round with exit code $LASTEXITCODE."
        }

        $first = Start-Workbench $resolvedExecutable $resolvedProject $resolvedDatabase
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($first.MainWindowHandle)
        [void](Wait-Element $root 'OverviewState' -ById)
        $openWorkspace = Find-Element $root 'Open workspace'
        if ($null -ne $openWorkspace -and $openWorkspace.Current.IsEnabled) {
            Invoke-UiElement $openWorkspace 'Open workspace'
        }

        Wait-ForAgentRuntime $root 90
        Invoke-UiElement (Wait-Element $root 'Confirm / Start Worker' 60) 'Confirm / Start Worker'
        Write-Output "Round ${round}: desktop UI started the configured Worker ($WorkerProvider)."

        Wait-ForReview $root 900
        Invoke-UiElement (Wait-Element $root 'Back to Project Overview') 'Back to Project Overview'
        [void](Wait-Element $root 'OverviewState' -ById)
        [void](Wait-TextContaining $root "$($round - 1) accepted statement(s).")
        [void](Wait-TextContaining $root '1 change(s) waiting for review.')
        Write-Output "Round ${round}: completion is pending; accepted count is still $($round - 1)."
        Invoke-UiElement (Wait-Element $root 'NavReview' -ById) 'Review'
        $reviewChange = Wait-Element $root 'Review change'
        Save-ReviewScreenshot $first $root "round-$round-review-queue.png"
        Invoke-UiElement $reviewChange 'Review change'
        [void](Wait-Element $root 'Accept')
        Save-ReviewScreenshot $first $root "round-$round-review-change.png"
        if (-not [string]::IsNullOrWhiteSpace($configuration.Successor)) {
            $advanced = Wait-Element $root 'Advanced decision options'
            Expand-UiElement $advanced 'Advanced decision options'
            Set-UiValue (Wait-Element $root 'ReviewNextWork' 60 -ById) $configuration.Successor
        }
        Invoke-UiElement (Wait-Element $root 'Accept' 60) 'Accept'
        [void](Wait-Element $root 'Confirm Decision' 60)
        Save-ReviewScreenshot $first $root "round-$round-review-preview.png"
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
        [void](Wait-Element $secondRoot 'OverviewState' -ById)
        [void](Wait-TextContaining $secondRoot "$round accepted statement(s).")
        [void](Wait-TextContaining $secondRoot $configuration.Statement)
        [void](Wait-TextContaining $secondRoot 'No action needed.')
        [void](Wait-TextContaining $secondRoot 'No Agents running')
        if (-not [string]::IsNullOrWhiteSpace($configuration.Successor)) {
            [void](Wait-TextContaining $secondRoot $configuration.Successor)
        }
        Write-Output "Round ${round}: accepted state recovered after process restart."
        Stop-VerifiedProcess $second $resolvedExecutable
        $second = $null
    }
    Write-Output "Configured Worker three-round product loop passed ($WorkerProvider)."
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
Write-Output "Screenshots=$screenshotDirectory"
