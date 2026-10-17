# Documentation map

## Start here

- [Current success criteria](guides/success-criteria.md): user objective confirmed
  2026-10-17; entered-plan winners, their profit, confidence calibration and the
  minimum rank-1 winner goal (1.5% profit sufficient).
- [Project overview](project-skills/ibswingtrader-scanner/references/PROJECT_OVERVIEW.md):
  commands, artifacts and code zones.
- [Collaboration notes](COLLABORATION_NOTES.md): language and working preferences.
- [Repository instructions](../AGENTS.md): cross-cutting responsibilities.

## Current project rules

Project skills and their references define current application contracts.
The success criteria define the goal; they do not imply implementation changes
or that the dependable-winner goal has been achieved.

- [Scanner](project-skills/ibswingtrader-scanner/SKILL.md): groups, ranking,
  readiness, watch universe, pricing and settings; routes to detailed references.
- [Evaluation](project-skills/ibswingtrader-evaluation/SKILL.md): saved-plan
  outcomes, participation, price provenance and replay semantics.
- [Research](project-skills/ibswingtrader-research/SKILL.md): supporting
  next-day mover coverage and recall investigation.

## Guides

- [Windows scanner schedule](guides/scanner-schedule.md).
- [Candidate CSV review](guides/candidate-csv-review.md): dated September field
  guide; current model/output contracts are in the skills and SETTINGS_MAP.
- [Investigation template](guides/investigation-template.md).

## Topic entry points

- BellUp timing, pauses and Triangle: [Series Playbook](project-skills/ibswingtrader-scanner/references/SERIES_PLAYBOOK.md),
  [latest impulse-level study](investigations/2026-10/investigation-2026-10-16-pause-impulse-level.md).
- ReversalHook: [EMA / fresh H4 recovery contract](project-skills/ibswingtrader-scanner/references/BOLLINGER_EMA_AND_RECOVERY.md),
  [active watch lifecycle](project-skills/ibswingtrader-scanner/references/REVERSAL_WATCH_LIFECYCLE.md),
  [manual edge and history](project-skills/ibswingtrader-scanner/references/REVERSAL_EDGE.md),
  [recognition study](investigations/2026-10/research-2026-10-10-reversal-recognition.md),
  [BMNR timing case](investigations/2026-10/investigation-2026-10-16-bmnr-scan-timing.md).
- Evaluation and confidence: [evaluation rules](project-skills/ibswingtrader-evaluation/references/EVALUATION_RULES.md),
  [Win model scope/settings](project-skills/ibswingtrader-scanner/references/SETTINGS_MAP.md),
  [confidence/outcome report](investigations/2026-10/investigation-2026-10-08-confidence-and-reevaluation.md).
- Operations and performance: [schedule guide](guides/scanner-schedule.md),
  [performance investigation](investigations/2026-10/investigation-2026-10-09-scan-performance.md).

## Historical investigations

Dated studies preserve their original data, hypotheses, decisions and caveats.
They are evidence, not automatic descriptions of current implementation.
The former amplitude-first/>10% objective and Runaway/Reversal family split
in older notes are historical. Follow current skills and success criteria.

### 2026-10

- [VSTM: recognized Daily hook rejected as Stalled](investigations/2026-10/investigation-2026-10-17-vstm-stalled-reclaim.md)

