# Reference audit F (work package C1f)

Pages: `docs/reference/calculators.md`, `pulse-calculator.md`, `plotting.md`, `mcp.md`, `docs/concepts.md`, `docs/capabilities.md`.
Date: 2026-11-25. Branch `dev/2026-11`.

Method: every factual claim was compared with the source of truth, not with other docs. Sources read: `python/src/ultrafast/calc/*`,
`plotting/*`, `plot.py`, `mcp/*`, `build.py` (not `types.py`); `src/UltrafastEngine.Core` (`Pulses/PulseCalculator.cs`, `Schema/BuiltIn*.cs`,
`Engine/Compiler.cs`, `Engine/GridPlanner.cs`, `Specs/RunSpec.cs`); `plugins/gpu`, `plugins/twin`; `python/tests`, `tests/UltrafastEngine.Tests`.
Signatures were taken from `inspect.signature`. Numbers (TBP table, the `analyze` example, the `single_lens_match` example, the 3.2 GW
critical power) were recomputed and agree. Only problems are listed. Severity: wrong (contradicts the code), stale (was true, no longer),
unclear (cannot be acted on or cannot be verified from the code), missing (the code has it, the page does not say so).

## docs/reference/calculators.md

| # | claim (page:line, quoted briefly) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| 1 | :58-59 `single_lens_match(waist_in_m=, waist_out_m=, wavelength_m=, focal_length_m=)`, `two_lens_match(w1, w2, ...)` | both take `m2=1.0` (the formulas on the page use M^2 but the signature omits it). `two_lens_match` positional names are `waist_in_m`, `waist_out_m` | `calc/beam.py:196`, `:221` | missing | fixed: signatures list `m2`, `two_lens_match` positional names and `max_distance_m`, `samples` |
| 2 | :59 `two_lens_match`: "every first-lens position that gives the output waist" | scans only `d1` in `[0, max_distance_m]` (default 20 x (abs f1 + abs f2 + separation + input Rayleigh range), `samples=20001`); a root outside the range or between samples is not returned; the result has only `solutions` (no output waist position beyond `distanceOutputM`) | `calc/beam.py:221-256` | unclear | fixed: Model and Limits state the search range, the sampling and the missed-root case; code finding C4-f1 proposes a warning |
| 3 | :61 `m_squared_from_widths(z_m, d4sigma_m, wavelength_m=)` | `wavelength_m` is required (no default, so `=` suggests a keyword with a default); needs >= 3 positions; raises `ValueError` if the data are not a hyperbola; returns `m2`, `waistDiameterM`, `waistPositionM`, `divergenceFullAngleRad`, `rayleighRangeM` (the page says "M^2, waist, position, divergence": the waist is a diameter, the divergence a full angle) | `calc/beam.py:285-299` | unclear | fixed: `m_squared_from_widths` row lists the required `wavelength_m`, the minimum of 3 positions, the error and all result keys |
| 4 | :62 `m_squared_from_waist_divergence`: "M^2 = pi w0 theta / lambda" | arguments are `(waist_radius_m, half_angle_rad, wavelength_m)`: theta is the half angle; the page does not say so | `calc/beam.py:280-282` | unclear | fixed: the row says the argument is the half angle |
| 5 | :60 `telescope(magnification=, focal_length_1_m=, kind=...)`: "`f2`, the separation `f1 + f2` and the divergence reduction" | keplerian needs `f1 > 0`, galilean `f1 < 0`, otherwise `ValueError`; keys are `focalLength1M`, `focalLength2M`, `separationM`, `divergenceReduction`; `magnification < 1` reduces the beam | `calc/beam.py:258-277`; test `test_telescope_rejects_the_wrong_sign` | missing | fixed: sign rules, result keys and the magnification rule are listed |
| 6 | :77 `collapse_distance_m`: "error up to ~40 % near `P_cr`, < 20 % for 2.5 < P/P_cr < 20" | the module docstring says only "within about 20 % for 2.5 < P/P_cr < 20"; no source or test for the 40 % figure | `calc/nonlinear.py:15`; `python/tests/test_calc.py:199-209` | unclear | fixed: the unsourced 40 % figure is removed; the page quotes the module docstring (about 20 % for 2.5 < P/P_cr < 20) |
| 7 | :78, :82 `peak_power_w(pulse_energy_j, duration_fwhm_s, shape)`; `b_integral_estimate(...)` | `shape` defaults to `"gaussian"` and only `gaussian` / `sech2` (aliases `gauss`, `sech`, `sech^2`) are accepted. `b_integral_estimate` also takes `shape` and `n0`; without `n0` the keys `criticalPowerW` and `powerOverCritical` are `nan`. Result keys (`peakPowerW`, `peakIntensityWPerM2`, `peakFluenceJPerM2`, `bPeakRad`, `bPulseAveragedRad`, `bBeamAveragedRad`, `bPulseAndBeamAveragedRad`, ...) are not listed | `calc/nonlinear.py:52-56`, `:99-111`; `test_b_integral_averages_gaussian_and_sech2` | missing | fixed: `shape` default and accepted names, `n0` and all result keys are listed |
| 8 | :87-93 "Crystals and cells": `phase_match(crystal, process, type, ...)`, `cell_mode(wavelength_m, mirror_radius_m=, separation_m=, peak_power_w=, gas=...)` | `phase_match` also takes `plane`, `theta_deg`, `phi_deg`, `qpm`, `temperature_c`, `polarizations`, `**extra`; `calc.crystal.crystals()` lists the built-in crystals. `cell_mode` also takes `reentrant`, `passes`, `plates`, `lens_model`, `slices` and `mirror_radius_m` may be a sequence | `calc/crystal.py` (signature); `calc/cell.py` (signature) | missing | fixed: full `phase_match` and `cell_mode` signatures, `crystals()` and sequence `mirror_radius_m` |

