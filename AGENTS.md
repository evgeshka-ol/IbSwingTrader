# Repository guidance

## Project instructions

Before changing scanner, evaluation, or research behavior, read the matching
project skill under `docs/project-skills/` and its relevant references:

- Scanner and candidate groups: `ibswingtrader-scanner/SKILL.md`
- Evaluation and outcome interpretation: `ibswingtrader-evaluation/SKILL.md`
- Research oracle and recall: `ibswingtrader-research/SKILL.md`

These files hold the detailed domain rules. Keep this file short and limited
to guidance that applies across the repository.

## Current candidate groups

The scanner classifies candidates into `BellUp`, `ReversalHook`, and `Other`.
Do not apply the former Daily-mid `Runaway`/`Reversal` family split as the
candidate-group definition. Those names can remain in legacy fields, older
datasets, and dated analysis notes. See the scanner skill for current
classification and output behavior.

## Responsibility boundary

Codex may inspect and edit source, analysis tools, and documentation. The
user runs application commands, broker-backed scans, builds, and tests unless
they explicitly authorize Codex to run them.

## Documentation maintenance

Treat dated investigation notes as historical records. When implementation
changes, update the current project skills and references without rewriting
old results as if they described current behavior. Label legacy terminology
where it could be mistaken for the current output contract.
