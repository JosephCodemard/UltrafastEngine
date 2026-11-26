# Pulse retrieval

The engine can turn a FROG or d-scan trace back into a pulse, and use the retrieved pulse as the input of a run --
so a measured pulse can be propagated through a system, or a simulated measurement checked against what an
instrument would let you recover. The methods belong to the `frog`, `xfrog` and `dscan` [instruments](instruments.md) and to near / far-field pairs.

## Keys

The methods and their options (the retrieval is a command, `ultrafast retrieve`, and `uf.retrieve`, not a system or run-spec key):


| method | trace | algorithm |
|---|---|---|
| `pcgpa` | SHG-, PG-, SD-FROG, XFROG | principal-component generalised projections (Kane 1999): project on the measured magnitudes, rebuild the outer product, one power iteration per step |
| `epie` | SHG-, PG-, SD-FROG, XFROG | ptychographic iterative engine adapted to FROG (Sidorenko et al. 2016): delays in random order, object and gate updated. Stochastic: in the tests it ends at a trace error of about 1e-3 (the stop rule is `tolerance`, default 1e-5, or `maxIterations`) and the retrieved duration scatters by about 2 % rms (rare tails to 15 % over 20 seeds), so use PCGPA when the trace is clean and ePIE for noisy traces, and compare several `seed`s |
| `dscan` | SHG d-scan (glass insertion or GDD) | the measured fundamental spectrum is kept; the spectral phase (a spline through `nodes` points) is fitted by Levenberg-Marquardt, with a per-column calibration of the SHG detection eliminated |
| `multiFrog` (`doubleFrog`) | several FROG traces sharing unknown pulses | all traces together ([below](#multi-trace-retrieval)) |

`gerchbergSaxton` (alias `gs`) retrieves a wavefront ([below](#wavefront-from-two-intensities-gerchberg-saxton)). All FROG and d-scan methods use exactly the forward models of the engine's `frog`, `xfrog` and `dscan` instruments.

## Trace kinds

| kind | gate | notes |
|---|---|---|
| `shg-frog` | `E(t) E(t - tau)` | symmetric in the delay; the direction of time (sign of the chirp) is ambiguous |
| `pg-frog` | `E(t) |E(t - tau)|^2` | no ambiguity |
| `sd-frog` | `E(t)^2 E*(t - tau)` | self-diffraction; no ambiguity; PCGPA takes `E` from the right singular vector (the probe is `E*`) |
| `xfrog` | `E(t) R(t - tau)`, `R` known | sum-frequency (SFG) cross-correlation FROG; the reference fixes the time origin and the direction; PCGPA is the least-squares step `E = O R* / sum |R|^2` |

**The kind is never guessed silently.** It comes from (1) the `kind` argument, (2) a `"kind"` key in a JSON / dict trace, (3) the
instrument that made the trace (`frogType`; `TraceData.FromInstrument`, and `summary.json` next to a `result.npz`). If none is
available the retrieval assumes `shg-frog` only when the trace is symmetric in the delay (as every SHG trace is) and says so in
its notes; an asymmetric trace stops with an error asking for the kind. Naming `shg-frog` for an asymmetric trace adds a warning note.
An XFROG trace needs its reference (`reference_time_s`, `reference_field_re`, `reference_field_im`, `reference_omega_rad_s`): the
`xfrog` instrument's arrays carry it.

## Command line

```bash
ultrafast retrieve pcgpa trace.npz -o retrieved            # FROG: delay_s, omega_rad_s (or wavelength_m), trace
ultrafast retrieve dscan dscan.npz --fundamental spectrum.csv --material FusedSilica -o retrieved
```

The command line takes `pcgpa`, `epie`, `dscan` and `gerchbergSaxton`, with `--kind` and `--fundamental` (a CSV read with `wavelengthUnit`, default `nm`). `multiFrog` (alias `doubleFrog`) goes through the JSON command ([below](#multi-trace-retrieval)).

The trace file is an NPZ or JSON with the row axis (`delay_s`, `insertion_m` or `gdd_s2`), the column axis
(`omega_rad_s`, or `wavelength_m` / `shg_wavelength_m`, signal frequencies) and `trace` (2-D, rows x columns). A d-scan file can also carry `fundamental_wavelength_m` and `fundamental_intensity`. A results folder's
`result.npz` works directly (the instrument's arrays are found by suffix). The output folder gets:

* `retrieved_spectrum.csv` -- wavelength (nm), intensity, phase: use it as a run-spec pulse
  (`"pulse": {"shape": "spectrum", "spectrum": {"csv": "retrieved/retrieved_spectrum.csv"}, "energyJ": ...}`);
* `retrieval.json` (error, iterations, FWHM, notes, and a ready-to-paste `pulse` object);
* `retrieval.npz` (field, spectrum, measured and retrieved traces, error history).

`multiFrog` writes `retrieved_spectrum_<pulse>.csv` for each pulse and `retrieval.json`, but no `retrieval.npz`; `gerchbergSaxton` writes `phase.npz`.

## Python

```python
res = uf.run(system, spec_with_frog)          # or a measured trace
out = uf.retrieve("pcgpa", {"delay_s": res["frog_delay_s"], "omega_rad_s": res["frog_omega_rad_s"], "trace": res["frog_trace"]})
out["error"], out["fwhmS"]
spec2 = b.run_spec(out["pulse"])              # the retrieved pulse as an input
```

## Multi-trace retrieval

Traces that share unknown pulses are retrieved together (Kida et al., Opt. Express 22, 3004 (2014), "double FROG"): the SHG-FROG
of a reference pulse, the SHG-FROG of a test pulse, and the XFROG trace of the test pulse gated by the reference. Each SHG trace
alone leaves the time direction of its pulse open; the XFROG trace ties them, and the two SHG traces remove the common twin.

```python
out = uf.retrieve_multi([
    {"trace": ref_shg,  "kind": "shg-frog", "pulse": "ref"},
    {"trace": test_shg, "kind": "shg-frog", "pulse": "test"},
    {"trace": xfrog,    "kind": "xfrog",    "pulse": "test", "gate": "ref"},   # gate: the unknown pulse that gates it
], maxIterations=600)
out["pulses"]["test"]["pulse"], out["traceErrors"]
```

Every pulse has its own carrier (fixed by an SHG / PG / SD trace; otherwise give `carriersRadS` in the options). All pulses live on
one grid (`points`, `dtS`); in every sweep each trace projects onto its measured magnitudes and updates its pulses (SHG / PG / SD: the
PCGPA step; XFROG: least squares for the test pulse with the gate fixed, then for the gate with the test pulse fixed). The JSON command
is `{"command": "retrieve", "method": "multiFrog", "traces": [...], "options": {...}}`; C#: `MultiTraceRetrieval.Run(terms, options)`.
An XFROG term without `gate` uses the reference stored in its trace as a known gate. Tested on six random starts for both pulses
(`MultiTraceRetrievalTests`).

## Wavefront from two intensities: Gerchberg-Saxton

A beam's phase from its near-field and far-field intensities (Gerchberg & Saxton, Optik 35, 237 (1972)):

```bash
ultrafast retrieve gerchbergSaxton pair.npz -o phase
```

`pair.npz` holds `near`, `far` (intensities, [x][y], even numbers of pixels), `x_m`, `y_m`. The far image must be centred and on the conjugate grid of the near field: far pixel = λ f / (N dx).
Plain error reduction stalls in local minima, so the method starts from a flat phase and from `restarts` (12) seeded smooth random phases, and continues the best for `iterations` (500)
(options, Python: `uf.retrieve("gerchbergSaxton", "pair.npz", output="phase", restarts=12)`). `phase/phase.npz` (`x_m`, `y_m`, `amplitude`, `phase_rad`) is ready for `initialProfile` kind `measured` or a `mask` of kind `file`.
The piston is undetermined, and a symmetric near field cannot tell φ(x, y) from −φ(−x, −y): check the far-field error (`farFieldError`) and use an asymmetric near field where you can.
Checked on a known phase (far-field error < 1 %, rms phase error < 0.1 rad, `FieldToolsTests`).

## C#

```csharp
var trace = TraceData.FromInstrument(result, "frog");          // or TraceData.Load("trace.npz", "shg-frog")
var r = Retrievals.Run("epie", trace, new JsonObject().Set("maxIterations", 200));
var pulse = r.ToPulseJson(energyJ: 1e-6);
```

Options and defaults:

| option | default | applies to |
|---|---|---|
| `points` | next power of two ≥ the delay count, ≤ 512 (d-scan: 2048) | FROG, multiFrog, d-scan |
| `dtS` | from the delay axis | FROG, multiFrog |
| `maxIterations` | 300 (pcgpa, ePIE), 60 (d-scan), 600 (multiFrog) | all but Gerchberg-Saxton |
| `tolerance` | 1e-5 (stop when the trace error falls below it) | pcgpa, ePIE, multiFrog |
| `seed` | 1 | ePIE, multiFrog, Gerchberg-Saxton |
| `stepSize` | 0.5 | ePIE |
| `nodes` | 16 | d-scan |
| `iterations`, `restarts` | 500, 12 | Gerchberg-Saxton |

## What to expect

* The FROG error G is the rms difference of the normalised traces on the retrieval grid; below ~5·10⁻³ on clean data.
* SHG-FROG cannot tell E(t) from E*(-t): the sign of the spectral phase (the direction of the chirp) is ambiguous.
  PG-FROG and d-scan are not.
* d-scan needs the fundamental spectrum (the measured spectrum, or `FundamentalOmega` / `FundamentalIntensity` in code).

## Your own algorithm

Implement `UltrafastEngine.Retrieval.IRetrieval` and register it (`Retrievals.Register`, or a plugin's
`ctx.RegisterRetrieval(...)`); it is then available by name in the CLI, the C API and Python.

## Validation

* `FrogKindsTests.Frog_PgAndSd_TraceCentroid_TiltsWithTheChirpRate`: PG and SD trace tilt against the analytic slopes 2b/3 and 4b/3 (Kane and Trebino 1993), 2 %; `Xfrog_TraceCentroid_TiltsWithTheChirpRate_AtTheSumFrequency`, 2 %.
* `FrogKindsTests.Frog_PcgpaAndEpie_RecoverAChirpedPulse_FromSdAndPgTraces_WithTheSign` and `Xfrog_PcgpaAndEpie_RecoverAChirpedPulse_WithTheSign`: duration within 3 % and GDD within 6 % for PCGPA, ePIE within 5 % and 8 % (median of 3 seeds); `Retrieve_WithoutAKind_DoesNotAssumeShg_ForAPolarisationGateTrace`: kind inference.
* `RetrievalTests.Frog_PcgpaAndEpieRecoverAChirpedPulse` (3 %, 5 %) and `DScan_RecoversPhaseAndFeedsARun` (5 %, 6 %; the retrieved field reused as a run input).
* `MultiTraceRetrievalTests.DoubleFrog_SixStarts_AllGiveTheRightTimeDirectionForBothPulses`: 6 % GDD, 5 % duration, sign exact (Kida et al. 2014).
* `FieldToolsTests`: Gerchberg-Saxton on a known phase, far field within 1 %, phase rms within 0.1 rad.

## Limits

* SHG-FROG leaves the time direction open; use PG, SD, XFROG, d-scan or `multiFrog`.
* ePIE is stochastic: compare several `seed`s.
* Gerchberg-Saxton leaves the piston undetermined and cannot separate φ(x, y) from −φ(−x, −y) for a symmetric near field.
