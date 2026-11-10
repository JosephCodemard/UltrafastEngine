# S3 AO plugin: handoff

Branch `dev/2026-11-s3-ao` (from `dev/2026-11` at 5469481). Page for users: [docs/plugins/ao.md](../../plugins/ao.md) (parameter tables, the
physics of each device, what each test checks). This page: what shipped, what did not, numbers, and what S5 may rely on.

## Shipped

| roadmap item | where | state |
|---|---|---|
| AO0 scaffold | `plugins/ao/` (`plugin.json`, `src/*.csproj`, `python/ultrafast_ao`), sln, test project reference, `build/CopyPlugins.targets`, `build/package.py` (`ao`, aliases `adaptive-optics`, `slm`, `dm`), `python/tests/test_packaging.py` | done; the default plugin list is now fitting, zemax, gpu, ao, hitran |
| AO1 `deformableMirror` | `plugins/ao/src/Devices.cs` | done: square / hex / ring / explicit layouts, Gaussian influence with stated coupling, stroke, drive bits, `2 cos(aoi)`, clear aperture; **hysteresis, creep, print-through, segmented and bimorph kinds not built** |
| AO2 `slm` | `Devices.cs` | done: pixels, pitch, fill factor, grey levels, linear / tanh / table curves, phase target to the nearest level, liquid-crystal `dn(lambda)` (Cauchy), cross-talk, zero-order leak; 1920 x 1080 as callback or NPZ; **the Delta-n preset is generic and unverified** |
| AO3 `tiledArray` | `ultrafast_ao.tiled_array`, `combining_metrics` | done as a **builder of the core tile source** (the advisor's correction: a source is not an element); no nested per-channel systems |
| AO4 sensing | `ultrafast.algorithms.control`: `shack_hartmann` (lenslet FFT + windowed centroid, noise), `modal_fit`, `southwell_reconstructor` | done, NumPy; the bench uses the engine's `wavefrontSensor` as the ideal zonal sensor (the engine has no complex-field output to feed the lenslet model) |
| AO5 calibrate / correct / loop | `control.interaction_matrix`, `control_matrix` (Tikhonov, TSVD), `correct`, `integrator_loop`; `ultrafast_ao.DmBench.calibrate / correct / loop` | done; **no `ultrafast ao ...` CLI commands** (they are Python) |
| AO6 holograms | `ultrafast.algorithms.holography`: GS (quantisation in the loop), WGS, MRAF, double-phase, Bolduc, `grey_levels` | done |
| AO7 sensorless | `sensorless`: SPGD, hill climb, modal search, phase-stepping transmission matrix | done; phase locking = SPGD on the tile pistons (tested through the engine) |
| TS1 `spectralShaper` | `plugins/ao/src/TemporalShapers.cs` | done (1-D transfer function; spot overlap by erf) |
| TS2 `aopdf` | same | done (polynomial or table phase, window, efficiency, replica); closed-loop shaping tested with the engine's peak power as the metric (GDD compensation); **FROG / d-scan as the metric is not wired** (the metric is any function; the engine's `frog` / `dscan` instruments can be called by the user) |
| physics review | `physics-reviewer` pass | see the end of this page |

## Numbers (development laptop, Apple silicon, CPU)

