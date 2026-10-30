# Tutorial 3 -- Larger systems

**Goal:** build a complete laser chain, a Yb chirped-pulse amplifier (CPA) with frequency doubling. Then change it
without editing it (overrides), and drive it from Python for a parameter study.

Files: `docs/examples/tutorial/cpa.ufs`, `run-cpa.json`, `cpa.py`.

## 1. The system

```text
1 µJ, 250 fs seed → stretcher → 8-pass Yb:YAG amplifier (150 W diode pump, 10 kHz) → compressor → LBO SHG → 515 nm
```

```jsonc
"elements": [
  { "id": "stretcher", "type": "gratingPair", "linesPerMm": 1740, "aoiDeg": 60.0, "separationM": -0.25,
    "order": -1, "passes": 2, "efficiency": 0.92 },
  { "id": "stretched", "type": "probe" },
  { "id": "amplifier", "type": "amplifier", "geometry": "multipass", "passes": 8,
    "medium": "Yb:YAG", "lengthM": 0.006, "modeRadiusM": 0.45e-3,
    "pump": { "kind": "cw", "powerW": 150, "wavelengthM": 940e-9, "radiusM": 0.5e-3,
              "profile": "superGaussian", "order": 4, "passes": 2 },
    "betweenPasses": { "loss": 0.03 },
    "thermalLens": { "rodRadiusM": 1.5e-3 } },
  { "id": "amplified", "type": "probe" },
  { "id": "compressor", "type": "gratingPair", "linesPerMm": 1740, "aoiDeg": 60.0, "separationM": 0.25,
    "order": -1, "passes": 2, "efficiency": 0.92 },
  { "id": "shg", "type": "crystal", "crystal": "LBO", "lengthM": 1.5e-3,
    "interaction": { "process": "shg", "type": "I" }, "cut": "auto",
    "beam": { "kind": "gaussian", "w0M": 2.5e-3 },
    "output": "sh", "record": ["fundamental"] }
]
```

New element types, each with its own reference page:

| element | what it does | reference |
|---|---|---|
| `gratingPair` | Treacy grating pair, exact dispersion; negative separation = stretcher | [System file](../reference/system-file.md) |
| `probe` | names a place in the beam (no effect on the pulse) | [System file](../reference/system-file.md) |
| `amplifier` | multipass or regenerative gain: library gain media, pumping with rate equations, saturation, gain narrowing, thermal lens; iterated to steady state at the repetition rate | [Gain media & amplifiers](../reference/gain.md) |
| `crystal` | χ(2) interaction (SHG, SFG, DFG, OPA) with automatic phase matching; `"output": "sh"` makes the second harmonic the main beam, `"record"` keeps the leftover fundamental as a by-product | [Crystals](../reference/crystals.md) |

Other building blocks: `dispersion` (plain GDD/TOD), `chirpedMirror`, `mirror` and `interface` (coatings,
Fresnel losses), `lens`, `aperture`, `waveplate`, `polarizer`, `filter`, `gain` (one spatially resolved pass),
`oscillator`, `saturableAbsorber`, and user elements written in Python ([Active elements](../reference/active-elements.md)).
Materials, coatings and gain media can be defined in the system file and used by name ([Materials](../reference/materials.md)).

## 2. The run spec: probes, instruments and overrides

```jsonc
"pulse": { "shape": "sech2", "centerWavelengthM": 1.03e-6, "durationFwhmS": 250e-15, "energyJ": 1e-6,
           "repRateHz": 10e3, "polarization": "x" },
"probes": ["stretched", "amplified", "compressor"],
"overrides": [ { "target": "element:shg", "property": "lengthM", "value": 2e-3 } ],
"instruments": [
  { "kind": "scope",          "name": "photodiode",  "at": "stretched", "riseTimeS": 20e-12 },
  { "kind": "spectrometer",   "name": "spec_amp",    "at": "amplified", "resolutionM": 0.05e-9 },
  { "kind": "powerMeter",     "name": "P_amp",       "at": "amplified" },
  { "kind": "autocorrelator", "name": "ac_ir",       "at": "compressor" },
  { "kind": "compressor",     "name": "residual_ir", "at": "compressor" },
  { "kind": "conversion",     "name": "shg_eff",     "at": "shg" },
  { "kind": "spectrometer",   "name": "spec_green",  "at": "end" },
  { "kind": "energyMeter",    "name": "E_green",     "at": "end" }
]
```

**Overrides** change any value or model of the system without editing the system file. Here a 2 mm crystal
replaces the 1.5 mm one. Other uses: another n2 for a material, a different coating, a model of your own for one
element or for all of them, or switching an element off (`"property": "enabled", "value": false`). Every value that
was used, and where it came from, is recorded in `provenance.json`. See [Models & overrides](../reference/models.md).

