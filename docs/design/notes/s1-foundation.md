# S1 Foundation: notes

Branch `dev/2026-11-s1-foundation`. Status: **plan drafted, nothing moved yet** (2026-11-09).

## Findings from reading (these change the roadmap's assumptions)

* `Sequential` and `Branched` are classes in `build.py` (`class Sequential(list)`, `class Branched(list)`), with `link`,
  `loop`, `virtual_input`. `ultrafast.system.Sequential` must therefore **re-export** them; `build` stays the owner.
* `plugins/fitting/python/ultrafast_fitting/figures.py` imports **private** helpers from `ultrafast.plotting`
  (`_figure, _has, _sanitize, _si, _Style, _theme, _cmap, fmt, load, _AXIS_LABELS, _fmtu, _named_axis, _value_unit,
  measurements`). `ultrafast.plotting` becomes a package, so its `__init__` must keep exporting every one of these.
* `ultrafast.plot` is the `ultrafast-plot` console script (`ultrafast.plot:main`) and is also run as a script path.
  It stays a file; only its import of `plotting` is unchanged.
* `core.py` (540 lines) holds `System, Run, Result, run, study, ...`; `__init__` re-exports them. They stay in `core.py`
  (moving them is risk without benefit); `ultrafast.results` re-exports `Result` and adds metrics.
* The existing `ultrafast.plotting.FIGURES`, `register_figure`, `report`, `plot`, `load`, `set_theme` are public API.

## Package plan (old -> new)

Rule: **no old import path breaks**. New packages are additive; moved code leaves the old name importable (the
`plotting` package keeps every name; `diagram.py` stays as a shim that re-exports from `plotting.diagrams`).

| old | new | how |
|---|---|---|
| `ultrafast.build.Sequential / Branched / link / loop / virtual_input / system` | `ultrafast.system.*` | re-export (no warning: `build` is the documented low-level API) |
| `ultrafast.build.<component builders>` | `ultrafast.components.*` (CapWords factories are thin aliases of the snake_case builders, e.g. `BeamSplitter = build.beam_splitter`) | re-export |
| `ultrafast.build.pulse / cw / measured_spectrum* / pedestal / gaussian_beam ...` | `ultrafast.sources` | re-export |
| `ultrafast.build.instrument / axis` | `ultrafast.instruments` | re-export |
| `ultrafast.core.Result` | `ultrafast.results.Result` plus metrics | re-export |
| `ultrafast.plotting` (module, 1371 lines) | `ultrafast.plotting` (package: `style, temporal, spectral, spatial, zernike, instruments, diagrams, animate`) | split; `__init__` re-exports every old public and underscore name used by plugins |
| `ultrafast.plot` | unchanged (CLI) | - |
| `ultrafast.diagram` | `ultrafast.plotting.diagrams` | `diagram.py` kept as a shim with `DeprecationWarning` on import |
| `ultrafast_fitting.figures` | keep file; helper imports re-pointed to the new submodules | no rename |
| `ultrafast.crystal / cells / laser / materials / measured / plugins` | unchanged | roadmap says kept |
| (new) `ultrafast.algorithms.zernike` | `noll_to_nm, evaluate, fit` | numpy only |
| (new) `ultrafast.plotting.zernike`, `ultrafast.plotting.animate` | Zernike decomposition figure, GIF/MP4 writer | |

Open question for the advisor: `DeprecationWarning` on `ultrafast.diagram` import vs silent alias (the repo's own docs and
tests may import it; warnings under `-W error` would break CI).

## Example regrouping

Per `docs/design/roadmap-ao-twin-2026-11.md` section 4.3, confirmed after reading READMEs (to do). One `git mv` pass,
`docs/examples/README.md` keeps an old-number -> new-path table.

## Left to do

Everything: structure, plotting, regroup, make_figures, examples package, Computing examples, docs.
