# S1 Foundation: handoff notes

Branch `dev/2026-11-s1-foundation` (from `dev/2026-11`), written 2026-11-09. **Status: all eight steps of the S1 brief are done.** What is not,
and what the next sessions must know, is at the end.

## 1. The advisor call (Opus 5.5) and what it changed

One call, after reading and before any edit. Applied: a committed snapshot of the 2.2.0 public surface before the split
(`python/tests/data/public_surface_2_2_0.json`) drives the import-every-old-name test; no submodule is named like a figure function
(`plotting.overview` is a function, the module is `run_figures`; `components.gain` is a function, the module is `amplifiers`); `ultrafast.diagram`
warns and no package module imports it; matplotlib and Pillow stay lazy (tested in a subprocess with `-W error::DeprecationWarning`); the generated API
docs were regenerated right after the move; the example regrouping checked ignored `results/` folders, relative `../` paths, the 08 -> 09 dependency and
`example_check.py` patterns before the single `git mv` pass.

## 2. Old -> new names (Python)

Nothing that was public in 2.2.0 disappeared. New names are additions; the only deprecated path is `ultrafast.diagram`.

| old | new | how |
|---|---|---|
| `ultrafast.plotting` (one module of 1371 lines) | package `ultrafast.plotting`: `style`, `data`, `time_domain`, `spectral`, `run_figures`, `spatial`, `instrument_panels`, `diagrams`, `zernike`, `animate` | `__init__` re-exports every old public name and the private helpers the fitting plugin imports (`_figure`, `_has`, `_sanitize`, `_si`, `_Style`, `_theme`, `_cmap`, `fmt`, `load`, `_AXIS_LABELS`, `_fmtu`, `_named_axis`, `_value_unit`, `measurements`); `report`, `register_figure`, `plot`, `FIGURES` live in `__init__` because they look figures up by name |
| `ultrafast.diagram` | `ultrafast.plotting.diagrams` | `diagram.py` is a shim that re-exports everything and raises a `DeprecationWarning` on import |
| `ultrafast.plot` (the `ultrafast-plot` script) | unchanged | |
| `build.Sequential`, `Branched`, `link`, `loop`, `virtual_input`, `system` | `ultrafast.system.*` (+ `System`, `load`, `save`) | re-exports |
| `build.mirror`, `lens`, `grating`, `grating_pair`, `prism`, `plate`, `aperture`, `soft_aperture`, `mask`, `wavefront`, `chirped_mirror`, `dispersion`, `substrate`, `interface`, `nd_filter`, `filter_element`, `coating_ripple`, `probe`, `user` | `ultrafast.components.optics` (CapWords alias of each) | the same function object |
| `build.beam_splitter`, `polarizing_splitter`, `dichroic`, `leak_mirror`, `delay` | `components.splitters` | alias |
| `build.waveplate`, `polarizer`, `thin_film_polarizer`, `faraday_rotator` | `components.polarisation` | alias |
| `build.crystal` / `fiber` | `components.crystals` / `components.fibres` | alias |
| `build.gain`, `pump`, `multipass_amplifier`, `regenerative_amplifier`, `saturable_absorber`, `thermal_lens`, `resonator` | `components.amplifiers` | alias |
| `build.bulk`, `gap` | `components.media` | alias |
| `build.pulse`, `cw`, `measured_spectrum`, `measured_spectrum_csv`, `pedestal`, `gaussian_beam`, `constant_beam`, `sampled_beam`, `aux_beam`, `initial_profile`, `profile_ripple`, `profile_noise` | `ultrafast.sources` | alias |
| `build.instrument`, `axis`, `detector*`, `measured` | `ultrafast.instruments` + 18 explicit factories (`Spectrometer`, `Frog`, `DScan`, `Camera`, `WavefrontSensor`, ...) | real `def`s, so signatures and return types are visible to editors |
| `core.Result`; `plotting.SavedResult`, `load` | `ultrafast.results` | re-export; adds `fwhm`, `rms_width`, `strehl_marechal` |
| `core.retrieve`, `retrieve_multi`, `study` | `ultrafast.algorithms.retrieval`, `.optimisation` | re-export |
| (new) | `ultrafast.algorithms.zernike`, `ultrafast.plotting.zernike`, `ultrafast.plotting.animate` | |
| `build.run_spec`, `physics`, `grid`, `tail_grid`, `compute`, `sampling`, `save`, `override`, `environment`, `air`, `transverse`, `material`, `sellmeier`, `gas`, `coating_gdd`, `coating_quarter_wave`, `coherence`, `delay_uniform`, `delay_exponential`, `delay_table`, `delayed_scatter` | stay in `build` only | a decision, not an omission: they describe a run or a global setting, not a component, source or instrument. A test fails if a new public builder is neither grouped nor on this list |

