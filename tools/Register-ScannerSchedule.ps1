#Requires -Version 5.1
#Requires -Modules ScheduledTasks
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateCount(1, 12)]
    [string[]] $AtLocal,
    [string] $ExePath = (Join-Path $PSScriptRoot '..\IbSwingTrader\bin\Debug\net10.0\IbSwingTrader.exe'),
    [string] $TaskName = 'IbSwingTrader-GetCandidates',
    [string] $LogDirectory = (Join-Path $PSScriptRoot '..\Data\logs\scheduled-scans'),
    [System.DayOfWeek[]] $DaysOfWeek = @(),
    [switch] $WakeComputer
)

$ErrorActionPreference = 'Stop'
$scannerExe = (Resolve-Path -LiteralPath $ExePath).ProviderPath
$scannerDirectory = Split-Path -Parent $scannerExe
$runnerPath = Join-Path $PSScriptRoot 'Invoke-ScheduledScanner.ps1'
$resolvedLogs = [System.IO.Path]::GetFullPath($LogDirectory)
if (-not (Test-Path -LiteralPath $scannerExe -PathType Leaf)) { throw "Executable not found: $scannerExe" }
if (-not (Test-Path -LiteralPath (Join-Path $scannerDirectory 'agentsettings.json') -PathType Leaf)) {
    throw "agentsettings.json must be beside the executable: $scannerDirectory"
}
if (-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) { throw "Runner not found: $runnerPath" }

$triggers = @(foreach ($timeText in ($AtLocal | Select-Object -Unique)) {
    if ($timeText -notmatch '^([01][0-9]|2[0-3]):[0-5][0-9]$') {
        throw "Invalid local time '$timeText'. Use HH:mm, for example 23:15."
    }
    $parsedTime = [datetime]::ParseExact($timeText, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
    # No UTC suffix/offset: Task Scheduler follows the local Windows wall clock.
    $localStart = [datetime]::Today.Add($parsedTime.TimeOfDay)
    if ($DaysOfWeek.Count -gt 0) {
        $trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek $DaysOfWeek -At $localStart
    } else {
        $trigger = New-ScheduledTaskTrigger -Daily -At $localStart
    }
    $trigger.StartBoundary = $localStart.ToString("yyyy-MM-dd'T'HH:mm:ss", [Globalization.CultureInfo]::InvariantCulture)
    $trigger
})

$powershellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$actionArguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -ExePath "{1}" -LogDirectory "{2}"' -f $runnerPath, $scannerExe, $resolvedLogs
$action = New-ScheduledTaskAction -Execute $powershellExe -Argument $actionArguments -WorkingDirectory $scannerDirectory
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$settingsParameters = @{
    MultipleInstances = 'IgnoreNew'
    ExecutionTimeLimit = [TimeSpan]::Zero
    AllowStartIfOnBatteries = $true
    DontStopIfGoingOnBatteries = $true
    WakeToRun = $WakeComputer.IsPresent
}
$settings = New-ScheduledTaskSettingsSet @settingsParameters
$description = 'Runs get-candidates at local Windows times {0}. TWS/Gateway must already be running in the signed-in user session.' -f ($AtLocal -join ', ')

if ($PSCmdlet.ShouldProcess($TaskName, "Register/update scanner schedule at local Windows times: $($AtLocal -join ', ')")) {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Principal $principal -Settings $settings -Description $description -Force | Out-Null
    Write-Host "Registered: $TaskName"
    Write-Host "Windows time zone: $([TimeZoneInfo]::Local.Id)"
    Write-Host "Local times: $($AtLocal -join ', ')"
    Write-Host "Scheduled output: $resolvedLogs"
    Get-ScheduledTaskInfo -TaskName $TaskName | Select-Object TaskName, NextRunTime, LastRunTime, LastTaskResult
}