```bash
ultrafast run cpa.ufs run-cpa.json -o results-3      # ~12 s
ultrafast-plot results-3
```

| where | instrument | result |
|---|---|---|
| after the stretcher | `photodiode` (20 ps rise time) | 431 ps pulse, 716 nJ (the gratings' efficiency) |
| after the amplifier | `P_amp`, `spec_amp` | 1.46 mJ = 14.6 W at 10 kHz (gain 2000), spectral FWHM 2.5 nm (gain narrowing) |
| after the compressor | `ac_ir`, `residual_ir` | 1.04 mJ; AC FWHM 796 fs; an ideal compressor would only reach 535 fs, against a transform limit of 534 fs, so the compressor already matches the stretcher |
| the crystal | `shg_eff` | 58 % conversion → 606 µJ at 515 nm (6.1 W) |

The amplifier also reports its own numbers (`summary.json → elements → amplifier → values`): steady state reached
after 28 pulses, small-signal gain per pass 2.9, thermal lens f = 14 cm. `gain.png` shows the gain spectrum, the
inversion and the pulse-train convergence. `probes.png` shows the pulse stretched, amplified and compressed.

Note that the summary line reports T = 60 600 %: the output energy relative to the 1 µJ seed, which includes the
gain.

## 3. The same system from Python

Writing the system in Python is convenient for parameter studies and generated systems (many passes, optimisation
loops). `ultrafast.build` returns the same dictionaries as the JSON files:

```python
import ultrafast as uf
from ultrafast import build as b

def cpa_system(pump_w=150.0, lbo_length_m=1.5e-3):
    return b.system("tutorial-cpa", [
        b.grating_pair("stretcher", 1740, 60.0, -0.25, efficiency=0.92),
        b.probe("stretched"),
        b.multipass_amplifier("amplifier", "Yb:YAG", 0.006, passes=8, mode_radius_m=0.45e-3,
                              pump=b.pump("cw", 940e-9, 0.5e-3, power_w=pump_w, profile="superGaussian", order=4, passes=2),
                              between_passes={"loss": 0.03}, thermalLens={"rodRadiusM": 1.5e-3}),
        b.probe("amplified"),
        b.grating_pair("compressor", 1740, 60.0, 0.25, efficiency=0.92),
        b.crystal("shg", "LBO", lbo_length_m, process="shg", type="I", output="sh", record=["fundamental"],
                  beam=b.gaussian_beam(w0_m=2.5e-3)),
    ])

spec = b.run_spec(b.pulse(1.03e-6, 250e-15, energy=1e-6, shape="sech2", rep_rate_hz=10e3),
                  instruments=[b.instrument("powerMeter", at="amplified", name="P_amp"),
                               b.instrument("conversion", at="shg", name="shg_eff")])

res = uf.run(cpa_system(), spec)
res.write("cpa_results")                       # the same files as the CLI; plot with ultrafast-plot
print(res.instrument("shg_eff")["efficiencyVsInput"])

for p in (60, 100, 150):                       # a scan is just a loop
    r = uf.run(cpa_system(pump_w=p), spec)
    print(p, r.instrument("P_amp")["averagePowerW"], r.summary["output"]["energyJ"] * 10e3)
```

```bash
python docs/examples/tutorial/cpa.py
```

It prints:

```
amplified: 14.58 W, compressible to 535 fs, SHG efficiency 41.9%
pump (W)   IR after amp (W)   green (W)
      60               0.35        0.00
     100               3.96        0.46
     150              14.58        4.37
```

With the 1.5 mm crystal of the system file the conversion is 42 %. The 2 mm override in `run-cpa.json` gave 58 %.
The output rises steeply with the pump power, and the green rises even faster: SHG grows with the square of the
intensity until it saturates.

Python can also supply **your own physics**: a refractive index n(ω), a time response n(t, I), a filter, a beam
size along z, a position-dependent index, or a whole element, all as NumPy functions
([Python](../reference/python.md), examples 04, 12, 16 and 18).

## 4. Where to go from here

* `docs/examples/` has 24 worked systems (README there): multipass cells, OPCPA, mid-IR DFG, oscillators, spatial gain,
  Zemax workflows.
* [Model levels](../model-levels.md) explains which physics each element includes and when to switch on more:
  spatial models, carrier-resolved fields, Raman.
* Systems designed in OpticStudio can be exported directly ([Zemax plugin](../plugins/zemax.md)).

Next: [Tutorial 4 -- Your own measured data](04-measured-data.md).
