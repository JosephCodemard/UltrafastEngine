# System file (`.ufs`)

A system file describes an optical system: the list of elements the pulse meets, the materials and coatings they use and
the air around them. It is JSON with comments (`//`, `/* */`) and trailing commas allowed, and SI units throughout. This page
explains the structure of the file and the element types. Every key with its type, unit and default is in the generated
[option reference](options.md#system-file); files that start with `"$schema": ".../schemas/ufs.schema.json"` get completion and
checking in editors ([Schemas](schema.md)).

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
    "air": { "temperatureC": 20, "pressurePa": 101325, "humidity": 0, "co2ppm": 450, "applyToGaps": false }
  },
  "materials": { "NAME": { ...material... } },
  "coatings":  { "NAME": { ...coating... } },
  "substrates": { "NAME": { ...mirror substrate... } },   // optional, see coatings.md
  "inputBeam": { ...beam model... },       // optional default beam for nonlinear elements without one
  "elements": [ { "id": "...", "type": "...", ... }, ... ],
  "overrides": [ ... ]                     // optional, see models.md
}
```

## Keys

* `format` is required; `version` is the file format version (`ultrafast migrate` writes 3; the Python builders write 2, or 3 for a branched system; 2 and 1 still load; see [Schemas](schema.md#format-versions-and-migrations)).
  The generated reference lists the default of `version` as 2; the engine itself writes 3.
* `topology` is `"sequential"` (the default: the elements run in order) or `"branched"` (elements carry `next`, `inputs` and, where
  light enters a loop, `loop`: see [Branched systems](branches.md)). A Sequential system can use splitters too, with `output` choosing the
  port that continues. `topology` is read and written by the engine but is not declared in the option registry, so it does not appear
  in the generated reference or the schema.
* `environment.air` is the air of the laboratory (default 20 °C, 101 325 Pa). With `applyToGaps: true` every `gap` uses it as its
  "Air"; a gap can also carry its own `air`. `indexReference: "air"` marks data quoted relative to air, and the engine adds the air dispersion.
* `substrates` holds mirror substrates by name ([Coatings](coatings.md#substrate-echoes)).
* Unknown top-level keys are kept when a file is loaded and saved again (they are not checked either). `substrates` is carried by its own field.

## Per-wavelength values

Any element field documented as *per-wavelength* accepts either a number (applies at all wavelengths) or an
array with one value per `sampling.wavelengthsM` entry (an element may carry its own `"wavelengthsM"`).
Sampled curves are spline-interpolated in angular frequency; outside the sampled range group delay continues
with constant GDD and losses hold their end values (the results report the spectral energy affected).

## Common element fields

Every element accepts the keys of the `elementCommon` group ([option reference](options.md#elementcommon)):

| field | meaning |
|---|---|
| `id`, `type` | unique id (used by overrides, probes and instruments) and the element type (below) |
| `label`, `enabled` | display name; `false` skips the element |
| `surfaces` | `[first, last]` source surfaces (OpticStudio rows), informational |
| `next`, `inputs`, `loop` | wiring of a [Branched](branches.md) system |
| `wavelengthsM` | wavelengths of this element's per-wavelength arrays (default: the file's `sampling`) |
| `indexProfile`, `absorptionProfile`, `n2Scale` | position-dependent properties ([below](#position-dependent-properties-any-element)) |
| `sameAs` | id of another element this one is a copy of ([identical elements](#identical-elements)) |
| `params` | free parameters for expressions and user elements |

`lengthM` is not a common key: each type declares its own. It is required for `bulk`, `gap` and `crystal`, and informational for
the others.

### Identical elements

A component that the light meets more than once (the passes of a multipass cell, a mirror hit twice) is one physical object, so
it has one set of numbers. Write the first pass in full and the others as copies:

```jsonc
{ "id": "optic",  "type": "transform", "operators": [ { "kind": "wavefront", "zernike": { "4": 0.08 } } ] },
// ... other elements ...
{ "id": "optic2", "sameAs": "optic" },
{ "id": "optic3", "sameAs": "optic", "label": "pass 3" }
```

* A copy has the **same type and the same physical numbers** as its source: operators, coatings, figure, damage, material. When the
  source changes (a fit writes new numbers, a study varies one, you edit the file), every copy changes with it.
* A copy is **otherwise an ordinary element**. It compiles on its own, receives its own pulse, spectrum, energy and beam (so the
  light on pass 5 differs from pass 1), and has its own record, probes and loss booking. Nothing is shared at run time: a gain
  medium's inversion or a device's drive is per instance.
* A copy may set only what belongs to one instance: `label`, `enabled`, the wiring of a [Branched](branches.md) system (`next`,
  `inputs`, `loop`, `output`, `surfaces`) and the geometry that may differ from pass to pass (`aoiDeg`, `aoiRad`, `tiltXRad`,
  `side`). Any other key is an error, so "identical" stays true. `type` may be repeated if it is the source's.
* A copy may point at another copy; the source may come later in the file. A cycle is an error.
* Overrides, probes and fit paths address an element by its id. They do **not** follow `sameAs`: to change the shared numbers
  address the source (a fit path into a copy is refused).
* A saved system keeps the copies as written.

Python: `same_as("optic2", "optic")` (`ultrafast.build` / `ultrafast.system`). Fitting: [transforms on several passes](../plugins/fitting.md#the-same-transform-on-several-passes).

## Element types

The engine declares 38 built-in element types (the plugins add more: `transform` for the fitting plugin, `slm`, `aopdf`,
`deformableMirror` and `spectralShaper` for the ao plugin). Each has its section in the
[option reference](options.md#elements) with every key, unit and default. This section names the types and the keys that are
easy to miss.

| group | types | details |
|---|---|---|
| propagation | `bulk` (alias `medium`), `gap`, `plate` (`window`), `ndFilter`, `prism`, `fiber` (`fibre`, `waveguide`) | [bulk / gap](#bulk-gap-propagation-in-a-material), [Optical components](components.md), [Fibres](fibers.md) |
| surfaces | `interface` (`surface`), `mirror`, `grating`, `chirpedMirror`, `gratingPair`, `leakMirror`, `dichroic`, `beamSplitter`, `polarizingSplitter` | [surfaces](#interface-mirror-grating-a-surface), [Branched systems](branches.md) |
| linear, analytic | `dispersion`, `filter` (`custom`), `waveplate`, `polarizer`, `thinFilmPolarizer`, `faradayRotator`, `rotator`, `aperture`, `mask`, `wavefront` (`aberration`), `delay`, `modulator`, `resonator`, `probe` | [analytic elements](#analytic-elements), [Optical components](components.md), [Modulators](modulator.md) |
| active | `crystal`, `lens`, `gain`, `amplifier`, `oscillator`, `saturableAbsorber` (`sesam`), `user` | [active elements](#active-elements) |

### `dispersion` -- sampled or polynomial linear dispersion

* `groupDelayS` (per wavelength; optional `groupDelayYS` for y), or the Taylor coefficients `gddS2`, `todS3`, `fodS4` with
  `groupDelayAtCarrierS` and `referenceWavelengthM`, or `phaseRad` (per wavelength). The three forms are alternatives.
* `transmission` (per wavelength power transmission; `lossMechanism` names it, default `transmission`).
* `airPathM`: length of air in this element whose data are relative to air (absolute-index correction).

### `bulk` / `gap` -- propagation in a material

`material` (a name from `materials` or a built-in; a `gap` defaults to `Air`), `lengthM` (required), `beam` (the beam model along the
element, needed for nonlinear propagation), `nonlinear` (`false` forces linear; a `gap` is linear unless `true`), `timeResponse` (an extra
time-domain response, see [Models](models.md)), `replaceKerr` (`true` switches off the built-in Kerr and Raman), `transverse` (a spatial model:
[Spatial models](transverse.md)), `transmission` (per-wavelength internal transmission, else the material's `attenuation`), `ghosts`,
`airPathM`. A `gap` also takes `air` (its own air; overrides `environment.air`).

`groupDelayS` (and `groupDelayYS`) is a traced group delay. `dispersionSource` chooses between it and the material index:

| value | the engine uses |
|---|---|
| `auto` (what the compiler uses when the key is absent) | the traced `groupDelayS` when present, unless the material's index was overridden by the system file or a run override; otherwise the material index |
| `data` | always the traced `groupDelayS` |
| `material` | always the material index |

The generated reference lists the default of `dispersionSource` as `data` and offers only `data` and `material`; the compiler default
and the third value `auto` are as in the table.

### `interface` / `mirror` / `grating` -- a surface

| field | meaning |
|---|---|
| `mode` | `refract` (interface), `reflect` (mirror), `diffract` (grating) |
| `before`, `after` | media on both sides (for a mirror, `after` is the substrate or metal; omitted = ideal mirror) |
| `aoiRad` / `aoiDeg` | angle of incidence, per wavelength (traced) or scalar |
| `coating` | coating name (from `coatings`) or inline coating; none = bare Fresnel |
| `linesPerMm` or `linesPerM`, `order` (default -1) | grating groove density and diffraction order: the angular dispersion, anamorphic magnification and spatio-temporal couplings follow from them |
| `efficiency`, `efficiencyS`, `efficiencyP` | diffraction efficiency (grating without a coating) |
| `kIn`, `kOut`, `normal` | global ray directions and surface normal: exact s/p basis transport |
| `planeOfIncidenceDeg` | s-direction angle from local x when no vectors are given (default 90 = fold in x-z) |
| `semiDiameterM` / `clearApertureM`, `aperture` | hard aperture (circular, or an aperture model) |
| `beam`, `decenterXM`, `decenterYM` | beam at the surface and its decentre on a circular or rectangular aperture (circular: closed-form radial integral, 1 - Marcum Q1 for a round beam) |
| `roughnessRmsM` | rms roughness: total integrated scatter loss |
| `lidt` | damage threshold (else the coating's, else the material's) |
| `radiusOfCurvatureM` / `focalLengthM` (mirror) | curved mirror: acts as a thin lens on a carried transverse profile |
| `ripple`, `delayedScatter`, `substrate`, `aberration`, `ghosts` | coating ripple, scatter returned as delayed light, mirror substrate, surface figure, ghost replicas ([Coatings](coatings.md), [Delayed light](delayed-light.md), [Optical components](components.md)) |

### Analytic elements

* `gratingPair`: `aoiDeg` and `separationM` (perpendicular, negative for a stretcher) are required; `linesPerMm` or `linesPerM` gives the groove
  density; `order` (-1), `passes` (2), `efficiency` (per diffraction).
* `chirpedMirror`: `gddS2`, `todS3`, `fodS4` per bounce (or `gddFs2`, `todFs3`, `fodFs4`), `bounces`, `reflectance`, `referenceWavelengthM`, and the
  ripple keys (`rippleGddFs2`, `ripplePeriodNm`, `complementaryPairs`, ...; see the option reference).
* `aperture`: `radiusM`, or `halfWidthXM` / `halfWidthYM`, or an `aperture` model, plus the beam size fields. A soft aperture takes `softRadiusM`
  (+ `order`) and acts on a carried transverse profile ([Spatial models](transverse.md)).
* `filter` / `custom`: `filter` (or `model`): any spectral filter model ([Models](models.md)); `mechanism` names the loss.
* `waveplate`: `plate` (`half`/`quarter`) or `retardanceWaves`, `axisDeg`, `designWavelengthM`. `polarizer`: `axisDeg`, `extinctionRatio`, `transmission`.
  `rotator`: `angleDeg` (rotation of the polarisation or image).
* `probe`: does nothing; a named place to record snapshots or put instruments.
* `mask`, `modulator`, `wavefront`, `delay`, `resonator`, `prism`, `ndFilter`, `thinFilmPolarizer`, `faradayRotator`, the splitters and `leakMirror`
  and `dichroic`: see the [option reference](options.md#elements) and [Optical components](components.md).

### Active elements

See [Active elements](active-elements.md) for aux beams, by-products and carrier changes.

* `crystal`: χ(2) frequency conversion and parametric amplification: `crystal`, `lengthM`, `interaction` (`process`, `type`, `noncollinearDeg`, `qpm`,
  `plane`, `polarizations`), `cut` and `detuneDeg`, `beam`, `aux`, `mainRole`, `output`, `record`, `overlap`, `deffPmPerV`, `temperatureC`, `chi3`,
  `n2`, `seedNoise` and `noiseSeed`, `spectralMargin`, `polarizationAxisDeg`, `ionization`, `ghosts`, `transverse`. Full reference: [Crystals](crystals.md).
  `seedNoise` is `false` in the engine when the key is absent; the generated reference lists `true`.
* `lens`: thin lens on a carried transverse profile: `focalLengthM` (or `focalLengthXM`, `focalLengthYM` for a cylindrical lens), optional `material`
  (chromatic focal length), `medium` (default Air), `thicknessM` (for the dispersion of the glass), `aberration`, `ghosts`.
* `user`: your own function of the main beam's spectrum: `callback` (`"@name"`), `params` (any JSON, passed to the function), `lengthM` (informational).
* `gain`: a pumped gain medium crossed once: `medium` (`Yb:YAG`, `Ti:sapphire`, `Nd:YAG` or an object), `lengthM`, `pump`, `inversion`,
  `beam` / `modeRadiusM`, `transverse`, `thermalLens`, `slices`, `segments`, `spm`, `host`, `repRateHz`, `steadyState`, `dopingProfile`, `temperatureK`,
  `dopingDensityM3` and the amplified spontaneous emission keys `ase`, `aseSeed`, `aseModel`. Full reference: [Gain media](gain.md).
* `amplifier`: multipass (`passes`, default 4, `betweenPasses`) or regenerative (`roundTrips` N or `"auto"`, `passesPerRoundTrip`, `cavity`,
  `maxRoundTrips`, default 300) around one gain medium; `geometry` selects the form; same medium and pump keys ([Gain media](gain.md)).
* `oscillator`: a mode-locked laser that replaces the incoming beam: `model` (`parametric`, `haus`, `cavity`), `repRateHz` / `cavityLengthM`,
  `outputCoupler`, `gain`, `absorber` (haus), `elements` (cavity), `start`, `roundTrips`, `gridPoints` ([Oscillators](oscillators.md)).
* `saturableAbsorber` (`sesam`): slow (SESAM), fast or Kerr-lens loss modulation ([Oscillators](oscillators.md)).

### Position-dependent properties (any element)

`indexProfile` (Δn), `absorptionProfile` (α, 1/m) and `n2Scale` as functions of x, y, z, r, theta: a number,
an expression, a Gaussian, a table or a callback. They act point by point with a `transverse` model and are
beam-averaged in 1-D ([Position-dependent properties](spatial-properties.md)).

## Materials

```jsonc
"materials": {
  "MyGlass":  { "index": { "kind": "sellmeier", "B": [..], "C": [..] }, "indexReference": "air",
                "n2": 3e-20, "raman": { "kind": "dampedOscillator", "tau1S": 12.2e-15, "tau2S": 32e-15, "fR": 0.18 },
                "attenuation": { "kind": "table", "wavelengthsM": [..], "internalTransmission": [..], "thicknessM": 0.01 },
                "lidt": { "fluenceJPerCm2": 5, "pulseDurationS": 10e-9, "scalingExponent": 0.5 } },
  "Ar@2bar":  { "gas": { "species": "Ar", "pressureBar": 2, "temperatureK": 293 } },
  "HotSilica": { "base": "FusedSilica", "n2": 2.4e-20 }      // start from a built-in and change something
}
```
See [Materials](materials.md) for built-ins and sources. A material entry can also hold a user χ(2) crystal:
`"myCrystal": { "crystal": { "base": "BBO", "deffPmPerV": 2.0 } }` ([Crystals](crystals.md#user-crystals)).

## Coatings

Kinds `none`, `stack`, `quarterWave`, `ideal`, `ar`, `gdd`, `table`, `efficiency`: see [Coatings, losses and polarisation](coatings.md).

## Validation

`ultrafast validate system.ufs` (or `Simulation.Validate`, `System.validate()` in Python, `uf_system_validate`) checks ids, types and array lengths,
compiles the system with a probe pulse and reports unknown keys against the option registry ([Schemas](schema.md)). `ultrafast validate system.ufs run.json`
also prints the memory estimate. Tests: `SchemaTests.ExamplesUseOnlyDeclaredOptions` runs every example system file through the checks; the identical-elements
rules are tested in `SameAsTests`.

## Limits

* Top-level keys are not checked against the registry: a misspelt top-level key is kept as an unknown key without a warning. Element keys are checked.
* Per-wavelength arrays must have one value per `sampling.wavelengthsM` entry; the file has one sampling grid (an element may override it).
* Outside the sampled wavelengths group delay continues with constant GDD and losses hold their end values; the results report the energy affected.
