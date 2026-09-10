"""Offline hypothesis screen; does not run the scanner or change its settings."""

import csv
import json
from decimal import Decimal
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read_rows(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def series(row, name):
    return [Decimal(value) for value in row.get("RecentH4" + name + "Series", "").strip("[] ").split()]


def features(row):
    upper = series(row, "BbUpperBand")
    lower = series(row, "BbLowerBand")
    hist = series(row, "MacdHistogram")
    close = series(row, "Close")
    if min(len(upper), len(lower), len(hist)) < 4:
        return {}
    width = upper[-2] - lower[-2]
    if width <= 0:
        return {}
    deceleration = (upper[-2] - upper[-3]) - (upper[-1] - upper[-2])
    lower_turn = lower[-1] - lower[-2]
    hist_decay = hist[-2] - hist[-1]
    result = {
        "upper_deceleration": deceleration / width,
        "lower_rise": lower_turn / width,
        "histogram_decay": hist_decay / width,
        "geometry_and_momentum": float(deceleration > 0 and lower_turn > 0 and hist_decay > 0),
    }
    if len(close) >= 4:
        travel = sum(abs(close[i] - close[i - 1]) for i in range(len(close) - 3, len(close)))
        result["close_decline"] = (close[-2] - close[-1]) / width
        result["low_directional_efficiency"] = 1 - abs(close[-1] - close[-4]) / travel if travel else 1
        result["geometry_momentum_and_price"] = float(result["geometry_and_momentum"] and close[-1] < close[-2])
    return {key: float(value) for key, value in result.items()}


def auc(samples):
    positive = [value for value, label in samples if label]
    negative = [value for value, label in samples if not label]
    if not positive or not negative:
        return None
    return round(sum((p > n) + 0.5 * (p == n) for p in positive for n in negative) / (len(positive) * len(negative)), 3)


def summarize(rows):
    result = {}
    for name in sorted({key for row in rows for key in row["features"]}):
        available = [row for row in rows if name in row["features"]]
        samples = [(row["features"][name], row["amplitude"] <= 10) for row in available]
        item = {"n": len(samples), "low_amplitude": sum(label for _, label in samples), "auc_low_amplitude": auc(samples)}
        if name.startswith("geometry"):
            flagged = [row for row in available if row["features"][name]]
            item.update(flagged=len(flagged), flagged_high_amplitude=sum(row["amplitude"] > 10 for row in flagged), flagged_wins=sum(row["outcome"] == "Win" for row in flagged))
        result[name] = item
    return result


def main():
    candidates = read_rows(ROOT / "Data/Tickers/candidates.csv")
    evaluations = read_rows(ROOT / "Data/datasets/evaluation-dataset.csv")
    outcomes = {(row["Ticker"], row["ScanTime"]): row for row in evaluations}
    # Latest admitted snapshot per ticker/day avoids overweighting same-day rescans.
    # The cohort is selected from candidate metadata, never a future pattern verdict.
    days = {}
    for row in sorted(candidates, key=lambda item: item["ScanTime"]):
        if row["CandidateGroup"] != "Runaway" or row["CandidateSource"] in ("Other", "DiagnosticRejected"):
            continue
        outcome = outcomes.get((row["Ticker"], row["ScanTime"]))
        if not outcome or outcome["Outcome"] == "NoData" or not outcome["AmplitudePct"]:
            continue
        days[row["Ticker"], row["ScanTime"][:10]] = {
            "ticker": row["Ticker"], "scan": row["ScanTime"],
            "amplitude": float(outcome["AmplitudePct"]), "outcome": outcome["Outcome"],
            "features": features(row),
        }
    rows = list(days.values())
    print(json.dumps({
        "cohort": "Admitted Runaway, latest evaluated snapshot per ticker/day; H4 and Daily confirmations mixed",
        "label": "AmplitudePct <= 10; higher feature values hypothesize exhaustion",
        "limitations": "Exploratory, not independent validation. Repeated tickers across dates, rounded CSV series, mixed evaluation horizons. No future cached candles used.",
        "all": summarize(rows),
        "before_2026_09_08": summarize([row for row in rows if row["scan"] < "2026-09-08"]),
        "from_2026_09_08": summarize([row for row in rows if row["scan"] >= "2026-09-08"]),
        "secz": [row for row in rows if row["ticker"] == "SECZ" and row["scan"] >= "2026-09-08"],
    }, indent=2))


if __name__ == "__main__":
    main()
