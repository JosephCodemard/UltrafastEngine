# WP0 (housekeeping that was promised): what was done

Done 2026-11-11 on `dev/2026-11`, from [plan-next.md](../plan-next.md) section 2, WP0. Four items; all four are done.

## 1. `UF_WITH_AO`

* `src/UltrafastEngine.Native/UltrafastEngine.Native.csproj`: a define and a project reference for `plugins/ao`, the same pattern as `UF_WITH_GPU` and
  `UF_WITH_HITRAN`. `-p:UF_WITH_AO=false` leaves the plugin out.
* `Exports.cs`: `AoPlugin.RegisterStatic()` in the module initialiser; `AoPlugin.RegisterStatic` is new (like `HitranPlugin.RegisterStatic`).
* `build/package.py`: `_ao_flag`, added to the flags of the NativeAOT publish; the native Python wheel no longer warns that `ao` is left out.
* Tests: `test_packaging.py` checks the flag. `test_native_library_lists_the_four_ao_elements` loads a built library through ctypes and looks for the
  four element names in `uf_capabilities`. It runs only when `ULTRAFAST_NATIVE_LIB` points at the library; **it was skipped here, because no NativeAOT
  library was built** (only the managed native project, with and without the flag, was built).
* The `twin` plugin needs nothing (a Python module plus a CLI command).

## 2. Grid-planner hook

* `CompiledElement.ProgrammedGddS2` (new): the magnitude of a spectral phase an element programs without a declared group delay. The shaper terms keep
  `GroupDelayAt = 0` on purpose (arrival-time bookkeeping), so the planner could not see them.
* `GridPlanner.Plan(..., programmedGddS2)`: adds `0.5 x |GDD| x (input -40 dB bandwidth)` to the half window, with a note. Contributions of several
  elements are added (worst case). `Compiler` and `Executor` pass the sum.
* `spectralShaper` (`TemporalShapers.cs`): orders 1 to 8 of the Weiner replicas, at `m x 2 pi / pixel bandwidth`, with level `exp(-2 (pi m w0 / pitch)^2)`
  (the spot blur only; the pixel-gap sinc is not used, so the room is an upper bound). A mask with no steps and no gaps adds nothing. They go through the
  existing `Satellites` machinery, so `grid.satelliteLevel` decides which orders count and `SatelliteRoom` reports what it left out.
* `aopdf`: `ProgrammedGddS2 = gddFs2`, and the programmed delay and the replica delay as a satellite.
* A fixed window (`grid.auto: false`) gets the existing "Grid looks too small" warning. Nothing new was needed.
* Not covered: a quadratic phase written into the pixels of a `spectralShaper` is not seen as a GDD. The docs say so (`docs/plugins/ao.md`).
* `docs/examples/AdaptiveOptics/temporal-shaping`: the pixel-gap case no longer sets `grid.windowS`. Its side pulse is now at 4.28 ps instead of 4.27 ps (exact:
  4.27 ps) because the planner chooses a coarser time step than the 40 ps window had; the README quotes 4.28.

Tests (`AoDeviceTests`): `Planner_SpectralShaper_WidensTheWindow_ForItsSidePulses` (the Weiner mask with no window, no warning, the +T pulse present),
`Planner_SpectralShaper_FlatMask_AddsNoRoom`, `Planner_Aopdf_Gdd_WidensTheWindow` (Gaussian chirp formula, 2 %), `Planner_Aopdf_FixedWindow_WarnsAboutWrap`.
The first version of the side-pulse hook gave only order 1 and the test failed on a 6.7 % edge-energy warning (the second order of a 0.75 fill-factor mask holds
4.5 % of the energy): that is why it carries eight orders. A flat-mask check that looked only at pixel values missed gaps; it now looks at the fill factor and
gap transmission as well.

## 3. Unknown-element message

`Compiler.UnknownElementMessage(type, id)` (public, so it can be tested): names the `ao` plugin for `deformableMirror`, `slm`, `spectralShaper`, `aopdf`;
other types still list the known types. Test: `UnknownElement_NamesTheAoPlugin`.

## 4. The open edges of S3

| edge | test | result |
|---|---|---|
| grating-derived dispersion of `spectralShaper` | `AoDeviceTests.SpectralShaper_GratingDispersion_MatchesLittrowReference` | at Littrow `dx/dlambda = 2 f tan(theta) / lambda`; the shaper built from the grating data equals the one given that dispersion, 1e-12 |
| Bolduc through an SLM with a varying field | `test_ao_plugin.py::test_bolduc_first_order_through_the_slm_carries_a_varying_complex_field` | first-order fluence correlates 0.99 with the transform of illumination x `A e^{i phi}`; first-order energy relative to an engine blaze of `A = 1` agrees with the transform ratio to 1.1 % |
| double phase through an SLM with a varying field | `..._double_phase_through_the_slm_...` | correlation 0.99998, energy ratio 0.5 % (the checkerboard light is outside the focal grid) |
| tiled-array lobe fractions beyond `0 < f < 1` | `..._tiled_array_lobe_fractions_match_the_array_factor_...` | 3 x 3 square array, in phase and with checkerboard pistons: central 0.147 / 0.110 and grating 0.631 / 0.542 against the analytic array factor, 2e-5 |

Findings while writing them (not fixed here; they belong to WP7):

* **The engine's SLM keeps less light in the first order of a sampled blaze than the Fourier sum.** An ideal sawtooth with 256 grey levels and a period of
  8 pixels (n = 64, 40 um pitch, 800 nm, f = 0.2 m, no fill factor, no cross-talk, `dispersion: none`) gives 0.854 of the incident energy in the first order
  in the engine and 1.000 for the Fourier sum of the same sampled field; at 16 pixels per period 0.946 against 0.983. All the missing light is missing from the
  focal grid altogether (the total fluence falls by the same factor), so it is lost to the absorbing boundary or to the sampling, not moved to another order.
  This is probably the "SLM engine against the Fourier prediction 0.98-0.99" finding of WP7 seen at a larger size. The cause has not been found. The Bolduc test
  therefore takes the first-order energy relative to an engine blaze of `A = 1`, which cancels it and still tests the amplitude encoding; this is stated in the
  test and in `docs/plugins/ao.md`. The correlation checks need no such normalisation.
* The Bolduc encoding needs a field that changes little over one period: a phase of 4 rad over the beam radius already gave 0.86 correlation for the *ideal*
  hologram (no engine involved), 1.5 rad gives 0.997. The test uses 1.5 rad.

## Verification

* C# suite (net10.0, all): 541 passed before the last tests; the only failure was the stale validation matrix, regenerated. The `AoDevice`, `Schema` and
  `ValidationMatrix` filters pass after (34 tests). `AoDeviceTests` now has 26 tests.
* Python (hosted engine, `ULTRAFAST_SLOW=1`, pyNLO): 220 passed, 2 skipped.
* `dotnet build UltrafastEngine.sln -c Release`: 0 errors. `mkdocs build --strict`: no warnings. `example_check.py temporal-shaping`: 0 problems.
* **Not run:** net48 (Mono), `/verify` in full, `example_check.py` for the other examples, a NativeAOT publish.

## Next

WP2 (gain: ASE and stress birefringence) in a new session. The SLM efficiency finding above should be picked up with the SLM item of WP7.
