"""Offline penalty experiment. Never rewrites candidates or application settings."""

import json
from collections import Counter, defaultdict
from decimal import Decimal
from statistics import mean

from analyze_bellup_exhaustion import ROOT, read_rows, series


def catchup(candidate):
    close, mid, upper, lower = [series(candidate, name) for name in
                                ("Close", "BbMidBand", "BbUpperBand", "BbLowerBand")]
    if len({len(x) for x in (close, mid, upper, lower)}) != 1 or len(close) < 2:
        return None
    width = upper[-2] - lower[-2]
    if width <= 0:
        return None
    if mid[-1] <= mid[-2] or close[-1] <= mid[-1]:
        return Decimal(0)
    shrinkage = (mid[-1] - mid[-2]) - (close[-1] - close[-2])
    # Full penalty at a one-bar gap reduction of 10% of prior band width.
    return min(Decimal(1), max(Decimal(0), shrinkage / (width * Decimal("0.10"))))


def load_batches():
    outcomes = {(r["Ticker"], r["ScanTime"]): r for r in
                read_rows(ROOT / "Data/datasets/evaluation-dataset.csv")}
    batches = defaultdict(dict)
    for candidate in read_rows(ROOT / "Data/Tickers/candidates.csv"):
        if candidate["CandidateGroup"] == "Runaway" and candidate["CandidateSource"] not in ("Other", "DiagnosticRejected"):
            batches[candidate["ScanTime"]].setdefault(candidate["Ticker"], candidate)
    usable, excluded = {}, Counter()
    for scan, candidates in sorted(batches.items()):
        rows = []
        for candidate in candidates.values():
            feature = catchup(candidate)
            outcome = outcomes.get((candidate["Ticker"], scan))
            if feature is None or not candidate["ScoreNextDayRank"] or not outcome or outcome["Outcome"] == "NoData" or not outcome["AmplitudePct"]:
                break
            rows.append({"ticker": candidate["Ticker"], "scan": scan,
                         "display_rank": int(candidate["DisplayRank"]),
                         "base_score": Decimal(candidate["ScoreNextDayRank"]), "catchup": feature,
                         "amplitude": float(outcome["AmplitudePct"]), "outcome": outcome["Outcome"]})
        if len(rows) != len(candidates) or len(rows) < 2:
            excluded["incomplete_or_singleton_batches"] += 1
            continue
        rows.sort(key=lambda r: r["display_rank"])
        if any(a["base_score"] < b["base_score"] for a, b in zip(rows, rows[1:])):
            excluded["saved_score_order_disagrees_with_display"] += 1
            continue
        usable[scan] = rows
    return usable, dict(excluded)


def rank(rows, strength):
    return sorted(rows, key=lambda r: (-(r["base_score"] - strength * r["catchup"]), r["display_rank"]))


def metrics(rows):
    return {"n": len(rows), "mean_amplitude": round(mean(r["amplitude"] for r in rows), 3) if rows else None,
            "amplitude_gt_10": sum(r["amplitude"] > 10 for r in rows),
            "outcomes": dict(Counter(r["outcome"] for r in rows))}


def evaluate(batches, strength):
    before, after, changed, drops, moves = [], [], [], [], []
    for scan, rows in sorted(batches.items()):
        shadow = rank(rows, strength)
        before.append(rows[0])
        after.append(shadow[0])
        if rows[0]["ticker"] != shadow[0]["ticker"]:
            changed.append({"scan": scan, "before": rows[0]["ticker"], "after": shadow[0]["ticker"],
                            "before_amplitude": rows[0]["amplitude"], "after_amplitude": shadow[0]["amplitude"],
                            "before_outcome": rows[0]["outcome"], "after_outcome": shadow[0]["outcome"]})
        old_places = {r["ticker"]: i for i, r in enumerate(rows, 1)}
        for i, row in enumerate(shadow, 1):
            if i != old_places[row["ticker"]]:
                moves.append({"scan": scan, "ticker": row["ticker"], "before": old_places[row["ticker"]],
                              "after": i, "amplitude": row["amplitude"], "outcome": row["outcome"]})
            if i > old_places[row["ticker"]] and (row["amplitude"] > 10 or row["outcome"] == "Win"):
                drops.append({"scan": scan, "ticker": row["ticker"], "before": old_places[row["ticker"]],
                              "after": i, "amplitude": row["amplitude"], "outcome": row["outcome"]})
    return {"max_penalty": strength, "baseline_top1": metrics(before), "shadow_top1": metrics(after),
            "top1_changes": changed, "strong_candidate_drops": drops, "rank_changes": moves}


def main():
    batches, excluded = load_batches()
    latest = {}
    for scan in batches:
        latest[scan[:10]] = scan
    days = {scan: batches[scan] for scan in latest.values()}
    print(json.dumps({
        "formula": "saved ScoreNextDayRank - strength * clip(gap shrinkage / (0.10 * previous H4 width), 0, 1); only if mid rises and price stays above mid",
        "coverage": {"eligible_scans": len(batches), "latest_scans_per_day": len(days), "excluded": excluded},
        "limitations": "Exploratory fixed penalties, not selected by outcome. Mixed builds and confirmation timeframes. Complete batches only. Latest eligible scan/day, correlated dates and mixed evaluation horizons. No intrabar reconstruction or live scanner change.",
        "all_scans_sensitivity": [evaluate(batches, s) for s in (25, 50, 100)],
        "latest_per_day": [evaluate(days, s) for s in (25, 50, 100)],
        "later_dates_sep_08_onward": [evaluate({t:r for t,r in days.items() if t >= "2026-09-08"}, s) for s in (25, 50, 100)],
        "latest_scan_shadow_50": [{"ticker": r["ticker"], "original_rank": r["display_rank"], "shadow_rank": i,
                                   "penalty": float(50*r["catchup"]), "outcome": r["outcome"], "amplitude": r["amplitude"]}
                                  for i,r in enumerate(rank(batches[max(batches)],50),1)] if batches else [],
    }, indent=2))


if __name__ == "__main__":
    main()
