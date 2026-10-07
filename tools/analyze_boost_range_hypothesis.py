"""Offline boost definition screen; no scanner, broker requests or production edits."""
import csv
import json
import re
import statistics
from collections import Counter
from datetime import datetime, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def dt(value):
    return datetime.fromisoformat(value).replace(tzinfo=None)


def candle_features(bars, index, lookback):
    candle, previous = bars[index], bars[index - 1]
    body = candle["Close"] - candle["Open"]
    span = candle["High"] - candle["Low"]
    previous_body = abs(previous["Close"] - previous["Open"])
    previous_span = previous["High"] - previous["Low"]
    history = bars[max(0, index - lookback):index]
    typical = statistics.median(b["High"] - b["Low"] for b in history) if history else 0
    return dict(body=body, range=span, median_range=typical,
                range_ratio=span / typical if typical > 0 and len(history) == lookback else None,
                body_fraction=body / span if span > 0 else 0,
                close_position=(candle["Close"] - candle["Low"]) / span if span > 0 else 0,
                old_boost=body > 0 and body + 1e-9 >= 2 * previous_body,
                previous_range_boost=body > 0 and span + 1e-9 >= 2 * previous_span)


def new_boost(features, multiplier, direction="green"):
    ratio = features["range_ratio"]
    directional = features["body"] > 0 if direction == "green" else features["body_fraction"] >= 0.35 and features["close_position"] >= 0.6
    return ratio is not None and ratio >= multiplier and directional


def main():
    with (ROOT / "Data/Tickers/candidates.csv").open(encoding="utf-8-sig", newline="") as f:
        candidates = list(csv.DictReader(f))
    cache = {}
    with (ROOT / "Data/datasets/evaluation-dataset.csv").open(encoding="utf-8-sig", newline="") as f:
        outcomes = {(x["Ticker"], x["ScanTime"]): x["Outcome"] for x in csv.DictReader(f)}
    output = []
    for row in candidates:
        rejected = "Recent boost" in row["PatternVerdictReason"]
        if row["ScanTime"] < "2026-09-23" or not row["PatternVerdictReason"].startswith("BellUp confirmed on "):
            continue
        if not rejected and row["CandidateGroup"] != "BellUp":
            continue
        ticker = row["Ticker"]
        if ticker not in cache:
            path = ROOT / f"Data/cache/{ticker}.json"
            cache[ticker] = json.loads(path.read_text()).get("Timeframes", {}) if path.exists() else {}
        match = re.search(r"(H4|Daily): BellUp boost on T-1 \(([^)]+)\)", row["ContextNotes"])
        frame = match[1] if match else ("H4" if "confirmed on H4" in row["PatternVerdictReason"] else "Daily")
        source = sorted(cache[ticker].get("H4" if frame == "H4" else "D1", []), key=lambda b: b["Time"])
        clock = dt(row["RunStartedAt"] or row["ScanTime"])
        bars = [b for b in source if (dt(b["Time"]) + timedelta(hours=4) if frame == "H4" else dt(b["Time"]).replace(hour=16)) <= clock]
        if match:
            bars = [b for b in source if dt(b["Time"]) <= dt(match[2])]
        item = dict(ticker=ticker, scan=row["ScanTime"], rejected=rejected, timeframe=frame,
                    reported_outcome=outcomes.get((ticker, row["ScanTime"])), status="")
        output.append(item)
        if len(bars) < 9:
            item["status"] = "insufficient_history"
            continue
        item["bar_time"] = bars[-1]["Time"]
        item["features"] = {str(n): candle_features(bars, len(bars) - 1, n) for n in (5, 8)}
        if rejected and not item["features"]["5"]["old_boost"]:
            item["status"] = "old_rule_not_reproduced"
            continue
        if not rejected and item["features"]["5"]["old_boost"]:
            item["status"] = "control_timing_not_reproduced"
            continue
        if any(v["range_ratio"] is None for v in item["features"].values()):
            item["status"] = "zero_median_range"
            continue
        item["status"] = "consistent"
        start = dt(row["PublishedAt"] or row["ScanTime"])
        future = sorted((b for b in cache[ticker].get("M5", []) if start <= dt(b["Time"]) <= start + timedelta(days=10)), key=lambda b: b["Time"])
        if future:
            price = future[0]["Open"]
            if price > 0:
                max_index = max(range(len(future)), key=lambda i: future[i]["High"])
                min_index = min(range(len(future)), key=lambda i: future[i]["Low"])
                item["future"] = dict(reference_open=price, first_bar=future[0]["Time"],
                    last_bar=future[-1]["Time"], bars=len(future), first_volume=future[0]["Volume"],
                    max_up_pct=(max(b["High"] for b in future) / price - 1) * 100,
                    min_pct=(min(b["Low"] for b in future) / price - 1) * 100,
                    min_time=future[min_index]["Time"], max_time=future[max_index]["Time"],
                    min_before_max_pct=(min(b["Low"] for b in future[:max_index + 1]) / price - 1) * 100)
    summaries = []
    valid = [x for x in output if x["status"] == "consistent"]
    for lookback, multiplier, direction in ((n, m, d) for n in (5, 8) for m in (1.5, 2, 2.5) for d in ("green", "directional")):
            rejected = [x for x in valid if x["rejected"]]
            released = [x for x in rejected if not new_boost(x["features"][str(lookback)], multiplier, direction)]
            controls = [x for x in valid if not x["rejected"]]
            blocked = [x for x in controls if new_boost(x["features"][str(lookback)], multiplier, direction)]
            observed = [x for x in released if "future" in x]
            summaries.append(dict(lookback=lookback, multiplier=multiplier, direction=direction, rejected=len(rejected),
                retained=len(rejected) - len(released), released=len(released),
                released_unique_tickers=len({x["ticker"] for x in released}),
                released_observed=len(observed), released_max5=sum(x["future"]["max_up_pct"] >= 5 for x in observed),
                released_max10=sum(x["future"]["max_up_pct"] >= 10 for x in observed),
                released_median_max=statistics.median(x["future"]["max_up_pct"] for x in observed) if observed else None,
                control_plans=len(controls), newly_blocked=len(blocked),
                newly_blocked_outcomes=dict(Counter(x["reported_outcome"] or "not_evaluated" for x in blocked))))
    peng = sorted(cache.get("PENG", {}).get("H4", []), key=lambda b: b["Time"])
    examples = [dict(time=b["Time"], features=candle_features(peng, i, 5)) for i, b in enumerate(peng)
                if i >= 5 and b["Time"] in ("2026-10-02T08:00:00", "2026-10-05T16:00:00", "2026-10-06T12:00:00", "2026-10-06T16:00:00")]
    result = dict(statuses=dict(Counter(x["status"] for x in output)), summaries=summaries, peng=examples, rows=output)
    (ROOT / "docs/analysis-2026-10-07-boost-range.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({k: v for k, v in result.items() if k != "rows"}, indent=2))


if __name__ == "__main__":
    main()