* 1920 x 1080 `slm`, 256 grey levels from a callback, cw, grid 2048 x 1280, `keepProfiles: "used"`: **2.4 s** (S2 limit 13 s).
* `DmBench` (128 x 128 cells, f = 0.5 m): about 0.1 s per run; calibration of 100 actuators (200 pokes) 7 s; modal search over six modes (54 runs) about 5 s.
* One-shot zonal correction of {Z4: 0.3, Z5: -0.2, Z7: 0.15} waves rms: Strehl **0.31 -> 0.87** (10 x 10 actuators of 0.4 mm), modal (Zernike fit) **0.965**; the loop at gain 0.7 reduces the slope rms by 0.3 per step (1 - g).
* Strehl against theory: the engine's focal peak is 0.5-2.4 % above the exact on-axis value `|<a e^{i phi}>|^2` for rotationally symmetric aberrations (the peak may leave the axis); Marechal `exp(-sigma^2)` is within 1 % at sigma = 0.25 rad and 5 % at 0.5 rad (defocus + astigmatism).
* SLM blazed efficiency: within 0.6 % of the sampled staircase for N = 4, 8, 16; at other wavelengths within 1.5 % of the `s(lambda)` law (900, 1030, 1200 nm).
* Weiner side pulses of a gapped shaper: energy ratio `[sin(pi f)/(pi f)]^2 exp(-2 (pi w0/pitch)^2)` within 1 %, position within 1 fs.
* Tile delay cost: 0.3 %, 0.04 %, 0.65 % from the Gaussian-overlap formula for rms delays of 10, 25, 40 fs (7 tiles, 30 fs).
* Transmission matrix: phases exact to 1e-9 rad; focusing enhancement over 3000 random outputs of N = 64: 50.40 against (pi/4)(N - 1) + 1 = 50.48 (standard error 0.07).
* GS: quantisation in the loop helps at 2 and 3 levels (6 of 6 seeds), **not** at 4 or 8 on the ring target.
* SPGD, 16 channels, 400 iterations: final lock 1.000 (minimum over 10 seeds) at gain 10, amplitude 0.2; gain 20 diverges (mean 0.67).

## What S5 (examples) may call

* `ultrafast_ao`: `DeformableMirror`, `Slm`, `SpectralShaper`, `Aopdf` (element dicts), `DmBench`, `tiled_array`, `combining_metrics`, `devices.*` twins. `DmBench(aberration={noll: waves}).strehl() / .calibrate() / .correct(D) / .loop(D) / .zernike_commands(coefs)`.
* `ultrafast.algorithms.holography / control / sensorless` (no plugin needed). `ultrafast.algorithms.zernike` for the aberration plots (S1).
* **Pitfalls for example authors** (all in `docs/plugins/ao.md`): the shaper needs `grid.windowS` to cover `2 pi / pixel bandwidth`; the engine's focal plane is sampled at the simulation pitch, not at `lambda f / (N dx)` (keep the pattern inside the window); give big SLM maps as a callback or file; a tile delay moves the envelope only; the tile source is normalised to the total power; the beam profile peak should be refined between pixels (`DmBench.peak_fluence` does).

## Not done / for the owner or later sessions

