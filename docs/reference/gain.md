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

The numbers of these media (lifetime, doping, bands, thermal and stress constants) are catalogue files (`Data/materials/gain`), each with its source and status (all are `approximate` or `unverified` so far; see [Material catalogue](materials-catalogue.md#gain-media)). A run that uses one says so in its notes and in `summary.json` (`unverifiedInputs`); thermal data are reported only with a thermal lens, stress constants only with `stress`. `physics.strictMaterials` refuses such a run ([strict mode](materials.md#strict-mode)). The cross sections are smooth fits to the published spectra (references in `provenance.json`), good to roughly
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

Two models, which can be used together.

**Noise in the pulse window** (`"ase": true`, `aseSeed`; 1-D only). In every slice each frequency bin (one temporal mode) receives noise of mean energy
ħω·σe(ω)·N2·dz in the signal's spatial mode and polarisation, which is then amplified like the signal. The seeded energy is booked as
`ASE (spontaneous emission)` (negative) and reported as `aseSeedEnergyJ`. Only the time window is simulated.

**ASE over the inversion lifetime** (`"aseModel": true` or an object; gain and amplifier elements). Between pulses (milliseconds) the ASE crosses the crystal in
picoseconds, so it is solved at frozen N2 and then depletes N2 in the rate equations (`PumpCw`, the decay between pulses, the steady state of the pulse train, a single shot).
For every transverse cell and every frequency ν of the emission band, the intensity I (W/m²/Hz) of the forward (from z = 0) and backward (from z = L) ASE obeys

`dI/dz = ±γ I + p h ν σe N2 q`, `γ = σe N2 − σa (N_t − N2)`, `q = n² Ω / λ0²`

with p the number of polarisations (default 2) and Ω the solid angle of the rays that matter. The slice step is exact for constant γ. The stimulated photons of each slice
(output − input − source) are taken from N2, so the photon balance is exact; the spontaneous part is already in the lifetime 1/τ and is not subtracted twice. Keys of `aseModel`:

| key | meaning | default |
|---|---|---|
| `solidAngleSr` | effective solid angle inside the medium of the ASE that depletes the inversion | min(π r_pump² / L², escape cone 2π(1 − √(1 − 1/n²))) |
| `polarizations` | 1 or 2 | 2 |
| `points` | frequencies over the emission band (where σe > 10⁻³ of its maximum) | 40 |
| `bandLowerM`, `bandUpperM` | restrict the band | automatic |

What it reports (values of the element; they are *not* in the pulse's energy ledger, which stays closed): `aseForwardPowerW`, `aseBackwardPowerW` (ASE leaving the signal
mode, one spatial mode of area π w²/2, at the state the next pulse sees), the spectra `aseForwardSpectrumWPerHz`, `aseBackwardSpectrumWPerHz` on `aseWavelengthM`,
`aseStimulatedPowerW` (the power the ASE takes from the inversion), `aseDepletionFraction` (that over the absorbed pump power), `aseSolidAngleSr`, and, with a repetition rate,
`asePedestalEnergyJ` and `asePedestalEnergyFraction`: the ASE energy in the signal mode per pulse period relative to the output pulse. That fraction is the strength to
give a `pulse.pedestal` of kind `ase` in the next stage (`energyFraction`).

Checks (`AseStressTests`): uniform inversion gives n_sp h ν Δν (G − 1) per polarisation at both ends (1e-12; Desurvire, *Erbium-doped fiber amplifiers*, 1994; the factor 2 for both
polarisations); a non-uniform inversion against a direct RK4 integration (1e-6); the source into 4π equals N2 ⟨hν⟩ / τ_rad by the Füchtbauer-Ladenburg relation (1e-9; fixes the
etendue factor q); the slice step integrates the homogeneously saturated law ln(G/G0) + (G − 1) P_in/P_sat = 0 (1e-4); in the cw steady state absorbed pump photons = N2 V / τ + stimulated
ASE photons (0.5 %); no solid angle gives no depletion and more angle gives more.

**Limits (stated, not hidden).**
* The depletion uses one *effective* solid angle for all rays. Real ASE in a bulk crystal comes from all directions and the exact depletion depends on the geometry: the default is a
  physical estimate (rays that stay in the pump region over the length L, capped by the escape cone), not a ray trace. Give `solidAngleSr` when the geometry is known. For a fibre use
  Ω = λ0² / (n² A) (one mode).
* **Transversely resolved ("3-D") ASE is not in this package.** On a transverse grid (`transverse`) every cell is its own column with its own inversion, so the depletion is local and follows the
  pump; but the rays do not cross from cell to cell, and there are no reflections at the faces (no parasitic lasing, no transverse ASE in a large-aperture disk). A true 3-D model needs
  oblique rays through the whole crystal (a Monte Carlo or ray-tiling model, O(cells² × slices)) and a reference for it (see `docs/design/next-steps.md`).
* The signal polarisation is not special: the same ASE depletes the inversion for both polarisations. ASE in the *pulse's* polarisation state is the input of the pedestal.
* Cost: cells × slices × points exponentials per evaluation, once per pumping step. A 1-D element costs milliseconds; a 64 × 64 cell grid costs seconds.

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
model with the pump alone (cw pumping to equilibrium, or a pulsed pump repeated to steady state at the repetition rate).
The pumped region is a **distributed lens**: each slice of the medium is a graded-index duct, n(r) = n₀ (1 − g² r²/2),
with the exact reduced ABCD matrix A = D = cos gL, B = sin(gL)/(n₀ g), C = −n₀ g sin gL (g² < 0 gives cosh / sinh for a
defocusing host), not a thin lens at one end. The power of each slice comes from the parabola fitted to its Δn(r)
**with the beam's intensity as weight** (the lens of least rms wavefront error over the beam), using the tracked beam radius
at the element (or `modeRadiusM` / `beam`): g² = (1/f_j)/(n₀ Δz). For a beam much smaller than the pump this is the on-axis
lens; for a beam that fills the pumped region it is weaker, since the Δn of a Gaussian pump flattens off the axis. The
change against slices of thin lenses is of order (gL)²: 0.4 % for the 4 mm rod of the examples, and it matters for long
media (gL = 0.6 changes the beam radius behind a 10 cm rod by 1 %).

The element reports two focal lengths, both with the unit in the key: `trackedThermalLensFocalLengthM`, the
**thin-lens equivalent** 1/f = n₀ g² L summed over the slices, and `trackedThermalLensEffectiveFocalLengthM`, the
**effective** focal length −1/C of the whole duct, which is what a measurement from outside the medium gives
(f_eff = f sin(gL)/(gL) for a uniform duct). A note gives the on-axis value of the pump alone. If the run's own thermal lens,
with the signal extracting energy, differs from the pump-alone estimate by more than 5 %, a note says so. A measured value
replaces the model and is the **effective** focal length: the engine finds the uniform duct that has it
(a focal length shorter than a quarter pitch of the medium can give, gL = π/2, is capped with a note):

```jsonc
"thermalLens": { "focalLengthM": 0.45 }                   // measured effective focal length: tracking (a uniform duct) and the 1-D thin lens
"thermalLens": { "rodRadiusM": 2e-3, "track": false }     // model, but leave the tracked beam alone
```

In a **multipass amplifier** the tracked beam takes the lens only on request, since the path between passes is otherwise unknown: `"thermalLens": {"track": true, "passGapM": 0.2}`
applies the lens once per pass with that free path between them. A **regenerative amplifier** takes it the same way, with an
integer `roundTrips` (`"auto"` is decided at run time, after the beam is tracked, so it is not tracked and a note says so):
`roundTrips x passesPerRoundTrip` passes through the duct, `passGapM` between them. The path is open: the cavity's own focusing optics are not part of the tracked beam, so
for a stable resonator give the gap of the equivalent unfolded path or use the resonator element. Both amplifier kinds
report the two focal lengths above.

What the Gaussian tracker leaves out is the aberrated part of the lens (the spherical aberration of the non-parabolic heat profile)
and gain narrowing; both need the radial spatial model (`transverse`), which has the full Δn(r, z). Against it, a 0.5 mm beam in a 60 W rod
pumped by a 0.6 mm Gaussian: the radius 0.4 m behind the rod differs by 5 % (the lens is aberrated, M² = 1.08), the radius at the exit of the rod by 9 % (gain
narrowing, which the tracker does not model); a 0.2 mm beam, which probes the aberrated centre of the lens, by 23 %.

**Stress birefringence.** `"thermalLens": {"stress": true}` adds the thermal birefringence of a uniformly heated rod of a cubic host (Koechner, *Solid-State Laser Engineering*, ch. 7;
built in for YAG hosts, `thermalExpansionPerK`, `stressCr`, `stressCphi` on a medium of your own). The radial and tangential polarisations differ by the retardation
δ = C (r/r₀)², C = n₀³ α C_B P_h / (λ K), C_B = C_r − C_φ. The depolarised fraction is the average of sin²(2φ) sin²(δ/2) over a Gaussian beam of radius w inside the rod. A beam filling the rod gives Koechner's D = (1 − sin C / C) / 4, tending to 1/4; a beam well inside it gives D = x² / (1 + x²) / 4 with x = C w² / (2 r₀²). The gain element rotates
that fraction of the field between x and y, which keeps the energy (the beam-averaged result) and reports `depolarizationFraction`, `stressRetardationRad`, and the two lenses
`stressFocalLengthRadialM`, `stressFocalLengthTangentialM` (dn/dT, stress and end-face bulging). The formula assumes uniform heating over the rod: an end-pumped rod is only roughly described.

*The four-lobed pattern.* On a **cartesian** transverse grid (`ny > 1`) with an x-polarised input the gain element applies, at every point (x, y), the Jones matrix of a
rod whose eigen-polarisations are the radial and the tangential ones: R(−φ) diag(1, e^{iδ}) R(φ), δ = C min(r, r₀)² / r₀² (the tangential one is delayed for C > 0). An x-polarised beam then leaks
|E|² sin²(2φ) sin²(δ/2) into y: no light on the axes, four lobes on the diagonals with alternating sign. The profile the beam carries afterwards has its own y shape (`DataY`), so a following
spatial element propagates the vector field. The matrix is unitary (energy kept to 1e-13) and the beam average equals the lumped fraction `D = (1 − ⟨cos δ⟩)/4` to 1e-6 on the grid (test). The element
reports `depolarizationFractionPattern` beside `depolarizationFraction`; the pattern sees the beam at the exit of the rod, the lumped value the beam at the middle, so they differ by the change of the
radius in the thermal lens (D grows as w⁴ for a small beam). Limits: the radial grid (no 2-D map; the lumped rotation is used), a y component at the input (the spatial gain is scalar; a note says so and the lumped
form is used), and the common part of the radial and tangential lenses (`stressFocalLength*`) is reported but not applied to the field; only the polarisation-dependent phase difference is.
The pattern is applied once at the exit, after the gain, and ignores the growth of the retardation along z inside the rod.

Checks (`BeamTrackingTests`): a small beam sees Innocenzi's f = πKw_p²/(P_h·dn/dT) (Appl. Phys. Lett. 56, 1831 (1990); 5 %); the duct matrix is
cos(gL), sin(gL)/(n₀g), −n₀g sin(gL) (1e-12), with the thin-lens limit and the hyperbolic case; N thin slices converge to the duct as 1/N²;
a measured effective focal length focuses the tracked beam like the exact duct (1e-9) and the thin lens at the centre of a 10 cm rod would be 1 % off; a
measured focal length focuses a thin rod like a thin lens (waist radius 0.2 %); N round trips of a regenerative amplifier equal N x passesPerRoundTrip passes of a multipass amplifier (1e-12); for a 0.5 mm beam in a rod pumped by a 0.6 mm
Gaussian, the tracked radius 0.4 m behind the rod is within 7 % of the radial spatial model (which also shows M² = 1.08 from the
lens's aberration, beyond a Gaussian beam parameter).
