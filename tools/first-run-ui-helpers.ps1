function Exit-WorkbenchThroughTray($Process, [string]$Executable, [string]$EvidenceDirectory) {
    if ($Process.HasExited) { throw 'Workbench exited before the requested UI shutdown.' }
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($Process.Path, $Executable)) {
        throw 'Refusing to close an unexpected process.'
    }
    if (-not ('FirstRunTrayPointer' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FirstRunTrayPointer {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
'@
    }
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $all = [System.Windows.Automation.Condition]::TrueCondition
    $descendants = [System.Windows.Automation.TreeScope]::Descendants
    $children = [System.Windows.Automation.TreeScope]::Children
    $classProperty = [System.Windows.Automation.AutomationElement]::ClassNameProperty
    $tray = $desktop.FindFirst($children,
        [System.Windows.Automation.PropertyCondition]::new($classProperty, 'Shell_TrayWnd'))
    if ($null -eq $tray) { throw 'Windows taskbar is unavailable.' }
    $bounds = $tray.Current.BoundingRectangle
    [void][FirstRunTrayPointer]::SetCursorPos([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Bottom - 1))
    Start-Sleep -Seconds 1
    $icon = @($tray.FindAll($descendants, $all) |
        Where-Object { $_.Current.Name.Trim() -eq 'AI Game Workbench' }) | Select-Object -First 1
    if ($null -eq $icon) {
        $overflow = $desktop.FindFirst($children,
            [System.Windows.Automation.PropertyCondition]::new($classProperty, 'TopLevelWindowForOverflowXamlIsland'))
        if ($null -eq $overflow -or $overflow.Current.IsOffscreen) {
            $overflowButton = @($tray.FindAll($descendants, $all) |
                Where-Object { $_.Current.Name -in @('显示隐藏的图标', 'Show hidden icons') }) | Select-Object -First 1
            if ($null -eq $overflowButton) { throw 'Hidden tray icons button is unavailable.' }
            $overflowButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            $deadline = (Get-Date).AddSeconds(5)
            do {
                Start-Sleep -Milliseconds 200
                $overflow = $desktop.FindFirst($children,
                    [System.Windows.Automation.PropertyCondition]::new($classProperty, 'TopLevelWindowForOverflowXamlIsland'))
            } while (($null -eq $overflow -or $overflow.Current.IsOffscreen) -and (Get-Date) -lt $deadline)
        }
        if ($null -eq $overflow) { throw 'Hidden tray icons did not open.' }
        $icon = @($overflow.FindAll($descendants, $all) |
            Where-Object { $_.Current.Name.Trim() -eq 'AI Game Workbench' }) | Select-Object -First 1
    }
    if ($null -eq $icon -or $icon.Current.IsOffscreen) { throw 'Workbench tray icon is not visible.' }
    $bounds = $icon.Current.BoundingRectangle
    [void][FirstRunTrayPointer]::SetCursorPos([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 2))
    [FirstRunTrayPointer]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero)
    [FirstRunTrayPointer]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
    $exit = $null
    $deadline = (Get-Date).AddSeconds(10)
    do {
        foreach ($window in $desktop.FindAll($children, $all)) {
            if ($window.Current.ProcessId -ne $Process.Id) { continue }
            $exit = $window.FindFirst($descendants,
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::NameProperty, '退出 Workbench'))
            if ($null -ne $exit) { break }
        }
        if ($null -eq $exit) { Start-Sleep -Milliseconds 200 }
    } while ($null -eq $exit -and (Get-Date) -lt $deadline)
    if ($null -eq $exit -or $exit.Current.IsOffscreen -or -not $exit.Current.IsEnabled) {
        throw 'Workbench Exit menu item is unavailable.'
    }
    $bounds = $exit.Current.BoundingRectangle
    if ($bounds.Width -le 0 -or $bounds.Height -le 0) { throw 'Exit menu item has no clickable bounds.' }
    # Avalonia native menu items expose no InvokePattern on this Windows host.
    [void][FirstRunTrayPointer]::SetCursorPos([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 2))
    [FirstRunTrayPointer]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    [FirstRunTrayPointer]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    if (-not $Process.WaitForExit(30000)) { throw 'Normal tray Exit did not terminate Workbench.' }
    if ($Process.ExitCode -ne 0) { throw "Workbench exited with code $($Process.ExitCode)." }
    $record = [pscustomobject]@{
        Mode = 'VisibleTrayExit'
        ProcessId = $Process.Id
        ExitCode = $Process.ExitCode
        CompletedAt = (Get-Date).ToString('o')
    }
    $record | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $EvidenceDirectory 'ui-exits.jsonl')
    Write-Host "TrayExit=Passed; ProcessId=$($Process.Id); ExitCode=$($Process.ExitCode)"
}

function Wait-WorkerDraft([string]$Prompt, [int]$Seconds = 240) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $lastErrorCount = 0
    $retries = 0
    $draftCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ConfirmWorkerDraft')
    $errorCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Leader request failed. You can try again.')
    do {
        Assert-TestWindowAlive
        $draft = $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $draftCondition)
        if ($null -ne $draft) { return $draft }
        $conversation = Find-Control 'RootWebArea' -ById
        $errorCount = $conversation.FindAll([System.Windows.Automation.TreeScope]::Descendants, $errorCondition).Count
        if ($errorCount -gt $lastErrorCount) {
            if ($retries -ge 4) { throw 'Leader request failed after four UI retries.' }
            $lastErrorCount = $errorCount
            $retries++
            Write-Host "LeaderRequestUiRetry=$retries"
            Set-Value (Find-Control 'input' -ById) $Prompt
            Click-Control (Find-Control 'action' -ById)
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw 'Leader did not produce a confirmable Worker draft.'
}
