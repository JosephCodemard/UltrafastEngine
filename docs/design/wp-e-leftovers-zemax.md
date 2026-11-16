# WP-E: leftovers and Zemax consolidation

**Status (2026-11-16): done.** E1 small items, E2 Zemax, and the tail: the AO component gallery (`AdaptiveOptics/component-gallery`) and the model-comparison twin example (`DigitalTwin/dependence-models`; the plugin has no spline *dependence*, so the comparison is proportional / polynomial / Chebyshev / network). `UF_WITH_AO` in the native library was already done (history, Stage 7; the spec was stale). Still open from the AO plan, not in this WP: an end-to-end complex-field shaping example with a phase-only SLM. `ase_pedestal()` is a Python helper (the next stage is a separate run; nothing is carried inside one run). Nd:YAG's Fuchtbauer-Ladenburg check found a single-line fit (8 % of 1/τ), recorded in its catalogue notes. The ZOS-API audit was a read-through without ZOS-API documentation access, so it produced `TODO(zemax-hw)` markers and one certain fix (lens units), not verified conventions.

Session 2.

## E1. Small items (from `next-steps.md` §3 and §2)

* Feed `asePedestalEnergyFraction` into the next stage's `pulse.pedestal`.
* Lens under-sampling warning: phase step per cell above π at the beam edge (would have caught the tiled-array case). Test with a deliberately under-sampled lens.
* Typed `unverifiedInputs` accessor in Python (`result.unverified_inputs`), with a test and a docs line.
* Examples: drop the explicit SPGD `gain=` where the new default is right; re-run with `ULTRAFAST_STRICT_OPTIONS=1`.
* Yb:YAG / Nd:YAG Fuchtbauer-Ladenburg check: do the catalogue's emission cross-section fits integrate to the radiative rate (`1/τ_rad = 8π n² c ∫σ_e(ν)/λ² dν`-form with the right g-factor)? A test with tolerance; record the outcome in the catalogue status.
* Component gallery (`tests/components`) completed; spline-against-network twin example; `UF_WITH_AO` compiled into the native library (the NativeAOT wheel includes `ao`).
* Anything that needs the owner's papers (CS2, BaF2, Raman T2, LBO) is **not** blocked on: list it as "unverified" in the release notes.

## E2. Zemax: status, honesty, removal from core docs

**Why.** The engine began as a Zemax-only tool; the Zemax plugin (`plugins/zemax`, ~2.1k lines: `Zemax.Sampling` + the net48 `Zemax` console app) has been neglected, and the owner has **no OpticStudio to test on**.
So: the plugin is **experimental, untested against a real OpticStudio** for this release. The goal is to make that visible and make the code as correct as can be shown without it.

1. **Core docs.** Remove Zemax from `README.md`, `docs/index.md`, `docs/getting-started.md`, `docs/capabilities.md`, `docs/install.md`, `docs/agents.md`, `docs/tutorials/03-larger-systems.md` and the headline of `ROADMAP.md`;
   keep **one** page, `docs/plugins/zemax.md` (and `docs/examples/Interop/zemax-workflow`), with a status box: "experimental, written against the ZOS-API documentation, verified only with the fake OpticStudio in the tests". `docs/reference/plugins.md` lists it with that status.
   `CLAUDE.md` / `AGENTS.md` keep the DLL rule. `validation-matrix` marks it "not verified on hardware".
2. **Audit (S).** Read `ZosOpticStudio.cs` and `Program.cs` against the public ZOS-API documentation (do **not** copy or touch the DLLs): connection modes (standalone / interactive extension), ray-trace calls, units (Zemax lens units vs SI), coordinate conventions, OPD / wavefront sign. Write a findings list; fix what is certain, mark the rest `TODO(zemax-hw)`.
3. **Tests without Zemax.** Extend `FakeOpticStudio` so the whole sampling → system builder → engine path is tested (Gaussian-beam profile, `SystemBuilder` round trip, unit conversion, error paths such as no licence / no file). `net48` build must pass.
4. **Hardware checklist.** `plugins/zemax/HARDWARE-CHECKLIST.md`: exact steps for the first run on a machine with OpticStudio (what to run, what output to paste back, known unknowns). Same format as the GPU Windows checklist.
5. Packaging: the Zemax plugin stays out of wheels and out of `[all]` (backlog decision 3); the packager filter for `ZOSAPI*` stays and keeps its test.

**Definition of done.** `grep -ril zemax` over core docs finds only the plugin page, the status table, the changelog and the DLL rule; plugin tests green on net48 + net10; checklist exists.
