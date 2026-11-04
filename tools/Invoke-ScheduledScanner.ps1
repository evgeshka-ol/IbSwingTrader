#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ExePath,
    [Parameter(Mandatory = $true)] [string] $LogDirectory,
    [string] $AtExchangeCsv = '04:15,16:15',
    [string] $ExchangeDaysCsv = '',
    [ValidatePattern('^([01][0-9]|2[0-3]):[0-5][0-9]$')] [string] $MorningStartExchange = '04:00',
    [ValidatePattern('^([01][0-9]|2[0-3]):[0-5][0-9]$')] [string] $MorningEndExchange = '12:00',
    [switch] $SkipMorningEvaluation
)

$ErrorActionPreference = 'Stop'
function Get-ScannerExchangeZone {
    try { return [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time') }
    catch { return [TimeZoneInfo]::FindSystemTimeZoneById('America/New_York') }
}

function ConvertTo-ScannerExchangeTime {
    param([datetime] $UtcNow)
    return [TimeZoneInfo]::ConvertTimeFromUtc([datetime]::SpecifyKind($UtcNow, [DateTimeKind]::Utc), (Get-ScannerExchangeZone))
}

function Get-ScannerScheduleDecision {
    param([datetime] $UtcNow, [string[]] $AtExchange,
        [string] $MorningStartExchange = '04:00', [string] $MorningEndExchange = '12:00',
        [System.DayOfWeek[]] $DaysOfWeek = @(), [int] $GraceMinutes = 5)
    $now = ConvertTo-ScannerExchangeTime $UtcNow
    $start = [TimeSpan]::ParseExact($MorningStartExchange, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture)
    $end = [TimeSpan]::ParseExact($MorningEndExchange, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture)
    if ($start -ge $end) { throw 'MorningStartExchange must be earlier than MorningEndExchange.' }
    $due = $null
    if ($DaysOfWeek.Count -eq 0 -or $DaysOfWeek -contains $now.DayOfWeek) {
        foreach ($text in ($AtExchange | Sort-Object -Descending -Unique)) {
            if ($text -notmatch '^([01][0-9]|2[0-3]):[0-5][0-9]$') { throw "Invalid exchange time '$text'; use HH:mm." }
            $slot = $now.Date.Add([TimeSpan]::ParseExact($text, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture))
            if ($now -ge $slot -and $now -lt $slot.AddMinutes($GraceMinutes)) { $due = $slot; break }
        }
    }
    return [pscustomobject]@{
        ExchangeNow = $now
        SlotKey = $(if ($null -ne $due) { $due.ToString('yyyyMMdd-HHmm', [Globalization.CultureInfo]::InvariantCulture) } else { $null })
        EvaluateAfterScan = ($now.TimeOfDay -ge $start -and $now.TimeOfDay -lt $end)
    }
}

$scanLock = $null
$wrapperLog = $null
try {
    # Use invocation ET even if the scan later crosses midnight or a DST boundary.
    $invokedAtUtc = (Get-Date).ToUniversalTime()
    $exchangeDays = @(if ($ExchangeDaysCsv) { $ExchangeDaysCsv.Split(',') | ForEach-Object { [System.DayOfWeek]$_ } })
    $decision = Get-ScannerScheduleDecision -UtcNow $invokedAtUtc -AtExchange ($AtExchangeCsv.Split(',')) -DaysOfWeek $exchangeDays -MorningStartExchange $MorningStartExchange -MorningEndExchange $MorningEndExchange
    if (-not $decision.SlotKey) { Write-Verbose 'Skipped: not a scheduled America/New_York slot.'; exit 0 }
    $evaluateAfterScan = (-not $SkipMorningEvaluation.IsPresent) -and $decision.EvaluateAfterScan
    $scannerExe = (Resolve-Path -LiteralPath $ExePath).ProviderPath
    $scannerDirectory = Split-Path -Parent $scannerExe
    if (-not (Test-Path -LiteralPath (Join-Path $scannerDirectory 'agentsettings.json') -PathType Leaf)) {
        throw "agentsettings.json not found beside $scannerExe"
    }
    $resolvedLogs = [IO.Path]::GetFullPath($LogDirectory)
    [IO.Directory]::CreateDirectory($resolvedLogs) | Out-Null
    $runPrefix = Join-Path $resolvedLogs ('scan-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID)
    $wrapperLog = "$runPrefix.status.log"
    function Write-RunStatus([string] $Message) {
        Add-Content -LiteralPath $wrapperLog -Value ('{0} {1}' -f [datetime]::Now.ToString('O'), $Message) -Encoding UTF8
    }
    Write-RunStatus "Scheduled invocation. ExchangeTime=$($decision.ExchangeNow.ToString('O')); Slot=$($decision.SlotKey); TimeZone=America/New_York; WindowsTimeZone=$([TimeZoneInfo]::Local.Id); Executable=$scannerExe; EvaluateAfterScan=$evaluateAfterScan; MorningWindow=$MorningStartExchange-$MorningEndExchange"
    try {
        # Shared file handle protects scheduled runners, including differently named tasks.
        $scanLock = [IO.File]::Open((Join-Path $resolvedLogs 'scanner.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    } catch [IO.IOException] {
        Write-RunStatus 'Skipped: another scheduled scanner wrapper holds the lock.'
        exit 0
    }
    $processName = [IO.Path]::GetFileNameWithoutExtension($scannerExe)
    $existing = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0) {
        Write-RunStatus "Skipped: $processName is already running (process IDs: $($existing.Id -join ', '))."
        exit 0
    }
    $claimsDirectory = Join-Path $resolvedLogs 'exchange-slots'
    [IO.Directory]::CreateDirectory($claimsDirectory) | Out-Null
    $claimPath = Join-Path $claimsDirectory ($decision.SlotKey + '.claimed')
    if ([IO.File]::Exists($claimPath)) { Write-RunStatus 'Skipped: this exchange slot was already claimed.'; exit 0 }
    # Claim under the shared lock before process start; failures do not auto-retry.
    $claim = [IO.File]::Open($claimPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $claim.Dispose()
    Write-RunStatus 'Starting get-candidates.'
    $scanProcess = Start-Process -FilePath $scannerExe -ArgumentList 'get-candidates' -WorkingDirectory $scannerDirectory -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$runPrefix.stdout.log" -RedirectStandardError "$runPrefix.stderr.log"
    $scannerExitCode = $scanProcess.ExitCode
    if ($null -eq $scannerExitCode) { throw 'The scanner did not return an exit code.' }
    Write-RunStatus "Scan completed. ExitCode=$scannerExitCode"
    if ($scannerExitCode -ne 0) {
        Write-RunStatus 'Evaluation skipped: scanner failed.'
        exit $scannerExitCode
    }
    if ($evaluateAfterScan) {
        Write-RunStatus 'Starting evaluate-candidates after successful morning scan.'
        # Keep the same wrapper lock through both processes. Evaluation has its own output logs.
        $evaluationProcess = Start-Process -FilePath $scannerExe -ArgumentList 'evaluate-candidates' -WorkingDirectory $scannerDirectory -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$runPrefix.evaluation.stdout.log" -RedirectStandardError "$runPrefix.evaluation.stderr.log"
        $evaluationExitCode = $evaluationProcess.ExitCode
        if ($null -eq $evaluationExitCode) { throw 'The evaluator did not return an exit code.' }
        Write-RunStatus "Evaluation completed. ExitCode=$evaluationExitCode"
        exit $evaluationExitCode
    }
    Write-RunStatus 'Evaluation skipped: invocation is outside the morning window or evaluation is disabled.'
    exit 0
} catch {
    $failure = $_.Exception.Message
    if ($wrapperLog) {
        try { Add-Content -LiteralPath $wrapperLog -Value ("Failed: $failure") -Encoding UTF8 } catch { }
    }
    [Console]::Error.WriteLine("Scheduled scanner failed: $failure")
    exit 1
} finally {
    if ($null -ne $scanLock) { $scanLock.Dispose() }
}
