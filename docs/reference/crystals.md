# χ(2) crystals

A `crystal` element mixes the main beam with up to two other fields inside a nonlinear crystal: second-harmonic
generation (SHG), sum-frequency generation (SFG), optical parametric amplification (OPA) and difference-frequency
generation (DFG). It is an **active element** (see [Active elements](active-elements.md)): it follows one main beam
in, picks one field to continue as the main beam out, and keeps the other fields as by-products.

```jsonc
{ "id": "nopa", "type": "crystal",
  "crystal": "BBO",                         // built-in name or a user crystal (below)
  "lengthM": 2e-3,
  "interaction": { "process": "opa", "type": "I", "noncollinearDeg": "magic" },
  "cut": "auto",                            // phase-match at the nominal wavelengths, or {"thetaDeg":..,"phiDeg":..}
  "beam": { "kind": "gaussian", "w0M": 0.55e-3 },     // the main beam inside the crystal
  "aux": [ { "role": "pump", "from": "../08-shg-lbo/results",
             "beam": { "kind": "gaussian", "radiusM": 0.6e-3 }, "delayS": "auto" } ],
  "output": "signal",                       // which field continues as the main beam
  "record": ["idler", "pump"] }             // by-products to keep (default: all others)
```

Worked examples: [08 SHG in LBO](../examples/08-shg-lbo/README.md), [09 NOPCPA in BBO](../examples/09-nopcpa-bbo/README.md),
[10 DFG in MgO:PPLN](../examples/10-ppln-dfg/README.md).

## Processes and roles

| `process` | main beam | aux beam(s) | generated | fields (`output`, `record` names) |
|---|---|---|---|---|
| `shg` | fundamental | -- | SH | `fundamental`, `sh` (type II: `fundamental_slow`, `fundamental_fast`, `sh`) |
| `sfg` | one input (`a`) | the other (`role: "b"`) | sum | `a`, `b`, `sum` |
| `opa` | signal (default), idler or pump | pump (if main is signal/idler); signal or idler (if main is pump) | the third | `signal`, `idler`, `pump` |
| `dfg` | as `opa` | as `opa` | as `opa`; default `output` is `idler` | `signal`, `idler`, `pump` |

`mainRole` sets which role the main beam plays; if omitted it is inferred from the aux roles (an aux pump makes the
main beam the signal; an aux pump and signal make it the idler; otherwise it is the pump). Default `output`: `sh`,
`sum`, the main role (`opa`) or `idler` (`dfg`).

Signal and idler are named by frequency: in `opa`/`dfg`, `signal` is the field you name as signal (main beam or aux)
and the idler is ω_p − ω_s, whichever is higher in frequency.

## Built-in crystals

| name | class / point group | indices | d (pm/V) |
|---|---|---|---|
| `BBO` | uniaxial (negative), 3m | Tamosauskas et al. 2018 | d22 = 2.2, d31 = 0.04 |
| `LBO` | biaxial, mm2 (polar axis Y) | Kato 1994 | d31 = −0.67, d32 = 0.85, d33 = 0.04 |
| `KTP` (`PPKTP`) | biaxial, mm2 | Kato & Takaoka 2002 | d31 = 1.95, d32 = 3.9, d33 = 15.3 |
| `KDP` | uniaxial (negative), −42m | Zernike 1964 | d36 = 0.39 |
| `LiNbO3` (`PPLN`) | uniaxial (negative), 3m | o: Zelmon et al. 1997; e: Jundt 1997 (temperature dependent) | d33 = 25, d31 = 4.6, d22 = 2.1 |
| `MgO:LiNbO3` | uniaxial (negative), 3m | o: congruent LN (Zelmon); e: Gayer et al. 2008 (temperature dependent) | d33 = 25, d31 = 4.4, d22 = 2.1 |
| `BiBO` | biaxial, 2 (monoclinic) | Umemura et al. 2007 | not built in: give `deffPmPerV` |
| `AgGaSe2` | uniaxial (negative), −42m | Kato & Shirahata 1996 | d36 = 33 |

`ultrafast info` lists them; `provenance.json` of every run records the index source, the d tensor and the angle the
cut was solved for. Each crystal carries its Sellmeier validity range: using it outside adds a note. Exact values and
references are embedded in `src/UltrafastEngine.Core/Crystals/Crystal.cs`.

