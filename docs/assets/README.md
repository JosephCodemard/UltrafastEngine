# Assets: synthetic measurements with an answer key

Recordings like the ones a lab produces, made by the engine from pulses whose parameters are known. The data files
do not contain the answers; `answer_key.json` does. Use them to learn the retrieval, fitting and contrast tools, to
check an analysis of your own, or as test data for code that reads measurements (people and AI agents alike: the
key says what a correct analysis must find).

| file | what it is | try |
|---|---|---|
| `frog_shg_1030nm.npz` | SHG-FROG of a chirped 150 fs pulse at 1030 nm, 256 × 256, with noise | `ultrafast retrieve epie frog_shg_1030nm.npz -o frog-result` |
| `dscan_800nm.npz` + `dscan_800nm_fundamental.csv` | glass-insertion d-scan (fused silica, -2 ... +4 mm) of a 25 fs pulse at 800 nm with residual GDD, and its spectrum | `ultrafast retrieve dscan dscan_800nm.npz --fundamental dscan_800nm_fundamental.csv --material FusedSilica -o dscan-result` |
| `crosscorrelation_1030nm.csv` | third-order cross-correlation, 100 dB dynamic range: a pre-pulse, a post-pulse, an exponential pedestal | read it, find the satellites and their levels (compare with [Instruments: crossCorrelator](../reference/instruments.md#crosscorrelator)) |
| `spm_spectrum.csv` + `spm_autocorrelation.csv` | spectrum and autocorrelation after a 6.35 mm fused-silica plate; the pulse energy is unknown | `ultrafast run spm_plate.ufs spm_fit.json -o fit-result` |
| `answer_key.json` | the true parameters and what a correct analysis finds | |
| `make_assets.py` | regenerates everything (fixed random seed) | `python make_assets.py` |

Run the commands from this folder.

## What a correct analysis finds

* **FROG**: FWHM 160 fs; spectral phase with |GDD| = 3000 fs². ePIE converges in the default 300 iterations
  (G ≈ 7·10⁻⁴, 158 fs); PCGPA needs more at this noise level (`--iterations 1500`: 157 fs). SHG-FROG cannot tell the
  sign of the chirp, and the weak TOD (2·10⁴ fs³) is below what this trace resolves. Fit the GDD from
  `retrieved_spectrum.csv` (phase vs angular frequency, weighted by the intensity).
* **d-scan**: FWHM 30 fs and GDD -150 fs² with its sign (the retrieval gives -151 fs²).
* **Cross-correlation**: a pre-pulse at -20 ps near -65 dB (3·10⁻⁷), a post-pulse at +35 ps at -40 dB (10⁻⁴),
  and a third feature at -35 ps near -80 dB that is **not** in the pulse: the correlator's gate artefact of the
  post-pulse (ε²). Noise floor about -99 dB. The pedestal is exponential (1.5 ps rise, 2 ps fall, 10⁻⁵ at the peak).
* **SPM**: the fit finds 5.85 ± 0.07 µJ (true value 6 µJ, B ≈ 1 rad) in 11 runs.

## Formats

* NPZ: `delay_s` or `insertion_m` (rows), `omega_rad_s` (SH signal frequencies, columns), `trace` (rows × columns).
  The same layout as a results folder's `result.npz`, so `ultrafast retrieve` reads both.
* CSV: a `#` comment line, a header with the unit in each column name (`wavelength_nm`, `delay_ps`), then numbers --
  the format the fitting plugin reads ([Measured data](../reference/measured-data.md#units-from-column-names)).