Verdict: numbers and formulas are correct; the weak points are incomplete signatures (m2, shape, n0), a search range that is not stated, and an unsourced 40 % accuracy figure.

## docs/reference/pulse-calculator.md

| # | claim (page:line, quoted briefly) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| 1 | :4-5 "the same code behind the CLI, Python and the C API" | `limit`, `analyze`, `measure` call the engine (`uf_pulse_calc`); `shapes()` and `tbp()` are a separate Python copy of the closed forms (kept equal by a test) | `calc/pulse.py:51-62` (comment "mirrors PulseCalculator.TbpOf; a test compares both"); `python/tests/test_calc.py:37` | unclear | fixed: the intro says `limit`, `analyze`, `measure` call the engine and `shapes`/`tbp` are a Python copy checked by a test |
| 2 | :21 "Aliases: `lorentz`, `asym-exp`, `exponential`" | the list is incomplete: also `gauss`, `sech`, `sech^2`, `square`, `rect`, `asymmetric_exponential` | `calc/pulse.py:60-62`; `Pulses/PulseCalculator.cs:44-49` | missing | fixed: all aliases listed |
| 3 | :40 `from ultrafast import calc  # = ultrafast.calc.pulse` | `calc` is the package `ultrafast.calc`; it re-exports `analyze, limit, measure, shapes, tbp` from the module `ultrafast.calc.pulse`, which is `calc.pulse` | `calc/__init__.py:16-19` | unclear | fixed: says `ultrafast.calc` re-exports from `ultrafast.calc.pulse` |
| 4 | :58 "A stretch factor below one means the shape, the widths or the units are wrong; the result says so" | the `note` says so only below 0.98; between 0.98 and 1.05 the note is "close to transform-limited" | `Pulses/PulseCalculator.cs:120-121` | unclear | fixed: the `note` thresholds (0.98, 1.05) are given in the results table |
| 5 | :50-56 Results table | the report also has `shape`, `durationFwhmS`, `bandwidthFwhmHz`, `bandwidthFwhmM`, `centerWavelengthM`, `rmsLimitProduct` (1/(4 pi), `measure` only) and `note` | `Pulses/PulseCalculator.cs:109-122`; `PulseReport` in `calc/pulse.py:33-48` | missing | fixed: results table lists every key |
| 6 | :24-46 argument rules | `limit` needs exactly one of duration or a bandwidth (not both); `bandwidth_m` needs `center_m`; `analyze` needs duration and a bandwidth; `measure` needs >= 5 points, equal-length `time_s` / `intensity`, and exactly one of `wavelength_m` / `frequency_hz` (a spectrum per unit wavelength is converted with `S(nu) = S(lambda) lambda^2 / c`); a pulse or spectrum that does not drop below half maximum is an error | `Pulses/PulseCalculator.cs:77-93`, `:95-107`, `:125-157` | missing | fixed: argument rules in a table |
| 7 | :26-32 CLI | the CLI also has `--bandwidth-hz` (limit and analyze), `--shape` (analyze), `--json`; page shows only `--bandwidth-m` / `--center` | `src/UltrafastEngine.Cli/Program.cs:419-421` | missing | fixed: `--bandwidth-hz`, `--shape`, `--json`, `--request` shown |