!!! note "Data choices"
    LBO uses Kato (1994), which reproduces the handbook XY-plane SHG angles (11.6° at 1064 nm, 13.8° at 1030 nm,
    31.7° at 800 nm); the Chen/Hanson–Dick fit gives angles up to 2.5° larger at short wavelengths.
    The MgO:LN temperature coefficient b4 = 1.516e-4 (as published by Gayer) is not cross-checked; pyNLO uses
    1.526e-5. It has no effect at the reference temperature 24.5 °C.

### User crystals

Anywhere a crystal name is accepted you can give an object, or define one under `materials` and use its name:

```jsonc
"materials": {
  "myBBO": { "crystal": { "base": "BBO", "deffPmPerV": 2.0 } },          // modify a built-in one
  "ideal": { "crystal": {
      "name": "dispersion-free test crystal",
      "no": { "kind": "constant", "value": 1.8 },                           // any index model (materials.md)
      "ne": { "kind": "sellmeier", "B": [...], "C": [...] },
      "pointGroup": "3m", "d": { "d22": 2.2, "d31": 0.04 },                 // or "deffPmPerV": ... directly
      "n2": 3e-20, "absorption": { "kind": "constant", "alphaPerM": 1.0 } } }
}
```

Biaxial crystals give `nx`, `ny`, `nz` (nx < ny < nz). Point groups: `3m`, `mm2` (polar axis Z), `mm2:y` (polar
axis Y, LBO convention), `-42m`, `general` (give any `dij`, Voigt notation). `deffPmPerV` on the element (or the
crystal) overrides the tensor contraction.

## Geometry: cut, type, planes, non-collinear

* **Eigenmodes.** For a propagation direction the two allowed polarisations are found from the index ellipsoid
  (uniaxial or biaxial alike), called `slow` (higher index) and `fast`. In a uniaxial crystal they are the
  ordinary and extraordinary waves (o = slow for negative crystals). The walk-off angle of each wave is computed.
* **`type`**: `I` (slow, slow → fast: e.g. ooe in BBO), `II`/`IIa` (slow, fast → fast), `IIb` (fast, slow → fast),
  `0` (all the same: QPM). Waves are ordered by frequency (lowest first). Default: `I`, or `0` if `qpm` is set.
  `interaction.polarizations` (`{"signal": "o", "idler": "o", "pump": "e"}`) sets them explicitly.
* **`cut`**: `"auto"` solves the phase-matching angle for the **nominal** wavelengths (the run pulse carrier and the
  aux carriers), in the best plane (`interaction.plane`: `XY`, `XZ`, `YZ` for biaxial crystals; found automatically
  by largest |deff| otherwise). `{"thetaDeg": .., "phiDeg": ..}` fixes it; `"detuneDeg"` (in `cut` or on the element)
  tilts it away -- useful for scans.
* **Non-collinear** (`opa`/`dfg`/`sfg`): `noncollinearDeg` is the angle between pump and signal inside the crystal;
  `"magic"` solves for the angle at which the idler's group velocity projected on the signal equals the signal's
  (broadest gain bandwidth). The cut is then the pump's angle to the optic axis (reported as `pumpThetaDeg`).
