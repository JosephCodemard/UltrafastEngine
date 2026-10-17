# 20 -- A complete Yb laser system: oscillator → stretcher → amplifier → compressor

```bash
ultrafast run system.ufs run.json -o results
ultrafast-plot results          # system, oscillator, gain, instruments ...
```

1. **Oscillator** (`oscillator`, cavity model). A real 2 mm Yb:YAG crystal (8 W at 969 nm, double pass) in a
   cavity with 12 bounces on −1500 fs² chirped mirrors, a SESAM (1 % modulation depth, 50 µJ/cm², 1 ps) and a
   5 % output coupler at 80 MHz.
2. **Pulse picker and stretcher.** The amplifier's `repRateHz` (10 kHz) replaces the 80 MHz of the oscillator
   pulse; the grating pair stretches it to 130 ps.
3. **Amplifier.** The 8-pass amplifier of example 13 (150 W pump, pulse train iterated to steady state at 10 kHz).
4. **Compressor**, matched to the stretcher.

| stage | result |
|---|---|
| oscillator | 1.08 W, 13.5 nJ, 871 fs, 1.29 nm (soliton: theory 873 fs), QML parameter 6.3 (stable), steady state after 1700 round trips |
| stretched | 130 ps (photodiode) |
| amplified | 277 µJ, 2.77 W at 10 kHz (gain 2.9×10⁴; thermal lens f = 15 cm); 1.18 nm (gain narrowing) |
| compressed | 198 µJ, 1.08 ps, transform limited (Strehl 0.9999) |

The oscillator iterates on a 4096-point part of the system's 32768-point window (`gridPoints`), so the long
stretcher does not slow it down. Its output is embedded exactly into the full window.
