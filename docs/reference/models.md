# Models and overrides

Every physical ingredient of a material or an element (refractive index, absorption, Raman response, extra
nonlinear responses, spectral filters, beam sizes, apertures, position-dependent fields) is a **model** built from a
small JSON spec. Overrides replace or add such models, and plain values, from the system file or the run spec without
editing the elements, and every value the compile resolves is recorded with its source. This page lists the model
kinds, the expression language, the override rules and the provenance report.

## Keys

The three forms work for every model:

| form | written as | is read as |
|---|---|---|
| data | `{"kind": "table", "wavelengthsM": [...], "n": [...]}`, or a plain number `1.45` | a number is `{"kind": "constant", "value": 1.45}` |
| expression | `{"kind": "expression", "expr": "1.45 + 0.004/lambda_um^2", "params": {...}}`, or the string `"1.45 + 0.004/lambda_um^2"` | a string not starting with `@` is an expression |
| callback | `{"kind": "callback", "name": "myIndex"}`, or the string `"@myIndex"` | a function registered from C#, Python or C under that name |

An object without `kind` is an expression if it has `expr`, a callback if it has `callback` (the name), and a constant
if it has `value`; otherwise it is an error. Kind names are case-insensitive. An unknown kind is an error that lists the
known kinds of its category. `ultrafast info` prints the kinds of the first seven categories of the table below,
including kinds that plugins register; it does not list the `field` category.

