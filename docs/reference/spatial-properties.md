# Position-dependent material properties

Three keys give an element quantities that vary with position: x, y measured from the beam axis (r, θ in polar form),
and z from the element entrance. Combined with an intensity- and time-dependent response ([models](models.md)),
this gives n(t, r, θ, z, I).

**Which elements use them.** The keys are declared for every element ([`elementCommon`](options.md#elementcommon)), but only
these read them: `bulk` / `gap` (also `medium`), the glass of `plate` / `window` and `ndFilter`, the substrate of
`thinFilmPolarizer`, the crystal of `faradayRotator`, and `gain` / `amplifier` (with a transverse model only). Other elements,
`crystal` and `fiber` among them, ignore them without a warning.

## Keys

| key | meaning | applied as |
|---|---|---|
| `indexProfile` | Δn(x, y, z), added to the material index | i(ω/c)Δn·E per frequency (lensing, guiding, steering) |
| `absorptionProfile` | α(x, y, z) in 1/m (intensity) | −α/2·E |
| `n2Scale` | factor on the third-order response | scales Kerr, delayed Raman, the cubic anisotropy (XPW) and the rotational Raman of gases point by point; user-defined `responses` are not scaled |

```jsonc
{ "id": "rod", "type": "bulk", "material": "FusedSilica", "lengthM": 0.005,
  "transverse": { "model": "radial", "points": 96 },
  "indexProfile": { "kind": "expression", "expr": "-0.5*n0*g2*r^2", "params": { "n0": 1.45, "g2": 689.655 } },
  "absorptionProfile": { "kind": "gaussian", "amplitude": 20, "radiusM": 1e-3 },
  "n2Scale": "1 + 0.5*z/0.005" }
```

## Field kinds

| form | example |
|---|---|
| number | `"n2Scale": 0.8` |
| expression (x, y, z, r, theta, params) | `"-2e-4*(r/1e-3)^2"`, `{"kind": "expression", "expr": "...", "params": {...}}` |
| constant | `{"kind": "constant", "value": 0.8}` |
| Gaussian / super-Gaussian | `{"kind": "gaussian", "amplitude": 1e-4, "radiusM": 1e-3, "order": 1, "centerXM": 0, "centerYM": 0, "background": 0}`: amplitude · exp(−2 (ρ/R)^(2·order)) + background, ρ measured from (`centerXM`, `centerYM`). `amplitude` defaults to 1, `order` to 1 for `gaussian` and to 4 for `"kind": "superGaussian"` |
| table (multilinear interpolation) | `{"kind": "table", "axes": "r", "r": [...], "values": [...]}`. `axes` is a string of the letters `r`, `x`, `y`, `z` (for example `r`, `z`, `rz`, `xy`, `xyz`); each letter names an array of that axis in m, strictly ascending; `values` is flattened in the order of `axes` with the **last axis fastest** (length = product of the axis lengths). `"outside": <number>` sets the value beyond the table; otherwise the edge value is held |
| callback | `"@name"`, registered from Python (`uf.run(..., fields={"name": f})` or `Run.field(name, f)`, f(x, y, z) → array), C# (`registry.RegisterCallback("name", (FieldCallback)...)`) or C (`uf_run_set_field_callback`) |

A callback receives whole arrays of points (one z per call) and must return one value per point. Fields are evaluated
once per element if they do not depend on z, otherwise at every step. An expression depends on z when the token `z` appears
in it; a table when its axes contain `z`; a callback always counts as z-dependent, so it is called at every step.

## Transverse model or not

With a `transverse` model (radial or cartesian) the properties are applied at every grid point, so a GRIN
profile focuses, a soft absorber apodises, and a Δn(x) gradient steers the beam. **Without** one (1-D), the beam
has no transverse structure:

* absorption is averaged over the Gaussian beam model (intensity-weighted) and applied as a flat loss;
* `n2Scale` is averaged with intensity² weighting and then scales n2 and the rotational Raman response alike;
* `indexProfile` has no effect (a note says so).

Absorption shows up in the energy budget as `absorption`. On a spatial grid the terms also limit the step to dz ≤ 0.2 / max(k₀|Δn| + α/2), the maximum taken over the grid points that hold more than 1e-4 of the peak energy, so strong profiles are
integrated accurately (RK4 is accurate to about 1e-5 at that step).

The gain elements use the same machinery for their thermal lens and accept the same three keys
([gain media](gain.md)), but only with a transverse model: a 1-D gain element ignores all three with a note. Time responses can also read position: `ResponseContext` has `X`, `Y`, `R`, `Theta` and
`Spatial` (true on spatial grids), so a custom n(t, I) can also depend on where it is evaluated.

## Validation

Example 16 writes the same GRIN lens as an expression, a table and a Python function, plus a z-tapered version
that focuses twice as far. The tests (`SpatialPropertyTests` in `tests/UltrafastEngine.Tests/GainTests.cs`,
`python/tests/test_gain.py`) check:

* the GRIN focus against thin-lens optics (3 %);
* Gaussian absorption T = (1 − e^{−α0L})/(α0L) (0.3 %);
* agreement between the expression, table and callback forms;
* n2 scaling.

## Limits

* In 1-D, `indexProfile` has no effect and the other two are averaged over the Gaussian beam model.
* The index change is real and frequency-independent (added to the material index at every frequency).
* User-defined `responses` are not scaled by `n2Scale`.
* Elements outside the list above ignore the three keys without a warning.