* **NativeAOT build does not compile the plugin in** (`UF_WITH_AO` in `src/UltrafastEngine.Native` is S2's area). The hosted engine (Python default, CLI) loads it. Request: add `UF_WITH_AO` as for `UF_WITH_HITRAN` if the native Python wheel should carry it.
* **Grid planner does not know a shaper's time extent** (`2 pi / pixel bandwidth` side pulses): the user sets `grid.windowS`. A hook in `GridPlanner` (core, S2's area) could read it from the term.
* **GPU parity** of the S2 primitives and the AO elements was never run on a device; the plugin adds nothing GPU-specific (maps are computed in C# and applied by the core modulator).
* **net48**: the solution builds for net48 (0 errors); the tests were run on net10.0 only (no Mono on this Mac).
* Edits outside `plugins/ao/**` that were needed: `UltrafastEngine.sln`, `build/CopyPlugins.targets`, `build/package.py`, `tests/UltrafastEngine.Tests/{UltrafastEngine.Tests.csproj,TestFramework.cs}` (reference and register the plugin), `SchemaTests.cs` (the shipped-origins list gained `"ao"`), `python/tests/test_packaging.py`, `python/src/ultrafast/algorithms/{__init__,holography,control,sensorless}.py`, `docs/plugins/ao.md`, `plugins/CLAUDE.md`, and the regenerated `schemas/*`, `docs/reference/options.md`, `python-api.md`, `validation-matrix.md`, `types.py`.
* **Shared files left for S5** (per the roadmap's ownership rule): `mkdocs.yml` and `llms.txt` (add `plugins/ao.md` to the nav; the page builds under `--strict` without being in the nav), `docs/plugins/index.md`, `docs/reference/plugins.md` (first-party list), `CHANGELOG.md`, `docs/testing.md`, `PROJECT_STATUS.md`, `docs/capabilities.md`, `ROADMAP.md`, `docs/roadmap.md`.

## Changelog lines (for S5 to paste into `CHANGELOG.md`, Unreleased)

Added:
* `ao` plugin: `deformableMirror`, `slm`, `spectralShaper` and `aopdf` elements (parametric, no vendor data); the first two compile into the core `modulator`.
* `ultrafast.algorithms.holography` (Gerchberg-Saxton with quantisation in the loop, weighted GS, MRAF, double-phase and Bolduc encoding, grey-level mapping), `.control` (Shack-Hartmann lenslet model, Zernike modal and Southwell reconstructors, interaction / control matrices, integrator loop) and `.sensorless` (SPGD, hill climbing, modal search, phase-stepping transmission matrix).
* `ultrafast_ao`: `DmBench` (simulated AO bench that drives the engine), `tiled_array` and `combining_metrics` for coherent tiled-aperture arrays, NumPy twins of the devices.

## `docs/testing.md` rows

| test file | tests | what |
|---|---|---|
| `AoDeviceTests.cs` | 17 (1 slow) | deformable mirror (tilt, 2 cos(aoi), coupling, stroke, hex), SLM (blaze, orders, liquid-crystal dispersion, nearest level, 1920 x 1080), erf, aopdf (delay, GDD, window, replica), spectral shaper (Weiner side pulses, delay) |
| `python/tests/test_ao_algorithms.py` | 18 | holography, sensing and control, sensorless, transmission matrix |
| `python/tests/test_ao_plugin.py` | 12 | Strehl and Marechal, device twins, fit residual, correction and loop, modal search, GS hologram through the slm element, tiled array, SPGD lock, GDD shaping |

## Physics review

One `physics-reviewer` pass (15 findings). **Fixed:** `spectralShaper.offsetM` cancelled out (now moves the array; test added); the frequency-to-position map is now exact in wavelength (was linear in omega); the AOPDF replica created energy (now split off the main pulse, total = efficiency; test added); the `deformableMirror` `commandsM` are documented as drive coefficients (a uniform command gives a ripple of about 1.7 x at coupling 0.15, not a flat piston); the nearest-grey-level search uses the circular distance for curves that span more than 2 pi (C# and Python; tests added); the Airy radius in `combining_metrics`; the unit hint (1e6 = 1 mm/nm); the `transmission_matrix` docstring (column 0 is a marker, cost 4(n-1)+2); the Weiner test now uses a 150 fs pulse and a 32 ps window because the spectral grid sampled the comb edges (5 samples per pixel gave 2.5 % errors: now in the docs). **Documented, not changed:** the field-overlap model of the spot (the Weiner test checks its implementation, not the choice); no anamorphic grating factor; a large programmed GDD or a shaper's side pulses can wrap around the time window without a warning (needs a planner hook in the core); `modal_fit` uses the gradient at the lenslet centre, not the subaperture mean. **Open (no test yet):** AOPDF GDD/TOD sign (red first for +GDD), grating-derived dispersion against a Littrow reference, SLM fill factor / gap / cross-talk through the plugin (covered by the core modulator tests), end-to-end Bolduc and double-phase through an SLM, drive-bit step size, tiled-array lobe fractions beyond `0 < f < 1`.

## Verification (test-runner, before the review fixes)

net10.0 `--slow` 523 passed / 0 failed / 9 skipped; Python with pyNLO and slow 205 passed / 1 skipped (no GPU); `example_check.py` 0 problems; `mkdocs build --strict` 0 warnings; solution builds with 0 errors. Not run: net48 tests (no Mono), GPU, OpticStudio. After the review fixes the changed tests, the ao Python tests, the regenerated files and the full net10 suite were re-run (see the commit message).
