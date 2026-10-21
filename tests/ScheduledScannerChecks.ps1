#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$runner = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tools\Invoke-ScheduledScanner.ps1'))
$register = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tools\Register-ScannerSchedule.ps1'))
foreach ($script in @($runner, $register)) {
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) { throw ($errors | Out-String) }
}
$directory = Join-Path ([IO.Path]::GetTempPath()) ('scheduled-scanner-checks-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
try {
    $bootstrap = Join-Path $directory 'bootstrap.ps1'
    @'
param($Runner, $Root, $At, [int]$ScanExit, [int]$EvaluationExit, [int]$CrossMidnight, [int]$DisableEvaluation)
$ErrorActionPreference = 'Stop'
$global:scannerTestClock = [datetime]::ParseExact($At, 'yyyy-MM-dd HH:mm', [Globalization.CultureInfo]::InvariantCulture)
$global:scannerTestScanResult = $ScanExit
$global:scannerTestEvaluationResult = $EvaluationExit
$global:scannerTestCrossesMidnight = $CrossMidnight
$global:scannerTestCallsFile = Join-Path $Root 'calls.txt'
function Get-Date {
    param([string]$Format)
    if ($Format) { return $global:scannerTestClock.ToString($Format) }
    return $global:scannerTestClock
}
function Get-Process { param($Name, $ErrorAction) return @() }
function Start-Process {
    param($FilePath, $ArgumentList, $WorkingDirectory, [switch]$NoNewWindow, [switch]$Wait,
        [switch]$PassThru, $RedirectStandardOutput, $RedirectStandardError)
    Add-Content -LiteralPath $global:scannerTestCallsFile -Value $ArgumentList
    [IO.File]::WriteAllText($RedirectStandardOutput, 'mocked output')
    [IO.File]::WriteAllText($RedirectStandardError, '')
    if ($ArgumentList -eq 'get-candidates') {
        if ($global:scannerTestCrossesMidnight) { $global:scannerTestClock = $global:scannerTestClock.Date.AddDays(1).AddMinutes(30) }
        return [pscustomobject]@{ ExitCode = $global:scannerTestScanResult }
    }
    return [pscustomobject]@{ ExitCode = $global:scannerTestEvaluationResult }
}
$exe = Join-Path $Root 'mock-scanner.exe'
[IO.File]::WriteAllText($exe, '')
[IO.File]::WriteAllText((Join-Path $Root 'agentsettings.json'), '{}')
& $Runner -ExePath $exe -LogDirectory (Join-Path $Root 'logs') -SkipMorningEvaluation:([bool]$DisableEvaluation)
exit $LASTEXITCODE
'@ | Set-Content -LiteralPath $bootstrap -Encoding UTF8
    $powershellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $cases = @(
        @{ Name='morning'; At='2026-10-20 10:00'; Scan=0; Evaluation=0; Cross=0; Disable=0; Expected=0; Calls='get-candidates,evaluate-candidates' },
        @{ Name='evening'; At='2026-10-20 23:15'; Scan=0; Evaluation=0; Cross=0; Disable=0; Expected=0; Calls='get-candidates' },
        @{ Name='midnight'; At='2026-10-20 23:15'; Scan=0; Evaluation=0; Cross=1; Disable=0; Expected=0; Calls='get-candidates' },
        @{ Name='scan-failure'; At='2026-10-20 10:00'; Scan=7; Evaluation=0; Cross=0; Disable=0; Expected=7; Calls='get-candidates' },
        @{ Name='evaluation-failure'; At='2026-10-20 10:00'; Scan=0; Evaluation=9; Cross=0; Disable=0; Expected=9; Calls='get-candidates,evaluate-candidates' },
        @{ Name='disabled'; At='2026-10-20 10:00'; Scan=0; Evaluation=0; Cross=0; Disable=1; Expected=0; Calls='get-candidates' }
    )
    foreach ($case in $cases) {
        $root = Join-Path $directory $case.Name
        [IO.Directory]::CreateDirectory($root) | Out-Null
        & $powershellExe -NoProfile -NonInteractive -File $bootstrap -Runner $runner -Root $root -At $case.At -ScanExit $case.Scan -EvaluationExit $case.Evaluation -CrossMidnight $case.Cross -DisableEvaluation $case.Disable
        if ($LASTEXITCODE -ne $case.Expected) { throw "$($case.Name): expected exit $($case.Expected), got $LASTEXITCODE" }
        $calls = (Get-Content -LiteralPath (Join-Path $root 'calls.txt')) -join ','
        if ($calls -ne $case.Calls) { throw "$($case.Name): unexpected command sequence $calls" }
        $status = Get-ChildItem -LiteralPath (Join-Path $root 'logs') -Filter '*.status.log'
        if (@($status).Count -ne 1) { throw "$($case.Name): missing status log" }
    }
    Write-Host 'Passed 6 scheduled scanner scenarios and PowerShell syntax checks. No application or broker process was started.'
} finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
