# Session D (WP2): ASE over the inversion lifetime and the stress-birefringence pattern

Done 2026-11-11 on `dev/2026-11`, from [plan-next.md](../plan-next.md) section 3, row D. Order as planned: spike, then ASE, then the pattern; an advisor pass on the design
before any edit. Reference page: [Gain media](../../reference/gain.md#ase). Numbers below are measured in the tests unless marked otherwise.

## Spike: does the spatial path carry a vector field?

* The **transverse profile** does: `TransverseProfile.DataY` is the y shape, `TransverseOps.ExpandY` / `CollapseVector` convert it, and `SpatialPropagation` has a vector solver
  that is switched on by a y component or a `DataY`. A **cartesian** 2-D basis exists (`CartesianBasis`), a radial one cannot hold a four-lobed pattern.
* The **gain element's own spatial pass** was scalar: `PassSpatial` takes one field and a note said "the y component passed unchanged".
* Consequence: no vector gain pass is needed. The cubic host's gain is the same for both polarisations; the stress map is a point-wise Jones matrix, so it acts on (E, E_y) after the gain
  and `CollapseVector` hands back x, y and a profile with its own y shape.

## ASE over the inversion lifetime (`Gain/AseModel.cs`)

**Model.** Between pulses N2 changes in milliseconds, the ASE crosses the crystal in picoseconds, so at each pumping or decay step the ASE is a boundary-value problem in z at frozen N2:
forward intensity from z = 0, backward from z = L, per transverse cell and per frequency of the emission band, `dI/dz = ±γ I + p h ν σe N2 q` with `q = n² Ω / λ0²`. The slice step is exact for constant γ.
The stimulated photons of a slice (output − input − source dz) leave N2, so the photon balance is exact by construction; the spontaneous part is in 1/τ already (not subtracted twice).
`GainCrystal.PumpCw`, `Decay` and `RelaxTowardsCw` take the extra decay rate (sub-stepped so that the rate times the step stays below 0.3), so the pulse-train steady state, the single shot and the thermal heat
(through the absorbed pump) all see it.

**Two geometric factors, kept apart** (the advisor's point, and the reason for `q`): the *depletion* uses the effective solid angle of the rays that cross the pumped region (`solidAngleSr`, default
min(π r_pump²/L², escape cone)); the *signal-mode output* (the contrast pedestal) uses exactly one spatial mode, `q = 1/A_mode`, same equation. For `p = 2` and one mode this is the textbook
P = n_sp h ν Δν (G − 1) per polarisation.

**Own spectral grid.** The ASE band is taken from the medium (σe above 1e-3 of its maximum, 40 points), not from the pulse's `FrequencyGrid`: Yb:YAG ASE at 1050 nm can sit outside a narrow
pulse window and still carry the depletion.

**Opt-in.** `"aseModel"` is a new key; `"ase": true` (noise in the window) is unchanged, so no existing result moved. The inter-pulse ASE is *not* in the pulse's energy ledger
(it is a power, not part of the pulse); the budget still closes (test).

**Checks** (`AseStressTests`, 9 tests, all pass):

| test | reference | result |
|---|---|---|
| uniform inversion, one and two polarisations, both ends | n_sp h ν Δν (G − 1) (Desurvire 1994; Siegman) | 1e-12 |
| linear inversion profile, 400 slices | direct RK4, 20000 steps | 1e-6 (the model is piecewise constant in z) |
| the source into 4π, both polarisations | Füchtbauer-Ladenburg: N2 ⟨hν⟩ / τ_rad (Aull and Jenssen 1982) | 1e-9 (fixes `q`, the factor 2, the 8π n²/c²) |
| slice step on a saturating medium | ln(G/G0) + (G − 1) P_in/P_sat = 0 (homogeneous saturation, integrated) | 1e-4 |
| cw steady state, Yb:YAG, 250 W, solid angle 1 sr | absorbed pump photons = N2 V / τ + stimulated ASE photons | 0.5 % (both without and with ASE) |
| solid angle 1e-12 / 0.01 / 0.3 sr | no depletion at zero angle (1e-6); monotonic | pass |
| amplifier element | reports, pedestal strength, inversion and extracted energy fall, budget closes (1e-6) | pass |

*Commented against the plan:* the plan named Saleh et al. 1990. The closed form used is the integrated homogeneous-saturation law, which is the same physics in textbook form (it has no spontaneous source, so it
checks the slice step and the sign of the saturation, not the source; the source is checked by the first and third rows). I could not confirm the attribution of the form to one paper by a search; it is derived
from dP/dz = g0 P / (1 + P/P_sat) in the test.

**What the model says for Yb:YAG (measured).** A 0.4 mm pump radius, 8 mm rod, 250 W at 940 nm, double pass: with the *default* solid angle (about 8 mrad) the ASE takes 0.07 % of the absorbed pump photons;
with the upper bound of 1 sr (the escape cone) 6.6 %, and the stored inversion falls by 6.8 %. So for a bulk rod of this size ASE is a small effect unless the geometry is large; the default is a
lower-bound estimate for a thin pumped column, and the notes in the run give the solid angle used.

## The user's question: why not 3-D ASE, and can we have it?

*Why it was written out of scope.* The word "3-D" covers two different things.

1. **Transverse-resolved inversion with ASE along z** ("2.5-D"). This is in the package: on a `transverse` grid every cell is its own column with its own N2, so a pump profile gives a local depletion
   (the ASE is stronger where the inversion is). It costs cells × slices × points exponentials per evaluation (a 1-D element: milliseconds; a 64 × 64 grid: seconds).
2. **Rays that cross cells.** ASE in a large-aperture disk or a slab travels at large angles, transversely, bounces off the faces and edges (transverse and parasitic ASE, parasitic lasing). That needs a
   ray-based model (Monte Carlo or ray tiling over the crystal, with surface reflections), O(cells² × slices) or a sampling error, and **a reference to validate against** (the engine has none: uniform
   slab, Linford-type formulas, or a published disk-laser ASE number must be read from the sources first). It is a package of its own (backlog), not part of an L-size item that also contains the polarisation and the stress pattern.

So: yes, we can have it. The first kind is delivered; the second is feasible as a ray-based module in the same `AseModel` shell (the slice step and the photon balance are reusable), but it needs a validation plan
before it can be called physics rather than a picture. Proposed for the backlog: *Monte Carlo ASE for bulk crystals and disks* with (a) a uniformly inverted cube against the closed-form
Linford / Koechner estimate, (b) a thin disk against a published transverse-gain limit, (c) the photon balance.

## The stress-birefringence pattern

* `GainElement.ApplyStressPattern`: per point (x, y) the Jones matrix R(−φ) diag(1, e^{iδ}) R(φ), δ = C min(r, r₀)²/r₀², unitary. C and r₀ are the lumped formula's (Koechner), so the
  pattern and the lumped value share one definition of the retardation.
* On a cartesian grid with an x-polarised input the element applies it, collapses with `CollapseVector` and reports `depolarizationFractionPattern`. The carried profile has its own y shape.
* **Consistency (the plan's test):** the beam average of the pattern equals `StressDepolarization(C, w, r₀)` to **1e-6** on a 256 × 256 grid (Gaussian well inside); energy kept to 1e-13; no y on the
  axes, four lobes on the diagonals, adjacent lobes of opposite sign.
* **A finding on the way.** In a gain element the pattern fraction was 0.0066 against the lumped 0.0085 (22 % apart). Not an error of either: the pattern is applied to the beam at the exit of the rod,
  the lumped formula uses the radius at the middle of the rod, and the thermal lens (Nd:YAG, 400 W) changes the radius; D grows as w⁴ for a small beam. With the lumped formula evaluated at the
  *output* radius the two agree to 5 % (test). Applying the retardation along z, slice by slice, would remove the difference; it is not done (stated in `gain.md`).
* Limits: radial grids and a y component at the input fall back to the lumped rotation (with a note); the common part of the radial and tangential lenses is reported (`stressFocalLength*`) but not applied to the field.

## Open

* Yb:YAG / Nd:YAG Füchtbauer-Ladenburg consistency: do the catalogue's smooth cross-section fits integrate to the radiative rate? A one-hour check, not done; the model's source term depends on σe only through the
  fits already in the catalogue (all `approximate`).
* Ray-based ASE (above); parasitic lasing; fibre use (backlog C3 can build on `AseModel`).
* Stress: slice-by-slice retardation; the common-mode radial / tangential lenses on the field; vector gain for a y input.
* `asePedestalEnergyFraction` is reported, not automatically fed into a `pulse.pedestal` of the next stage.
