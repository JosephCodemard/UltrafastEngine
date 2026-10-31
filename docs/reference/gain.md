# Gain media and amplifiers

Laser gain is modelled with a library of gain media, pumping with rate equations, the thermal lens, and two
elements: `gain` (one pass through a pumped crystal, 1-D or spatial) and `amplifier` (multipass or regenerative,
1-D). Both iterate the pulse train at the repetition rate until it reaches steady state.

```jsonc
{ "id": "rod", "type": "gain", "medium": "Yb:YAG", "lengthM": 0.004,
  "pump": { "kind": "cw", "powerW": 60, "wavelengthM": 940e-9, "radiusM": 0.6e-3, "profile": "superGaussian", "passes": 2 },
  "beam": { "kind": "gaussian", "w0M": 0.5e-3 },
  "transverse": { "model": "radial", "points": 48 },      // optional: spatial gain
  "thermalLens": { "rodRadiusM": 2e-3 } }
```

## Gain media

| name | lifetime | doping | emission peak | notes |
|---|---|---|---|---|
| `Yb:YAG` | 0.95 ms | 2 at.% (2.76e26 m⁻³) | 1030 nm, σe ≈ 2.1e-20 cm² | quasi-three-level (zero line 968.8 nm), pumped at 940 / 969 nm |
| `Ti:sapphire` | 3.2 µs | 3.3e25 m⁻³ | ≈ 795 nm, σe ≈ 3e-19 cm² | four-level, broadband, pumped at 490–532 nm |
| `Nd:YAG` | 230 µs | 1 at.% | 1064.2 nm, σe ≈ 2.8e-19 cm² | four-level, narrow line, pumped at 808 nm |

The cross sections are smooth fits to the published spectra (references in `provenance.json`), good to roughly
10–20 %; replace them with your own data when it matters. For quasi-three-level media the emission and absorption
spectra are linked by the **McCumber relation** σe/σa = exp((E_zl − hν)/kT), so each one also has the other's
reabsorption tail. At room temperature this makes an unpumped Yb:YAG crystal absorb at 1030 nm (see example 15).

Everything can be changed:

```jsonc
"medium": { "base": "Yb:YAG", "dopingDensityM3": 1.4e26, "temperatureK": 250 }       // overrides
"medium": { "name": "my medium", "lifetimeS": 1e-3, "dopingDensityM3": 1e26, "host": "YAG",
            "zeroLineWavelengthM": 975e-9,                       // omit for a four-level medium
            "emission":   { "kind": "gaussians", "bands": [ { "wavelengthM": 1.03e-6, "sigmaM2": 2e-24, "fwhmM": 8e-9 } ] },
            "absorption": { "kind": "table", "wavelengthsM": [ ... ], "values": [ ... ] },   // m^2
            "thermalConductivity": 10, "dnDT": 7.8e-6 }
```