* **Main-beam polarisation.** The main beam's Jones vector is projected on the crystal's axis for its role
  (`polarizationAxisDeg` sets the angle of the crystal's slow axis in the lab x-y plane; default: aligned with the
  beam's dominant polarisation). Type-II SHG splits a single beam at 45°. The component on the other axis is
  propagated linearly (with that axis' index) and recombined.

## Quasi-phase matching

```jsonc
"interaction": { "process": "dfg",
  "qpm": { "periodM": 30.6e-6,             // omit to solve it for the nominal wavelengths
           "model": "averaged",            // or "domains"
           "phaseDeg": 0,                  // grating phase at z = 0 (domains model)
           "periodExpr": "30.6e-6 + 0.5e-6*z/L" } }   // chirped / fan-out gratings: period(z), L = length
```

* `averaged`: first-order QPM, d → (2/π) d with the grating phase K_G z subtracted from the mismatch. Fast.
* `domains`: the sign of d flips domain by domain (d ∝ sign cos(K_G z − phase)); the integrator lands exactly on
  every domain wall, so each step sees a smooth right-hand side. Captures higher QPM orders and end effects of
  the last domain; ~3-5x slower. `phaseDeg: 90` starts with a full positive domain (pyNLO's convention).

`qpm: true` with `cut: "auto"` uses θ = 90°, φ = 0 (propagation along X, polarisations along Z: d33 for LN/KTP).

## Beams and coupling

The main beam's size comes from `beam` (or a carried transverse profile, or the run's `inputBeam`). Each aux beam
has its own `beam`. Generated fields get 1/w² = Σ 1/w²(drivers).

**1-D model** (no `transverse`): the fields are coupled through an overlap factor `"overlap"`:

| `overlap` | coupling | use |
|---|---|---|
| `"gaussian"` (default) | exact overlap integral of the three Gaussian modes, O = (2/π)^{3/2}/(w1 w2 w3) · π/Σ(1/w²) | low to moderate conversion; Gaussian beams |
| `"planeWave"` / `{"model": "planeWave", "radiusM": r}` | plane wave at the on-axis intensity of a Gaussian of radius r (default: the highest-frequency input), O = √(2/(π r²)) | textbook estimates; matching pyNLO |
| `{"areaM2": A}` | one mode area for all fields, O = 1/√A | waveguides |

The 1-D overlap model is exact at low conversion. At high conversion the centre of a Gaussian beam converts more
than its wings; use a transverse model for that.

**Transverse models** (`"transverse": {"model": "radial"}` or `"cartesian"`, see [Spatial models](transverse.md)):
every field is resolved across the beam and coupled locally (O = 1), with diffraction of each field at its own
index, walk-off and non-collinear drift (cartesian), beam-quality changes and spatial depletion. Low-conversion
results agree with the 1-D Gaussian overlap to <0.5 %; at 60-70 % conversion they differ by 10-15 %.

## Equations

Amplitudes A_j (√W in 1-D, √(W/m²) in transverse models) on a common co-moving time grid, each field on its own
carrier ω_j (snapped to the grid so that ω3 = ω1 + ω2 exactly):

```
dA1/dz = L1 A1 + i ω1 Γ O A3 A2* e^{-iφ}
dA2/dz = L2 A2 + i ω2 Γ O A3 A1* e^{-iφ}
dA3/dz = L3 A3 + i ω3 Γ O A1 A2  e^{+iφ}          Γ = deff √(2 / (ε0 c³ n1 n2 n3)),  φ = Δk0 z − Φ_QPM(z)
SHG:  dAF/dz = LF AF + i ωF Γ O AH AF* e^{-iφ},  dAH/dz = LH AH + i ωF Γ O AF² e^{+iφ},  Γ = deff √(2/(ε0 c³ nF² nH))
L_j(ω) = i [kz_j(ω) − kz_j(ω_j) − (ω − ω_j)/v_ref] − α_j(ω)/2   (+ diffraction and drift in transverse models)
```

* Exact dispersion of every field (no Taylor expansion): group-velocity mismatch, GVD and higher orders, the
  frequency dependence of the phase mismatch (acceptance bandwidths) all follow from kz(ω).
* The ω factors are applied per frequency (self-steepening of the χ(2) terms) when `physics.selfSteepening` is on.
* χ(3): SPM and XPM of all fields (n2 of the crystal; `"chi3": false` switches it off; `"n2"` overrides).
* These equations conserve photon numbers exactly; `values.manleyRoweResidual` reports the spectrally resolved
  photon bookkeeping (typically 1e-8 or better).
* **Noise seeding**: `"seedNoise": true` adds one photon per mode (random phase, `noiseSeed`) to the generated
  field -- parametric superfluorescence / OPG from noise.

### Internal grid and spectral margin

The crystal works on its own grid: the window holds the main beam, the aux beams (with delays) and the group-delay
walk-off over the length; the time step resolves the widest of the input and generated spectra times
`"spectralMargin"` (default 2.5). The outer 10 % of the internal spectral window is an absorbing boundary, so content
that reaches it cannot alias. Energy absorbed there is booked as "numerical" in the energy budget; above 0.1 % the
engine warns ("raise spectralMargin"). Strong back-conversion with group-velocity mismatch broadens spectra a lot
(example 10 uses 5). The output field is resampled onto a grid planned for the rest of the system.

## Outputs

* `res.Records[i].Values` / `summary.json → elements[i].values`: `thetaDeg`, `phiDeg`, `noncollinearDeg`,
  `deffPmPerV` (incl. the 2/π for averaged QPM), `deltaK0PerM` (carrier mismatch before QPM), `qpmPeriodM`,
  `conversionEfficiency` (main out / all energy in), `energy_<field>_J`, `manleyRoweResidual`.
* By-products: `res.At["<id>#<field>"]`, snapshots `probe_<id>_<field>_*` in `result.npz`, summaries in
  `elements[i].byproducts`.
* Energy budget: `from <role> (aux)` (negative: energy brought in), `to <field>` (energy leaving as by-products),
  `numerical` or `absorption`.
* `carrierWavelengthM` changes when the output is a different field; a note says so, and the system dispersion
  starts afresh for the new field.

## Instruments for crystals

| kind | what it does |
|---|---|
| `conversion` | energies and photon numbers of every field leaving the crystal, aux energy in, efficiencies |
| `phaseMatchingCurve` | low-conversion sinc²(ΔkL/2) of the crystal's own geometry vs `scan`: `angle` (`rangeDeg`), `wavelength` (`rangeM`), `temperature` (`rangeK`, LN/MgO:LN), `period` (`rangeM`, QPM); reports peak and FWHM |
| `delayScan` | re-runs the crystal on the pulse that entered it for aux delays `fromS`..`toS` (`points`): the OPCPA timing curve |
| `beamProfile` | fluence vs radius (radial) or x/y cuts (cartesian), radius, M² -- after a transverse crystal |

See [Instruments](instruments.md) for the common options.

## Phase-matching calculator

The same solver is available on its own:

```bash
ultrafast crystal BBO --process shg --fundamental 1030e-9
ultrafast crystal BBO --process opa --pump 515e-9 --signal 800e-9 --nc magic --length 2e-3
ultrafast crystal MgO:LiNbO3 --process dfg --pump 1064e-9 --signal 1550e-9 --qpm --temperature 24.5 --json
```

```python
import ultrafast as uf
r = uf.crystal.phase_match("LBO", "shg", fundamental=1030e-9)
r["best"]["phiDeg"], r["best"]["deffPmPerV"], r["best"]["acceptance"]
```

C API: `uf_crystal_phase_match(request_json, &result_json)`. The result lists all solutions (planes, types) and for
the best one: angles, deff, indices, group indices and walk-off of every wave, GVM, acceptance bandwidths (spectral,
angular, thermal; FWHM for the given length), the small-signal OPA gain spectrum (if `pumpIntensityWPerM2` is
given), the magic angle, and the QPM period.

## Validation

| check | result | test |
|---|---|---|
| SHG vs Armstrong tanh² (dispersion-free crystal, plane wave) | < 2e-4 from 0.02 % to 85 % conversion | `Chi2Tests.Shg_PlaneWave_MatchesArmstrongTanhSquared` |
| OPA vs cosh² small-signal gain | < 1e-3 at gain 34 | `Chi2Tests.Opa_SmallSignalGain_*` |
| angle detuning vs sinc² | < 1 % | `Chi2Tests.Shg_AngleDetuning_FollowsSincSquared` |
| cascaded χ(2) phase vs κ²P/Δk (L − sin ΔkL/Δk) | < 2 % | `Chi2Tests.CascadedChi2_*` |
| QPM averaged vs domain-resolved | < 2 % | `Chi2Tests.Qpm_AveragedAndDomainResolvedModelsAgree` |
| calculator vs handbook angles (BBO, LBO, KDP, KTP, NC magic angle, PPLN periods) | < 0.15-0.8° | `Chi2Tests.PhaseMatchCalculator_*` |
| radial vs 1-D Gaussian overlap, low conversion | < 1 % | `Chi2Tests.Shg_RadialModelAgrees*` |
| DFG in MgO:PPLN vs pyNLO | 0.1-0.7 % (small signal), 0.5 % at gain 50 | `python/tests/test_pynlo_dfg.py` |
