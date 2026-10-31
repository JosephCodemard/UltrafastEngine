# System file reference (`.ufs`)

A system file is JSON (comments `//`, `/* */` and trailing commas are accepted) with SI units throughout. Every key
with its type, unit and default is in the generated [option reference](options.md#elements); files that
start with `"$schema": ".../schemas/ufs.schema.json"` get completion and checking in editors ([Schemas](schema.md)).

```jsonc
{
  "$schema": "schemas/ufs.schema.json",   // optional: editor completion
  "format": "ultrafast-system",          // required
  "version": 3,                           // 2 still loads; `topology` and `next` need 3
  "name": "stretcher",
  "topology": "sequential",               // or "branched": every element says where its light goes
  "description": "optional text",
  "source": { "kind": "zemax", ... },     // provenance of the file (written by exporters; free-form)
  "sampling": { "wavelengthsM": [ ... ] }, // wavelengths of all per-wavelength arrays below
  "environment": {                         // optional
    "indexReference": "air",               // data quoted relative to air (OpticStudio) -> air dispersion added
    "air": { "temperatureC": 20, "pressurePa": 101325, "humidity": 0, "co2ppm": 450 }
  },
  "materials": { "NAME": { ...material... } },
  "coatings":  { "NAME": { ...coating... } },
  "inputBeam": { ...beam model... },       // optional default beam for nonlinear elements without one
  "elements": [ { "id": "...", "type": "...", ... }, ... ],
  "overrides": [ ... ]                     // optional, see models.md
}
```

Unknown top-level keys are preserved when a system file is loaded and saved again.

`"topology": "sequential"` (the default) runs the elements in order. With `"branched"`, elements carry `next`, `inputs` and, where light enters a loop, `loop`: see [Branched systems](branches.md). A Sequential system can use splitters too, with `output` choosing the port that continues.

## Per-wavelength values

Any element field documented as *per-wavelength* accepts either a number (applies at all wavelengths) or an
array with one value per `sampling.wavelengthsM` entry (an element may carry its own `"wavelengthsM"`).
Sampled curves are spline-interpolated in angular frequency; outside the sampled range group delay continues
with constant GDD and losses hold their end values (the results report the spectral energy affected).

## Common element fields

| field | meaning |
|---|---|
| `id` | unique id (used by overrides, probes and instruments) |
| `type` | element type (below) |
| `label` | display name |
| `surfaces` | `[first, last]` source surfaces (OpticStudio rows), informational |
| `enabled` | `false` skips the element |
| `lengthM` | geometric length (reported; required for bulk) |

## Element types

### `dispersion` -- sampled or polynomial linear dispersion
| field | |
|---|---|
| `groupDelayS` | per-wavelength absolute group delay (e.g. traced group path / c). Optional `groupDelayYS` for y |
| `gddS2`, `todS3`, `fodS4`, `groupDelayAtCarrierS`, `referenceWavelengthM` | Taylor coefficients (alternative) |
| `phaseRad` | per-wavelength spectral phase (alternative) |
| `transmission` | per-wavelength power transmission; `lossMechanism` names it (default `transmission`) |
| `airPathM` | length of air in this element whose data is relative to air (absolute-index correction) |

### `bulk` / `gap` -- propagation in a material (nonlinear when possible)
| field | |
|---|---|
| `material` | material name (system `materials` or built-in); `gap` defaults to `Air` |
| `lengthM` | required |
| `groupDelayS` | traced group delay (used instead of the material index unless the material's index is overridden or `dispersionSource: "material"`) |
| `transmission` | per-wavelength internal transmission (else the material's `attenuation`) |
| `beam` | beam model along the element (z from the element start) -- needed for nonlinear propagation |
| `nonlinear` | `false` forces linear; `gap` is linear unless `true` |
| `timeResponse` | an extra time-domain response model (see models.md) |
| `replaceKerr` | `true` switches off the built-in Kerr/Raman (use with your own `timeResponse`) |
| `transverse` | spatial model: `{"model": "radial"|"cartesian", ...}` -- diffraction, self-focusing, M² ([Spatial models](transverse.md)) |

### `interface` / `mirror` / `grating` -- a surface
| field | |
|---|---|
| `mode` | `refract` (interface), `reflect` (mirror), `diffract` (grating) |
| `before`, `after` | media on both sides (for a mirror, `after` is the substrate/metal; omitted = ideal mirror) |
| `aoiRad` / `aoiDeg` | angle of incidence, per wavelength (traced) or scalar |
| `coating` | coating name (from `coatings`) or inline coating object; none = bare Fresnel |
| `efficiency`, `efficiencyS`, `efficiencyP` | diffraction efficiency (grating, no coating given) |
| `kIn`, `kOut`, `normal` | global ray directions and surface normal -> exact s/p basis transport |
| `planeOfIncidenceDeg` | s-direction angle from local x when no vectors are given (default 90 = fold in x-z) |
| `semiDiameterM` / `clearApertureM`, `aperture` | hard aperture (circular, or an aperture model) |
| `beamRadiusM` (per wavelength) / `beamRadiusXM`, `beamRadiusYM`, `beam` | beam size at the surface (clipping, fluence) |
| `decenterXM`, `decenterYM` | beam decentre on a rectangular aperture |
| `roughnessRmsM` | rms roughness -> total integrated scatter loss |
| `lidt` | damage threshold (else the coating's, else the material's) |
| `radiusOfCurvatureM` / `focalLengthM` (mirror) | curved mirror: acts as a thin lens on a carried transverse profile |

### Analytic elements
* `gratingPair`: `linesPerMm`, `aoiDeg`, `separationM` (perpendicular, negative for a stretcher), `order` (-1),
  `passes` (2), `efficiency` (per diffraction).
* `chirpedMirror`: `gddS2`, `todS3`, `fodS4` per bounce, `bounces`, `reflectance`, `referenceWavelengthM`.
* `aperture`: `radiusM` or `halfWidthXM`/`halfWidthYM` or `aperture` model, plus the beam size fields.
* `filter` / `custom`: `filter` (or `model`): any spectral filter model (models.md); `mechanism` names the loss.
* `waveplate`: `plate` (`half`/`quarter`) or `retardanceWaves`, `axisDeg`, `designWavelengthM`.
* `polarizer`: `axisDeg`, `extinctionRatio`, `transmission`.
* `rotator`: `angleDeg` (rotation of the polarisation / image).
* `probe`: does nothing; a named place to record snapshots or put instruments.

### Active elements
See [Active elements](active-elements.md) for aux beams, by-products and carrier changes.

* `crystal`: χ(2) frequency conversion and parametric amplification -- `crystal`, `lengthM`, `interaction`
  (`process`, `type`, `noncollinearDeg`, `qpm`, `plane`, `polarizations`), `cut`, `beam`, `aux`, `mainRole`,
  `output`, `record`, `overlap`, `deffPmPerV`, `temperatureC`, `chi3`, `n2`, `seedNoise`, `spectralMargin`,
  `polarizationAxisDeg`, `transverse`. Full reference: [Crystals](crystals.md).
* `lens`: thin lens on a carried transverse profile -- `focalLengthM`, optional `material` (chromatic focal length),
  `medium` (default Air), `thicknessM` (for the dispersion of the glass).
* `user`: your own function of the main beam's spectrum -- `callback` (`"@name"`), `params` (any JSON, passed to
  the function), `lengthM` (informational).
* `gain`: a pumped gain medium crossed once -- `medium` (`Yb:YAG`, `Ti:sapphire`, `Nd:YAG` or an object),
  `lengthM`, `pump`, `inversion`, `beam` / `modeRadiusM`, `transverse`, `thermalLens`, `slices`, `segments`,
  `spm`, `host`, `repRateHz`, `steadyState`, `dopingProfile`. Full reference: [Gain media](gain.md).
* `amplifier`: multipass (`passes`, `betweenPasses`) or regenerative (`roundTrips` N or `"auto"`,
  `passesPerRoundTrip`, `cavity`) around one gain medium, same medium / pump keys ([Gain media](gain.md)).

* `oscillator`: a mode-locked laser that replaces the incoming beam -- `model` (`parametric`, `haus`, `cavity`),
  `repRateHz` / `cavityLengthM`, `outputCoupler`, `gain`, `absorber` (haus), `elements` (cavity), `start`,
  `roundTrips`, `gridPoints` ([Oscillators](oscillators.md)).
* `saturableAbsorber` (`sesam`): slow (SESAM), fast or Kerr-lens loss modulation ([Oscillators](oscillators.md)).
* `aperture` also takes `softRadiusM` (+ `order`) and acts on a carried transverse profile ([Spatial models](transverse.md)).

### Position-dependent properties (any element)
`indexProfile` (Δn), `absorptionProfile` (α, 1/m) and `n2Scale` as functions of x, y, z, r, theta: a number,
an expression, a Gaussian, a table or a callback. They act point by point with a `transverse` model and are
beam-averaged in 1-D ([Position-dependent properties](spatial-properties.md)).

## Materials

```jsonc
"materials": {
  "MyGlass":  { "index": { "kind": "sellmeier", "B": [..], "C": [..] }, "indexReference": "air",
                "n2": 3e-20, "raman": { "kind": "dampedOscillator", "tau1": 12.2e-15, "tau2": 32e-15, "fR": 0.18 },
                "attenuation": { "kind": "table", "wavelengthsM": [..], "internalTransmission": [..], "thicknessM": 0.01 },
                "lidt": { "fluenceJPerCm2": 5, "pulseDurationS": 10e-9, "scalingExponent": 0.5 } },
  "Ar@2bar":  { "gas": { "species": "Ar", "pressureBar": 2, "temperatureK": 293 } },
  "HotSilica": { "base": "FusedSilica", "n2": 2.4e-20 }      // start from a built-in and change something
}
```
See [Materials](materials.md) for built-ins and sources. A material entry can also hold a user χ(2) crystal:
`"myCrystal": { "crystal": { "base": "BBO", "deffPmPerV": 2.0 } }` ([Crystals](crystals.md#user-crystals)).

## Coatings
See [Coatings, losses and polarisation](coatings.md): kinds `none`, `stack`, `quarterWave`, `ideal`, `ar`,
`gdd`, `table`, `efficiency`.

## Validation

`ultrafast validate system.ufs` (or `Simulation.Validate`, `System.validate()` in Python, `uf_system_validate`)
checks ids, types and array lengths and compiles the system with a probe pulse.
