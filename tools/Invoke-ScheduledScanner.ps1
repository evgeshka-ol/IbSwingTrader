#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ExePath,
    [Parameter(Mandatory = $true)] [string] $LogDirectory
)

$ErrorActionPreference = 'Stop'
$scanLock = $null
$wrapperLog = $null
try {
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
    Write-RunStatus "Scheduled invocation. TimeZone=$([TimeZoneInfo]::Local.Id); Executable=$scannerExe"
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
    Write-RunStatus "Completed. ExitCode=$scannerExitCode"
    exit $scannerExitCode
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
