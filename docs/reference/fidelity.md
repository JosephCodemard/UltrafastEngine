# Fidelity: quick, standard, full

The run-spec key `"fidelity"` trades accuracy for time. It is a **named bundle of switches**: every switch it turns is
also an ordinary key you can write yourself, and **a key you write is never changed**. Use `quick` to explore (scans,
first looks), confirm in `full`, and report `full` numbers.

## Keys

```jsonc
{ "fidelity": "quick", "pulse": { ... } }            // run.json
```

```bash
ultrafast run system.ufs run.json --fidelity quick   # the same, from the command line
```

```python
uf.scan(system, spec, {"element:plate/lengthM": lengths}, ["pulse:end/fwhmS"], fidelity="quick")
```

| level | what it changes | when |
|---|---|---|
| `full` (default) | nothing: bit-identical to a run without the key | results you will report |
| `standard` | `stepper.tolerance` 1e-5 (default 1e-6) | most work |
| `quick` | `physics.raman` off, `physics.selfSteepening` off, `physics.polarization` scalar, `stepper.tolerance` 1e-4, spatial (`transverse`) models of bulk and gap elements replaced by the 1-D beam model, `frog` / `xfrog` / `sfgFrog` / `dscan` instruments skipped (unless the instrument has `"keepInQuick": true`) | exploring: scans, first looks |

Rules of the bundle (`Specs/Fidelity.cs`):

* A switch written in the run spec's `physics` or `stepper` is kept as written and listed in `keptAsWritten`.
* The tolerance is only ever loosened: a tolerance already looser than the level's value is kept.
* Spatial models of bulk and gap elements are replaced only when beam tracking is on or there is an input beam (run or
  system `inputBeam`), or when every element with a spatial model also has a `beam` of its own; otherwise none is replaced.
  χ(2) crystals and gain elements keep their spatial models. A run with a `coherence` section keeps them all.

The levels do **not** touch carrier-resolved propagation and the third harmonic, ionisation and plasma, gain (with ASE
and its pedestal), satellites, ghosts and pedestals, or the grid size. Switch those yourself (`physics.fieldMode`,
`physics.ionization`, ...) when you want them cheaper.

## Model

### Never silent

* `summary.json` has a `fidelityReport`: the level, every switch the bundle changed (old and new value), the switches it
  would have changed but the run spec fixed (`keptAsWritten`), the indicators below, and the reasons a result may be
  unreliable (`unreliableBecause`).
* The run notes repeat the changes; a collapsed spatial element has its own note.
* After a quick or standard run the engine computes cheap dimensionless indicators and **warns** (the run still
  finishes):

| warning | when |
|---|---|
| strongly nonlinear | quick, and the total B-integral above 3 rad: every dropped term can matter |
| self-steepening dropped | quick, and B / (ω0 T0) above 0.01 (T0 = FWHM / 1.665) |
| Raman dropped | quick, B above 1 rad and a pulse shorter than 2 ps (spectral shift, NER) |
| spatial model collapsed | some element was collapsed, and the peak power in an element is above 0.3 of the critical power for self-focusing |
| polarisation made scalar | the bundle made the polarisation scalar and the input is not linearly polarised along x or y (the smaller Jones component above 1e-3 of the larger) |

### The workflow for agents and scripts: explore in quick, confirm in full

1. Scan in `quick` (`uf.scan(..., fidelity="quick")`, or `"fidelity": "quick"` in a study's run spec).
2. Re-run the best one to three points in `full` and compare. Report both, and say which numbers came from which level.
3. If a quick warning fired, do not trust the quick numbers for that region.

## Validation

Tests (`FidelityTests`): full is the default and changes nothing (`Fidelity_FullIsTheDefaultAndChangesNothing`); quick
fills only unset switches and reports them (`Fidelity_QuickFillsOnlyUnsetSwitchesAndReportsThem`); at B about 0.15
quick stays within 1 % of full in spectral width and 1e-4 in energy (`Fidelity_QuickStaysCloseToFullAtLowB`); at B about
15 it warns (`Fidelity_QuickWarnsWhenTheDroppedPhysicsMatters`); FROG is skipped unless kept
(`Fidelity_QuickSkipsFrogUnlessKept`); a spatial model is replaced by the beam model
(`Fidelity_QuickReplacesASpatialModelByTheBeamModel`).

### Measured against full

`tools/bench` case `fidelity` (`dotnet run -c Release --project tools/bench -- fidelity`), run 2026-11-26 on an Apple M3
(8 cores, 16 GB), macOS, .NET 10, Accelerate FFT, default threads. The run specs have no physics or stepper keys, so the
bundles apply; instruments are off. "×" is the speed-up over full. The accuracy columns are deterministic; the times
are single runs of 5 ms to 0.3 s and vary from run to run.

| case | level | speed-up | output FWHM | spectral FWHM | energy | unreliable warnings |
|---|---|---|---|---|---|---|
| `spm-plate` (B small) | standard | ×2.5 | 0.00 % | 0.00 % | 2e-9 | 0 |
| | quick | ×3.6 | −0.02 % | +0.01 % | 2e-5 | 0 |
| `mpc-argon` (20 passes, B about 9 rad) | standard | ×1.0 | 0.00 % | 0.00 % | 1e-7 | 0 |
| | quick | ×1.1 | −0.15 % | **+3.11 %** | 4e-7 | **3** |
| `spatial-kerr` (radial, 3 P_cr) | standard | ×1.6 | 0.00 % | 0.00 % | 2e-6 | 0 |
| | quick | ×1.8 | −0.05 % | −0.02 % | 3e-5 | 1 |

On these cases a full run takes 10 ms to 0.3 s, so the bundle saves a factor of 1 to 4, not orders of magnitude; on the
multipass cell quick is wrong by 3 % in bandwidth and warns three times. The value of `quick` is in long scans and
optimisations of heavy systems, together with the warning that says when it cannot be trusted.

## Limits

* The indicators are estimates from the run itself (B-integral, P / P_cr, pulse duration); they do not prove a quick
  result right when they stay quiet.
* `standard` changes only the tolerance, so its gain depends on how many steps the tolerance sets; on the multipass
  cell above it gains nothing.
* Among bulk and gap elements quick replaces all spatial models or none: one element without a beam keeps them all.
  Crystals and gain elements always keep theirs, so a quick run of a spatial SHG or amplifier is not much faster.
