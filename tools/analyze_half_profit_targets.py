"""Offline BellUp target hypothesis. Reads snapshots/cache; never changes production data."""
import csv
import json
from collections import Counter
from datetime import datetime, timedelta
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
D = Decimal


def rows(path):
    with path.open(encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def time(value):
    return datetime.fromisoformat(value).replace(tzinfo=None)


def replay(bars, target, stop):
    for bar in bars:
        lo, hi = D(str(bar["Low"])), D(str(bar["High"]))
        win, loss = lo <= target <= hi, lo <= stop <= hi
        if win and loss:
            return "Ambiguous", bar["Time"]
        if win or loss:
            return ("Win" if win else "Loss"), bar["Time"]
    return "Open", ""


def main():
    candidates = {(r["Ticker"], r["ScanTime"]): r for r in rows(ROOT / "Data/Tickers/candidates.csv")}
    dataset = rows(ROOT / "Data/datasets/evaluation-dataset.csv")
    cache = {}
    output = []
    for row in dataset:
        key = row["Ticker"], row["ScanTime"]
        snapshot = candidates.get(key)
        # Current production contract, not retrospectively relabelled legacy groups.
        if not snapshot or snapshot["CandidateGroup"] != "BellUp" or row["ScanTime"] < "2026-09-23":
            continue
        entry, target, stop = (D(snapshot[k]) for k in ("EntryPrice", "ExitPrice", "StopLoss"))
        if entry <= 0:
            continue
        changed = (target / entry - 1) * 100 > 10
        new_target = ((entry + target) / 2).quantize(D("0.01"), rounding=ROUND_HALF_UP) if changed else target
        item = dict(ticker=key[0], scan=key[1], old=row["Outcome"], changed=changed,
                    entry=str(entry), stop=str(stop), target=str(target), new_target=str(new_target),
                    old_profit=float((target / entry - 1) * 100),
                    new_profit=float((new_target / entry - 1) * 100), status="")
        output.append(item)
        if not row["EntryTime"]:
            item.update(status="no_entry", new=row["Outcome"])
            continue
        if key[0] not in cache:
            path = ROOT / f"Data/cache/{key[0]}.json"
            cache[key[0]] = json.loads(path.read_text()).get("Timeframes", {}).get("M5", []) if path.exists() else []
        start = time(row["EntryTime"])
        end = min(time(row["EvaluatedAt"]), time(row["ScanTime"]) + timedelta(days=10))
        bars = sorted((b for b in cache[key[0]] if start <= time(b["Time"]) <= end), key=lambda b: b["Time"])
        if not bars or time(bars[0]["Time"]) != start:
            item["status"] = "missing_entry_bar"
            continue
        original, original_time = replay(bars, target, stop)
        new, new_time = replay(bars, new_target, stop)
        item.update(replay_old=original, new=new, new_time=new_time, original_time=original_time,
                    bars=len(bars), last_bar=bars[-1]["Time"])
        if original != row["Outcome"]:
            item["status"] = "baseline_mismatch"
            continue
        # Match reported excursions as an additional cache consistency check.
        hi = max(D(str(b["High"])) for b in bars)
        lo = min(D(str(b["Low"])) for b in bars)
        if (hi.quantize(D("0.01"), rounding=ROUND_HALF_UP) != D(row["MaxPrice"]) or
                lo.quantize(D("0.01"), rounding=ROUND_HALF_UP) != D(row["MinPrice"])):
            item["status"] = "excursion_mismatch"
            continue
        if new == "Ambiguous":
            item["status"] = "ambiguous_target_stop"
            continue
        if changed and new_time and time(new_time) == start:
            item["status"] = "entry_exit_same_bar"
            continue
        item["status"] = "consistent"
        item["old_realized"] = item["old_profit"] if row["Outcome"] == "Win" else float((stop / entry - 1) * 100) if row["Outcome"] == "Loss" else 0
        item["new_realized"] = item["new_profit"] if new == "Win" else float((stop / entry - 1) * 100) if new == "Loss" else 0

    dest = ROOT / "docs/analysis-2026-10-05-half-profit-targets.json"
    dest.write_text(json.dumps(output, indent=2, ensure_ascii=False) + "\n")
    print("TOTAL", len(output), "STATUS", dict(Counter(x["status"] for x in output)))
    for label, selected in (("all", output), ("friday", [x for x in output if x["scan"].startswith("2026-10-02")])):
        valid = [x for x in selected if x["status"] in ("consistent", "no_entry")]
        affected = [x for x in valid if x["changed"]]
        print(label, "n", len(valid), "affected", len(affected), "old", dict(Counter(x["old"] for x in valid)), "new", dict(Counter(x["new"] for x in valid)))
        print("transitions", dict(Counter(x["old"] + "->" + x["new"] for x in affected)))
        print("closed-profit-sum", round(sum(x.get("old_realized", 0) for x in valid), 3), round(sum(x.get("new_realized", 0) for x in valid), 3))
    for item in output:
        if item["status"] == "consistent" and (item["old"] != item["new"] or item["scan"].startswith("2026-10-02")):
            print(json.dumps(item, ensure_ascii=False))


if __name__ == "__main__":
    main()
