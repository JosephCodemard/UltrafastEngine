# S5 Examples, docs, verification: handoff

Branch `dev/2026-11-s5-examples` (from `dev/2026-11` at 098dc80), 2026-11-10. What was built, what was verified, what was not, and what is left of the
plan in [roadmap-ao-twin-2026-11.md](../roadmap-ao-twin-2026-11.md).

## 1. State of the whole plan after S5

| part | state |
|---|---|
| S1 Python structure, plotting, grouped examples, Computing | done, merged (2026-11-09) |
| S2 core: `modulator`, in-memory maps, streaming, sparse sampling, tiles, fast path | done; GPU parity written, **never run on a device** |
| S3 `ao` plugin | done; not built: DM hysteresis / creep / segmented / bimorph, per-channel tile systems, FROG / d-scan as a loop metric, `ultrafast ao` CLI commands, TS3 (4f line with spatial chirp), an engine-side Shack-Hartmann instrument |
| S4 fitting across conditions (F1-F5) and the `twin` plugin (T1-T5, T7) | done; **T6 MCP server not built** |
| S5 examples, docs, verification | done as listed below; the items in section 3 are **not** done |

## 2. What S5 delivered

**New examples** (each: a README with the commands and the quoted run output, `make_figures.py`, a hero figure, an animation where an algorithm
iterates, an assertion of the key number; all run under `ULTRAFAST_STRICT_OPTIONS=1`):

| group | example | key number checked | run time |
|---|---|---|---|
| AdaptiveOptics | `dm-correction` | Strehl 0.32 to 0.90 (zonal), 0.97 (modal); slope rms falls by 1 - gain per step | 9 s |
| AdaptiveOptics | `sensorless-correction` | modal search 0.94 in 36 runs, SPGD 0.95 in 240 | 13 s |
| AdaptiveOptics | `aberrated-high-power` | peak fluence 234 to 102 J/m², peak nonlinear phase 1.55 to 0.60 rad | 45 s |
| AdaptiveOptics | `slm-beam-shaping` | engine against the Fourier prediction of the written 8-bit phase: correlation 0.98-0.99; full 1920 x 1080 panel (`--full`): 91 % on target | 3 s (+ 17 s full) |
| AdaptiveOptics | `tiled-array` | N x peak (7.00, 61.0); piston 0.785 vs 0.787; delay cost within 2 % of the overlap formula; SPGD lock | 10 s |
| AdaptiveOptics | `temporal-shaping` | pulse spacings; GDD x bandwidth 555 vs 554 fs; Weiner side-pulse energy 0.0159 vs 0.0159; chirp found to -900 fs², -4018 fs³ | 1 s |
| AdaptiveOptics | `scatterer-focusing` | enhancement 47.5 against 50.0 (Rayleigh matrix, Gaussian beam) | 3 s |
| DigitalTwin | `build-the-twin` | hidden centre, width, slope recovered; held-out predicted; saturating lab flagged | 4 s |
| DigitalTwin | `explore-the-twin` | what-if bands; information-gain ranking; drift followed to 0.02 nm and flagged | 20 s |
| Fibres (new group) | `soliton-breathing` | N = 1 within 0.03 %; N = 3 back to 1.003 x at z0 = (π/2) L_D | 8 s |
| Fibres | `raman-soliton-shift` | rate against T0 has slope -4.3 (Gordon: -4); absolute rate 16-36 % above Gordon's formula (stated, not tuned away) | 35 s |
| Fibres | `spm-compression` | pure-SPM bandwidth growth 8.85 x = the formula; 300 fs to 88 fs after a compressor found from the peak power | 100 s |
| Wavefronts | `spatial-gain-thermal-lens` (existing): a new hero with the 2-D beam, wavefront, Zernike content and the beam behind the rod | - | - |

**Code and docs:** `docs/examples/_viz.py` (shared figure helpers; excluded from the examples wheel); `Fibres/` group (README, `make_all.py`);
`python/tests/test_twin.py` (9 tests, the S4 gap); a C# test that `ultrafast twin` is registered and exits with 2 when the interpreter cannot be started;
`docs/plugins/twin.md`, `plugins/twin/README.md`; the nav, `llms.txt`, plugin index, plugin system page, capabilities, roadmaps, agents guide, testing rows, changelog
and status; the gallery (`build/gallery.py` knows `Fibres`).

**Findings made while writing the examples** (all stated in the READMEs):

* The engine's focal plane equals the hologram's FFT lattice at `f = N dx² / λ`; patterns must stay in the inner half of the window and the prediction needs the
  |sinc|² of the square pixel (correlation 0.90 without it, 0.98-0.99 with it).
* The SPGD gain matters: gain 1.5 gave 0.64, gain 3 gave 0.95, gain 5 gave 0.80 in the same 80 iterations on the sensorless example (the example uses 3).
* Tiled-array SPGD ends at 1.02 of the in-phase reference on the 128-point grid: not explained, within the example's bound.
* Pure SPM broadening is exactly the analytic formula (8.85 x); with the fibre's dispersion it is 4.05 x.
* `ultrafast twin` works from the real CLI when `ULTRAFAST_PYTHON` points to a Python with NumPy and `ultrafast` (checked: `init`, `fit`, `validate` on the synthetic lab).
* The wavefront sensor reports phase noise outside the beam (lenslets without light): maps are masked inside 1.5 beam radii (aberrated-high-power).