Model kinds with declared parameters (`sellmeier`, the Raman models, the beam and filter shapes, ...) have their keys
and defaults in the generated [options reference](options.md#model-kinds). One override rule is declared as
[`override`](options.md#override).

## Model

### Kinds per category
<span id="index"></span><span id="beam"></span>

| category (where it is used) | kinds |
|---|---|
| `index` (material `index`) | `constant` (`value`), `sellmeier` (`B`, `C`, `A` default 1), `cauchy` (`coefficients`), `table` (`wavelengthsM`, or `wavelengthsUm` / `wavelengthsNm`, with `n`), `expression`, `callback`, `ciddor` (air: `temperatureC` 20, `pressurePa` 101325, `humidity` 0, `co2ppm` 450), `gas` (Borzsonyi et al., Appl. Opt. 47, 4856 (2008): `species`, `pressureBar` 1, `temperatureK` 293.15), `library` (a built-in material by `name`), `refractiveindex` (a refractiveindex.info page: [Materials](materials.md#refractiveindexinfo-pages)) |
| `attenuation` (material; power attenuation α in 1/m) | `constant`, `table` (`alphaPerM`, or `internalTransmission` with `thicknessM`), `expression`, `callback`, `refractiveindex` (from the page's k), `none` |
| `delayedResponse` (material `raman`) | `silica` (alias `blowWood`), `linAgrawal`, `hollenbeck`, `dampedOscillator` (`tau1S`, `tau2S`), `orientational` (`riseS`, `decayS`), `expression` in `t`, `callback`, `none`. Shapes, default fR and anisotropic fractions: [Nonlinear physics: Raman models](nonlinear.md#raman-models); `fR` and `anisotropicFraction` go in the same object |
| `timeResponse` (element key or override) | `expression` (`expr` or `exprRe` for Δn_re, `exprIm` for Δn_im, `carrierResolved`), `callback`, `kerr` (an extra `n2`, m²/W), `twoPhotonAbsorption` (`beta`, m/W), `chi2` (`deffPmPerV`, optional `periodM` for quasi-phase matching and `n0`; carrier-resolved: [Crystals](crystals.md)) |
| `filter` (filter elements, instrument `filter`, override `filter`) | `table` (`transmission`, `phaseRad` or `groupDelayS`, `outside`: `zero` or `hold`), `expression` (`transmission`, `phase`; optional `omega0` or `centerWavelengthM`), `gaussian`, `superGaussian` (`order` 2), `constant` (`value` or `transmission`), `callback` |
| `beam` (bulk `beam`, `inputBeam`) | `gaussian` (`w0M`, `zWaistM`, `m2`, `wavelengthM`, `n` or `zRayleighM`; x / y variants), `sampled` (`zM`, `radiusM` or `radiusXM` / `radiusYM`), `constant` (`radiusM` or `areaM2`), `expression` in z (`radius`, or `radiusX` / `radiusY`), `callback` |
| `aperture` | `circular` (`radiusM`), `rectangular` (`halfWidthXM`, `halfWidthYM` default `halfWidthXM`), `none` |
| `field` (`indexProfile`, `absorptionProfile`, `n2Scale`, gain `dopingProfile`) | `constant`, `expression`, `gaussian`, `superGaussian`, `table`, `callback`: [Position-dependent properties](spatial-properties.md#field-kinds) |

### Expression variables

| category | variables |
|---|---|
| index, attenuation | `omega` (rad/s), `lambda` (m), `lambda_um`, `lambda_nm`, `nu` (Hz) |
| filter | `omega`, `lambda`, `lambda_um`, `lambda_nm`, `domega` (= omega − omega0), `omega0` (the spec's `omega0` or `centerWavelengthM`, else the run's carrier) |
| timeResponse | `t` (co-moving time, s), `t_lab` (lab time, s), `t_arrival` (s), `z` (m from the element start), `L` (element length, m), `I`, `Ix`, `Iy` (cycle-averaged intensity, W/m²), `I_inst` (instantaneous intensity; equal to `I` in the envelope model), `E` (real field in √W units, so that `I_inst` = E²/Aeff; 0 in the envelope model, undefined on spatial grids), `omega0`, `lambda0`, `Aeff` (m²), `x`, `y`, `r`, `theta` (position of the grid point on spatial models, 0 in 1-D), `spatial` (1 on spatial grids), `F` (running fluence ∫I dt, J/m²) |
| beam | `z` (m) |
| delayedResponse | `t` (s) |
| field | `x`, `y`, `z`, `r`, `theta` |

A name is looked up as a variable first, then in `"params": {...}`, then among the constants `pi`, `e`, `c`, `eps0`,
`mu0`, `h`, `hbar`, `kB`, `qe` (SI values). Names are case-sensitive (`e` is Euler's number, `E` the field).
Functions: `sin cos tan asin acos atan atan2 sinh cosh tanh sech exp log log10 sqrt abs sign floor ceil round step pow
hypot min max if clamp gauss`; `step(x)` is 1 for x ≥ 0, `if(c, a, b)` is a when c ≠ 0, `clamp(x, lo, hi)`, and
`gauss(x, s)` = exp(−x²/(2 s²)). Operators: `+ - * / ^ ** < <= > >= == != && || !`; `^` and `**` are right-associative,
and comparisons give 1 or 0. An expression is compiled once and evaluated over whole arrays (one call per solver
stage). With a GPU back-end the `expression`, `kerr` and `twoPhotonAbsorption` time responses are translated into a
device kernel, so the element stays on the device ([GPU](../plugins/gpu.md#what-runs-on-the-device)).

### The time-domain response
<span id="time-response"></span>

Nonlinear propagation adds an index change Δn(t) = Δn_re + i Δn_im to the medium (a positive imaginary part is
absorption). The built-in Kerr effect is one such response, Δn = n2 [(1 − fR) I + fR (h_R ∗ I)(t)] in the scalar case
(the vector form: [Nonlinear physics](nonlinear.md#envelope-model-default)). Further responses come from three places:

* the element's own `timeResponse` key: **added** to the Kerr effect;
* an override with `"mode": "add"` (on `element:...`, `type:...` or `global`): **added**;
* an override with `"mode": "replace"` (the default mode): **replaces** the Kerr effect and every added response of
  that element; an element rule wins over a `global` one. `"replaceKerr": true` on the element switches the built-in
  Kerr effect off and keeps the added responses.

`twoPhotonAbsorption` is Δn_im = β I c / (2 ω0), so that dI/dz = −β I². A response sees co-moving and lab time,
intensity (total and per polarisation), position in the element, the beam area and, in the carrier-resolved model, the
real field and the instantaneous intensity. A model that varies on the optical-cycle scale declares
`"carrierResolved": true` (a callback declares it when it is registered); with `physics.fieldMode` `auto` (the default)
the element is then propagated with the real field, with `envelope` it is not ([Nonlinear
physics](nonlinear.md#carrier-resolved-field-model)).

**Memory.** A response computed sample by sample from I(t) and F(t) needs no room in the window. A callback that
convolves the intensity with a kernel on the periodic window does: give the time its index change lasts after the
light as `"memoryS"` (s; for exp(−t/τ), 9.2 τ leaves 1e-4 of the weight), and the auto grid makes the window hold
it ([Concepts: grids](../concepts.md#grids)). The engine cannot find it from a callback.

```jsonc
{ "target": "element:cell", "property": "timeResponse", "mode": "add",
  "model": { "kind": "callback", "name": "slowKerr", "memoryS": 46e-12 } }
```

### Overrides

Overrides change a model or a value without editing the elements: another n2, another coating, an added response on
every nonlinear element. They are a list under `"overrides"` in the system file or in the run spec:

```jsonc
"overrides": [
  { "target": "material:FusedSilica", "property": "n2",    "value": 2.2e-20 },
  { "target": "material:N-BK7",       "property": "index", "model": { "kind": "table", ... } },
  { "target": "element:pass*",        "property": "timeResponse", "mode": "add", "model": "@plasma" },
  { "target": "element:m07",          "property": "coating", "value": { "kind": "gdd", "R": 0.998, "gddS2": -50e-30 } },
  { "target": "type:bulk",            "property": "beam",  "model": { "kind": "constant", "radiusM": 1e-4 } },
  { "target": "global",               "property": "timeResponse", "mode": "add", "model": { "kind": "twoPhotonAbsorption", "beta": 1e-13 } },
  { "target": "element:window",       "property": "enabled", "value": false }
]
```

* **Target.** `material:NAME`, `element:ID`, `type:TYPE` (every element of that type) or `global` (time responses
  only). `NAME` and `ID` may be `*` (all) or end in `*` (a prefix). Names, ids, types and properties match regardless
  of case.
* **Value.** `value` or `model` (the same thing, in any form of the table under [Keys](#keys)). `mode` is `replace`
  (default) or `add`; `add` exists for `timeResponse` and `filter` (an added filter acts after the element).
* **Element properties.** The keys an element's compiler reads through the override lookup: the numeric keys
  (`lengthM`, `aoiDeg`, `n2`, `semiDiameterM`, `focalLengthM`, `thicknessM`, ...), `material`, `beam`, `coating`,
  `raman`, `selfSteepening`, `timeResponse`, `filter`, `enabled`, `nonlinear`, `replaceKerr`, `transverse`,
  `ionization` and `dispersionSource` (`auto`, `data` or `material`). A rule on a key that the element does not read
  this way matches nothing, and says so.
* **Material properties.** `index`, `n2`, `kerrAnisotropy`, `raman` (with `fR` inside), `ramanFraction`,
  `rotationalRaman`, `attenuation`, `lidt`.
* **Priority** (highest first): run-spec override > system-file override > data the system file carries for that
  element (for example traced group delay) > material default > engine default. Between two rules of the same source
  the later one wins. A material `index` override makes bulk elements of that material use the material model instead
  of the traced group delay (`dispersionSource: "data"` keeps the data).
* **Unmatched rules** give a warning: `Override element:typo / n2 (...) matched nothing.`, or `material not used in this
  system` for a material rule.

### Provenance

Every resolved value is recorded in `provenance.json` (written with `outputs.json`) and in
`RunResult.Compiled.Provenance`:

```json
{ "target": "element:plate", "property": "n2", "value": "2.2E-20 m^2/W", "source": "RunOverride", "origin": "run spec overrides[0]" }
```

`source` is one of `EngineDefault`, `MaterialDefault`, `SystemData`, `SystemOverride`, `RunOverride`, `Api`. An entry
may also list the lower-priority candidates it shadowed (`shadowed`) and, for a catalogue value, its status and source
([Materials](materials.md#the-material-catalogue)).

## Validation

* Overrides: a run override beats a system-file override and is recorded as `RunOverride`; a rule on an unknown id
  gives a warning; without overrides the value is a `MaterialDefault` (`SystemTests.Overrides_PriorityAndProvenance`).
  A material index override replaces traced group-delay data (`SystemTests.Overrides_MaterialIndexOverrideBeatsTracedData`).
* Time responses: a callback in add mode and an expression `n2x*I` in replace mode both reproduce the built-in Kerr
  effect to 1e-9 of the peak field, and the provenance records the replacement
  (`PropagationTests.TimeResponse_CallbackAndExpressionReproduceBuiltInKerr`); a lab-time phase modulation gives
  Bessel sidebands J_n(m) (`PropagationTests.TimeResponse_LabTimeModulationCreatesBesselSidebands`); the position and
  fluence variables (`LaserTests.ExpressionResponse_SeesPositionAndFluence`).
* Expressions: precedence, functions, parameters and errors (`FoundationTests.Expression_PrecedenceFunctionsAndErrors`);
  the device kernels of `expression`, `twoPhotonAbsorption` and `kerr` against the CPU to 1e-9
  (`GpuTests.ExpressionResponses_CompiledToKernels_MatchTheCpu`).
* Field kinds: expression, table and callback give the same profile (`GainTests.FieldKinds_ExpressionTableAndCallback_Agree`).

## Limits

* No test compares `twoPhotonAbsorption` with the analytic plane-wave transmission 1 / (1 + β I0 L); it is checked
  only between the CPU and the device.
* `timeResponse` callbacks run on the CPU; with a GPU back-end an element that has one stays on the CPU.
* A replace-mode `timeResponse` override also removes the element's own added responses; to keep them, use `add` and
  `"replaceKerr": true`.
* A material override on a property that materials do not have is ignored without a warning.
