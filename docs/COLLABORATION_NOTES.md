# Collaboration notes

Not project mechanics — how this user prefers to work, kept separate from
the `project-skills/` skill docs so it doesn't get mixed with code/model
facts.

## Language and tone

The user communicates in Russian and prefers informal address ("ты", not
"вы") — more comfortable for them. Use the informal register by default in
Russian conversation on this project.

## Working method: validate before recalibrating

When a scoring/matching/ranking mechanism looks miscalibrated, the user
wants the underlying signal tested empirically (e.g. AUC or nearest-neighbor
separation against real historical outcome data) *before* proposing new
threshold numbers — not a straight jump to "let's retune the constants."

This came out of a concrete case: the scanner's series-similarity ranking
had been hand-tuned for months without reaching its goal; testing first
showed the mechanism itself carried no signal (winners weren't closer to
other winners than to losers), so retuning its thresholds would only have
changed which noise fired, not fixed anything. See
`project-skills/ibswingtrader-scanner/references/SCANNER_MODEL.md` ("Ranking
implementation status") for how that investigation played out and what
replaced it, and `references/SETTINGS_MAP.md` for the same principle applied
to scanner settings generally.

The user responds well to this approach being applied unprompted — including
multi-step empirical investigations — rather than a quick config change, even
when it takes longer to get to an answer.
