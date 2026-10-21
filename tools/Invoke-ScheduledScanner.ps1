#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ExePath,
    [Parameter(Mandatory = $true)] [string] $LogDirectory,
    [ValidatePattern('^([01][0-9]|2[0-3]):[0-5][0-9]$')] [string] $MorningStartLocal = '06:00',
    [ValidatePattern('^([01][0-9]|2[0-3]):[0-5][0-9]$')] [string] $MorningEndLocal = '12:00',
    [switch] $SkipMorningEvaluation
)

$ErrorActionPreference = 'Stop'
$scanLock = $null
$wrapperLog = $null
try {
    # Classify by invocation time, even when an evening scan finishes after midnight.
    $invokedAt = Get-Date
    $morningStart = [TimeSpan]::ParseExact($MorningStartLocal, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture)
    $morningEnd = [TimeSpan]::ParseExact($MorningEndLocal, 'hh\:mm', [Globalization.CultureInfo]::InvariantCulture)
    if ($morningStart -ge $morningEnd) { throw 'MorningStartLocal must be earlier than MorningEndLocal.' }
    $evaluateAfterScan = (-not $SkipMorningEvaluation.IsPresent) -and $invokedAt.TimeOfDay -ge $morningStart -and $invokedAt.TimeOfDay -lt $morningEnd
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
    Write-RunStatus "Scheduled invocation. TimeZone=$([TimeZoneInfo]::Local.Id); Executable=$scannerExe; EvaluateAfterScan=$evaluateAfterScan; MorningWindow=$MorningStartLocal-$MorningEndLocal"
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