- [ReversalHook: частичная H4 и ограничения распознавания](investigations/2026-10/research-2026-10-10-reversal-recognition.md)
- [Пауза у вершины импульса против ступеней выше неё](investigations/2026-10/investigation-2026-10-16-pause-impulse-level.md)
- [Полоса относительно тела свечи: уточнение гипотезы](investigations/2026-10/investigation-2026-10-16-pause-body-position.md)
- [BMNR: разворот 15 октября и публикация 16 октября](investigations/2026-10/investigation-2026-10-16-bmnr-scan-timing.md)
- [PENG Daily: Л-Pattern and loser-watch enrollment](investigations/2026-10/investigation-2026-10-15-peng-l-pattern.md)
- [Верхняя полоса относительно предыдущей свечи: исследование пауз](investigations/2026-10/investigation-2026-10-15-pause-upper-position.md)
- [NVAX H4: П-Pattern and BellUp readiness warning](investigations/2026-10/investigation-2026-10-15-nvax-p-pattern.md)
- [Scan slowdown and guards — 2026-10-09](investigations/2026-10/investigation-2026-10-09-scan-performance.md)
- [Win~ и переходы Open — отчет 2026-10-08](investigations/2026-10/investigation-2026-10-08-confidence-and-reevaluation.md)
- [Импульс по диапазону свечи — 2026-10-07](investigations/2026-10/investigation-2026-10-07-boost-range.md)
- [Проверка риска относительно окончательной прибыли — 2026-10-06](investigations/2026-10/investigation-2026-10-06-profit-relative-stops.md)
- [Признаки вероятности Win — 2026-10-05](investigations/2026-10/investigation-2026-10-05-win-prediction-features.md)
- [Проверка половинной цели BellUp — 2026-10-05](investigations/2026-10/investigation-2026-10-05-half-profit-targets.md)
- [EstimatedHitRatePct comparison — 2026-10-05](investigations/2026-10/investigation-2026-10-05-estimated-hit-rate.md)
- [ACN reversal phase investigation — 2026-10-02](investigations/2026-10/investigation-2026-10-02-acn-reversal-phase.md)

### 2026-09

- [Post-rise consolidation and oval-envelope cache study](investigations/2026-09/TRIANGLE_HISTORY_STUDY_2026-09-17.md)
- [Runaway shadow penalty experiment](investigations/2026-09/RUNAWAY_SHADOW_RANK_2026-09-10.md)
- [BellUp to convergence: forward-shape investigation](investigations/2026-09/BELLUP_TRANSITION_2026-09-10.md)
- [BellUp exhaustion investigation, 2026-09-10](investigations/2026-09/BELLUP_EXHAUSTION_2026-09-10.md)

## Implementation records

These describe changes at their recorded date. Later behavior is maintained
in project references, including subsequent overrides.

- [EMA Bollinger and fresh H4 support for recognized reversals](implementation/implementation-2026-10-17-bollinger-ema-h4-recovery.md)
- [Reversal-only watch cycles and meaningful Daily-mid drops](implementation/implementation-2026-10-17-reversal-watch-lifecycle.md)
- [BellUp late-entry ranking penalty](implementation/BELLUP_LATE_ENTRY_PENALTY_2026-09-14.md)
- [BellUp lower-band turn veto](implementation/BELLUP_LOWER_TURN_VETO_2026-09-11.md)
- [BellUp exit from previous boosts](implementation/BELLUP_TWO_BOOST_EXIT_2026-09-11.md)
- [Signed evaluation amplitude](implementation/EVALUATION_AMPLITUDE_SIGN_2026-09-14.md)
- [Evaluation selection fix, September 11](implementation/EVALUATION_SELECTION_FIX_2026-09-11.md)
- [Triangle: continuous post-spike plateau](implementation/TRIANGLE_CONTINUOUS_PLATEAU.md)
- [Резерв текущей цены для ReversalHook](implementation/implementation-2026-10-10-reversal-live-quote-fallback.md)
- [Наблюдение за упавшими тикерами и диагностика ReversalHook](implementation/implementation-2026-10-10-reversal-watch-list.md)

## Reproducible artifacts

[Artifact inventory](artifacts/README.md) links saved study results and the
frozen experimental Win model. Scripts in `tools/` use the new artifact paths.
Artifact payloads retain their historical content, including old provenance paths.

## Placement and maintenance

- Keep only this map and COLLABORATION_NOTES.md in the docs root.
- Put current domain rules in the matching project skill/reference; link to
  the shared success criteria instead of copying detailed metric definitions.
- Put operating instructions and reusable writing templates in guides/.
- Put dated studies in investigations/YYYY-MM/ and dated implementation
  records in implementation/. Preserve existing historical filenames.
- Put saved JSON, compressed event/CSV results and model files in artifacts/.
  Name new notes/artifacts consistently with a date and descriptive topic.
- New studies should link their script and artifacts; adopted behavior belongs
  in current references. Do not rewrite old results to imply a later rule existed.
- When moving a file, update Markdown links, repository-relative references
  and script input/output paths together. Keep artifact payloads unchanged.
