---
description: Re-run documentation examples and update the numbers quoted in their READMEs and tutorials
argument-hint: "[example folder names, e.g. 21-measured-data 26-transforms; empty = all]"
---
Re-check examples: $ARGUMENTS

For each example (`docs/examples/<name>`), run its commands exactly as the README states (with
`ULTRAFAST_STRICT_OPTIONS=1`; pairings in docs/CLAUDE.md), compare every number quoted in the README (and in the
tutorial that uses it) with the fresh output, and update the text where it changed. If a change is larger than
noise, explain the cause (a physics fix, a new default) in one sentence in the README or CHANGELOG. Hand long runs to
the `test-runner` subagent.
