#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$runner = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tools\Invoke-ScheduledScanner.ps1'))
$register = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tools\Register-ScannerSchedule.ps1'))
foreach ($script in @($register, $runner)) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($script, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw ($errors | Out-String) }
    # Import only pure production function definitions; no registration or runner body.
    $functions = $ast.FindAll({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -in @('Get-ScannerExchangeZone','ConvertTo-ScannerExchangeTime','Get-ScannerUtcTriggerTimes','Get-ScannerScheduleDecision')
    }, $true)
    foreach ($definition in $functions) { . ([scriptblock]::Create($definition.Extent.Text)) }
}
function Utc([string]$Text) { [datetime]::SpecifyKind([datetime]::ParseExact($Text,'yyyy-MM-dd HH:mm',[Globalization.CultureInfo]::InvariantCulture),[DateTimeKind]::Utc) }
$utcSlots = @((Get-ScannerUtcTriggerTimes -AtExchange @('04:15','16:15') -Year 2026) | ForEach-Object { $_.ToString('hh\:mm') }) -join ','
if ($utcSlots -ne '08:15,09:15,20:15,21:15') { throw "Unexpected UTC trigger candidates: $utcSlots" }
foreach ($case in @(
    @{ At='2026-06-15 08:15'; Expected='20260615-0415' },
    @{ At='2026-11-04 09:15'; Expected='20261104-0415' },
    @{ At='2026-03-09 08:15'; Expected='20260309-0415' },
    @{ At='2026-10-26 08:15'; Expected='20261026-0415' },
    @{ At='2026-11-02 09:15'; Expected='20261102-0415' },
    @{ At='2026-11-04 08:15'; Expected=$null },
    @{ At='2026-06-15 09:15'; Expected=$null },
    @{ At='2026-11-04 21:15'; Expected='20261104-1615' },
    @{ At='2026-11-04 09:19'; Expected='20261104-0415' },
    @{ At='2026-11-04 09:20'; Expected=$null }
)) {
    $decision = Get-ScannerScheduleDecision -UtcNow (Utc $case.At) -AtExchange @('04:15','16:15')
    if ($decision.SlotKey -ne $case.Expected) { throw "Exchange slot mismatch for $($case.At): $($decision.SlotKey)" }
}
$weekdays = @([DayOfWeek]::Monday,[DayOfWeek]::Tuesday,[DayOfWeek]::Wednesday,[DayOfWeek]::Thursday,[DayOfWeek]::Friday)
if ((Get-ScannerScheduleDecision -UtcNow (Utc '2026-11-07 09:15') -AtExchange @('04:15') -DaysOfWeek $weekdays).SlotKey) { throw 'Exchange Saturday passed weekday gate.' }
if ((Get-ScannerScheduleDecision -UtcNow (Utc '2026-11-07 04:30') -AtExchange @('23:30') -DaysOfWeek $weekdays).SlotKey -ne '20261106-2330') { throw 'Friday ET was lost because UTC/local calendar date is Saturday.' }
# Converting the same instant through different host zones must not change ET eligibility.
foreach ($id in @('FLE Standard Time','Pacific Standard Time','UTC')) {
    $instant = Utc '2026-11-04 09:15'
    $localView = [TimeZoneInfo]::ConvertTimeFromUtc($instant,[TimeZoneInfo]::FindSystemTimeZoneById($id))
    $roundTrip = [TimeZoneInfo]::ConvertTimeToUtc($localView,[TimeZoneInfo]::FindSystemTimeZoneById($id))
    if ((Get-ScannerScheduleDecision -UtcNow $roundTrip -AtExchange @('04:15')).SlotKey -ne '20261104-0415') { throw "Host-zone drift: $id" }
}
$directory = Join-Path ([IO.Path]::GetTempPath()) ('scheduled-scanner-checks-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
try {
    $bootstrap = Join-Path $directory 'bootstrap.ps1'
    @'
param($Runner, $Root, $At, [int]$ScanExit, [int]$EvaluationExit, [int]$CrossMidnight, [int]$DisableEvaluation)
$ErrorActionPreference = 'Stop'
$global:scannerTestClock = [datetime]::SpecifyKind([datetime]::ParseExact($At, 'yyyy-MM-dd HH:mm', [Globalization.CultureInfo]::InvariantCulture),[DateTimeKind]::Utc)
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
        if ($global:scannerTestCrossesMidnight) { $global:scannerTestClock = $global:scannerTestClock.Date.AddDays(1).AddHours(5.5) }
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
        @{ Name='summer-morning'; At='2026-06-15 08:15'; Scan=0; Evaluation=0; Cross=0; Disable=0; Expected=0; Calls='get-candidates,evaluate-candidates' },
        @{ Name='winter-morning'; At='2026-11-04 09:15'; Scan=0; Evaluation=0; Cross=0; Disable=0; Expected=0; Calls='get-candidates,evaluate-candidates' },
        @{ Name='evening'; At='2026-11-04 21:15'; Scan=0; Evaluation=0; Cross=0; Disable=0; Expected=0; Calls='get-candidates' },
        @{ Name='midnight'; At='2026-11-04 21:15'; Scan=0; Evaluation=0; Cross=1; Disable=0; Expected=0; Calls='get-candidates' },
        @{ Name='scan-failure'; At='2026-11-04 09:15'; Scan=7; Evaluation=0; Cross=0; Disable=0; Expected=7; Calls='get-candidates' },
        @{ Name='evaluation-failure'; At='2026-11-04 09:15'; Scan=0; Evaluation=9; Cross=0; Disable=0; Expected=9; Calls='get-candidates,evaluate-candidates' },
        @{ Name='disabled'; At='2026-11-04 09:15'; Scan=0; Evaluation=0; Cross=0; Disable=1; Expected=0; Calls='get-candidates' },
        @{ Name='wrong-utc-candidate'; At='2026-11-04 08:15'; Scan=0; Evaluation=0; Cross=0; Disable=0; Expected=0; Calls='' }
    )
    foreach ($case in $cases) {
        $root = Join-Path $directory $case.Name
        [IO.Directory]::CreateDirectory($root) | Out-Null
        $invoke = @('-NoProfile','-NonInteractive','-File',$bootstrap,'-Runner',$runner,'-Root',$root,'-At',$case.At,'-ScanExit',$case.Scan,'-EvaluationExit',$case.Evaluation,'-CrossMidnight',$case.Cross,'-DisableEvaluation',$case.Disable)
        & $powershellExe @invoke
        if ($LASTEXITCODE -ne $case.Expected) { throw "$($case.Name): expected exit $($case.Expected), got $LASTEXITCODE" }
        $callsPath = Join-Path $root 'calls.txt'
        $calls = $(if (Test-Path -LiteralPath $callsPath) { (Get-Content -LiteralPath $callsPath) -join ',' } else { '' })
        if ($calls -ne $case.Calls) { throw "$($case.Name): unexpected command sequence $calls" }
        if ($case.Name -eq 'winter-morning') {
            & $powershellExe @invoke
            $repeatCalls = (Get-Content -LiteralPath $callsPath) -join ','
            if ($LASTEXITCODE -ne 0 -or $repeatCalls -ne $case.Calls) { throw 'Duplicate exchange slot launched a second scan.' }
        }
    }
    Write-Host 'Passed exchange-time/DST decisions, UTC trigger candidates, host-zone independence, 8 mocked runner scenarios, slot deduplication and PowerShell syntax checks.'
} finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
