# Signed evaluation amplitude

`AmplitudePct` is signed by extremum order so a human reviewing the table
can immediately skip large downward excursions:

- `MinFirst`: positive magnitude, the favorable high came after the low.
- `MaxFirst`: negative magnitude, the price reached its high first and then
  fell. Example: SLS `-15.71`, OLMA `-48.27`.
- `SameBar` or missing timestamps: positive magnitude because intrabar order
  is unknown; inspect `ExtremumOrder` for that ambiguity.

The magnitude and denominator are unchanged: `(high - low) / low * 100`,
rounded as before. This is not realized trade P&L or a percentage decline
from the high. `ExtremumOrder` remains exported as the explicit direction
field.

All numeric filters and duplicate selection use `Math.Abs(AmplitudePct)`,
so negative rows remain in the dataset. GroupLabel classification uses the
signed value: `MaxFirst` rows are not promoted as positive trade candidates.
Existing historical negative values are re-normalized against current
extremum timestamps during the next successful dataset write. New rows get
the sign when built.

The report may render the numeric value with visual bars, e.g. `|-48.27|`,
but bars must remain display decoration and must not be written into the CSV
numeric field.

Run after rebuilding:

    dotnet run --project tests/EvaluationAmplitudeChecks

The test project covers cross-day order, same-bar/missing timestamps,
zero values and idempotent sign normalization. Build/tests/evaluation were
not run by Codex.
