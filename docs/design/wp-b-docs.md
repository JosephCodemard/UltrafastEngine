# WP-B: honest documentation

Session 5. After the code work, so numbers are final.

* `ROADMAP.md` and `docs/roadmap.md` (identical): three explicit blocks, **can do now**, **cannot do and why**, **want to do**. Include: Zemax experimental, Metal after 1.0, WP9 after 1.0, unverified material values, GPU only verified on the machines tested.
* `docs/capabilities.md`, `PROJECT_STATUS.md` (counts regenerated from the test run; "Not checked" cut to what is genuinely unverified), `README.md` (no Zemax in the headline, new install story, version 1.0.0, quick-mode/fidelity mention).
* `CHANGELOG.md`: fold Unreleased into a single `1.0.0` entry; the old 2.x history stays below as "pre-release history".
* Remove every "PyPI forbidden" sentence (`CLAUDE.md` ground rules, `backlog.md` decision 2, `next-steps.md`, memory notes). Replace with: "upload with `twine` only on the owner's explicit go-ahead."
* Version: change every `2.2.0` to `1.0.0` (csproj `Version`, `pyproject`, `ultrafast.h`, docs, installer presets, tests that check versions); migration note for users of internal 2.x (there are none public).
* `docs/design/`: fold `HANDOFF.md` into `history.md` (if not already done in WP-D); new `fidelity.md` reference page; update `speed.md` tables.
* WP9: wave 1 (the 12 S5 examples) and the landing pages only; the rest listed in the roadmap.
* `mkdocs build --strict`: 0 warnings; `/example-check` after number changes; `llms.txt` refreshed.
