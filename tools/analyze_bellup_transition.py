"""Offline forward-shape study, not a production BellUp classifier."""

import json
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from decimal import Decimal, ROUND_HALF_UP
from statistics import mean, pstdev

from analyze_bellup_exhaustion import ROOT, read_rows, series, auc


def summary(rows):
    decided = [r for r in rows if r["label"] in ("Convergence", "Continuation")]
    names = sorted({k for r in rows for k in r["features"]})
    return {"events": len(rows), "labels": dict(Counter(r["label"] for r in rows)),
            "auc_convergence_vs_continuation": {
                k: auc([(r["features"][k], r["label"] == "Convergence") for r in decided]) for k in names},
            "double_lower_rise_labels": dict(Counter(r["label"] for r in rows if r["features"]["double_lower_rise"]))}


def signature(values):
    return tuple(Decimal(str(x)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP) for x in values)


def collect():
    caches, events, counts = {}, {}, Counter()
    for row in sorted(read_rows(ROOT / "Data/Tickers/candidates.csv"), key=lambda r: r["ScanTime"]):
        names = ("Open", "High", "Low", "Close", "BbUpperBand", "BbMidBand", "BbLowerBand")
        arrays = [series(row, name) for name in names]
        if len({len(a) for a in arrays}) != 1 or len(arrays[0]) < 10:
            continue
        ticker = row["Ticker"]
        if ticker not in caches:
            path = ROOT / "Data/cache" / (ticker + ".json")
            index = defaultdict(list)
            if path.exists():
                bars = json.loads(path.read_text())["Timeframes"].get("H4", [])
                for i in range(2, len(bars)):
                    key = signature(b[k] for b in bars[i-2:i+1] for k in ("Open", "High", "Low", "Close"))
                    index[key].append(datetime.fromisoformat(bars[i]["Time"]))
            caches[ticker] = index
        op, high, low, close, upper, mid, lower = arrays
        times = []
        scan = datetime.fromisoformat(row["ScanTime"])
        for i in range(len(close)):
            key = signature(a[j] for j in range(i-2, i+1) for a in arrays[:4]) if i >= 2 else ()
            matches = [t for t in caches[ticker].get(key, []) if t + timedelta(hours=4) <= scan]
            times.append(matches[0] if len(matches) == 1 else None)
        for t in range(6, len(close)-3):
            relevant = times[t-3:t+4]
            if any(x is None for x in relevant) or any(a >= b for a, b in zip(relevant, relevant[1:])):
                counts["unmapped_windows"] += 1
                continue
            width = upper[t-1] - lower[t-1]
            gain = close[t-1] - close[t-4]
            if width <= 0 or gain < width * Decimal("0.20") or mid[t-1] <= mid[t-4] or upper[t-1] - lower[t-1] <= upper[t-4] - lower[t-4]:
                continue
            # Label only the next three bars; features stop at t.
            future = close[t+1:t+4]
            convergence = (max(future + [close[t]]) - min(future + [close[t]]) <= gain * Decimal("0.50")
                           and abs(close[t+3] - close[t]) <= gain * Decimal("0.25")
                           and mid[t+3] > mid[t]
                           and 0 < close[t+3] - mid[t+3] < close[t] - mid[t]
                           and upper[t+3] - upper[t] < upper[t] - upper[t-3])
            continuation = close[t+3] > max(high[t-3:t+1]) and close[t+3] - close[t] > gain * Decimal("0.50")
            features = {
                "lower_rise": (lower[t] - lower[t-1]) / width,
                "double_lower_rise": int(lower[t] > lower[t-1] > lower[t-2]),
                "fresh_lower_turn": int(lower[t] > lower[t-1] and lower[t-1] <= lower[t-2]),
                "mid_catchup": ((mid[t] - mid[t-1]) - (close[t] - close[t-1])) / width,
                "mid_rise": (mid[t] - mid[t-1]) / width,
                "price_decline": (close[t-1] - close[t]) / width,
            }
            features["catchup_without_price_drop"] = int(mid[t] > mid[t-1] and 0 <= close[t] - close[t-1] < mid[t] - mid[t-1])
            key = ticker, times[t].isoformat()
            events.setdefault(key, {"ticker": ticker, "time": times[t].isoformat(), "end": times[t+3].isoformat(),
                                    "snapshot": row["ScanTime"], "features": {k: float(v) for k, v in features.items()},
                                    "label": "Convergence" if convergence else "Continuation" if continuation else "Other"})
    # Select non-overlapping forward windows before inspecting their labels.
    rows, ends = [], {}
    for row in sorted(events.values(), key=lambda r: (r["time"], r["ticker"])):
        if row["time"] <= ends.get(row["ticker"], ""):
            continue
        rows.append(row)
        ends[row["ticker"]] = row["end"]
    return rows, {"unique_events": len(events), **counts}


def secz_partial():
    data = json.loads((ROOT / "Data/cache/SECZ.json").read_text())["Timeframes"]
    scan = datetime(2026, 9, 9, 9, 49, 58)
    start = scan.replace(hour=8, minute=0, second=0)
    fine = sorted([b for b in data["M5"] if start <= datetime.fromisoformat(b["Time"]) and
                   datetime.fromisoformat(b["Time"]) + timedelta(minutes=5) <= scan], key=lambda b: b["Time"])
    expected = [start + timedelta(minutes=5*i) for i in range(int((scan-start).total_seconds() // 300))]
    if [datetime.fromisoformat(b["Time"]) for b in fine] != expected:
        return {"error": "Incomplete M5 grid"}
    history = [b["Close"] for b in sorted(data["H4"], key=lambda b: b["Time"]) if datetime.fromisoformat(b["Time"]) < start]
    def bands(closes):
        values = closes[-20:]
        m, s = mean(values), pstdev(values)
        return {"upper": round(m+2*s, 4), "mid": round(m, 4), "lower": round(m-2*s, 4)}
    candidate = next(row for row in read_rows(ROOT / "Data/Tickers/candidates.csv")
                     if row["Ticker"] == "SECZ" and row["ScanTime"] == "2026-09-09 09:49:58")
    reference = float(candidate["TradePlanLiveReferencePrice"])
    return {"basis": "Reconstructed 20-close SMA +/- 2 population SD; not Yahoo or a replay of live feature engine",
            "completed_h4": bands(history), "partial_h4": bands(history+[fine[-1]["Close"]]),
            "partial_close": fine[-1]["Close"], "known_through": (datetime.fromisoformat(fine[-1]["Time"])+timedelta(minutes=5)).isoformat(),
            "m5_bars": len(fine),
            "quote_sensitivity_only": {"reference": reference, "bands": bands(history+[reference]),
                                       "caveat": "Saved trade-plan reference is not a timestamp-verified H4 close at scan time"}}


def main():
    rows, coverage = collect()
    print(json.dumps({"coverage": coverage, "all": summary(rows),
                      "before_sep_01": summary([r for r in rows if r["time"] < "2026-09-01"]),
                      "from_sep_01": summary([r for r in rows if r["time"] >= "2026-09-01"]),
                      "secz_partial": secz_partial(),
                      "limitations": "Historical subwindows selected from later saved snapshots, not prospective BellUp admissions. Cache OHLC only maps timestamps, does not supply features. Mixed groups, rounded series, repeated tickers, survivor selection and heuristic labels. No trade-return claim."}, indent=2))


if __name__ == "__main__":
    main()
