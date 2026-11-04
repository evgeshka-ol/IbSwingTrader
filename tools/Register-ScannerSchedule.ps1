#Requires -Version 5.1
#Requires -Modules ScheduledTasks
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateCount(1, 12)] [string[]] $AtExchange = @('04:15', '16:15'),
    [string[]] $AtLocal,
    [string] $ExePath = (Join-Path $PSScriptRoot '..\IbSwingTrader\bin\Debug\net10.0\IbSwingTrader.exe'),
    [string] $TaskName = 'IbSwingTrader-GetCandidates',
    [string] $LogDirectory = (Join-Path $PSScriptRoot '..\Data\logs\scheduled-scans'),
    [System.DayOfWeek[]] $DaysOfWeek = @(),
    [switch] $WakeComputer,
    [ValidatePattern('^([01][0-9]|2[0-3]):[0-5][0-9]$')] [string] $MorningStartExchange = '04:00',
    [ValidatePattern('^([01][0-9]|2[0-3]):[0-5][0-9]$')] [string] $MorningEndExchange = '12:00',
    [switch] $SkipMorningEvaluation
)

$ErrorActionPreference = 'Stop'
if ($PSBoundParameters.ContainsKey('AtLocal')) { throw 'AtLocal is retired. Use -AtExchange HH:mm (America/New_York).' }
function Get-ScannerExchangeZone {
    try { return [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time') }
    catch { return [TimeZoneInfo]::FindSystemTimeZoneById('America/New_York') }
}

function Get-ScannerUtcTriggerTimes {
    param([string[]] $AtExchange, [int] $Year = [datetime]::UtcNow.Year)
    $zone = Get-ScannerExchangeZone
    # Both exchange offsets are scheduled in UTC. The runner admits only the
    # matching ET slot, so neither the Windows zone nor unequal DST dates drift it.
    $offsets = @(foreach ($y in @(($Year - 1), $Year, ($Year + 1))) {
        foreach ($month in @(1, 7)) { $zone.GetUtcOffset([datetime]::new($y, $month, 15)) }
    }) | Sort-Object Ticks -Unique
    $minutes = @(foreach ($slot in $AtExchange) {
        if ($slot -notmatch '^([01][0-9]|2[0-3]):[0-5][0-9]$') { throw "Invalid exchange time '$slot'; use HH:mm." }
        $time = [TimeSpan]::ParseExact($slot, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture)
        foreach ($offset in $offsets) {
            $utcMinutes = [int](($time - $offset).TotalMinutes)
            (($utcMinutes % 1440) + 1440) % 1440
        }
    }) | Sort-Object -Unique
    return @($minutes | ForEach-Object { [TimeSpan]::FromMinutes($_) })
}


$scannerExe = (Resolve-Path -LiteralPath $ExePath).ProviderPath
$scannerDirectory = Split-Path -Parent $scannerExe
$runnerPath = Join-Path $PSScriptRoot 'Invoke-ScheduledScanner.ps1'
$resolvedLogs = [System.IO.Path]::GetFullPath($LogDirectory)
$morningStart = [TimeSpan]::ParseExact($MorningStartExchange, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture)
$morningEnd = [TimeSpan]::ParseExact($MorningEndExchange, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture)
if ($morningStart -ge $morningEnd) { throw 'MorningStartExchange must be earlier than MorningEndExchange.' }
if (-not (Test-Path -LiteralPath $scannerExe -PathType Leaf)) { throw "Executable not found: $scannerExe" }
if (-not (Test-Path -LiteralPath (Join-Path $scannerDirectory 'agentsettings.json') -PathType Leaf)) {
    throw "agentsettings.json must be beside the executable: $scannerDirectory"
}
if (-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) { throw "Runner not found: $runnerPath" }

$triggers = @(foreach ($utcTime in (Get-ScannerUtcTriggerTimes -AtExchange $AtExchange)) {
    $startUtc = [datetime]::UtcNow.Date.Add($utcTime)
    if ($startUtc -le [datetime]::UtcNow) { $startUtc = $startUtc.AddDays(1) }
    $trigger = New-ScheduledTaskTrigger -Daily -At ($startUtc.ToLocalTime())
    # Explicit Z boundary: recurrence is in UTC, not the Windows wall clock.
    $trigger.StartBoundary = $startUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
    $trigger
})

$powershellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$actionArguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -ExePath "{1}" -LogDirectory "{2}"' -f $runnerPath, $scannerExe, $resolvedLogs
$actionArguments += ' -AtExchangeCsv "{0}"' -f ($AtExchange -join ',')
if ($DaysOfWeek.Count -gt 0) { $actionArguments += ' -ExchangeDaysCsv "{0}"' -f ($DaysOfWeek -join ',') }
$actionArguments += ' -MorningStartExchange "{0}" -MorningEndExchange "{1}"' -f $MorningStartExchange, $MorningEndExchange
if ($SkipMorningEvaluation) { $actionArguments += ' -SkipMorningEvaluation' }
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
$description = 'Runs get-candidates at America/New_York times {0}; evaluates after a successful exchange-morning invocation ({1}-{2}, disabled={3}). UTC candidates are filtered/deduplicated by the runner. TWS/Gateway must already be running.' -f ($AtExchange -join ', '), $MorningStartExchange, $MorningEndExchange, $SkipMorningEvaluation.IsPresent

if ($PSCmdlet.ShouldProcess($TaskName, "Register/update scanner schedule at America/New_York times: $($AtExchange -join ', ')")) {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Principal $principal -Settings $settings -Description $description -Force | Out-Null
    Write-Host "Registered: $TaskName"
    Write-Host "Windows time zone: $([TimeZoneInfo]::Local.Id)"
    Write-Host "Exchange times (America/New_York): $($AtExchange -join ', ')"
    Write-Host "Scheduled output: $resolvedLogs"
    Write-Host 'NextRunTime may show an inactive UTC candidate; the runner admits only the configured exchange slots.'
    Get-ScheduledTaskInfo -TaskName $TaskName | Select-Object TaskName, NextRunTime, LastRunTime, LastTaskResult
}
