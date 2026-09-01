<#
.SYNOPSIS
Backfills raw Daily/H4 OHLC candle series for already-decided Reversal candidates,
reconstructed from the local historical cache (Data/cache/*.json) instead of
waiting for new scans to accumulate raw-candle history.

.DESCRIPTION
For every decided (Win/Loss) row in evaluation-dataset.csv belonging to the given
CandidateGroup, loads that ticker's cached H4 candle history and rebuilds the same
"as of scan time" windows the live scanner captures (30 daily bars / 40 H4 bars),
using only candles that existed at or before ScanTime (no lookahead).

Daily bars are built via proper OHLC aggregation (Open = first H4 bar of the day,
High/Low = max/min across the day, Close = last H4 bar's close) - this matches the
fixed CandidateFinder.cs/EvaluationDatasetBuilder.cs logic, not the single-bar
proxy the earlier capture code used.

.PARAMETER EvaluationDatasetPath
Path to evaluation-dataset.csv. Defaults to Data/datasets/evaluation-dataset.csv
relative to the repo root (parent of this script's folder).

.PARAMETER CacheFolder
Path to the historical cache folder. Defaults to Data/cache.

.PARAMETER OutputPath
Where to write the backfilled CSV. Defaults to Data/datasets/reversal-candle-backfill.csv.

.PARAMETER CandidateGroup
Which CandidateGroup to backfill. Defaults to Reversal.

.EXAMPLE
.\Backfill-ReversalCandles.ps1
#>

param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$EvaluationDatasetPath,
    [string]$CacheFolder,
    [string]$OutputPath,
    [string]$CandidateGroup = 'Reversal'
)

$ErrorActionPreference = 'Stop'

if (-not $EvaluationDatasetPath) { $EvaluationDatasetPath = Join-Path $RepoRoot 'Data/datasets/evaluation-dataset.csv' }
if (-not $CacheFolder) { $CacheFolder = Join-Path $RepoRoot 'Data/cache' }
if (-not $OutputPath) { $OutputPath = Join-Path $RepoRoot 'Data/datasets/reversal-candle-backfill.csv' }

if (-not (Test-Path $EvaluationDatasetPath)) { throw "Evaluation dataset not found: $EvaluationDatasetPath" }
if (-not (Test-Path $CacheFolder)) { throw "Cache folder not found: $CacheFolder" }

$DailyWindow = 30
$H4Window = 40
$culture = [System.Globalization.CultureInfo]::InvariantCulture

function Format-Series {
    param([double[]]$Values)

    if (-not $Values -or $Values.Count -eq 0) { return '[]' }

    $parts = $Values | ForEach-Object {
        $rounded = [math]::Round($_, 2, [System.MidpointRounding]::AwayFromZero)
        $rounded.ToString('0.##', $culture)
    }

    return '[' + ($parts -join ' ') + ']'
}

function Get-H4Bucket {
    param([datetime]$Time)

    $hour = [int]([math]::Floor($Time.Hour / 4) * 4)
    return [datetime]::new($Time.Year, $Time.Month, $Time.Day, $hour, 0, 0)
}

Write-Host "Loading evaluation dataset: $EvaluationDatasetPath"
$rows = Import-Csv -Path $EvaluationDatasetPath

$decided = $rows | Where-Object {
    $_.CandidateGroup -eq $CandidateGroup -and ($_.Outcome -eq 'Win' -or $_.Outcome -eq 'Loss')
}

Write-Host "Decided '$CandidateGroup' rows: $($decided.Count)"

$cacheByTicker = @{}
$results = [System.Collections.Generic.List[object]]::new()
$skippedNoCache = 0
$skippedNoScanTime = 0
$skippedNoCoverage = 0
$i = 0

foreach ($row in $decided) {
    $i++
    if ($i % 25 -eq 0) { Write-Host "  processed $i / $($decided.Count)" }

    $ticker = $row.Ticker
    if (-not $ticker) { continue }

    if (-not $cacheByTicker.ContainsKey($ticker)) {
        $path = Join-Path $CacheFolder "$ticker.json"

        if (-not (Test-Path $path)) {
            $cacheByTicker[$ticker] = $null
        }
        else {
            try {
                $json = Get-Content -Raw -Path $path | ConvertFrom-Json
                $h4Raw = $json.Timeframes.H4

                if ($h4Raw) {
                    $parsed = $h4Raw | ForEach-Object {
                        [pscustomobject]@{
                            Time  = [datetime]::Parse($_.Time, $culture)
                            Open  = [double]$_.Open
                            High  = [double]$_.High
                            Low   = [double]$_.Low
                            Close = [double]$_.Close
                        }
                    } | Sort-Object Time

                    $cacheByTicker[$ticker] = @($parsed)
                }
                else {
                    $cacheByTicker[$ticker] = $null
                }
            }
            catch {
                Write-Warning "Failed to load cache for $ticker : $_"
                $cacheByTicker[$ticker] = $null
            }
        }
    }

    $candles = $cacheByTicker[$ticker]
    if (-not $candles -or $candles.Count -eq 0) { $skippedNoCache++; continue }

    $scanTimeRaw = $row.ScanTime
    if (-not $scanTimeRaw) { $skippedNoScanTime++; continue }

    try {
        $scanTime = [datetime]::Parse($scanTimeRaw, $culture)
    }
    catch {
        $skippedNoScanTime++; continue
    }

    # Last candle at or before ScanTime - mirrors the live scanner's "as of scan" state, no lookahead.
    $scanIndex = -1
    for ($j = $candles.Count - 1; $j -ge 0; $j--) {
        if ($candles[$j].Time -le $scanTime) { $scanIndex = $j; break }
    }
    if ($scanIndex -lt 0) { $skippedNoCoverage++; continue }

    $visible = $candles[0..$scanIndex]

    # Daily bars: proper aggregation across every H4 bar of the day, not a single-bar proxy.
    $dailyGroups = $visible | Group-Object { $_.Time.Date } | Sort-Object { [datetime]$_.Name }
    $dailyBars = foreach ($g in $dailyGroups) {
        $ordered = @($g.Group | Sort-Object Time)
        [pscustomobject]@{
            Open  = $ordered[0].Open
            High  = ($ordered | Measure-Object -Property High -Maximum).Maximum
            Low   = ($ordered | Measure-Object -Property Low -Minimum).Minimum
            Close = $ordered[-1].Close
        }
    }
    $dailyBars = @($dailyBars | Select-Object -Last $DailyWindow)

    # H4 bars: dedup by 4h bucket (matches StartOfH4Bucket in the app), most recent N, chronological order.
    $seenBuckets = [System.Collections.Generic.HashSet[datetime]]::new()
    $h4Indexes = [System.Collections.Generic.List[int]]::new()
    for ($j = $scanIndex; $j -ge 0; $j--) {
        $bucket = Get-H4Bucket -Time $candles[$j].Time
        if ($seenBuckets.Add($bucket)) {
            $h4Indexes.Add($j)
            if ($h4Indexes.Count -ge $H4Window) { break }
        }
    }
    $h4Indexes.Reverse()
    $h4Bars = @($h4Indexes | ForEach-Object { $candles[$_] })

    $results.Add([pscustomobject]@{
        Ticker                 = $ticker
        ScanTime               = $row.ScanTime
        EntryTime              = $row.EntryTime
        CandidateGroup         = $row.CandidateGroup
        DetectedPattern        = $row.DetectedPattern
        Outcome                = $row.Outcome
        AmplitudePct           = $row.AmplitudePct
        DailyBarsUsed          = $dailyBars.Count
        H4BarsUsed             = $h4Bars.Count
        RecentDailyOpenSeries  = Format-Series -Values ($dailyBars | ForEach-Object { $_.Open })
        RecentDailyHighSeries  = Format-Series -Values ($dailyBars | ForEach-Object { $_.High })
        RecentDailyLowSeries   = Format-Series -Values ($dailyBars | ForEach-Object { $_.Low })
        RecentDailyCloseSeries = Format-Series -Values ($dailyBars | ForEach-Object { $_.Close })
        RecentH4OpenSeries     = Format-Series -Values ($h4Bars | ForEach-Object { $_.Open })
        RecentH4HighSeries     = Format-Series -Values ($h4Bars | ForEach-Object { $_.High })
        RecentH4LowSeries      = Format-Series -Values ($h4Bars | ForEach-Object { $_.Low })
        RecentH4CloseSeries    = Format-Series -Values ($h4Bars | ForEach-Object { $_.Close })
    })
}

$results | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8

$totalSkipped = $skippedNoCache + $skippedNoScanTime + $skippedNoCoverage
Write-Host ""
Write-Host "Done."
Write-Host "  Rows written:               $($results.Count)"
Write-Host "  Skipped (no cache file):    $skippedNoCache"
Write-Host "  Skipped (no/bad ScanTime):  $skippedNoScanTime"
Write-Host "  Skipped (no cache coverage before ScanTime): $skippedNoCoverage"
Write-Host "  Output: $OutputPath"
