# Workflow Checklist

Daily loop:

1. Run `get-candidates`.
2. Run `evaluate-candidates`.
3. Run `build-research-dataset`.
4. Compare:
   - yesterday's scanner
   - today's research
5. Decide what kind of problem you have:
   - `RunawayCandidates -> next-day research` miss
   - recall
   - `WishList -> TodayResearchLike` promotion
   - ranking
   - `TradePlan`

## Decision guide

### If winners are absent from logs

Fix recall/universe/filtering.

### If winners are in logs or `WishList` only

Fix promotion and gating.

### If winners are in full candidates but not summary

Fix ranking.

### If `RunawayCandidates` summary names do not become research winners

Fix scanner selection/ranking first.

This is the top-priority feedback loop.

### If winners are in summary but `NoEntry` with strong amplitude

Fix `TradePlan`.

### If summary names repeatedly show low amplitude

Fix scanner ranking and stale-candidate suppression.

### If many rows are `Loss`, `NoEntry`, or `Open` but amplitude is strong

Fix `TradePlan`.

The scanner found movement; execution failed to convert it.
