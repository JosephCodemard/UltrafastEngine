# 28 -- Delayed light: a plate ghost, a mirror-substrate echo and a coating pedestal on the tail grid

A 100 fs pulse passes a fused-silica window with a wavelength-dependent AR coating and reflects from an HR mirror on a
12.7 mm substrate. The light that is delayed -- the window's ghost, the echo from the mirror's back face, the coating's
ripple pedestal -- is 10⁻⁶ of the pulse and 49 to 124 ps behind it. A cross-correlator reads it. The nonlinear grid
stays 4.3 ps long: the delayed light lives on the tail grid ([Delayed light](../../reference/delayed-light.md)).

```bash
cd docs/examples/28-delayed-light
ultrafast run cell.ufs run.json -o results
```

```jsonc
"grid": { "tail": true }          // run.json: the main grid is sized for the pulse alone
```

Expected output (a fresh run):

```
  grid.tail: ghosts, echoes and other satellites are carried on the long tail grid; the main window is sized for the pulse only.
  Grid auto-sized: N = 512, window = 4.267 ps, dt = 8.333 fs.
  energy 9.874E-07 J (T = 98.74 %), FWHM 100.1 fs (TL 100 fs), peak 9.268E+06 W, B = 0 rad
```

`summary.json -> tail`: N = 131 072 (256 x the main grid), window 1.09 ns, 1.7e-10 J (0.017 % of the output) in the tail. The
loss budget carries it as `delayed light (tail)`.

## What the cross-correlator reads (`contrastAt_*`)

| delay | source | engine | by hand |
|---|---|---|---|
| +48.8 ps | window ghost | -58.2 dB | `R(1030 nm)²` with R interpolated from the AR table (0.0012): 1.5e-6 = -58.2 dB |
| +123.96 ps | mirror echo | -54.7 dB | `T² R_b / R` = 0.01² x 0.0337 / 0.99 = 3.4e-6 = -54.7 dB; delay 2 n_g d / c |
| +3 ps | coating ripple (phase 0.02 rad rms, within 5 ps) | -52.6 dB | first-order pedestal 4e-4 of the energy spread over 0 .. 5 ps |

The delays are the group delays of the exact transfer functions (`exp(i 2 n(ω) ω d cos θ / c)`), not numbers typed in: 48.8 ps
for the 5 mm window and 123.96 ps for the 12.7 mm substrate. The notes of each element say so. The cross-correlator makes its
SHG gate from the main field only; the probe channel sees main + tail.

## Things to try

* Replace `"grid": {"tail": true}` by `"grid": {"auto": false, "n": 2048, "windowS": 20e-12}`: the run warns, by name, that the
  window ghost (+48.8 ps) and the mirror echo (+123.96 ps) wrap around, with their levels and energies. Add
  `"extendForSatellites": true` to grow the window, or `"strict": true` to make it an error.
* Set `"substrate": {"material": "FusedSilica", "thicknessM": 12.7e-3, "backReflectance": 1e-3}`: an AR-coated back face lowers the
  echo by 34 (15 dB).
* Add `"delayedScatter": {"fraction": 1e-4, "delay": {"kind": "uniform"}}` to the mirror: scatter that returns as light, uniformly
  over 0 .. 124 ps.
