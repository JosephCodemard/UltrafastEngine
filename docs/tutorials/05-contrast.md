# Tutorial 5 -- Contrast of an amplifier chain

**Goal:** follow a weak satellite pulse through a CPA chain and see how a nonlinear element turns a harmless
post-pulse into a pre-pulse; measure it as a third-order cross-correlator would; scan the pulse energy with a
parameter study and compare with the analytic estimate.

Files: `docs/examples/22-contrast-chain/` (`chain.ufs`, `run.json`, `study.json`). Run the commands from that folder.

## 1. The chain

```jsonc
"elements": [
  { "id": "stretcher",  "type": "dispersion", "gddFs2": 5.0e6 },
  { "id": "window",     "type": "plate", "material": "FusedSilica", "thicknessM": 3e-3,
                        "ghosts": { "orders": 1, "overlap": 1.0 } },
  { "id": "rod",        "type": "bulk", "material": "YAG", "lengthM": 0.02, "nonlinear": true,
                        "beam": { "kind": "constant", "radiusM": 0.5e-3 } },
  { "id": "compressor", "type": "dispersion", "gddFs2": -5.0e6 }
]
```

A 300 fs pulse is stretched to about 46 ps. It crosses an uncoated 3 mm window: the part reflected at the back
surface and again at the front surface follows the main pulse by 2 n_g d / c = 29.3 ps with R² = 1.14·10⁻³ of its
intensity (the window's note in `summary.json` says so). This ghost overlaps the stretched pulse. Then a 2 cm YAG
rod (standing in for the amplifier crystal) adds a B-integral of about 1.9 rad, and the compressor removes the
stretch. `"overlap": 1.0` assumes the ghost stays on the main beam (normal incidence, no wedge); leave it out and
the engine computes the overlap from the walk-off, the wedge and the beam size ([components](../reference/components.md#ghost-reflections)).

The run spec gives the input a weak exponential pedestal and measures the compressed pulse with a cross-correlator
and the virtual `contrast` instrument:

```jsonc
"pulse": { "shape": "gaussian", "centerWavelengthM": 1.03e-6, "durationFwhmS": 300e-15, "energyJ": 5e-3,
           "pedestal": [ { "kind": "exponential", "level": 1e-6, "riseS": 2e-12, "fallS": 3e-12 } ] },
"instruments": [
  { "kind": "crossCorrelator", "name": "xc", "at": "end", "rangeS": [-60e-12, 60e-12], "stepS": 0.1e-12,
    "fineStepS": 20e-15, "fineRangeS": 2e-12, "dynamicRangeDb": 100, "probeDelaysS": [-29e-12, 29e-12] },
  { "kind": "contrast", "at": "end" }, { "kind": "pulseSummary" } ]
```

```bash
ultrafast run chain.ufs run.json -o results
```

The planner reports `Estimated total B-integral (peak power after the dispersion before each element): 1.98 rad`
-- it applies the stretcher before estimating the rod's peak power -- and sizes the grid for the 84 ps group-delay
spread plus the ghost (N = 32768, 15.6 fs steps, about one second).

The engine also warns that the peak power in the rod is 70 times the critical power for self-focusing. That is true
of every mJ-class stretched pulse; whole-beam self-focusing needs a collapse length (here ~6 cm for a 0.5 mm beam)
shorter than the crystal, so it is not an issue in 2 cm. Add `"transverse": {"model": "radial"}` to the rod to see it.

## 2. What the cross-correlator shows

`summary.json → instruments → xc`:

| value | | meaning |
|---|---|---|
| `postPulseContrastDb` | -26.6 dB at +29.1 ps | the ghost: -29.4 dB from the window, raised by the Kerr effect |
| `prePulseContrastDb` | -30.6 dB at -29.4 ps | a **pre-pulse** that did not exist before the rod |
| `fwhmS` | 0.51 ps | the correlation width (gate and pulse convolved) |

Plot it with `ultrafast-plot results/` (`xc_signal_db` vs `xc_delay_s`), or in Python:

```python
import ultrafast as uf, matplotlib.pyplot as plt
r = uf.load("results")
plt.plot(r["xc_delay_s"] * 1e12, r["xc_signal_db"]); plt.xlabel("delay (ps)"); plt.ylabel("signal (dB)")
```

Why the pre-pulse appears: in the stretched pulse every frequency arrives at a different time. The ghost is a
delayed copy, so where it overlaps the main pulse the two beat at the frequency difference set by the 29 ps delay;
the Kerr effect writes that beat as a phase modulation of the main pulse, which creates two sidebands -- after
compression one at +29 ps (adding to the post-pulse) and one at -29 ps: a pre-pulse. Its relative intensity is
approximately ε (B/2)² (Didenko et al., Opt. Express 16, 3178 (2008)), here 1.14·10⁻³ × 0.9 → -29.9 dB; the
simulation gives -30.6 dB because the ghost sits where the stretched intensity, and so the local B, is lower than at
the peak.

## 3. Scan the pulse energy

`study.json` runs the chain for nine energies between 0.1 and 10 mJ:

```jsonc
{ "format": "ultrafast-study", "system": "chain.ufs", "run": "run.json",
  "mode": "scan", "sampling": "grid", "parallel": 4,
  "parameters": [ { "path": "pulse/energyJ", "min": 0.1e-3, "max": 10e-3, "scale": "log", "points": 9 } ],
  "outputs": [ "record:rod/BIntegralRad", "instrument:xc/prePulseContrastDb",
               "instrument:xc/postPulseContrastDb", "pulse:end/fwhmS" ] }
```

```bash
ultrafast study study.json -o study-results        # 9 runs in ~5 s; study-results/study.csv
```

| energy (mJ) | B (rad) | pre-pulse (dB) | post-pulse (dB) | FWHM (fs) |
|---|---|---|---|---|
| 0.10 | 0.04 | -58.1 | -29.5 | 300 |
| 0.32 | 0.12 | -54.0 | -29.5 | 300 |
| 1.0 | 0.38 | -45.4 | -29.5 | 300 |
| 3.2 | 1.21 | -35.3 | -28.4 | 313 |
| 5.6 | 2.15 | -29.3 | -25.9 | 381 |
| 10 | 3.82 | -24.4 | -23.1 | 1068 |

Three regimes:

* **Low energy**: the pre-pulse stays near -58 dB. That is not the pulse: a third-order correlator also shows a
  post-pulse ε at -T through its gate, with intensity ~ε² (1.3·10⁻⁶ → -59 dB). Real devices have this artefact too
  (the instrument's note says so) -- check `signal_ideal` or the `contrast` instrument to tell them apart.
* **Middle**: the pre-pulse rises by 20 dB per decade of energy, i.e. as B² -- the signature of this mechanism.
* **High energy** (B > 2): the SPM chirp is no longer removed by the compressor (the FWHM grows), and pre- and
  post-pulse approach each other.

The grid was frozen at the nominal run's plan, so all points are directly comparable; the stretcher and window
are cached and only the rod and compressor are recomputed ([Parameter studies](../reference/studies.md)).

## 4. Things to try

* Wedge the window: `"wedgeDeg": 0.1, "ghosts": { "orders": 1, "beamRadiusM": 0.5e-3 }` (no fixed `overlap`). The
  ghost is tilted by 2 n α and no longer overlaps the beam (the note gives the overlap); the pre-pulse falls to the
  floor (-52 dB).
* Move the window after the rod: the ghost is then created after the nonlinearity and stays a post-pulse.
* Replace the window by an `ndFilter` (`"kind": "reflective"`): its ghost is much stronger.
* Add `{ "kind": "prePulse", "delayS": 12e-12, "relativeIntensity": 1e-6 }` to the pedestal and watch it in the
  cross-correlator, or a `phaseNoise` component for the coherent pedestal of a real stretcher.
* Optimise instead of scanning: `"mode": "optimize", "objective": {"output": "instrument:xc/prePulseContrastDb",
  "goal": "target", "target": -40}` with the energy as the only parameter (no `points`) finds 1.88 mJ in 40
  runs (`analysis.optimum`).