Cross sections can also be an expression in `omega`/`lambda` or a callback (`"@name"`) through the usual model
machinery. A medium defined under `materials.NAME.gain` in the system file can be used by name. The **host**
(`"host"`, or the element's `"host"`) supplies the dispersion and n2.

## Pump

| key | meaning |
|---|---|
| `kind` | `cw` (diode, power on all the time), `pulsed` (e.g. a Q-switched green laser) or `none` |
| `powerW` / `energyJ` | cw power or pump-pulse energy |
| `wavelengthM`, `radiusM` | pump wavelength; 1/e² radius (Gaussian) or edge radius (top hat) |
| `profile` | `tophat` (default), `gaussian`, `superGaussian` (+ `order`) |
| `passes` | 2: the unabsorbed pump is sent back through the crystal |
| `delayS` | pulsed: how long before the seed the pump pulse arrives (the inversion decays meanwhile) |

Instead of a pump (or as a starting state), give the inversion directly: `"inversion": {"fraction": 0.3}`
(N2/N_t) or `"inversion": {"smallSignalGain": 2.5}` (single pass, at the carrier or `wavelengthM`).

**Rate equations.** Between pulses, each z slice (and each transverse point) follows
dN2/dt = (σap(N_t − N2) − σep·N2)·Φp − N2/τ, where Φp is the local pump photon flux. The flux is attenuated
slice by slice with the *current* populations, so pump bleaching and the reabsorption of quasi-three-level media
are included. The equation is integrated exactly (exponential update) within each time step. A pulsed pump is
deposited in 40 sub-steps (bleaching), then decays until the seed arrives.

## Amplification

The crystal is cut into `slices` (default 24) along z. In each slice:

1. half a step of host dispersion (and diffraction, in spatial models);
2. **gain** g(ω) = σe(ω)·N2 − σa(ω)·(N_t − N2) on every frequency bin, with the energy gain of the slice scaled
   to the **Frantz–Nodvik** value G = ln(1 + G0(e^{F/F_sat} − 1))·F_sat/F. This makes saturation exact for any
   slice thickness in the narrow-band limit, and it reduces to exp(g·dz) for weak pulses. N2 is depleted by one
   ion per net stimulated photon, so energy is conserved exactly. The energy budget books it as
   `"gain (from inversion)"`, a negative loss;
3. **time-ordered saturation**: the spectrum is split into `segments` groups of bins in arrival order (sorted by
   group delay). Each group depletes the inversion before the next one is amplified. For a chirped (stretched)
   pulse, time and frequency map onto each other, so the leading colours see more gain. This is the spectral
   reshaping and red shift of CPA amplifiers. `segments` is chosen automatically from the time–bandwidth product
   (1 for a transform-limited pulse, up to 24 for a stretched one);
4. SPM with the host n2 on the mode area (sub-stepped by B-integral);
5. the other half step.

In **1-D** the signal is one cell of area πw²/2, where w is the beam radius from `beam`, `modeRadiusM`, the
carried profile, or the pump radius. This is the on-axis fluence of a Gaussian beam. **Spatial** gain
(`transverse`) keeps a separate inversion and pump intensity at every transverse point. The beam diffracts, is
amplified point by point (gain guiding and gain-induced profile changes come out), and the thermal lens and any
`indexProfile` / `absorptionProfile` / `n2Scale` act as position-dependent terms.

## Steady state at the repetition rate

When the pulse has a `repRateHz` (or the element has `repRateHz`) and there is a pump, the element iterates the
pulse train: pump for one period → amplify → pump → … Each pulse is computed with a fast energy model (the
same saturated spectral gain, without phases). The iteration stops when three successive output energies agree to
`tolerance` (default 1e-5) or after `maxPulses`. The **last pulse** is then propagated with the full model from
the inversion it actually sees. A train that alternates between two energies (**period doubling**, typical of
regenerative amplifiers run near 1/τ) is detected and reported as a warning. Without a repetition rate, the run
is a single shot: cw pumping to equilibrium, or the pump pulse followed by `delayS` of decay. Turn the iteration
off with `"steadyState": false`, or tune it with `{"maxPulses": 400, "tolerance": 1e-5}`.

## ASE

`"ase": true` (with `aseSeed`) on a 1-D gain or amplifier element seeds amplified spontaneous emission. In every
slice, each frequency bin (one temporal mode) receives noise of mean energy ħω·σe(ω)·N2·dz in the signal's
spatial mode and polarisation. This is spontaneous emission into the mode, which is then amplified like the
signal. The seeded energy is booked as `ASE (spontaneous emission)` (negative) and reported as
`aseSeedEnergyJ`. Only the time window is simulated, so the ASE emitted between pulses is not counted.

## Thermal lens

The heat load is `heatFraction` × absorbed pump power (default: the quantum defect 1 − λp/λs). It is
distributed over the pump profile in each slice. The radial heat equation is solved for a rod of radius
`rodRadiusM` (default 3× the pump radius) held at the sink temperature. This gives ΔT(r, z) and
Δn = dn/dT·ΔT. The element reports `thermalLensFocalLengthM` (from the parabolic part near the axis),
`peakTemperatureRiseK` and `heatW`.

* spatial gain: Δn(r, z) is applied as a position-dependent index, so aberrations of a non-parabolic profile
  appear (M², Strehl ratio, wavefront sensor);
* 1-D gain: applied as a thin lens to a carried transverse profile (`"apply": false` to only report it);
* amplifier: reported only;
* the tracked Gaussian beam ([beam tracking](components.md#gaussian-beam-tracking), `"track": true`, the default):
  see below.

Check: top-hat pump f = 2πKw²/(P_h·dn/dT); Gaussian pump f = πKw²/(P_h·dn/dT) (tests).

**On the tracked beam.** Elements after a gain element take its thermal lens through the tracked q parameters. The
tracking happens when the system is compiled, before the pulse train has run, so the engine evaluates the same heat
model with the pump alone (cw pumping to equilibrium, or a pulsed pump repeated to steady state at the repetition rate)
and applies one thin lens per slice. Each slice's lens is the parabola fitted to its Δn(r) **with the beam's intensity as
weight** (the lens of least rms wavefront error over the beam), using the tracked beam radius at the element (or
`modeRadiusM` / `beam`). For a beam much smaller than the pump this is the on-axis lens; for a beam that fills the pumped
region it is weaker, since the Δn of a Gaussian pump flattens off the axis. The element reports `trackedThermalLensFocalLengthM`
(the sum of the slices) and a note with the on-axis value. If the run's own thermal lens, with the signal extracting energy,
differs from the pump-alone estimate by more than 5 %, a note says so. A measured value replaces the model:

```jsonc
"thermalLens": { "focalLengthM": 0.45 }                   // one thin lens at the centre: tracking and the 1-D thin lens
"thermalLens": { "rodRadiusM": 2e-3, "track": false }     // model, but leave the tracked beam alone
```

In a **multipass amplifier** the tracked beam takes the lens only on request, since the path between passes is otherwise unknown: `"thermalLens": {"track": true, "passGapM": 0.2}`
applies the lens once per pass with that free path between them. Regenerative amplifiers do not put it on the tracked beam.

**Stress birefringence.** `"thermalLens": {"stress": true}` adds the thermal birefringence of a uniformly heated rod of a cubic host (Koechner, *Solid-State Laser Engineering*, ch. 7;
built in for YAG hosts, `thermalExpansionPerK`, `stressCr`, `stressCphi` on a medium of your own). The radial and tangential polarisations differ by the retardation
δ = C (r/r₀)², C = n₀³ α C_B P_h / (λ K), C_B = C_r − C_φ. The depolarised fraction is the average of sin²(2φ) sin²(δ/2) over a Gaussian beam of radius w inside the rod. A beam filling the rod gives Koechner's D = (1 − sin C / C) / 4, tending to 1/4; a beam well inside it gives D = x² / (1 + x²) / 4 with x = C w² / (2 r₀²). The gain element rotates
that fraction of the field between x and y, which keeps the energy (the beam-averaged result, not the four-lobed pattern) and reports `depolarizationFraction`, `stressRetardationRad`, and the two lenses
`stressFocalLengthRadialM`, `stressFocalLengthTangentialM` (dn/dT, stress and end-face bulging). The formula assumes uniform heating over the rod: an end-pumped rod is only roughly described.

Checks (`BeamTrackingTests`): a small beam sees Innocenzi's f = πKw_p²/(P_h·dn/dT) (Appl. Phys. Lett. 56, 1831 (1990); 5 %); a
measured focal length focuses the tracked beam as a thin lens (waist radius 0.2 %); for a 0.5 mm beam in a rod pumped by a 0.6 mm
Gaussian, the tracked radius 0.4 m behind the rod is within 7 % of the radial spatial model (which also shows M² = 1.08 from the
lens's aberration, beyond a Gaussian beam parameter).

The element also records the lens as a Gradient-1 GRIN (z-averaged): `thermalGrinNr2PerM2`,
`thermalDnOnAxis`, `hostIndex`. `UltrafastEngine.Zemax thermal-lens` writes it into OpticStudio
([Zemax plugin](../plugins/zemax.md)).

**Repetition rate.** An element `repRateHz` overrides the pulse's (a pulse picker in front of the amplifier); the
output pulse carries the element's rate.

## The amplifier element

```jsonc
{ "id": "mp", "type": "amplifier", "geometry": "multipass", "passes": 8, "medium": "Yb:YAG", "lengthM": 0.006,
  "modeRadiusM": 0.45e-3, "pump": { ... },
  "betweenPasses": { "loss": 0.03, "gddFs2": 0, "todFs3": 0, "materials": [ { "name": "FusedSilica", "lengthM": 0.003 } ] } }

{ "id": "rg", "type": "amplifier", "geometry": "regenerative", "roundTrips": "auto",   // or a number
  "passesPerRoundTrip": 2, "maxRoundTrips": 300, "medium": "Ti:sapphire", "lengthM": 0.012, "pump": { ... },
  "cavity": { "lossPerRoundTrip": 0.06, "gddFs2": 0, "materials": [ ... ] } }
```

Passes alternate direction through the crystal. The materials between passes add their dispersion and SPM (on the
mode area). With `"roundTrips": "auto"` the pulse is ejected at the energy maximum. The intracavity energy history
is kept so you can see the build-up. The amplifier is 1-D; use a chain of `gain` elements and `gap`s for a
spatially resolved multipass.

## Results

Values (`summary.json → elements[i].values`): `smallSignalGain`, `gainPeakWavelengthM`, `storedEnergyJ`
(above transparency at the carrier), `extractedEnergyJ`, `extractionEfficiency`, `inversionFractionBefore/After`
(on axis), `saturationFluenceJPerM2`, `absorbedPumpEnergyJ`, `absorbedPumpPowerW`, `heatW`,
`thermalLensFocalLengthM`, `peakTemperatureRiseK`, `gain`, `averageOutputPowerW`, `segments`, `steadyStatePulses`,
`converged`, `periodDoubling`, `passes` / `roundTrips`.

Arrays (`result.npz`, `el_<id>_<name>`): `gainSpectrumWavelengthM` + `gainSpectrum`, `zM` + `inversionBefore` /
`inversionAfter`, `energyVsPulse` (the train), `energyPerPass` / `energyPerRoundTrip`, and for spatial gain
`cellXM`, `cellYM`, `inversionVsCellBefore/After`. `ultrafast-plot` draws them in `gain.png`.

## Validation

`GainTests` checks:

* Frantz–Nodvik saturated gain, to 3e-4;
* the small-signal gain specification;
* the cw rate-equation steady state;
* top-hat and Gaussian thermal-lens focal lengths;
* regen small-signal growth (G0²(1 − L))^N;
* auto ejection at the energy maximum, with the energy budget closing;
* the red shift of a red-leading chirp (time ordering);
* the pulse train against the full model;
* spatial gain with the thermal lens.

## Limitations

* Gain is scalar and polarisation independent; there is no spectral hole burning or excited-state absorption.
  ASE is 1-D only and in the signal polarisation.
* The thermal lens is radial (rod geometry); there is no thermal stress birefringence and no end bulging.
* Steady-state spatial trains use the input fluence profile for all pulses (no diffraction in the energy model).
* Cross sections are approximate (see above).
