"""Exploratory Win/Loss separation using only saved scan-time features."""
import csv
import json
import statistics
from pathlib import Path
from collections import Counter

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    with path.open(encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def auc(values):
    wins = [v for v, label in values if label]
    losses = [v for v, label in values if not label]
    if not wins or not losses:
        return None
    return sum((w > l) + 0.5 * (w == l) for w in wins for l in losses) / (len(wins) * len(losses))


def features(row):
    names = ["PlannedProfitPct", "PlannedLossPct", "DiagnosticsRankingQualityScore",
             "ContextDailyRSI14", "DiagnosticsVolumeRatio20", "DiagnosticsATRRatio",
             "DiagnosticsBBMidSignedDistancePct", "DiagnosticsWeeklyMACDHistDelta",
             "ScoreWeeklyScore", "ScoreDailyScore", "ScoreEntryScore", "DisplayRank",
             "PriceToPublicationSeconds"]
    result = {name: float(row[name]) for name in names if row.get(name)}
    risk = abs(result.get("PlannedLossPct", 0))
    if risk:
        result["ProfitToRisk"] = result["PlannedProfitPct"] / risk
    result["H4Pattern"] = float("confirmed on H4" in row["PatternVerdictReason"])
    result["BoostExit"] = float(row["ExitProfile"].startswith("bellup-"))
    for frame in ("Daily", "H4"):
        for name in ("Close", "BbMidBand", "BbUpperBand", "MacdHistogram", "Rsi"):
            raw = row.get(f"Recent{frame}{name}Series", "").strip("[] ")
            values = [float(v) for v in raw.split()] if raw else []
            if len(values) >= 4:
                result[f"{frame}{name}Last"] = values[-1]
                if name in ("MacdHistogram", "Rsi"):
                    result[f"{frame}{name}Change3"] = values[-1] - values[-4]
                elif values[-4]:
                    result[f"{frame}{name}Change3Pct"] = 100 * (values[-1] / values[-4] - 1)
    price = float(row["EntryPrice"])
    if price > 0:
        for name in ("DiagnosticsWeeklyMACDHistDelta", "DailyMacdHistogramLast", "H4MacdHistogramChange3"):
            if name in result:
                result[name + "PctOfEntry"] = 100 * result[name] / price
    return result


def summarize(rows):
    result = []
    for name in sorted({k for row in rows for k in row["features"]}):
        available = [row for row in rows if name in row["features"]]
        wins = [row["features"][name] for row in available if row["win"]]
        losses = [row["features"][name] for row in available if not row["win"]]
        if not wins or not losses:
            continue
        latest = {row["ticker"]: row for row in sorted(available, key=lambda row: row["scan"])}
        item = dict(feature=name, n=len(available), wins=len(wins), losses=len(losses),
                    win_median=statistics.median(wins), loss_median=statistics.median(losses),
                    auc=auc([(row["features"][name], row["win"]) for row in available]),
                    auc_latest_per_ticker=auc([(row["features"][name], row["win"]) for row in latest.values()]),
                    unique_tickers=len(latest))
        for label, subset in (("before_sep30", [row for row in available if row["scan"] < "2026-09-30"]),
                              ("sep30_onward", [row for row in available if row["scan"] >= "2026-09-30"])):
            item[label] = dict(n=len(subset), wins=sum(row["win"] for row in subset),
                               auc=auc([(row["features"][name], row["win"]) for row in subset]))
        result.append(item)
    return sorted(result, key=lambda row: abs(row["auc"] - 0.5), reverse=True)


def main():
    dataset = {(row["Ticker"], row["ScanTime"]): row for row in read(ROOT / "Data/datasets/evaluation-dataset.csv")}
    samples = []
    all_rows = []
    for row in read(ROOT / "Data/Tickers/candidates.csv"):
        evaluated = dataset.get((row["Ticker"], row["ScanTime"]))
        if row["CandidateGroup"] != "BellUp" or row["ScanTime"] < "2026-09-23" or not evaluated:
            continue
        sample = dict(ticker=row["Ticker"], scan=row["ScanTime"], outcome=evaluated["Outcome"],
                      win=evaluated["Outcome"] == "Win", features=features(row))
        all_rows.append(sample)
        if evaluated["Outcome"] in ("Win", "Loss"):
            samples.append(sample)
    summary = summarize(samples)
    result = dict(outcomes=dict(Counter(row["outcome"] for row in all_rows)),
                  closed_unique_tickers=len({row["ticker"] for row in samples}),
                  exploratory_features=summary, samples=all_rows)
    (ROOT / "docs/analysis-2026-10-05-win-features.json").write_text(json.dumps(result, indent=2) + "\n")
    print("OUTCOMES", result["outcomes"], "closed unique", result["closed_unique_tickers"])
    for row in summary[:14]:
        print(json.dumps(row))
    for name in ("H4Pattern", "BoostExit"):
        for value in (0, 1):
            subset = [row for row in all_rows if row["features"][name] == value]
            print(name, value, dict(Counter(row["outcome"] for row in subset)))


if __name__ == "__main__":
    main()
