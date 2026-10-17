# Pulse retrieval

The engine can turn a FROG or d-scan trace back into a pulse, and use the retrieved pulse as the input of a run --
so a measured pulse can be propagated through a system, or a simulated measurement checked against what an
instrument would let you recover.

| method | trace | algorithm |
|---|---|---|
| `pcgpa` | SHG-FROG, PG-FROG | principal-component generalised projections (Kane 1999): project on the measured magnitudes, rebuild the outer product, one power iteration per step |
| `epie` | SHG-FROG, PG-FROG | ptychographic iterative engine adapted to FROG (Sidorenko et al. 2016): delays in random order, object and gate updated |
| `dscan` | SHG d-scan (glass insertion or GDD) | the measured fundamental spectrum is kept; the spectral phase (a spline through `nodes` points) is fitted by Levenberg-Marquardt, with a per-column calibration of the SHG detection eliminated |

All three use exactly the forward models of the engine's `frog` and `dscan` instruments.

## Command line

```bash
ultrafast retrieve pcgpa trace.npz -o retrieved            # FROG: delay_s, omega_rad_s (or wavelength_m), trace
ultrafast retrieve dscan dscan.npz --fundamental spectrum.csv --material FusedSilica -o retrieved
```

The trace file is an NPZ or JSON with the row axis (`delay_s`, `insertion_m` or `gdd_s2`), the column axis
(`omega_rad_s` or `wavelength_m`, signal frequencies) and `trace` (2-D, rows x columns). A results folder's
`result.npz` works directly (the instrument's arrays are found by suffix). The output folder gets:

* `retrieved_spectrum.csv` -- wavelength (nm), intensity, phase: use it as a run-spec pulse
  (`"pulse": {"shape": "spectrum", "spectrum": {"csv": "retrieved/retrieved_spectrum.csv"}, "energyJ": ...}`);
* `retrieval.json` (error, iterations, FWHM, notes, and a ready-to-paste `pulse` object);
* `retrieval.npz` (field, spectrum, measured and retrieved traces, error history).

## Python

```python
res = uf.run(system, spec_with_frog)          # or a measured trace
out = uf.retrieve("pcgpa", {"delay_s": res["frog_delay_s"], "omega_rad_s": res["frog_omega_rad_s"], "trace": res["frog_trace"]})
out["error"], out["fwhmS"]
spec2 = b.run_spec(out["pulse"])              # the retrieved pulse as an input
```

## C#

```csharp
var trace = TraceData.FromInstrument(result, "frog");          // or TraceData.Load("trace.npz", "shg-frog")
var r = Retrievals.Run("epie", trace, new JsonObject().Set("maxIterations", 200));
var pulse = r.ToPulseJson(energyJ: 1e-6);
```

Options: `points` (FROG grid, default the next power of two ≥ the delay count, ≤ 512), `maxIterations`,
`tolerance`, `seed`; ePIE `stepSize`; d-scan `nodes` (16), `points` (2048).

## What to expect

* The FROG error G is the rms difference of the normalised traces on the retrieval grid; below ~5·10⁻³ on clean data.
* SHG-FROG cannot tell E(t) from E*(-t): the sign of the spectral phase (the direction of the chirp) is ambiguous.
  PG-FROG and d-scan are not.
* d-scan needs the fundamental spectrum (the measured spectrum, or `FundamentalOmega` / `FundamentalIntensity` in code).

## Your own algorithm

Implement `UltrafastEngine.Retrieval.IRetrieval` and register it (`Retrievals.Register`, or a plugin's
`ctx.RegisterRetrieval(...)`); it is then available by name in the CLI, the C API and Python.

Tests: `RetrievalTests` (PCGPA and ePIE on a chirped pulse: error, duration, |GDD|; d-scan: GDD with its sign,
duration, and the retrieved field reused as a run input).