## 3. Not done (the owner decides the order)

1. **MCP server** (T6) and therefore the agent-driven design example (E14). The design is in `s4-twin.md` section 5; `ultrafast twin serve` exits with 2.
2. **E1 component gallery** and **E6 complex-field shaping with a phase-only device** (double-phase / Bolduc through an SLM) for the `ao` plugin.
3. **E13 spline against network transform** and **E15 wavefront twin** (needs a spatial project template).
4. **The older examples.** The owner's note asks that every example tells a story with plots and animations. Only `Wavefronts/spatial-gain-thermal-lens` was redone
   here. Still the generic overview hero: all other examples in `GettingStarted`, `LaserSystems` (wavefronts, contrast, FROG trace at the end of each chain),
   `Nonlinear`, `Diffraction`, `Branched`, `Diagnostics`, `Components`, `Wavefronts/position-dependent-index`, `DigitalTwin/transforms`. The pattern to follow is
   in the new examples (a physics module that prints and asserts, a `make_figures.py` that draws the story).
5. Core requests (S2's area): `UF_WITH_AO` in the native library; a grid-planner hook for a shaper's side pulses and large programmed GDD; the unknown-element
   message naming `ao`.
6. Not run anywhere: GPU parity (CUDA, OpenCL), OpticStudio, **net48 tests** (no Mono on this Mac; CI runs them), NativeAOT with the `ao` plugin.
7. Untested items listed in `s3-ao.md` (grating-derived dispersion of the shaper against a Littrow reference, end-to-end Bolduc, lobe fractions beyond 0 < f < 1).

## 4. Verification

Local run, macOS arm64, .NET 10, Python 3.12, 2026-11-10 (CI is the check for Linux, Windows and net48):

* `dotnet build UltrafastEngine.sln -c Release`: 0 errors.
* C# `--slow`: 543 passed, 0 failed, 9 skipped before the new twin test; `PluginTests` (8, with the new one) pass: 544.
* Python (hosted engine, `ULTRAFAST_SLOW=1`, pyNLO): 216 passed, 1 skipped. Run **without** `ULTRAFAST_STRICT_OPTIONS`: with it, `test_zernike.py::test_python_fit_agrees_with_the_engines_wavefront_sensor` fails because the test writes the snake_case key `reference_wavelength_m` (a warning normally, an error in strict mode; a one-key fix in that test, not done here).
* `example_check.py` on AdaptiveOptics, DigitalTwin, Fibres, Wavefronts: all ok after one fix (a timing printed in `build-the-twin`).
* `mkdocs build --strict`: 0 warnings. Validation matrix regenerated.
* Not run: net48 tests, GPU, OpticStudio, the full `example_check` of the other groups (unchanged), the physics-reviewer agent on the new examples.

## 5. Session log (2026-11-10, S5)

1. Read the roadmap and the S1-S4 notes; wrote the done / to-do summary (sections 1-3 above). Advisor call 1 (before the examples): check CI, find how CI runs examples
   (hosted engine, so `ao` is available), close the S4 gaps first, do not duplicate the Dudley PCF supercontinuum.
2. Built `docs/examples/_viz.py`, then the examples in the order DM correction, SLM shaping (incl. the full panel), sensorless, tiled array, temporal shaping,
   scatterer, aberrated high-power beam, the two twin examples, the three fibre examples, and a new Wavefronts hero. Every physics claim was run first and the
   README quotes the real output; disagreements are stated (Raman rate 16-36 % above Gordon's formula; engine against Fourier prediction 0.98-0.99, not 0.999).
3. Closed S4 gaps: `python/tests/test_twin.py` (9 tests), a C# test for the `twin` command, `ultrafast twin init|fit|validate` run from the real CLI, `docs/plugins/twin.md`.
   Decided **not** to build the MCP server (T6) and E14 in this session; recorded in sections 1 and 3.
4. Merge wave: changelog, testing rows, status counts, README badges (544 C#, 216 Python), nav, `llms.txt`, plugin index and system page, capabilities, roadmaps,
   agents guide, gallery (`Fibres`), validation matrix.
5. Verification: see section 4. **Advisor call 2 (before the merge)** found three things, all fixed: the README commands ran `make_figures.py` in CI (now marked
   `# optional:` so the check runs only the physics script), the `test_zernike.py` key (`referenceWavelengthM`), and the roadmap status rows.
6. CI on `dev/2026-11` after the S5 merge: every job green except "Docs and example check", which failed on a wall time printed by `tiled_array.py` (the quoted
   output must not contain timings; `build-the-twin` had the same problem earlier). Fixed by removing the time from the output; the next run was started.
7. Merged to `main` on the owner's instruction after that fix. No tag, no release, no version bump (2.3.0 is the convention when the owner asks).

**Rule learned:** a script whose output is quoted in a README must print no wall time, and `example_check` runs every line of a ```bash block, so figure scripts
need `# optional:`.