Verdict: shapes, formulas, the `analyze` example (0.848, 1.92, 156 fs, 14 400 fs^2) and the test names are right; the page under-documents arguments and result keys, and the "same code" claim is not literally true for `tbp` / `shapes`.

## docs/reference/plotting.md

| # | claim (page:line, quoted briefly) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| 1 | :3-4 "what WP-2 added", :55 "the plotting module of the commit before WP-2", :44 | work-package and commit history in a reference page; a reader cannot act on "before" pictures | page text; images `graph-*-before.png` | stale | fixed: work-package and "before" images removed (the image files stay in `docs/assets/plotting/`; `make_plotting_docs.py` still writes them, see C4-f2) |
| 2 | :5 "Everything needs only NumPy and matplotlib (Pillow for the GIF)" | the default install brings numpy and matplotlib only; Pillow is in the extras `animate` and `all`; the page does not tell how to get it | `python/pyproject.toml:22,31-35` | unclear | fixed: the extras `animate` and `paper` are named |
| 3 | :44 "The ninth series and beyond are never generated hues" | the themes hold 8 series colours; nothing in the code folds, facets or refuses a ninth series, the matplotlib colour cycle simply wraps | `plotting/style.py:35,40,62`, `:185` | unclear | fixed: the page says eight colours and that a ninth series reuses the first; code finding C4-f3 notes there is no check |
| 4 | :63 "Runs of identical elements ... are one box" | grouping needs a run of >= 3 identical elements (`min_run=3`) | `plotting/graph_layout.py:53`, `:94` | missing | fixed: run of at least 3 |
| 5 | :66 "`up.system(result, params=True)` adds the key numbers of each element under its box" | `params` has an effect only for a Branched system drawn as a graph; a Sequential system ignores it | `plotting/diagrams.py:525-545` | unclear | fixed: `params` affects only a Branched graph |
| 6 | :78 `up.spectrogram(result, "output")  # {"time_s", "wavelength_m", "intensity"}` | the dict also holds `window_sigma_s`; the function takes `n_time`, `n_wavelength`, `level` (default 1e-3); `spectrogram_3d` takes `floor`, `elev`, `azim` | `plotting/spectrogram.py:65-67`, `:111`, `:114` | missing | fixed: `window_sigma_s`, the parameters and their defaults are listed |
| 7 | :84-85 "its time axis is centred on the pulse's group delay" | the rebuilt time axis is `(arange(n) - n//2) * dt`, centred on t = 0; the group delay appears as an offset of the pulse inside it | `plotting/spectrogram.py:50-54` | unclear | fixed: page says t = 0 centre and the group delay as an offset; code finding C4-f4 is the misleading source comment |
| 8 | :97-98 "stays under its 2.5 MB budget at the default 80 dpi" | the budget is not enforced; `animate` returns `overBudget` (True above 2.5 MB). Defaults: `fps=3`, `dpi=80`, `pause_last_s=1.5` | `plotting/animate.py:86`; `plotting/spectrogram.py:158`, `:228` | unclear | fixed: budget reported (`overBudget`), not enforced; defaults listed; code finding C4-f5 |
| 9 | :96 "One frame per location with the spectrum and the pulse power" | each frame also notes the pulse width: the main-lobe FWHM (`mainLobeFwhmS`), with `fwhmS` "(with satellites)" when a satellite crosses half maximum (added by the merge of `mpc-session6`) | `plotting/spectrogram.py:164-165`, `:194-200` | missing | fixed: main-lobe and outer FWHM described |

Verdict: the style, palette and schematic descriptions match the code (colours, 3.4 / 7.0 in, 600 dpi, tick and grid settings, loop boxes, `--style` / `--column` / `--dark`); problems are internal history text, two unenforced promises and undocumented parameters.

## docs/reference/mcp.md

