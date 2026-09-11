# Evaluation amplitude display

`AmplitudePct` remains a positive, directionless range. Direction is already
available in `ExtremumOrder`: `MinFirst`, `MaxFirst`, or `SameBar`.

This keeps numeric sorting, filters and historical AUC calculations intact.
For a human-facing table, the desired visual convention is to render the
range as `|4.3|` (or `|11.68|`). The vertical bars are display decoration,
not part of the CSV numeric field. Adding them to `AmplitudePct` itself
would break decimal parsing and downstream calculations.

SMR therefore remains `11.68` with `ExtremumOrder=MaxFirst`; it is not a
positive trade signal despite the positive magnitude. SameBar remains
ambiguous only at the available candle granularity.

`EvaluationAmplitude.WithDirection` now normalizes every value with
`Math.Abs`, including historical negative rows on the next successful
dataset write. MinAmplitudePct filtering and duplicate selection continue
to use the magnitude. New and existing rows remain schema-compatible.

Run `dotnet run --project tests/EvaluationAmplitudeChecks` after rebuilding.
