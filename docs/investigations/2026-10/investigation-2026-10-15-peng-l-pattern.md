# PENG Daily: Л-Pattern and loser-watch enrollment

Recorded 2026-10-15 from the user's chart observation and local saved data.

The user calls a smooth sloping rise followed by a large red fall toward the
Bollinger middle band Л-Pattern, after its resemblance to the Cyrillic Л.
The preceding rise may resemble BellUp. PENG Daily on October 14 is a useful
example. The user identifies the strong October 13 upper-band bend as the
useful warning available before that fall.

Chart: `Data/Charts/PENG-D-Л-Pattern.png`. The displayed October 14 candle is
O76.63/H77.41/L67.21/C67.59, with Bollinger mid about 61.63. Thus the fall
approached the mid but neither its low nor its close crossed below it.
The displayed premarket 65.20 is a later quote, not the October 14 scan price.

Saved candidate evidence, joined here only by ticker and scan timestamp:

- October 12 04:09:04: BellUp, confirmed on Daily, ScanPrice 76.40.
- October 12 10:05:13: Other, Daily BellUp rejected as Late phase.
- October 13 04:50:25: Other, Daily BellUp rejected as Not ready.
- October 14 05:54:49: playable BellUp, confirmed on Daily, ScanPrice 77.32.
  Its saved upper-band tail is 73.41 -> 76.81 -> 79.35, whose increments
  slow from 3.40 to 2.54; saved mid ends at 60.68. Upper-band slowing was
  therefore visible in that snapshot, before the October 14 daily fall.

Current native D1 cache recalculation using SMA20 and population Bollinger
deviation gives upper 73.3852/76.7848/79.3184 for October 9/12/13 and
mid 61.6325 on October 14. These slightly differ from saved scanner rows;
the later cache is corroborating evidence, not an exact original replay.
The completed October 14 candle cannot justify an earlier scan decision.

One example does not validate a universal upper-band-kink veto. Earlier
band-kink studies found weak standalone signal; preserve that limitation.
Next research should distinguish successful continuation pauses from actual
Л-shaped failures using scan-time features and later fixed-horizon labels.
No Л detector or ranking/admission penalty was added here.

The user's separate wishlist requirement was confirmed explicitly: compare
current price after a loser sighting to the last completed Daily Bollinger
mid. Since October 15 the source code enrolls/renews only when current price
is strictly below that mid. Fresh M5 supplies current price; historical
Daily close is not substituted. Unknown price/history does not qualify.
Existing stored watch rows are retained; recognition of a previously seeded
ReversalHook above mid remains independent. See SETTINGS_MAP.md for details.

Local chart, CSV, log and cache inspection only. Builds, tests, scanner and
broker requests were not run. Actual behavior needs a user-run scan.