| # | claim (page:line, quoted briefly) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| 1 | :31 `scan`: "a grid of one to four parameters (at most 400 points)" | also takes `parallel` (1 to 4 points at once, default 1); 1 to 20 `outputs`; all of `system`, `run`, `axes`, `outputs` are required | `mcp/tools.py:53-60`, `:157-176` | missing | fixed: `parallel`, output count, required arguments in the tool table |
| 2 | :35 `study` | also takes `samples` (random, lhs, sobol; default 64), `seed`, `parallel`; `objective.maxEvaluations` defaults to 100 (capped at 400); a parameter may also carry `values` and `scale`; 1 to 4 parameters and 1 to 20 outputs | `mcp/tools.py:77-89`, `:190-241` | missing | fixed: `samples`, `seed`, `parallel`, `maxEvaluations` default, `values`, `scale` listed |
| 3 | :38 `plot` | also takes `column` (`single` / `double`) and `dpi` (50 to 200, default 110, so the 600 dpi of the paper styles is never reached); figure list also has `spectrogram_3d`, `byproducts`, `gain`, `oscillator`, `measurements`; an image above 900 kB is not returned inline | `mcp/tools.py:30`, `:108-114`; `mcp/ops.py:250`, `:257-260` | missing | fixed: `column`, `dpi`, all figures, the 900 kB inline limit |
| 4 | :49 "strings that start with `@` ..., `kind` / `type` of `callback`, `python`, `expression` or `user`" | the keys `callback` and `callbacks` are refused whatever their value | `mcp/policy.py:190-191` | missing | fixed: `callback` / `callbacks` keys listed |
| 5 | :50 file refusals | a system or run file larger than 8 MB is refused; keys ending in `file`, `folder`, `dir`, `directory` and any `path` key (unless it is a study path) are path-checked even without a data extension; the extension list is longer (`.ufs`, `.txt`, `.dat`, `.h5`, `.png`, ...) | `mcp/policy.py:29-32`, `:145`, `:193` | missing | fixed: 8 MB, key-name path checks and extensions listed |
| 6 | :51 "names for result folders that are not 1 to 64 letters, digits, `_`, `-`, `.`" | the first character must be a letter or digit; a name that ends in `.` or contains `..` is refused | `mcp/policy.py:25`, `:162` | unclear | fixed: first-character, trailing `.` and `..` rules |
| 7 | :53 "a run or scan whose memory estimate is above the limit or 60 % of the free memory" | the pre-flight also runs for `compare_to_measured`, `fit`, `study` and `backward`; the study and scan estimate is multiplied by `parallel` | `mcp/ops.py:127`, `:156`, `:180`, `:210`, `:273`, `:300` | unclear | fixed: pre-flight tools listed, `parallel` multiplier; the wording of the pre-flight is in Model |
| 8 | :7 `--budget-s` "time budget of one run or scan" | the budget applies to `run`, `scan`, `compare_to_measured`, `study`, `backward`, `fit` and the twin tools (`fit` / `twin_fit`: `timeBudgetS`, capped at the budget); `describe` and `validate` are capped at 60 s, `plot` at 120 s, `explain_warning` at 5 s | `mcp/server.py:169-174`; `mcp/tools.py:44-114` | unclear | fixed: Model lists which tools the budget caps and the fixed caps; code finding C4-f6 is the `--budget-s` help text |

Verdict: tool names, the thirteen-tool table, resources, prompts, protocol revisions, the 400-point and 4 MB limits and the refusal rules are correct; the tool table omits several arguments and limits that an agent meets as refusals.

## docs/concepts.md

| # | claim (page:line, quoted briefly) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| 1 | :67-68 "a margin (1.3 linear, `max(3, 1 + B_estimate)` nonlinear, and at least +-10 % of the carrier so user responses have room)" | the 1.3 / `max(3, 1+B)` margins are right; the 10 % minimum half-span (`grid.minSpanFraction`) applies to nonlinear runs only (0 for a linear system) | `Engine/Compiler.cs:1783`, `:1791` (`anyNl ? MinSpanFraction : 0.0`); `Schema/BuiltInOptions.cs` (`minSpanFraction`: "Nonlinear runs") | unclear | fixed: the 10 % minimum is stated as nonlinear-only |
| 2 | :46 `snapCarrier` | with `grid.tail` the default of `snapCarrier` becomes false (the carrier stays where the user put it) unless it is written; not mentioned | `Specs/RunSpec.cs:86-89` | missing | fixed: `grid.tail` default of `snapCarrier` added |
| 3 | :136-147 physics switches table | the `physics` object also has `strictValidity`, `strictMaterials`, `strictMaterialsAllow` (the table is not exhaustive and does not say so) | `Schema/BuiltInOptions.cs:125-127` | missing | fixed: `strictValidity`, `strictMaterials`, `strictMaterialsAllow` added and the table is said to be partial |

Verdict: the conventions (sign, units, co-moving frame, mechanisms and loss names, ports, switches and defaults, cw handling of `snapCarrier`) match the code; only the grid-margin sentence and two omissions need work. Line 46 is correct and `capabilities.md:167` contradicts it.

## docs/capabilities.md

