# 19 -- Kerr-lens mode-locked Ti:sapphire oscillator (Haus model)

The master-equation model of a KLM Ti:sapphire laser at 80 MHz ([docs/oscillators.md](../../reference/oscillators.md)).
Each round trip applies:

* saturated gain with a 100 nm bandwidth;
* 2 % loss plus a 5 % output coupler;
* −500 fs² of net GDD;
* SPM of the crystal, δ = 1.5×10⁻⁶ rad/W;
* the Kerr-lens loss modulation q(P) = q0 − κP.

The laser starts from quantum noise.

```bash
ultrafast run laser.ufs run.json -o results
ultrafast-plot results          # oscillator.png: build-up, pulse and spectrum vs round trip
```

| | |
|---|---|
| round trips to steady state | 5925 (from one photon per mode) |
| output | 825 pJ, 66 mW at 80 MHz (16.5 nJ intracavity) |
| pulse | 74.1 fs, 8.1 nm, TBP 0.28 (sech²: 0.315), single pulse, 0.2 % background |
| soliton area theorem 3.53 \|GDD\|/(δE) | 71.3 fs (ratio 1.04) |
| B-integral per round trip | 0.30 rad |

`oscillator.png` shows the start-up: noise first, then a few noise spikes competing, then one pulse winning
(around 3500 round trips) and shaping into a soliton. Try `"gddFs2": -800` (longer pulse), a larger
`kappaPerW` (stronger mode-locking), or `"saturationEnergyJ": 20e-9` (more energy than one soliton can carry:
the laser becomes unstable or multi-pulses).
