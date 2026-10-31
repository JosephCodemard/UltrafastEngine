# HITRAN plugin: gas lines in the air

The `hitran` plugin adds line-by-line **absorption and dispersion** of air: H2O (required), CO2 and O2 (optional), from
[HITRAN](https://hitran.org) line lists, as the air of your system. Water lines near 1 µm are only a few GHz wide, so they
ring behind a femtosecond pulse for hundreds of picoseconds; with [`grid.tail`](../reference/delayed-light.md) that ring-down
is carried without a nanosecond window on the nonlinear grid.

The line data are **not** part of the plugin or the engine: HITRAN has its own terms of use. You download them once.

## Install and get the data

The plugin ships with the engine packages (`--plugins hitran`; the NativeAOT C library and the Python hosted engine have it
compiled in). Download the lines with the HITRAN API (`pip install hitran-api`; the module is `hapi`, or `hapi2` in the newer
release):

```bash
python -m ultrafast_hitran download H2O                       # 6500-14000 cm-1 (714-1538 nm), main isotopologues
python -m ultrafast_hitran download H2O CO2 O2 --dir ./hitran --numin 6500 --numax 14000
```

(An isotopologue the server has no lines for in the range is skipped with a message; the download fails only when no line is found at all.)

```python
import ultrafast_hitran as hit
hit.download(["H2O"], numin=6500, numax=14000)                 # writes ~/.ultrafast/hitran/H2O.csv
```

The files are looked up in the working directory, `dataDir`, `$ULTRAFAST_HITRAN_DIR` and `~/.ultrafast/hitran`. Both the CSV
written by the downloader (HAPI column names `molec_id, local_iso_id, nu, sw, a, gamma_air, gamma_self, elower, n_air, delta_air`)
and HITRAN's own 160-character `.par` records are read. Main isotopologues are downloaded: H2O 161, 181, 171, 162; CO2 626,
636, 628, 627; O2 66, 68, 67. (Whether the HITRAN API's package name and its `fetch` signature are as assumed here could not
be checked without a network: the downloader is tested with a fake API, so please report any mismatch.)

## Use

```jsonc
// system file
"environment": { "air": { "kind": "hitran", "applyToGaps": true,
                          "temperatureC": 22, "pressurePa": 101325, "humidity": 0.45, "co2ppm": 420,
                          "species": { "H2O": true, "CO2": true } } }
// run spec
"grid": { "tail": { "windowS": 2e-9 } }          // the carrier is not snapped with a tail: it stays on the line you chose
```

```python
from ultrafast import build as b
import ultrafast_hitran as hit
env = b.environment(air=hit.air(humidity=0.45, temperature_c=22, species=hit.species(h2o=True, co2=True)))
system = b.system("cell", [b.gap("g1", 10.0)], environment=env)
spec = b.run_spec(b.pulse(1.03e-6, 150e-15, energy=1e-6), grid=b.grid(tail=b.tail_grid(window_s=2e-9)))
```

C and C++ (`ultrafast.h`) take the same JSON; the plugin is registered when the library loads. A single gap can override the
air (`"air": {"humidity": 0.2}` on the `gap`).

| key of the `hitran` air | default | meaning |
|---|---|---|
| `temperatureC`, `pressurePa`, `humidity` (0..1), `co2ppm` | 20, 101325, 0, 450 | state of the air: Ciddor index and the number densities |
| `species` | `{"H2O": true}` | `true`, a file name, or `{file, moleFraction, partitionExponent}` per species (H2O, CO2, O2) |
| `rangeCm1` | `[6500, 14000]` | lines kept (cm-1) |
| `cutoffCm1` | 25 | each line is evaluated within this distance of its centre |
| `baseIndex` | `ciddor` | the smooth index; `vacuum` leaves it out (n = 1 + lines) |
| `dataDir`, `lineScale` | | folder of the line files; a factor on all line strengths |
| `applyToGaps` | false | use this air as the "Air" of every gap (`environment.air` only) |

**`environment.air` is opt-in.** Without `applyToGaps: true` the `environment.air` block does what it always did (it only
supplies the air used for data quoted relative to air), so existing results do not change. When it is on, a note in the
result says so, and `gap` elements may override it with their own `air`.

## What the model is

For each line the complex Voigt line shape `F = w(z) / (gamma_D sqrt(pi))`, `z = (nu - nu0 + i gamma_L) / gamma_D`
(`w` the Faddeeva function, Weideman's rational approximation) gives **both** parts, so absorption and dispersion are
Kramers-Kronig consistent by construction (exp(-iωt) convention, n > 1 below a line):

```text
alpha(nu) = n_d S Re F                 intensity absorption, 1/cm            kappa = n_d S Re F / (4 pi nu)
Dn(nu)    = - n_d S Im F / (4 pi nu)   (nu in cm-1)
```

* Strength: `S(T) = S(296 K) [Q(296)/Q(T)] exp(-c2 E'' (1/T - 1/296)) [1 - exp(-c2 nu/T)] / [1 - exp(-c2 nu/296)]`.
* Pressure width (HWHM): `gamma_L = (296/T)^n_air [gamma_air (p - p_self) + gamma_self p_self]`, pressure in atm. Shift: `nu0 = nu + delta_air p`.
* Doppler HWHM: `nu0 sqrt(2 k T ln2 / (m c^2))` (H2O at 1 µm and 296 K: 0.014 cm-1, FWHM 0.85 GHz).
* Number density `n_d = x p / (k T)`; the water-vapour mole fraction comes from the relative humidity with the CIPM-2007 saturation
  pressure (the same formula as the Ciddor index).
* The lines are cut at `+-cutoffCm1`; there is **no continuum absorption**; the smooth Ciddor index stays.

**Approximations to know about.**

* **Partition sums.** The downloader also writes `<NAME>.partition.csv` (`iso,T,Q`, 100-500 K in 1 K steps, the TIPS sums of `hapi.partitionSum` for
  each isotopologue it fetched). When that file lies next to the line file (or `partitionFile` names it) the strengths are scaled with the real
  `Q(296)/Q(T)` per isotopologue; the description of the model says "TIPS partition sums". Without it the model falls back to
  `(T/296)^1.5` for H2O and `(T/296)` for CO2 and O2 (or your `partitionExponent`), which neglects the vibrational part and can be a percent or more off
  away from 296 K. The tables are not bundled (they are data you generate), and a temperature outside the table is an error.
* `delta_air` is not temperature dependent; widths use a single `n_air`; no line mixing, no speed dependence.
* Water lines are broader at 1 atm than the Doppler width: the Lorentz half width is about 0.1 cm-1 (3 GHz) for typical lines
  (check `gamma_air`, `gamma_self` of your lines), so the ring-down lasts a few 1/(2 pi gamma) = 50 ps e-folds.
* The frame of a propagation step is referenced to the smooth index, not to the line ([why](../reference/delayed-light.md#the-frame-of-a-narrow-line)).

## Validation

`HitranTests`: Faddeeva values (w(0), w(iy), Dawson), continuity at the asymptotic switch; HITRAN `.par` and CSV round trips;
Doppler width against the kinetic formula; a pressure-broadened (Lorentz) line against `n_d S / (pi gamma_L)` and the dispersion
extremum `n_d S / (8 pi^2 nu gamma_L)`; **Kramers-Kronig** by a numerical Hilbert transform of a Voigt line; the temperature and
density scaling; energy lost in a gap against Beer-Lambert `sum S (1 - exp(-alpha L))`; and the ring-down of three lines in a
10 m gap on the tail grid (main N = 1024, tail N = 262 144) against the same system on **one 2 ns window (N = 524 288)** --
total energy to 1e-5, tail power 100-900 ps to 2 %, main peak power to 0.1 %. The same lines in a nonlinear gap, a radial gap and a vector radial gap reach the tail of the linear gap to 2 %. The always-on tests use no HITRAN data (the lines are
synthetic, written in the HITRAN format).

**Real lines against the reference implementation.** `plugins/hitran/tools/make_hapi_reference.py DIR` downloads real lines (needs the network and
`pip install hitran-api`) and lets HAPI -- the HITRAN team's own code, with TIPS partition sums -- compute its Voigt absorption coefficient at 250, 296 and
350 K; the opt-in test `HitranTests.AgainstHapi_RealLines_AgreeWithTheReferenceImplementation`
(`ULTRAFAST_HITRAN_VALIDATE_DIR=DIR`) compares the plugin with it. With the same wing cutoff as HAPI's default (`cutoffCm1: 5`, 50 half widths), results of
the run of 2026-10-29:

| band | lines (points on a line) | integrated absorption, plugin / HAPI | worst point on a line |
|---|---|---|---|
| H2O 9690-9730 cm-1 (1 µm) | 1590 | 1.002 - 1.004 | 1.4 - 3.0 % |
| H2O 7180-7200 (1.39 µm) | 1500 | 0.995 - 1.004 | 1.1 - 3.2 % |
| CO2 6970-7000 (1.43 µm) | 5000-6000 | 1.002 - 1.004 | 1.2 - 2.8 % |
| O2 7870-7900 (1.27 µm) | 3500-3900 | 1.005 - 1.007 | 2.6 - 2.8 % |
| O2 13110-13150 (A band, 0.76 µm) | 1850-2500 | 1.005 - 1.007 | 2.6 - 3.9 % |

The ratio does not change with temperature, so the TIPS scaling is right. With the plugin's default cutoff of 25 cm-1 the integrated absorption is 0.8 - 1.5 %
higher: the far Lorentz wings of the 300 - 6000 lines add up (no line is accurate that far out; there is no continuum model to correct them). This compares the
plugin with an independent implementation of the same physics, **not** with a measured spectrum: a comparison with a measured transmission of humid air is
still open.