| # | claim (page:line, quoted briefly) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| 1 | :167 "A continuous-wave source is one frequency-grid line and sits on the nearest bin (a relative error of about 3e-7 ...); only pulses are built at the requested carrier" | a cw source is not snapped: the lattice is shifted so that a bin sits exactly on the requested carrier (unless `snapCarrier: true` is written). The 3e-7 error is gone, and `concepts.md:46` already says so | `Engine/Compiler.cs:1722-1728`; `Specs/RunSpec.cs:24` | wrong | fixed: cw source shifts the lattice; `snapCarrier: true` restores snapping |
| 2 | :186 "... amplifiers and ionisation run on the CPU" (GPU) | ionisation in 1-D envelope elements runs on the device; only ionisation in spatial elements is CPU | `docs/plugins/gpu.md:60-61`; `plugins/gpu/src/EnvelopeStepper.cs:299`, `plugins/gpu/src/Kernels.cs:324` | wrong | fixed: ionisation in 1-D envelope elements runs on the device |
| 3 | :113 gpu plugin "(double precision)" | `compute.precision: "mixed"` evaluates the nonlinear operator of 1-D envelope and chi(2) elements in single precision on GPU back-ends | `Schema/BuiltInOptions.cs` (`compute` spec, `precision`); `docs/plugins/gpu.md:74` | stale | fixed: single-precision `mixed` mentioned (describes the code today) |
| 4 | :103-116 "Plugins" lists fitting, zemax, gpu, hitran | the repository also has the `ao` plugin (its elements are used on :51 but it is not listed here) and the `twin` plugin (digital twin, `ultrafast twin`, MCP tools `twin_*`); neither appears in this list | `plugins/ao/plugin.json`, `plugins/twin/plugin.json`; `Engine/Compiler.cs:1623` (`AoElementTypes`) | missing | fixed: `ao` and `twin` added to the plugin list |
| 5 | :24-52 "Every element type below is declared in ..." | `modulator` (SLM, deformable-mirror surface, tile mask) is a core element type but has no row in the element table (it appears only in the "What is modelled" table, :73) | `Engine/Compiler.cs:153` (`ElementTypes`), `:587` | missing | fixed: `modulator` row added to the element table |

Checked and still true: carrier-resolved mode is scalar (`Compiler.cs:383`), cubic Kerr anisotropy throws on spatial and carrier-resolved models, the `spectrum at non-positive frequency` loss name (`Executor.cs:352`), Apple GPUs unused (no Metal back-end in `plugins/gpu/src`), no Marcatili model, aliases (`medium`, `window`, `surface`, `tfp`, `faraday`, `fibre`, `waveguide`, `sesam`, `marker`, `aberration`), `ultrafast crystal` and `ultrafast pulse` commands, `cellMode` being a command and function.

Verdict: two statements are wrong against the code (cw carrier, ionisation on the GPU), one is stale (double precision only), and the plugin list and element table are incomplete; the remaining limits were checked and hold.

## Code findings for WP C4

No code was edited in WP C2f. Proposed fixes:

| id | where | problem | proposed fix |
|---|---|---|---|
| C4-f1 | `python/src/ultrafast/calc/beam.py:221-256` (`two_lens_match`) | returns `{"solutions": []}` with no message when no root lies in `[0, max_distance_m]`, so a missing root cannot be told from no solution | raise `ValueError` or add a `note` key saying the range searched and suggesting a larger `max_distance_m` |
| C4-f2 | `python/tools/make_plotting_docs.py` | still writes the `graph-*-before.png` images that the page no longer shows | drop the "before" images from the script and delete `docs/assets/plotting/graph-*-before.png` |
| C4-f3 | `python/src/ultrafast/plotting/style.py:35,40,62` | a ninth series silently reuses a colour | document in the `use_style` docstring, or warn when a figure has more than eight series |
| C4-f4 | `python/src/ultrafast/plotting/spectrogram.py:50-52` | the comment says the time axis is "centred on the pulse's group delay"; the axis is `(arange(n) - n//2) * dt`, centred on t = 0 | correct the comment |
| C4-f5 | `python/src/ultrafast/plotting/animate.py:24,86` | the 2.5 MB budget is only reported in `overBudget` | warn (`warnings.warn`) when `overBudget` is true in `animate_evolution` |
| C4-f6 | `python/src/ultrafast/mcp/server.py:221` | `--budget-s` help says "time budget of one run or scan"; it caps `run`, `scan`, `compare_to_measured`, `study`, `backward`, `fit`, `twin_*` | reword the help text |
| C4-f7 | `python/src/ultrafast/mcp/tools.py:114`, `mcp/ops.py:250` | `plot` caps `dpi` at 200, so `style: paper` never reaches its 600 dpi | decide: keep (image size) and state it in the tool description, or allow 300 for the paper styles |

