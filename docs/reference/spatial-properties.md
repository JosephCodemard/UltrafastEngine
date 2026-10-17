# Position-dependent material properties

Any element can carry quantities that vary with position: x, y measured from the beam axis (r, θ in polar form),
and z from the element entrance. Combined with an intensity- and time-dependent response ([models](models.md)),
this gives n(t, r, θ, z, I).

| key | meaning | applied as |
|---|---|---|
| `indexProfile` | Δn(x, y, z), added to the material index | i(ω/c)Δn·E per frequency (lensing, guiding, steering) |
| `absorptionProfile` | α(x, y, z) in 1/m (intensity) | −α/2·E |
| `n2Scale` | factor on n2 | scales the Kerr term point by point |

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
| Gaussian / super-Gaussian | `{"kind": "gaussian", "amplitude": 1e-4, "radiusM": 1e-3, "order": 1, "centerXM": 0, "background": 0}` — amplitude·exp(−2(ρ/R)^(2·order)) |
| table (linear interpolation) | `{"kind": "table", "axes": "r", "r": [...], "values": [...]}`; axes `r`, `z`, `rz`/`zr`, `xy`, `xyz`; `"outside": 0` sets the value beyond the table (default: clamp) |
| callback | `"@name"`, registered from Python (`uf.run(..., fields={"name": f})` or `Run.field(name, f)`, f(x, y, z) → array), C# (`registry.RegisterCallback("name", (FieldCallback)...)`) or C (`uf_run_set_field_callback`) |

A callback receives whole arrays of points (one z per call) and must return one value per point. Fields are
evaluated once per element if they do not depend on z; otherwise they are evaluated at every step.

## Transverse model or not

With a `transverse` model (radial or cartesian) the properties are applied at every grid point, so a GRIN
profile focuses, a soft absorber apodises, and a Δn(x) gradient steers the beam. **Without** one (1-D), the beam
has no transverse structure:

* absorption is averaged over the Gaussian beam model (intensity-weighted) and applied as a flat loss;
* `n2Scale` is averaged with intensity² weighting;
* `indexProfile` has no effect (a note says so).

Absorption shows up in the energy budget as `absorption`. The terms also limit the step size (≈ 0.2 rad of
phase per step where the beam is), so strong profiles are integrated accurately.

The gain elements use the same machinery for their thermal lens and accept the same three keys
([gain media](gain.md)). Time responses can also read position: `ResponseContext` has `X`, `Y`, `R`, `Theta` and
`Spatial` (true on spatial grids), so a custom n(t, I) can also depend on where it is evaluated.

## Examples and checks

Example 16 writes the same GRIN lens as an expression, a table and a Python function, plus a z-tapered version
that focuses twice as far. The tests (`SpatialPropertyTests`, `python/tests/test_gain.py`) check:

* the GRIN focus against thin-lens optics (3 %);
* Gaussian absorption T = (1 − e^{−α0L})/(α0L) (0.3 %);
* agreement between the expression, table and callback forms;
* n2 scaling.
