# 31 -- A resonator and a partially coherent beam

Two short runs with one-frequency (`"shape": "cw"`) sources.

**A ring resonator** (`lens-guide.ufs`): a lens of f = 0.5 m every 0.6 m, with a 1.1 mm aperture. The tracker gives the round-trip matrix and the Gaussian eigenmode; the `transverse` grid adds the
Fox-Li iteration, a wave calculation that knows nothing of ABCD matrices ([Resonators](../../reference/oscillators.md#resonators)).

**A Gaussian-Schell beam** (`beam.ufs`, `run-m2.json`): a 0.5 mm waist with M² = 2.5, one Rayleigh range of free space. `"coherence"` turns the run into 11 coherent modes (those above 10⁻⁴ of the energy)
([Partial coherence](../../reference/transverse.md#partial-coherence)).

```bash
cd docs/examples/31-cavity-and-coherence
ultrafast run lens-guide.ufs run-cavity.json -o results-cavity
ultrafast run beam.ufs run-m2.json -o results-m2
python report.py
```

```
(A + D) / 2      0.4000   analytic 0.4000
mode radius (mm) 0.3876   analytic 0.3876
Fox-Li radius    0.3877   round-trip loss 0.003 %, 29 round trips
waist (mm)       0.4998   analytic 0.5000
at z_R (mm)      1.3456   analytic 1.3463
```

The Fox-Li mode has the radius of the ABCD eigenmode, and the aperture, 2.8 mode radii, costs 0.003 % per round trip. The Gaussian-Schell beam has the same waist as a coherent one but
is 1.9 times larger after a Rayleigh range (1.346 mm against 0.707 mm), as w(z) = w₀ √(1 + (M² z / z_R)²) says.

## Things to try

* Close the aperture to 0.6 mm (1.5 mode radii): the loss rises and the mode shrinks and departs from the Gaussian.
* Make the cavity unstable (`lengthM` of 1.2 m each, L > 4 f): a warning, and no eigenmode.
* `"m2": 1`: one mode, the coherent beam.
