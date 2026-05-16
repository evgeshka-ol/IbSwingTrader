# Workflow Checklist

Daily loop:

1. Run `get-candidates`.
2. Run `evaluate-candidates`.
3. Run `build-research-dataset`.
4. Compare:
   - yesterday's scanner
   - today's research
5. Decide what kind of problem you have:
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

### If winners are in summary but `NoEntry` with strong amplitude

Fix `TradePlan`.

### If summary names repeatedly show low amplitude

Fix scanner ranking and stale-candidate suppression.
