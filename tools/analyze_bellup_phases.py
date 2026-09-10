"""Exploratory causal H4 phase labels from saved scanner snapshots only."""

import json
from collections import Counter
from decimal import Decimal
from statistics import median

from analyze_bellup_exhaustion import ROOT, read_rows, series


def classify(row, window=3, progress_ratio=Decimal("0.25")):
    names = ("Close", "High", "Low", "BbUpperBand", "BbLowerBand", "MacdHistogram")
    values = [series(row, name) for name in names]
    if len({len(value) for value in values}) != 1 or len(values[0]) < 2 * window + 1:
        return None
    close, high, low, upper, lower, hist = values
    width = [u - l for u, l in zip(upper, lower)]
    state, box, transitions = "Unclassified", None, []
    for t in range(2 * window, len(close)):
        start = t - window
        prior = close[start] - close[start - window]
        current = close[t] - close[start]
        prior_expansion = prior > 0 and width[start] > width[start - window] and upper[start] > upper[start - window]
        expanding = close[t] > close[t - 1] and upper[t] > upper[t - 1] and width[t] > width[t - 1]
        warning = (close[t] < close[t - 1] and hist[t] < hist[t - 1]
                   and upper[t] - upper[t - 1] < upper[t - 1] - upper[t - 2])
        overlap = all(max(low[i], low[i - 1]) <= min(high[i], high[i - 1]) for i in range(start + 2, t + 1))
        slow_bands = upper[t] - upper[start] < upper[start] - upper[start - window]
        plateau = prior_expansion and abs(current) <= progress_ratio * prior and overlap and slow_bands
        previous_state = state
        if box is not None:
            if close[t] > box[1]:
                state, box = "RenewedExpansion" if expanding else "Unclassified", None
            elif close[t] < box[0]:
                state, box = "Breakdown", None
            else:
                state = "Plateau"
        elif plateau:
            state = "Plateau"
            box = (min(low[start + 1:t + 1]), max(high[start + 1:t + 1]))
        elif warning and (prior_expansion or state in ("Expansion", "RenewedExpansion", "PossibleExhaustion")):
            state = "PossibleExhaustion"
        elif expanding:
            state = "Expansion"
        elif state != "PossibleExhaustion":
            state = "Unclassified"
        if state != previous_state:
            transitions.append({"bars_before_snapshot_end": len(close) - 1 - t, "state": state})
    return {"state": state, "transitions": transitions}


def stats(rows):
    return {
        "n": len(rows), "amplitude_gt_10": sum(row["amplitude"] > 10 for row in rows),
        "median_amplitude": round(median(row["amplitude"] for row in rows), 2) if rows else None,
        "outcomes": dict(Counter(row["outcome"] for row in rows)),
    }


def group_stats(rows):
    return {state: stats([row for row in rows if row["phase"]["state"] == state])
            for state in sorted({row["phase"]["state"] for row in rows})}


def cohort(admitted_only=True):
    candidates = read_rows(ROOT / "Data/Tickers/candidates.csv")
    outcomes = {(row["Ticker"], row["ScanTime"]): row
                for row in read_rows(ROOT / "Data/datasets/evaluation-dataset.csv")}
    days = {}
    for candidate in sorted(candidates, key=lambda row: row["ScanTime"]):
        if admitted_only and (candidate["CandidateGroup"] != "Runaway" or candidate["CandidateSource"] in ("Other", "DiagnosticRejected")):
            continue
        outcome = outcomes.get((candidate["Ticker"], candidate["ScanTime"]))
        if not outcome or outcome["Outcome"] == "NoData" or not outcome["AmplitudePct"]:
            continue
        days[candidate["Ticker"], candidate["ScanTime"][:10]] = {
            "candidate": candidate, "ticker": candidate["Ticker"], "scan": candidate["ScanTime"],
            "amplitude": float(outcome["AmplitudePct"]), "outcome": outcome["Outcome"],
        }
    return list(days.values())


def main():
    source = cohort()
    rows = []
    for row in source:
        phase = classify(row["candidate"])
        if phase:
            rows.append({**row, "phase": phase})
    sensitivity = []
    for window in (2, 3, 4):
        for ratio in ("0.15", "0.25", "0.40"):
            classified = [{**row, "phase": classify(row["candidate"], window, Decimal(ratio))} for row in rows]
            flagged = [row for row in classified if row["phase"] and row["phase"]["state"] in ("Plateau", "PossibleExhaustion")]
            sensitivity.append({"window": window, "progress_ratio": ratio, "flagged": stats(flagged),
                                "plateau_only": stats([row for row in flagged if row["phase"]["state"] == "Plateau"])})
    broader = []
    for row in cohort(admitted_only=False):
        phase = classify(row["candidate"])
        if phase:
            broader.append({**row, "phase": phase})
    print(json.dumps({
        "limitations": "Hypothesis labels, not expert ground truth. Mixed H4/Daily confirmations and outcome horizons. No cache backfill or future verdict selection. Snapshot bars lack timestamps; phases are relative to saved sequence end, not verified bar-close times.",
        "coverage": {"joined_ticker_days": len(source), "aligned_ohlc_indicator_rows": len(rows), "dates": sorted({row['scan'][:10] for row in rows})},
        "baseline": stats(rows),
        "snapshots_with_past_renewal": sum(any(event["state"] == "RenewedExpansion" for event in row["phase"]["transitions"]) for row in rows),
        "all": group_stats(rows),
        "before_2026_09_08": group_stats([row for row in rows if row["scan"] < "2026-09-08"]),
        "from_2026_09_08": group_stats([row for row in rows if row["scan"] >= "2026-09-08"]),
        "sensitivity": sensitivity,
        "secondary_all_candidate_groups": {"baseline": stats(broader), "phases": group_stats(broader)},
        "flagged_examples": [{**{k: v for k, v in row.items() if k not in ("candidate", "phase")}, "state": row["phase"]["state"]} for row in rows
                             if row["phase"]["state"] in ("Plateau", "RenewedExpansion", "PossibleExhaustion")],
        "secz": [{k: v for k, v in row.items() if k != "candidate"} for row in rows if row["ticker"] == "SECZ"],
    }, indent=2))


if __name__ == "__main__":
    main()
