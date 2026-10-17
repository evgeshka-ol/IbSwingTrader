# Investigation note template

Create a dated note under `docs/investigations/YYYY-MM/` using
`investigation-YYYY-MM-DD-short-topic.md`. This template is for future notes;
existing investigations retain their original dates, language and results.

```markdown
# Question or case

Date: YYYY-MM-DD.
Status: hypothesis / exploratory study / validated on later dates / superseded.

## Question and success criterion

State the hypothesis and which current Win/profit/confidence objective it
supports. Distinguish supporting amplitude/shape labels from trade outcomes.

## Data and method

Sources, original decision timestamps, feature cutoffs, cohort, participation,
outcome horizon, exclusions and repeated ticker/date handling. Declare the
primary comparison before inspecting outcomes.

## Results

Counts, denominators, winning profit percentages and compatible confidence
comparison where relevant; distinguish Open, NoEntry and missing data.

## Limitations

Coverage, availability at decision time, policy/model versions and whether
later dates provide independent validation.

## Decision

What the evidence supports, what remains untested, and whether any production
change was made. Link the current project reference for any adopted rule.

## Reproduction and artifacts

Link the analysis script and files in docs/artifacts/. State what was actually
executed; application commands, builds and tests remain user-run by default.
```

Record implementation details under `docs/implementation/`. Update the
canonical project skill/reference when behavior changes. Keep study outcomes
historical; use a dated status note/link when later decisions supersede them.
