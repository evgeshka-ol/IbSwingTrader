"""Descriptive cache-only shape search; not a scanner rule or outcome backtest."""

import csv
import json
import math
from collections import Counter
from datetime import datetime, timedelta
from pathlib import Path
from statistics import median

ROOT = Path(__file__).resolve().parents[1]


def mean(values):
    return sum(values)/len(values)

PROFILES = {
    "strict": (0.30, 0.25, 0.15, 0.70),
    "base": (0.45, 0.40, 0.25, 0.80),
    "loose": (0.60, 0.55, 0.35, 0.90),
}


def deduplicate(rows):
    episodes = []
    for row in sorted(rows, key=lambda r: (r["end"], r["start"])):
        if episodes and row["start"] <= episodes[-1]["last_end"]:
            episodes[-1]["last_end"] = max(episodes[-1]["last_end"], row["end"])
            continue
        episodes.append(dict(row, last_end=row["end"]))
    return episodes


def search(bars, ticker, timeframe):
    close = [b["Close"] for b in bars]
    body = [abs(b["Close"] - b["Open"]) for b in bars]
    bands = [None] * len(bars)
    for i in range(19, len(bars)):
        window = close[i-19:i+1]
        mid = mean(window)
        sd = math.sqrt(sum((x-mid)**2 for x in window)/20)
        bands[i] = (mid+2*sd, mid, mid-2*sd, 4*sd)
    hits = {p: {"plateau": [], "oval": []} for p in PROFILES}
    for peak in range(25, len(bars)-8):
        for ramp in (1, 3, 6):
            start = peak-ramp
            gain = close[peak]-close[start]
            if gain < max(close[start]*0.05, bands[start][3]*0.75) or gain <= 0:
                continue
            up = sum(max(0, close[j]-close[j-1]) for j in range(start+1, peak+1))
            if up <= 0 or gain/up < 0.75:
                continue
            ramp_body = mean(body[start+1:peak+1])
            # A gap can produce tiny bodies throughout; do not invent a body contraction.
            if ramp_body <= 0:
                continue
            for length in (8, 12, 16, 20):
                end = peak+length
                if end >= len(bars):
                    continue
                tail = close[peak+1:end+1]
                spread = (max(tail)-min(tail))/gain
                distance = max(abs(x-close[peak]) for x in tail)/gain
                drift = abs(mean(tail[-3:])-mean(tail[:3]))/gain
                bodies = median(body[peak+1:end+1])/ramp_body
                widths = [bands[j][3] for j in range(start, end+1)]
                top = max(range(len(widths)), key=widths.__getitem__)
                width_peak = widths[top]
                expansion = width_peak/max(bands[start][3], 1e-12)
                contraction = bands[end][3]/max(width_peak, 1e-12)
                down = widths[top:]
                travel = sum(abs(b-a) for a, b in zip(down, down[1:]))
                smooth = (down[0]-down[-1])/travel if travel else 0
                turn = start+top
                oval = (expansion >= 1.3 and end-turn >= 3 and smooth >= 0.65
                        and bands[end][0] < bands[turn][0]-gain*0.05
                        and bands[end][2] > bands[turn][2]+gain*0.10)
                row = dict(ticker=ticker, timeframe=timeframe,
                           start=bars[start]["Time"], ramp_end=bars[peak]["Time"],
                           end=bars[end]["Time"], ramp_bars=ramp, plateau_bars=length,
                           gain_pct=round(100*gain/close[start], 2),
                           body_ratio=round(bodies, 3), spread_ratio=round(spread, 3),
                           drift_ratio=round(drift, 3), contraction=round(contraction, 3),
                           smoothness=round(smooth, 3),
                           positive_volume_fraction=round(sum(b["Volume"] > 0 for b in bars[peak+1:end+1])/length, 3))
                for name, (s, b, d, c) in PROFILES.items():
                    if spread <= s and distance <= s and bodies <= b and drift <= d:
                        hits[name]["plateau"].append(row)
                        if oval and contraction <= c:
                            hits[name]["oval"].append(row)
    return hits


def main():
    results = {p: {k: [] for k in ("plateau", "oval")} for p in PROFILES}
    coverage = {t: Counter() for t in ("H4", "D1")}
    dates = {t: [] for t in coverage}
    for file_index, path in enumerate(sorted((ROOT/"Data/cache").glob("*.json"))):
        if file_index % 250 == 0:
            print(f"Processed {file_index} files", flush=True)
        data = json.loads(path.read_text())
        if not isinstance(data, dict) or not isinstance(data.get("Timeframes"), dict):
            continue
        for tf in coverage:
            raw = data.get("Timeframes", {}).get(tf, [])
            # Exclude today's potentially unfinished bars. No future bars used per window.
            bars = sorted({b["Time"]: b for b in raw if b["Time"][:10] < "2026-09-17"
                           and min(b["Open"], b["Close"], b["High"], b["Low"]) > 0}.values(),
                          key=lambda b: b["Time"])
            if len(bars) < 34:
                continue
            coverage[tf]["tickers"] += 1
            coverage[tf]["bars"] += len(bars)
            dates[tf].extend((bars[0]["Time"], bars[-1]["Time"]))
            # Split obvious holes; weekends/holidays remain inside a segment.
            segments = [[]]
            for bar in bars:
                if segments[-1] and (datetime.fromisoformat(bar["Time"])-datetime.fromisoformat(segments[-1][-1]["Time"])).days > 7:
                    segments.append([])
                segments[-1].append(bar)
            for segment in segments:
                if len(segment) < 34:
                    continue
                found = search(segment, path.stem, tf)
                for profile in results:
                    for kind in results[profile]:
                        results[profile][kind].extend(deduplicate(found[profile][kind]))
    summary = {"coverage": {tf: dict(c, first=min(dates[tf]), last=max(dates[tf])) for tf, c in coverage.items()}, "profiles": {}}
    for profile, kinds in results.items():
        summary["profiles"][profile] = {}
        for kind, rows in kinds.items():
            summary["profiles"][profile][kind] = {
                tf: dict(episodes=len(selected := [r for r in rows if r["timeframe"] == tf]),
                         tickers=len({r["ticker"] for r in selected}),
                         multi_bar_ramps=sum(r["ramp_bars"] > 1 for r in selected)) for tf in coverage}
    out = Path("/tmp/triangle-history")
    out.mkdir(exist_ok=True)
    (out/"summary.json").write_text(json.dumps(summary, indent=2))
    for kind, rows in results["base"].items():
        with (out/f"{kind}.csv").open("w", newline="") as f:
            if rows:
                writer = csv.DictWriter(f, fieldnames=list(rows[0]))
                writer.writeheader()
                writer.writerows(rows)
    print(json.dumps(summary, indent=2))
    print("Recent base oval examples:")
    print(json.dumps(sorted(results["base"]["oval"], key=lambda r:r["end"], reverse=True)[:20], indent=2))


if __name__ == "__main__":
    main()
