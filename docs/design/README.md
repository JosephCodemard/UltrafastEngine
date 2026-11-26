# docs/design: internal design documents

**This folder is for internal use: the people and agents developing UltrafastEngine. It is not user documentation.**
It is excluded from the documentation site (`mkdocs.yml`, `exclude_docs`), and user pages do not link into it. What a user needs
is in the rest of `docs/`: [capabilities and limitations](../capabilities.md), the public [roadmap](../roadmap.md), the reference pages.

| file | holds | kept current by |
|---|---|---|
| [architecture.md](architecture.md) | the current architecture in one page, with the invariants a change must keep; points to `docs/architecture/` for files and processes | whoever changes a layer or an invariant |
| [decisions.md](decisions.md) | every owner decision still in force, numbered, with its reason; superseded ones struck through with what replaced them | the session in which the owner decides |
| [roadmap-internal.md](roadmap-internal.md) | open engineering work: size, area, acceptance criteria, dependencies; what the public roadmap summarises | every session that opens or closes an item |
| [coordinator.md](coordinator.md) | the procedure and the paste-in prompt for the coordinator session that runs the work packages of `roadmap-internal.md` section 0; `build/wp/` holds its scripts | when the procedure changes |
| [limitations.md](limitations.md) | known limitations with their cause, a repro or test, and whether they are planned | every session that finds or removes one |
| [future-plugins.md](future-plugins.md) | designs that are not scheduled: separate plugins (coating design, crystal design, fibres) and larger core updates (fitting estimators, Pockels cells) | when a design is refined or scheduled |
| [hardware-checklists.md](hardware-checklists.md) | step-by-step checks that need hardware the development machines may lack (GPU); Zemax has its own in `plugins/zemax/HARDWARE-CHECKLIST.md` | after each hardware run |
| [history.md](history.md) | what was built, stage by stage, with the findings and numbers of each session; working notes that still help | at the end of every session |

Rules:

* One fact lives in one file. The public roadmap (`docs/roadmap.md`, copied to `ROADMAP.md`) is the user-facing summary of
  `roadmap-internal.md` and `future-plugins.md`; `docs/capabilities.md` is the user-facing summary of `limitations.md`.
* No hand-off files: a session ends by updating `history.md` (what was done) and `roadmap-internal.md` (what is open). There is no `HANDOFF.md`.
* An item leaves `roadmap-internal.md` when it is done (it moves to `history.md`), or when it is reclassified (to `limitations.md` or
  `future-plugins.md`), never silently.
* Rules of `CLAUDE.md` apply: physics changes need an analytic or published reference in a test; a disagreement is a finding, never tuned away;
  tags, releases and PyPI uploads only on the owner's explicit go-ahead.
