# WP-D: core engine fixes

Session 1. Reproduce each issue in a minimal failing test first (skill `bug-report`), then fix. Use the `bug-hunter` agent for the sweep (D3).

## D1. `n2Scale` scales every third-order response

**Finding.** The spatial path (`Transverse/SpatialPropagation.cs`) already multiplies Kerr, delayed Raman and the rotational-Raman tensor by the local `n2Scale`.
The 1-D path (`Compiler.ApplySpatialPropertiesIn1D`) does only `med.N2 *= sc`, so a gas element's rotational Raman (air in a multipass cell) keeps its full strength.
`Gain/GainCrystal.cs` scales its Kerr phase only (no Raman there; check and note).

**Design.** Scale factor `s` applies to: `N2` (this carries Kerr, the delayed Raman fraction and XPW anisotropy, because they are fractions of n2), and `Rotational`
(wrap in a `ScaledAnisotropicResponse : IAnisotropicResponse` that multiplies `Convolve` and `AdiabaticN2` by `s`, so the adiabatic-limit checks stay consistent).
User `ExtraResponses` are the user's own expressions and are **not** scaled; the element note says so. The note also states when Raman was scaled.
The averaging (intensity²-weighted) stays. Update `docs/reference/spatial-properties.md` and the `n2Scale` key description (says "scales the Kerr term").

**Tests.** (1) air, 1 bar, `n2Scale: 0.5` vs. a run with half the pressure's rotational strength: rotational-Raman phase shift halves (compare against the same run with `n2Scale: 1`, ratio 0.5 to 1e-9);
(2) with a solid (Kerr + delayed Raman), the spectrum equals the run with `n2` halved; (3) `n2Scale` 1-D result equals the spatial-model on-axis result for a uniform scale (the two paths agree);
(4) provenance / notes mention Raman.

## D2. Kerr lens in the 1-D beam model

**Question first.** Today the 1-D pulse model carries a beam (`IBeamModel`, ABCD) that does not feel the pulse's Kerr index. `CellModeSolver.KerrLensPowerPerM` has the thin-slice formula
for a cell (c n2 dz I0 / w², I0 = 2P/(πw²); c=1 second-moment, c=4 paraxial).

**Most physical choice** (owner: "whatever is most physically accurate"): each time slice of a pulse sees a different power, so the true lens is time-dependent and
the beam is not one Gaussian (the spatial model does this exactly). The 1-D model can only carry one beam, so:
1. **Default `kerrLens: "peak"`**: thin lens per step from the pulse's peak power (Marburger self-focusing, the limit that matters for collapse and for the cellMode comparison). Documented as the upper bound.
2. **`kerrLens: "pulseWeighted"`**: power-weighted over the pulse, `∫P(t)² dt / ∫P(t) dt` as the effective lensing power (the second moment of the slice-wise lens power, which is what moves the time-integrated beam size).
3. Warn (provenance note) when P_peak / P_cr > 0.1 that the 1-D result is a lower-fidelity answer and the spatial model should be used.
The power P_cr = 3.77 λ² / (8π n0 n2) (Marburger / Fibich-Gaeta). Off by default (keeps every existing result unchanged); opt-in key on elements with a beam model and nonlinear medium.

**Implementation.** In the nonlinear stepper callback that already knows the step length and field, compute peak (or weighted) power from `|A|²·A_eff`, form the thin-lens power with `KerrLensPowerPerM`, and update the
beam's `q` (an ABCD lens after the step, split-step symmetric). `A_eff` must follow the updated radius (dependence handled step by step). Beam radius change feeds back into `A_eff` for the next step.

**Tests.** Gaussian beam at P = 0.5 P_cr in a long Kerr medium: waist position against the Marburger / Dawes-Marburger result for the collapse distance `z_f = 0.367 z_R / sqrt[(sqrt(P/P_cr) - 0.852)² - 0.0219]` (Marburger 1975) within a few %;
at P ≪ P_cr the lens focal length against `f = w² / (c n2 I0 L)` to 1e-6; reproduce `cellMode` for one Herriott cell (the same lens per pass); P = 0 changes nothing (bit-identical).

## D3. Sweep for other problems and small improvements

Run, with `bug-hunter`: all `docs/examples` in `ULTRAFAST_STRICT_OPTIONS=1`; the analytic-limit checks in `tests/components`; unit-key consistency (keys without a unit in the name); the Python builders against the schema (autocompletion intact);
energy-closure on every example (loss budget exact); `net48` build warnings; flaky or slow tests (list the slowest 10). Triage into: fix now (S), file in `next-steps.md` (M+), or drop. Record the list in the progress note.
Also check the five generated files are fresh (`/regen`), and that `HANDOFF.md` is folded into `history.md` and deleted (it says "released as 2.2.0", which is stale).