`ultrafast_fitting/figures.py` was **not** folded into the plotting package: the figure reads the fitting result format and is registered only when
the plugin loads, so core plotting must not import it. It keeps its file and imports the stable names from `ultrafast.plotting`.

## 3. Examples: old number -> new path

One `git mv` pass (ignored `results*/` folders moved with their folders). The table with old numbers is in `docs/examples/README.md`; `example_check.py`
accepts a name, a group or an old number (`08`). `LaserSystems/shg-lbo` sits with `nopcpa-bbo` (the roadmap had it under Nonlinear) because 09 reads 08's
results; `ultrafast-examples copy` brings it along. Group folders: GettingStarted (5), LaserSystems (10), Nonlinear (8), Diffraction (3), Wavefronts (2),
Branched (4), Diagnostics (7), Components (2), DigitalTwin (1: transforms), Interop (zemax-workflow, cpp), Computing (4, new), AdaptiveOptics (README only).
138 files had links rewritten by a script that resolves each relative path from the old location and re-expresses it from the new one.

## 4. What was built

* **Computing examples** (`docs/examples/Computing/`): 4f-correlator, optical-convolution, matrix-vector, parallelism-and-time, with a shared `_optics.py`.
  The one rule: **f = N dx² / lambda** makes `lens + gap(f)` the DFT (the Fourier-plane pixels are the DFT bins), and a 4f system with a mask is the
  convolution theorem. Numbers against numpy: lens as DFT 9e-3 relative l2 (random sparse complex vector), 4f identity 2e-3, blur 1e-4, edge filter 3.8e-3,
  matched filter 3.8e-2 over the whole map with peak positions identical and heights within 0.5 %.
* **`docs/examples/make_all.py`** (`--group`, `--only`, `--no-animations`, `--jobs`, `--report`, size-budget check, table) and a `make_figures.py` in every example
  (42) plus `make_all.py` per group. The generic `make_figures.py` runs the README commands and writes `figures/overview-*.png` and `figures/hero.png` (git-ignored);
  Computing has purpose-made scripts that write a committed `hero.png` and `animation.gif`. READMEs link only the committed files (a missing image breaks `mkdocs --strict`).
* **`build/package.py --examples-wheel`** -> `ultrafast_examples-2.2.0-py3-none-any.whl` (3.8 MB) with the `ultrafast-examples` command; extras `animate` and `examples` in `pyproject.toml`.
* **`build/gallery.py`** -> `docs/gallery.md` (in nav and `llms.txt`); `--hero NAME` copies an animation to `assets/hero.gif` for the README (S5 decides).

## 5. Numbers

* Animation budget: four GIFs, 39-329 kB (limit 2.5 MB); four hero PNGs 55-282 kB (limit 300 kB; one example, `Diffraction/iris-diffraction/diffraction-images.png`, was already
  over 300 kB before S1 and is not touched).
* `make_all.py --group Computing`: 4 examples in about 12 s on the owner's Mac. `parallelism-and-time` (N up to 1024): engine 0.02 ... 1.6 s, numpy FFT 0.00004 ... 0.02 s, optical model about 1e-6 s (4 f / c = 0.4 ... 6.8 ns plus an *assumed* 1 us exposure).
* Python tests: 173 pass, 1 skipped (was 127 at the last status update; the 38 new tests are `test_structure`, `test_zernike`, `test_computing`, `test_examples_package`).

## 6. Commands

```bash
python docs/examples/make_all.py [--group Computing] [--only 4f-correlator] [--no-animations] [--jobs 4] [--report r.json]
python build/gallery.py [--check] [--hero 4f-correlator]
python build/package.py --examples-wheel --out dist/       # then: pip install --find-links dist/ ultrafast-examples
python build/example_check.py shg-lbo Diffraction 26        # a name, a group or an old number
```

## 7. Changelog lines (for the merging session; `CHANGELOG.md` is a shared file)

