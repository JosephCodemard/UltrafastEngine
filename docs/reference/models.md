# Models and overrides

Every physical ingredient is a **model** created from a small JSON spec. The same three forms work everywhere:

| form | example |
|---|---|
| data | `{"kind": "table", "wavelengthsM": [...], "n": [...]}`, or a plain number |
| expression | `{"kind": "expression", "expr": "1.45 + 0.004/lambda_um^2"}`, or a string `"1.45 + 0.004/lambda_um^2"` |
| callback | `{"kind": "callback", "name": "myIndex"}`, or the string `"@myIndex"` -- a function registered from C#, Python or C |

## Kinds per category
<span id="index"></span><span id="beam"></span>

| category (where used) | kinds |
|---|---|
| `index` (material `index`) | `constant`, `sellmeier` (B, C, A), `cauchy`, `table`, `expression`, `callback`, `ciddor` (air), `gas` (Borzsonyi 2008), `library` (a built-in by name), `refractiveindex` (a refractiveindex.info page: [Materials](materials.md#refractiveindexinfo-pages)) |
| `attenuation` (material, 1/m power) | `constant`, `table` (`alphaPerM` or `internalTransmission` + `thicknessM`), `expression`, `callback`, `refractiveindex` (from a page's k), `none` |
| `delayedResponse` (material `raman`) | `silica` (fR 0.18), `dampedOscillator` (tau1, tau2, fR), `expression` in t, `callback`, `none` |
| `timeResponse` (element / override) | `expression`, `callback`, `kerr` (extra n2), `twoPhotonAbsorption` (beta, m/W) |
| `filter` (filter elements, override `filter`) | `table`, `expression` (`transmission`, `phase`), `gaussian`, `superGaussian`, `constant`, `callback` |
| `beam` (bulk `beam`, `inputBeam`) | `gaussian` (w0M, zWaistM, m2, wavelengthM, n or zRayleighM; x/y variants), `sampled` (zM, radiusM / radiusXM, radiusYM), `constant` (radiusM or areaM2), `expression` in z, `callback` |
| `aperture` | `circular` (radiusM), `rectangular` (halfWidthXM, halfWidthYM), `none` |

`ultrafast info` prints the live list (including kinds you registered yourself).

### Expression variables

| category | variables |
|---|---|
| index, attenuation | `omega`, `lambda` (m), `lambda_um`, `lambda_nm`, `nu` (Hz) |
| filter | the above + `domega` (= omega - omega0), `omega0` |
| timeResponse | `t`, `t_lab`, `t_arrival`, `z`, `L`, `I`, `Ix`, `Iy`, `I_inst`, `E`, `omega0`, `lambda0`, `Aeff`, `x`, `y`, `r`, `theta` (position of the grid point on spatial models, 0 in 1-D), `spatial` (1 on spatial grids), `F` (running fluence ∫I dt, J/m²) |
| beam | `z` |
| delayedResponse | `t` |

Constants: `pi`, `e`, `c`, `eps0`, `mu0`, `h`, `hbar`, `kB`, `qe`, plus anything in `"params": {...}`.
Functions: `sin cos tan asin acos atan atan2 sinh cosh tanh sech exp log log10 sqrt abs sign floor ceil round
step pow hypot min max if clamp gauss`; operators `+ - * / ^ ** < <= > >= == != && || !`. Expressions are
compiled once and evaluated over whole arrays (one call per solver stage), so they are fast.

## The time-domain response n(t, I)

Nonlinear propagation adds an index change Delta n(t) = Delta n_re + i Delta n_im to the medium (positive
imaginary part = absorption). The built-in Kerr effect is one such response:
Delta n = n2 [(1 - fR) I + fR (h_R * I)(t)]. Your responses are **added** to it (`"mode": "add"`) or
**replace** it (`"mode": "replace"`, or `"replaceKerr": true` on the element). A response sees co-moving and lab
time, intensity (total and per polarisation), position in the element, the beam area and -- in carrier-resolved
mode -- the real field and instantaneous intensity. A model that varies on the optical-cycle scale declares
`"carrierResolved": true` and the element is then propagated with the real field ([Nonlinear physics](nonlinear.md)).

## Overrides

Overrides change any model or value **without editing the system file** -- e.g. to try another n2, swap a
coating, or add your own response to every nonlinear element:

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

Targets: `material:NAME`, `element:ID` (trailing `*` wildcard), `element:*`, `type:TYPE`, `global`.
Element properties: any numeric field (`lengthM`, `aoiDeg`, `n2`, `semiDiameterM`, ...), `material`,
`beam`, `coating`, `raman`, `selfSteepening`, `timeResponse`, `filter` (add mode appends a filter), `enabled`,
`nonlinear`, `dispersionSource` (`data` | `material`). Material properties: `index`, `n2`, `raman`,
`ramanFraction`, `attenuation`, `lidt`.

**Priority** (highest first): run-spec override > system-file override > data the system file carries for that
element (e.g. traced group delay) > material default > engine default. Overriding a material's index makes
bulk elements of that material use the material model instead of traced data (it is your explicit request).
Overrides that match nothing are reported as warnings.

## Provenance

Every resolved value is recorded in `provenance.json` (and `RunResult.Compiled.Provenance`):

```json
{ "target": "element:plate", "property": "n2", "value": "2.2E-20 m^2/W", "source": "RunOverride", "origin": "run spec overrides[0]" }
```
