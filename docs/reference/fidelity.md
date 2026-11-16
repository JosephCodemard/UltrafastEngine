# Fidelity: quick, standard, full

A run-level key `"fidelity"` trades accuracy for time. It is a **named bundle of switches**, nothing more: every switch it
turns is also an ordinary key you can write yourself, and **a key you write is never overridden**.

```jsonc
{ "fidelity": "quick", "pulse": { ... } }            // run.json
```

```bash
ultrafast run system.ufs run.json --fidelity quick   # same, from the command line
```

```python
uf.scan(system, spec, {"element:plate/lengthM": lengths}, ["pulse:end/fwhmS"], fidelity="quick")
```

| level | what it changes | when |
|---|---|---|
| `full` (default) | nothing: bit-identical to a run without the key | results you will report |
| `standard` | looser solver tolerance (`stepper.tolerance` 1e-5 instead of 1e-6) | most work; about 1.2 to 2 times faster |
| `quick` | `physics.raman` off, `physics.selfSteepening` off, `physics.polarization` scalar, `stepper.tolerance` 1e-4, spatial (`transverse`) models replaced by the 1-D beam model where every spatial element has a beam, `frog` / `xfrog` / `dscan` instruments skipped (unless the instrument has `"keepInQuick": true`) | exploring: scans, first looks |

Things the levels deliberately **do not** touch: carrier-resolved propagation and third harmonic, ionisation and plasma, gain
(including ASE and its pedestal), satellites, ghosts and pedestals, and the grid size. Switch those yourself
(`physics.fieldMode`, `physics.ionization`, ...) when you want them cheaper; the cost of a wrong guess there is larger than the saving.

## Never silent

* `summary.json` has a `fidelityReport`: the level, every switch the bundle changed (old and new value), the switches it would have
  changed but your run spec fixed (`keptAsWritten`), the indicators below, and the reasons a result may be unreliable.
* The notes of the run repeat the changes; a collapsed spatial element has its own note.
* After a quick or standard run the engine computes cheap dimensionless indicators and **warns** (the run still finishes):

| warning | when |
|---|---|
| strongly nonlinear | quick and total B-integral above 3 rad: every dropped term can matter |
| self-steepening dropped | quick and B / (ω0 T0) above 0.01 (T0 = FWHM / 1.665) |
| Raman dropped | quick, B above 1 rad and a pulse shorter than 2 ps (spectral shift, NER) |
| spatial model collapsed | peak power above 0.3 of the critical power for self-focusing in a collapsed element |
| polarisation made scalar | the input is not linearly polarised along x or y |

## The workflow for agents and scripts: explore in quick, confirm in full

1. Scan in `quick` (`uf.scan(..., fidelity="quick")`, `"fidelity": "quick"` in a study's run spec).
2. Re-run the best one to three points in `full` and compare. Report both, and say which numbers came from which level.
3. If a quick warning fired, do not trust the quick numbers for that region.

## Measured against full

`tools/bench` (case `fidelity`), Apple silicon, one thread; the run specs have no physics or stepper keys so the bundles apply, instruments
off. "x" is the speed-up over full.

| case | level | time | output FWHM | spectral FWHM | energy | unreliable warnings |
|---|---|---|---|---|---|---|
| `spm-plate` (B small) | standard | x1.9 | 0.00 % | 0.00 % | 2e-9 | 0 |
| | quick | x2.7 | -0.02 % | +0.01 % | 2e-5 | 0 |
| `mpc-argon` (20 passes, B about 9 rad) | standard | x1.2 | 0.00 % | 0.00 % | 1e-7 | 0 |
| | quick | x1.3 | -0.15 % | **+3.11 %** | 4e-7 | **3** |
| `spatial-kerr` (radial, 3 P_cr) | standard | x2.1 | -0.00 % | 0.00 % | 2e-6 | 0 |
| | quick | x1.9 | -0.05 % | -0.02 % | 3e-5 | 1 |

Read the table honestly: the engine is already fast on these cases (20 ms to 0.5 s), so the bundle saves a factor of 1 to 3, not
orders of magnitude, and on the multipass cell quick is wrong by 3 % in bandwidth and says so. The value of `quick` is in long scans
and optimisations of heavy systems, and in the warning that tells you when it cannot be trusted. Tests: `FidelityTests` (full is
bit-identical, only unset switches change, quick stays within 1 % of full at B = 0.15 and warns at B = 15, a spatial model is collapsed
only where there is a beam).
