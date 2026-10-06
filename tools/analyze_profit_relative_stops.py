"""Offline risk-cap replay; production plans, settings and datasets remain unchanged."""
import csv
import json
from collections import Counter
from datetime import datetime, timedelta
from decimal import Decimal, ROUND_HALF_UP, ROUND_CEILING
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
D = Decimal
CENT = D("0.01")


def read(path):
    with path.open(encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def dt(value):
    return datetime.fromisoformat(value).replace(tzinfo=None)


def replay(bars, target, stop, entry_time=None, detect_gaps=False):
    for bar in bars:
        low, high = D(str(bar["Low"])), D(str(bar["High"]))
        win, loss = low <= target <= high, low <= stop <= high
        if detect_gaps and high < stop:
            # A tighter stop can be jumped over. Stop/stop-limit fill cannot be inferred from M5.
            return "GapBelowStop", bar["Time"]
        if win and loss:
            return "Ambiguous", bar["Time"]
        if (win or loss) and entry_time is not None and dt(bar["Time"]) == entry_time:
            return "EntryBarAmbiguous", bar["Time"]
        if win or loss:
            return ("Win" if win else "Loss"), bar["Time"]
    return "Open", ""


def main():
    candidates = {(r["Ticker"], r["ScanTime"]): r for r in read(ROOT / "Data/Tickers/candidates.csv")}
    cached = {}
    output = []
    for row in read(ROOT / "Data/datasets/evaluation-dataset.csv"):
        key = row["Ticker"], row["ScanTime"]
        snapshot = candidates.get(key)
        if not snapshot or snapshot["CandidateGroup"] != "BellUp" or key[1] < "2026-09-23":
            continue
        entry, original_target, original_stop = (D(snapshot[k]) for k in ("EntryPrice", "ExitPrice", "StopLoss"))
        if not (0 < original_stop < entry < original_target):
            continue
        # Do not halve current plans a second time. Normalize historical plans to current policy.
        target = original_target
        if "-profit-reduced" not in snapshot["ExitProfile"] and original_target >= entry * D("1.10"):
            target = max(((entry + original_target) / 2).quantize(CENT, rounding=ROUND_HALF_UP),
                         (entry * D("1.015")).quantize(CENT, rounding=ROUND_HALF_UP))
        stops = {"baseline": original_stop,
                 "risk_100": max(original_stop, (entry - (target - entry)).quantize(CENT, rounding=ROUND_CEILING)),
                 "risk_75": max(original_stop, (entry - (target - entry) * D("0.75")).quantize(CENT, rounding=ROUND_CEILING))}
        item = dict(ticker=key[0], scan=key[1], reported=row["Outcome"], profile=snapshot["ExitProfile"],
                    entry=str(entry), target=str(target), original_target=str(original_target),
                    stops={k: str(v) for k, v in stops.items()}, status="", scenarios={})
        output.append(item)
        if not row["EntryTime"]:
            item["status"] = "no_entry"
            continue
        if key[0] not in cached:
            path = ROOT / f"Data/cache/{key[0]}.json"
            cached[key[0]] = json.loads(path.read_text()).get("Timeframes", {}).get("M5", []) if path.exists() else []
        start = dt(row["EntryTime"])
        end = min(dt(row["EvaluatedAt"]), dt(key[1]) + timedelta(days=10))
        bars = sorted((b for b in cached[key[0]] if start <= dt(b["Time"]) <= end), key=lambda b: b["Time"])
        if not bars or dt(bars[0]["Time"]) != start:
            item["status"] = "missing_entry_bar"
            continue
        original, _ = replay(bars, original_target, original_stop)
        high = max(D(str(b["High"])) for b in bars).quantize(CENT, rounding=ROUND_HALF_UP)
        low = min(D(str(b["Low"])) for b in bars).quantize(CENT, rounding=ROUND_HALF_UP)
        if original != row["Outcome"] or high != D(row["MaxPrice"]) or low != D(row["MinPrice"]):
            item["status"] = "report_cache_mismatch"
            continue
        last_close = D(str(bars[-1]["Close"]))
        for name, stop in stops.items():
            changed = target != original_target or stop != original_stop
            outcome, timestamp = replay(bars, target, stop, start if changed else None, detect_gaps=True)
            realized = (target / entry - 1) * 100 if outcome == "Win" else (stop / entry - 1) * 100 if outcome == "Loss" else None
            mark = realized if realized is not None else (last_close / entry - 1) * 100 if outcome == "Open" else None
            item["scenarios"][name] = dict(outcome=outcome, time=timestamp,
                                          realized=float(realized) if realized is not None else None,
                                          marked=float(mark) if mark is not None else None,
                                          risk_pct=float((1 - stop / entry) * 100))
        item["last_bar"] = bars[-1]["Time"]
        item["status"] = "consistent" if all(v["outcome"] in ("Win", "Loss", "Open") for v in item["scenarios"].values()) else "ambiguous"
    usable = [x for x in output if x["status"] == "consistent"]
    summary = {}
    for scope, subset in (("all", usable), ("oct5", [x for x in usable if x["scan"].startswith("2026-10-05")]),
                          ("reduced_targets", [x for x in usable if x["target"] != x["original_target"] or "-profit-reduced" in x["profile"]]),
                          ("baseline_closed", [x for x in usable if x["scenarios"]["baseline"]["outcome"] in ("Win", "Loss")])):
        summary[scope] = {}
        for name in ("baseline", "risk_100", "risk_75"):
            scenarios = [x["scenarios"][name] for x in subset]
            summary[scope][name] = dict(n=len(subset), outcomes=dict(Counter(v["outcome"] for v in scenarios)),
                closed_sum=round(sum(v["realized"] or 0 for v in scenarios), 4),
                marked_sum=round(sum(v["marked"] or 0 for v in scenarios), 4),
                transitions=dict(Counter(x["scenarios"]["baseline"]["outcome"] + "->" + x["scenarios"][name]["outcome"] for x in subset)),
                changed_stops=sum(x["stops"][name] != x["stops"]["baseline"] for x in subset))
    result = dict(statuses=dict(Counter(x["status"] for x in output)), summary=summary, rows=output)
    (ROOT / "docs/analysis-2026-10-06-profit-relative-stops.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(dict(statuses=result["statuses"], summary=summary), indent=2))


if __name__ == "__main__":
    main()