* Python: `ultrafast.plotting` is a package; new `ultrafast.system`, `components`, `sources`, `instruments`, `algorithms`, `results`; `ultrafast.diagram` moved to `ultrafast.plotting.diagrams` (the old name warns).
* Python: `algorithms.zernike` (Noll, NumPy), `plotting.zernike` decomposition figure (map, fit, residual, coefficient bars, rms and Maréchal Strehl; several wavefronts; animated), `plotting.animate` (GIF with Pillow, optional MP4, size budget).
* Examples regrouped into folders by topic (`docs/examples/README.md` has the old numbers); `make_all.py` and `make_figures.py` per example; new Computing examples (4f correlator, optical convolution, matrix-vector, parallelism and time); `docs/gallery.md`.
* Packaging: `build/package.py --examples-wheel` and the `ultrafast-examples` command; extras `animate`, `examples`.
* `.gitignore`: `!python/src/ultrafast/results/` (the `results/` rule had hidden the new Python package from git; CI caught it).

`docs/testing.md` rows to add (shared file): Python structure (every 2.2.0 name imports, shim warns, matplotlib lazy, builder table complete); Zernike (orthonormal to 1e-12 by quadrature,
engine `wavefrontSensor` coefficients to 1e-6, Maréchal); GIF writer (frames, size, loop, held last frame); Computing (lens = DFT 2e-2, shift theorem 0.05 px, each example's own checks);
examples wheel (metadata, contents, `list` / `copy` / `run`).

## 8. Not done, and for the next sessions

* **Not run here:** `net48` (no Mono on the owner's Mac); `ultrafast-examples run` of a CLI-driven example needs the `ultrafast` program on the PATH (only Python-driven ones were exercised end to end).
* **The other 38 examples have only the generic hero** (`figures/`, git-ignored); the 4 Computing examples are the only ones with committed figures and animations. S5 decides which others get a purpose-made figure and a committed animation (and the repository-size question of section 8 of the roadmap).
* **Mask phase is sampled on the grid.** A phase map with detail at every pixel (a matched filter) costs about 4e-2 relative l2 over the whole map; smooth filters cost 1e-4. S2 (`modulator`) should keep this in mind.
* **The lens is round.** A general matrix-vector multiplier needs an anamorphic layout (cylindrical lenses). Note for S2: a `lens` with separate x/y focal lengths.
* **Thin grids:** a "1-D" system uses `ny = 4` rows with `halfWidthYM = ny dx / 2` (so dy = dx). `ny = 1` fails with a `measured` profile (the input was not reproduced); not investigated further.
* **Grid coordinates:** the cartesian grid is `x_i = (i - N/2) dx` (it contains 0). A map written with half-pixel-centred axes is interpolated and blurred.
* `docs/examples/README.md` prose in the example READMEs still says "example 08", "example 26": the old numbers are in the index table; rewriting the prose was left alone.

## 9. Verification of the finished S1 (2026-11-09)

C# `--slow` on net10.0: 468 pass, 0 fail, 8 skipped (7 GPU, 1 opt-in HAPI); net48 not run (no Mono). Python with `ULTRAFAST_SLOW=1` and pyNLO: 173 pass, 1 skipped (no FP64 GPU).
Generated files unchanged by regeneration. `mkdocs build --strict`: 0 warnings. `make_all.py --group Computing`: 0 problems; `gallery.py --check` current; the examples wheel builds.
`example_check.py`: the first full run reported 9 problems. Eight were the test runner using a stale `bin/native` engine (no `m2Y`, `packetsAtEnd`, `externalAngleDeg`); with `ULTRAFAST_LIBRARY` set to
the hosted build they all reproduce their README numbers. **One was a real regression of the regrouping**: scripts that locate `python/src` with a fixed number of `..` or `parents[n]` (15 files)
and `compare_pynlo.py` ran only because `PYTHONPATH` was set; they now add the extra level (checked by evaluating each path). Lesson: after moving folders, grep for `"..", ".."` and `parents[`.
The test runner's example runs also rewrote two tracked PNGs (`f2f-cep/f2f.png`, `interferometers/interferometers.png`); they were restored from the index.
`google417a1b176c5dfb1e.html` (Search Console) was moved into `docs/` so it is published at the site root; the meta tag in `overrides/main.html` covers every page.
