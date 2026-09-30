# Documentation map

## Current project instructions

Project skills are the canonical operating references. Start with the skill
for the work area, then follow its `Read these references` list:

- [`ibswingtrader-scanner`](project-skills/ibswingtrader-scanner/SKILL.md):
  scanner behavior, candidate groups, ranking, timing, and settings.
- [`ibswingtrader-evaluation`](project-skills/ibswingtrader-evaluation/SKILL.md):
  outcome interpretation and trade-plan scope.
- [`ibswingtrader-research`](project-skills/ibswingtrader-research/SKILL.md):
  next-day research oracle and recall workflow.

Repository-wide agent instructions live in the root [`AGENTS.md`](../AGENTS.md).
Keep cross-cutting rules there; keep detailed domain guidance in the matching
project skill.

## Historical investigations

Top-level dated notes record the data, hypotheses, and decisions from a
particular investigation. They are historical evidence, not automatic
descriptions of current implementation. The latest implementation contract
is in the project skills and their references.

Notable current-era notes include:

- BellUp timing, exhaustion, and exit investigations (`BELLUP_*.md`)
- Triangle detector and cache research (`TRIANGLE_*.md`)
- evaluation semantics and CSV review (`EVALUATION_*.md`,
  `CANDIDATE_CSV_REVIEW.md`)

When a code change supersedes a historical rule, update the canonical skill
and label old terminology in the historical note where it could mislead a
reader. Preserve original study results and caveats.
