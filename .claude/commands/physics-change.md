---
description: Checklist and workflow for any change to the propagation physics (nonlinear terms, Raman, dispersion, gain, crystals, grids)
argument-hint: "<what is changing>"
---
Physics change: $ARGUMENTS

Load the `engine-physics` skill first. Then:

1. **State the physics** in one paragraph with the equation and its source (paper, textbook) before editing code.
   Check sign conventions against docs/concepts.md (E = Re{A e^{-iω0 t}}, +GDD red first, SPM +iγ|A|²A).
2. **Find every implementation** of the term: the 1-D envelope problem (`Physics/Nonlinear.cs`), the
   carrier-resolved field problem, the spatial problem (`Transverse/SpatialPropagation.cs`), crystals/gain if
   relevant. Keep them consistent.
3. **Write the test first**: an analytic limit or a published value (e.g. SPM broadening, soliton period, NER
   rotation B·sin2χ/3, Manley-Rowe, Frantz-Nodvik). Tolerance from the numerics, stated in a comment.
4. Implement. Keep scalar (x-polarised) results bit-for-bit or within 1e-9 unless the change is meant to alter them.
5. Run: the targeted tests, `PropagationTests`, `NerTests`, `FiberTests`, then the full suite, then the pyNLO
   cross-checks (`python/tests/test_pynlo*.py`). Ask the `physics-reviewer` subagent to audit the diff.
6. Document: `docs/reference/nonlinear.md` (or the relevant page) with the equation, defaults and validity;
   CHANGELOG; `docs/testing.md` row; re-run examples whose numbers may change (`/example-check`).
