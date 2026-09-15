[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectPath = '',

    [Parameter(Mandatory = $false)]
    [string]$ExecutablePath = '',

    [Parameter(Mandatory = $false)]
    [string]$SeedDllPath = '',

    [Parameter(Mandatory = $false)]
    [string]$DatabasePath = '',

    [Parameter(Mandatory = $false)]
    [string]$Statement = 'The score button now adds 2 per click.',

    [Parameter(Mandatory = $false)]
    [string]$SuccessorAssignmentContract = '',

    [Parameter(Mandatory = $false)]
    [int]$ExpectedAcceptedStatementCount = 1
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

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $publishDirectory = Join-Path $repo ('artifacts\local\product-loop-ui-accept-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
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
    $DatabasePath = Join-Path $repo ('artifacts\local\product-loop-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.db')
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
        [int]$TimeoutSeconds = 30,
        [switch]$ById
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        $(if ($ById) { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::NameProperty }),
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
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $items = $Root.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($item in $items) {
            if ($item.Current.Name -and $item.Current.Name.Contains($Text, [StringComparison]::Ordinal)) {
                return $item
            }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for UI text containing: $Text"
}

function Invoke-UiElement {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Name
    )

    try {
        $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        return
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
    $deadline = (Get-Date).AddSeconds(20)
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
    if ([string]::IsNullOrWhiteSpace($processPath)) {
        throw "Cannot verify the Workbench process path before stopping PID $($Process.Id)."
    }
    $resolvedProcessPath = (Resolve-Path -LiteralPath $processPath).Path
    $resolvedExpectedPath = (Resolve-Path -LiteralPath $ExpectedExecutable).Path
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($resolvedProcessPath, $resolvedExpectedPath)) {
        throw "Refusing to stop unexpected process path: $resolvedProcessPath"
    }
    Stop-Process -Id $Process.Id -Force
    $Process.WaitForExit(10000) | Out-Null
}

& dotnet $resolvedSeed `
    --database $resolvedDatabase `
    --project $resolvedProject `
    --statement $Statement
if ($LASTEXITCODE -ne 0) {
    throw "ProductLoopUiSeed failed with exit code $LASTEXITCODE."
}

$first = $null
$second = $null
try {
    $first = Start-Workbench $resolvedExecutable $resolvedProject $resolvedDatabase
    $firstRoot = [System.Windows.Automation.AutomationElement]::FromHandle($first.MainWindowHandle)
    [void](Wait-Element $firstRoot 'OverviewState' -ById)
    Invoke-UiElement (Wait-Element $firstRoot 'NavReview' -ById) 'Review'
    Invoke-UiElement (Wait-Element $firstRoot 'Review change') 'Review change'
    if (-not [string]::IsNullOrWhiteSpace($SuccessorAssignmentContract)) {
        $advanced = Wait-Element $firstRoot 'Advanced decision options'
        $advanced.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Set-UiValue (Wait-Element $firstRoot 'ReviewNextWork' -ById) $SuccessorAssignmentContract
        $advanced.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    }
    Invoke-UiElement (Wait-Element $firstRoot 'Accept') 'Accept'
    if (-not [string]::IsNullOrWhiteSpace($SuccessorAssignmentContract)) {
        [void](Wait-TextContaining $firstRoot 'Next work')
    }
    Invoke-UiElement (Wait-Element $firstRoot 'Confirm Decision') 'Confirm Decision'
    Start-Sleep -Seconds 2
    Write-Output 'Desktop UI Confirm clicked; verifying the committed result after restart.'

    Stop-VerifiedProcess $first $resolvedExecutable
    $first = $null

    $second = Start-Workbench $resolvedExecutable $resolvedProject $resolvedDatabase
    $secondRoot = [System.Windows.Automation.AutomationElement]::FromHandle($second.MainWindowHandle)
    [void](Wait-Element $secondRoot 'OverviewState' -ById)
    [void](Wait-TextContaining $secondRoot "$ExpectedAcceptedStatementCount accepted statement(s).")
    [void](Wait-TextContaining $secondRoot 'No action needed.')
    [void](Wait-TextContaining $secondRoot $Statement)
    if (-not [string]::IsNullOrWhiteSpace($SuccessorAssignmentContract)) {
        [void](Wait-TextContaining $secondRoot $SuccessorAssignmentContract)
    }
    Write-Output 'Desktop UI restart recovery passed.'
}
finally {
    Stop-VerifiedProcess $first $resolvedExecutable
    Stop-VerifiedProcess $second $resolvedExecutable
}

Write-Output 'Product loop UI Accept harness passed.'
