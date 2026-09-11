# BellUp exit from two previous boosts

Implemented at the user's request; effectiveness has not been validated
across historical trade outcomes. This changes future scanner trade plans,
not existing candidate/evaluation CSV rows.

## Rule

For an admitted, timing-ready Runaway BellUp, use its confirmed timeframe
(Daily or canonical broker H4). Exclude forming/future candles at the scan
timestamp, using the same completion convention as entry timing: H4 start
plus four hours, Daily date plus 16 hours in exchange time.

Starting at the most recent completed candle, walk backwards through the
current confirmed episode. A boost has a positive C-O body at least twice
the absolute body of its immediately preceding candle. Skip other candles,
including ordinary green candles. Wicks and distance to a previous boost
do not define the test. Exactly the two nearest qualifying boosts are used.
The shared `BellUpEntryTiming.IsBoost` keeps entry veto and exit selection
consistent, including the existing green-after-doji behavior.

    exit = round(entry + (latestBoostBody + earlierBoostBody) / 2, 2,
                 MidpointRounding.AwayFromZero)

This replaces the already-built exit after percentage floors and other
exit adjustments; those floors must not inflate the target again. Entry,
stop and stop-limit remain unchanged. ProfitPercent is recalculated from
the new target. ExitProfile becomes `bellup-two-boosts-H4` or
`bellup-two-boosts-Daily`; logs include both candle times, bodies, average,
old exit and new exit. Existing ranking formulas are unchanged, although
the existing profit-percentage tie-break can reflect the changed plan.

## Episode boundary and fallback

There is no separately stored geometric onset timestamp in the current
classifier. The conservative operational boundary is the latest continuous
run of BellUp confirmations on historical prefixes of the selected candle
stream. Reuse BellPatternClassifier, with existing feature-engine bands,
rounding and series window lengths. Stop at the first nonconfirmation;
do not search an earlier episode for a convenient large candle. A boost
at the first confirmed candle can use the predecessor outside the episode
to measure its body ratio, but that predecessor is not selected as a boost.

This is a confirmation boundary, not a claim that geometric onset and
confirmation coincide. Pauses that interrupt classification can shorten
the search. Daily uses the real available D1 stream (existing aggregate
fallback only when D1 is absent); differing confirmation on this stream
causes fallback, not an invented historical onset.

Keep the existing exit and log the fallback when fewer than two boosts
are available, the latest completed prefix is not confirmed, inputs are
insufficient, or price rounding yields a target no higher than entry.
Two-decimal rounding follows the discussed stock-price output convention;
no new exchange tick-size or fee model is introduced.

## VET caveat

The user's illustrative bodies 0.11 and 0.16 average 0.135, giving 13.51
from entry 13.37, provided both selected candles satisfy the boost rule.
The cached September 9 VET bodies are 0.055, 0.085 and 0.1608. Compared
with their immediate predecessors (including -0.04 for the first), none
is at least twice the predecessor's absolute body. They cannot be used as
the two boosts merely because they are green or marked on another vendor's
chart. The implementation therefore does not guarantee a 13.51 VET target.

Reducing a target without reducing the stop changes reward/risk. The
implementation does not claim improved profitability and does not alter
stops without a separately agreed rule.

## Verification

Added a standalone console check project with 16 assertions: skip ordinary
candles, select nearest boosts, episode boundary, no current episode,
invalid inputs, exact 2x comparison, red/doji behavior, VET cached bodies,
rounding, and completed H4/Daily selection.

User commands:

    dotnet run --project tests/BellUpBoostExitChecks
    dotnet run --project tests/ScannerTimingChecks

Build, checks and application runs were not executed by Codex, per project
instructions. Static diff checks passed. On the next user-run scan inspect
`BellUp two-boost exit applied` or `BellUp two-boost exit not applied` in
the log and the saved ExitProfile/ProfitPercent in candidates.csv.
