# Reference audit C (WP C1c)

Pages: `docs/reference/` crystals.md, materials.md, coatings.md, components.md, active-elements.md, modulator.md, spatial-properties.md.
Date: 2026-11-25. Read-only audit; no page was edited.

Method: every factual claim was checked against `src/UltrafastEngine.Core` (`Schema/BuiltInElements.cs`, `BuiltInOptions.cs`, `Crystals/`,
`Materials/`, `Data/materials/**/*.json` (the catalogue source, not the generated page), `Optics/`, `Physics/Interfaces.cs`, `Models/SpatialFields.cs`,
`Engine/Compiler*.cs`, `Engine/Active.cs`), the Python package and `tests/UltrafastEngine.Tests`. Test names and quoted tolerances were looked up with grep.
Where a number was recomputed (GDD of a tilted plate, BBO walk-off length, Gaussian overlap, grating angle, prism angle, substrate echo delays, ghost delay)
it is stated. Items that agree with the code are not listed. Severity: wrong (contradicts the code), stale (was true, no longer is), unclear (ambiguous or inconsistent
between page, schema and code), missing (important behaviour not on the page).

## crystals.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :57 "Exact values and references are embedded in `src/UltrafastEngine.Core/Crystals/Crystal.cs`" | The indices, d tensors, n2 and validity ranges are catalogue JSON files, one per crystal. `Crystal.cs` holds only the point-group tensor code and the loader. | `Data/materials/crystals/{BBO,LBO,KTP,KDP,LiNbO3,MgOLiNbO3,BiBO,AgGaSe2}.json`; `Crystals/Crystal.cs:208-242, 270-292`. The page itself says so at :42 (self-contradiction). | stale |
| 2 | :81-82 "Biaxial crystals give `nx`, `ny`, `nz`" (user crystal, no other key) | A user crystal starts as `OpticalClass = "uniaxial"`; only the key `"class": "biaxial"` changes it. Without it `nx/ny/nz` are read but the crystal is treated as uniaxial (plane `"uniaxial"`, o/e roles, Ny ignored). `"class"` is not mentioned anywhere on the page. | `Crystals/Crystal.cs:301-303, 119`; `CrystalElement.cs:927`; `PhaseMatching.cs:251` | wrong (missing key that makes the example work) |
| 3 | :37-38 "`signal` is the field you name as signal ... and the idler is ω_p − ω_s, whichever is higher in frequency" | Roles are fixed by the names (signal, idler, pump); idler frequency = ω_p − ω_s always. "Whichever is higher" only decides the internal wave order (lowest frequency first, `ws <= wi`), not the names. | `CrystalElement.cs:137-146` | unclear |
| 4 | :165-166 `"seedNoise": true` adds one photon per mode (no default given) | Code default is `false`. The schema declaration says default `"true"`, so the generated `options.md` and the JSON schema state the opposite of the behaviour. | `CrystalElement.cs:773` (`GetBool("seedNoise", false)`); `Schema/BuiltInElements.cs:269` (default "true") | unclear (declaration wrong; page silent on the default) |
| 5 | :162 "`chi3`: false switches it off" | `chi3` is true only if the key is not false **and** `physics.nonlinear` is on; `physics.nonlinear: false` also removes SPM/XPM of the crystal. Not stated. | `CrystalElement.cs:777` | missing |
| 6 | whole page: key list | Element keys accepted but never documented here: `temperatureC` (needed for LiNbO3 / MgO:LiNbO3 e-ray, default 20 degC, ignored with a note for the other crystals), `ionization` (Keldysh/MPI per field, needs catalogue band gap or `bandGapEv`), `ghosts`, `inputs` (virtual beams, only in the NOPA section), `n2`. | `Schema/BuiltInElements.cs:256-271`; `Crystals/Crystal.cs:287-290`; `CrystalElement.cs:777-790` | missing |
| 7 | :144 "Low-conversion results agree with the 1-D Gaussian overlap to <0.5 %" vs :231 "< 1 %" | Same test (`Shg_RadialModelAgreesWithGaussianOverlapAtLowConversion`) asserts 1 %. | `tests/.../Chi2Tests.cs:364-366` | unclear (two numbers for one check) |
| 8 | :230 "calculator vs handbook angles ... < 0.15-0.8°" | Asserted tolerances are 0.15 / 0.2 / 0.3 / 0.8 degrees, but the test's `Tolerance` attribute (which feeds the validation matrix) says "1 %, 5 %, 10 %" and the reference says only "SNLO" for BBO, not the KDP/LBO/KTP handbook values. | `Chi2Tests.cs:209-236` | unclear (test metadata disagrees with page) |
| 9 | :94-95 `interaction.plane` "found automatically by largest \|deff\|" and :123 "`qpm: true` with `cut: "auto"` uses θ = 90°, φ = 0" | Matches `CrystalElement.cs:917`; but `"plane"` default for a uniaxial crystal is `"uniaxial"` and for a biaxial fallback is `"XZ"` (`:927`), so "largest \|deff\|" is not the only rule. | `CrystalElement.cs:917, 927` | unclear |
| 10 | :194 `phaseMatchingCurve` scan `temperature` "(`rangeK`, LN/MgO:LN)" | Code comment says LiNbO3; also the `angle` scan moves φ (not θ) in the XY plane. Page does not say which angle is scanned. | `Instruments/ActiveInstruments.cs:74-96` | unclear |
| 11 | :116-121 domain-resolved QPM: "A step that would end within 10 % of a step before a wall is stretched onto it", "3e-8", "4e-4", "4e-6" | The numbers are quoted from a test; the test's reference text says "1e-7 between tolerances 1e-7 apart"; the 3e-8 / 4e-4 / 4e-6 figures were not found in a test or script. | `Chi2Tests.cs:159-182` | unclear (unverifiable numbers) |

Verified OK (not rows): d tensors and point groups, index sources and verified/unverified split of the catalogue, SHG / OPA / DFG equations and Γ, Gaussian and plane-wave overlap
formulas, spectral margin default 2.5 and the 10 % absorbing edge and 0.1 % warning, type table, type default, output defaults, mainRole inference, QPM 2/π, validation test names and
tolerances (2e-4, 1e-3, 1 %, 2 %, 2 %), walk-off length 0.3 mm (recomputed: 95 fs/mm from the BBO catalogue gives 0.32 mm), CLI/Python/C API names, NOPA tests.

Verdict: physics and numbers are sound; two real defects (user biaxial crystals need an undocumented `class` key, source location stale) and a handful of undocumented keys and an unreliable `seedNoise` default in the declaration.

## materials.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :12 `Air` n2 "1.0e-23 x density" | The Air catalogue entry has `n2Electronic` = 8.5e-24 m²/W per bar at 273 K (the page says 8.5e-24 itself at :20 and :36). 1.0e-23 is the Ar value. | `Data/materials/gases/Air.json` (`n2Electronic.valueM2PerWPerBar273K`); `MaterialLibrary.cs:77` | wrong |
| 2 | :22 "`N2`, `O2`, `CO2`, `H2` ... Borzsonyi / Peck / Bideau-Mehu" | N2: Borzsonyi 2008; H2: Peck and Huang 1977; CO2: Bideau-Mehu 1973; **O2: Zhang et al. 2008 (not named)**. | `Data/materials/gases/{N2,H2,CO2,O2}.json` `index.source` | wrong (O2 source missing) |
| 3 | :23 `Quartz`, `MgF2-e`, `Sapphire-e`, `Calcite`, `YVO4`, `aBBO` ... n2 "typical" | n2 is typical (3e-20, approximate) for Quartz, Sapphire-e and MgF2-e only. `Calcite`, `Calcite-e`, `YVO4`, `YVO4-e`, `aBBO`, `aBBO-e` have n2 = 0 (status unverified): a nonlinear run through them has no Kerr effect. | `Data/materials/solids/{Calcite,Calcite-e,YVO4,YVO4-e,aBBO,aBBO-e}.json` `n2.valueM2PerW = 0.0` | wrong |
| 4 | :23 same row, index "Sellmeier" | YVO4 and aBBO indices are `vendorSellmeier`, status **unverified**, vendor not recorded; Calcite and Quartz are Ghosh 1999 (verified). The page gives no status, so a user reads all as sourced. | `Data/materials/solids/YVO4*.json`, `aBBO*.json` `index.source/status` | missing |
| 5 | :25 `Ta2O5`, `TiO2`, `HfO2`, `Nb2O5`, `Al2O3_film`, `SiO2_film` "Cauchy fits (thin films)" | Also n2 = 0 (unverified), so no nonlinearity in films; index status `approximate`, source not recorded. | `Data/materials/solids/*_film.json`, `Ta2O5.json` ... | missing |
| 6 | :27 "The nonlinear coefficients are typical values ... (status `approximate`)" | Not all: CS2 n2, BaF2 n2 and kerrAnisotropy, and the CS2 `ramanFraction` are `unverified`; the zero n2 values are `unverified`. | `Data/materials/solids/{CS2,BaF2}.json` | unclear |
| 7 | :143-144 lookup order "file, bundled page, user library (`$ULTRAFAST_HOME/materials`, default `~/.ultrafast/materials`), `ULTRAFAST_RII_PATH`" | Order is as stated, but the user folder is `Commands.UserMaterialsFolder()`; `ULTRAFAST_HOME` is documented for plugins at `Plugins.cs:239`. The catalogue's user folder is a different variable (`ULTRAFAST_MATERIALS`, :77 of the page). Two env variables for two "user material" folders is not explained. | `Materials/RefractiveIndexInfo.cs:255-262`; `Materials/Catalogue.cs:112-122` | unclear |
| 8 | :5 aliases "`F_SILICA`, `BK7`, `CAF2`, `SAPPHIRE`" | True. One catalogue defect: `Air.json` lists an empty-string alias `""`. | `Data/materials/gases/Air.json` `aliases` | unclear (data glitch) |
| 9 | :89-94 `gain/` and `crystals/` quantities, `defaults:gainMedium`, `defaults:solidIonization` | Present (`Data/materials/defaults/*.json`). The page never says that `crystals/` entries carry `aliases` (`ppln`, `ppktp`, `mgln`) and `temperatureDependent`. | `Data/materials/crystals/*.json` | missing (minor) |

Verified OK (not rows): FusedSilica Blow-Wood 12.2 / 32 fs, fR 0.18; n2 values of FS, BK7, CaF2, Sapphire, YAG, SF11, MgF2; gas n2 per bar; Ciddor defaults (20 degC, 101325 Pa, 0 % RH, 450 ppm);
CS2 formula and n(589.3 nm) = 1.6276 (recomputed), orientational constants; strict-materials keys and env variables; `unverifiedInputs` and `unverified_inputs`; catalogue key names.

Verdict: the catalogue mechanics are accurate; the summary table has one wrong number (Air n2), one missing source (O2) and a misleading "typical n2" for six birefringent crystals whose n2 is 0.

## coatings.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :15-24 coating kind table | A ninth kind exists: `gddRipple` (alias `chirped`): `R` (0.998), `gddS2`/`gddFs2`, `todS3`/`todFs3`, `rippleGddS2`/`rippleGddFs2`, `ripplePeriodNm`/`ripplePeriodThz`/`ripplePeriodRadPerS`, `ripplePhaseRad`, `reflectanceRipple`. `none` also accepts `bare`. components.md :298 mentions `gddRipple`, this page does not. | `Physics/Interfaces.cs:211-232, 168-171` | missing |
| 2 | :19-22 defaults not stated | `quarterWave`: `H` defaults to Ta2O5, `L` to SiO2_film, `M` has no default; `ar`: `R` default 0.0025; `gdd`: `R` default 0.999; `ideal`: `R` default 1.0, `T` default 1 − R, `phaseRad` applies to the **reflection only** (transmission phase 0). | `Interfaces.cs:177-205` | missing |
| 3 | :22 `gdd` coating | Phase is φ = gddS2 Δω²/2 + todS3 Δω³/6 + fodS4 Δω⁴/24 added to reflected amplitude as exp(+iφ), Δω from `referenceWavelengthM` (default carrier); T = 1 − R. Not stated on the page; users cannot tell the sign convention for a chirped mirror. | `Interfaces.cs:205-217` | missing |
| 4 | :23 `table` | Wavelengths may be given as `wavelengthsM`, `wavelengthsNm` or `wavelengthsUm`; amplitude is linearly interpolated and held at the end values, **phase is a natural cubic spline with linear extrapolation**; `T` defaults to 1 − R; phase from `phaseRs` etc. or integrated from `groupDelay*S`. | `Interfaces.cs:219-258, 300-320` | missing |
| 5 | :24 `efficiency` "fields `s`, `p`" | Also accepts a single `efficiency` for both; default 1; amplitude only (phase 0). | `Interfaces.cs:274-281` | missing (minor) |
| 6 | :26 "All kinds take an optional `lidt`" | True; the `lidt` keys are never listed: `fluenceJPerM2` or `fluenceJPerCm2`, `pulseDurationS` (default 10 ns), `scalingExponent` (default 0.5), `wavelengthM`, `notes`. Only the Cm2 form appears in the example. | `Materials/Material.cs:21-35` | missing |
| 7 | :54 `delayedScatter` example | Only `kind: uniform` shown. Code has `exponential` (`tauS`, `maxS` default 8 tauS) and `table` (`delaysS`, `weights`); `spectralExponent` default is 0 (example uses 4); on a mirror/interface (not a plate) `delay.maxS` is **required** ("no default delay for this element"). Page says only "on a plate the default delay is ...". | `Engine/CompilerSubstrate.cs:105-117, 202` | missing |
| 8 | :61-71 loss mechanisms table | List is incomplete: the executor books also `dispersion` (8 sites), `ghost`, `grid boundary`, `user`, `oscillator (new beam)`, `gain (from inversion)`, `ASE (spontaneous emission)`, `saturable absorption`, `ndFilter (reflected)`, `ionisation and plasma absorption`. `grating` is booked only when the interface mode is `diffract`; `fresnel` only without a coating. | `Physics/Interfaces.cs:357`; `Engine/Executor.cs:357,835`; `Transverse/SpatialPropagation.cs:563`; grep of `ctx.Energy(` | missing |
| 9 | :74 "together they close exactly on input - output energy" | Only if gain/aux energy entries (negative) are included; stated without that caveat. | `Gain/GainElements.cs:709`; `CrystalElement.cs:635` | unclear |
| 10 | :30 example `"HR1030@45"` with `designWavelengthM: 1.13e-6` | Name says 1030 nm, design wavelength is 1130 nm. | page only | unclear |
| 11 | :48 `ripple` keys | Also `components` (default 64) and `seed` (default derived from the element id, not a fixed number); "pedestal within ±maxDelayS" and first-order level φ_rms² / amplitudeRms² not stated. | `CompilerSubstrate.cs:158-170` | missing (minor) |

Verified OK (not rows): clip formula (circular and elliptical), TIS reflection and transmission formulas (`Compiler.cs:1885-1892`), surface-model in-mode share, LIDT scaling exponent 0.5 and
`F = 2E/(π wx wy)`, substrate echo delays 62 / 93 / 124 ps (recomputed with n_g = 1.467), `physics.polarization` default `jones` / `scalar`, `planeOfIncidenceDeg` default 90, `groupDelayYS`.

Verdict: no wrong statement found; the page omits one coating kind, all defaults and the units/keys of `lidt`.

## components.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :179-180 tilted plate GDD "at 40° and 70° for 5 mm of fused silica at 1030 nm is 0.3 and 1.0 fs² **above** what the path d / cos θ_t at the carrier would give" | Recomputed with the engine's phase (ω/c)·d·(√(n²−sin²θ) + sinθ·tanθ_t0) and Malitson n(λ): GDD is 105.55 / 123.60 fs² against 105.83 / 124.56 fs² for d·n(ω)/cosθ_t0: **0.28 and 0.96 fs² below**, not above. | `tests/.../OpenBugRegressionTests.cs:25-48` (phase formula); recomputation in this audit | wrong (sign) |
| 2 | :310 python example `cells.cell_mode(1030e-9, mirror_radius=0.35, separation=0.54)` and `peak_power=` | The signature is `mirror_radius_m`, `separation_m`, `peak_power_w`. The short names still work only through a deprecated rename shim (`renamed_kwargs`), so the page teaches deprecated keyword names. | `python/src/ultrafast/calc/cell.py:15-20` | stale |
| 3 | :284-288 Faraday `"none"` "for a constant angle" | The key is `verdetDispersion` (`"inverseSquare"` default or `"none"`); the page gives the value without the key. | `Schema/BuiltInElements.cs` faradayRotator (`verdetDispersion`) | unclear |
| 4 | :24-30 inputBeam keys | `m2` is also split in `m2x`, `m2y`; `zWaistM` also `zWaistXM/YM`; `w0M` per-axis keys not mentioned. Fine for a summary, but the element keys that **enable** astigmatic tracking are not listed. | `Optics/BeamTracking.cs:120-123`; `Schema/BuiltInInstruments.cs:174-175` | missing (minor) |
| 5 | :150-155 `aberration` | `terms` (named Zernike terms, `{"Z4": 0.1}`) and `seed` default 1 exist; page shows only `zernikeNollWaves`. `correlationLengthM` default is "0.1 x grid". | `Schema/BuiltInElements.cs:128-135` | missing (minor) |
| 6 | :214-226 ndFilter | Defaults not stated for the absorptive filter: substrate N-BK7, thickness 2 mm (page gives only the reflective 1 mm). | `Schema/BuiltInElements.cs:147-148` | missing (minor) |
| 7 | :310-331 `cellMode` | Keys documented match. The Python docstring says the launch plane **is** mirror 1; the page says "just after mirror 1". | `calc/cell.py` docstring vs page :319 | unclear |
| 8 | :19 `wavefront` row, :147 "Mirrors, surfaces and lenses accept an `aberration`" | `lens` aberration is declared (BuiltInElements `lens`) but `plate`, `ndFilter` are not listed; fine. No error found. | — | (none) |

Verified OK (not rows): Kostenbauder recursion, grating equation and angles in the two grating examples (31.93 degrees recomputed), prism example angles (34.40 / −25.60 recomputed),
f_t = R cos θ / 2, f_s = R / (2 cos θ), Maréchal Strehl, GDD-ripple formula and phase integration (`Interfaces.cs:230`), ghost delay 49 ps and R² = 1.1e-3, ghost defaults (`orders` 1,
`satelliteLevel` 1e-8), TFP defaults (56 degrees, 12 pairs, HfO2 / SiO2_film, `bandPosition` 0.5), Faraday pole 258 nm and θ(λ) law, quartz group-delay split 30 fs/mm, report key
names (`trackedAngularDispersion*`, `strehlEstimate`, `profileTiltStepRad`), validation tolerances for `BeamTrackingTests`, test names (`Ghost_KerrMediumCreatesPrePulsePartner`,
`ThickPolarizerAndLens_AddTheirGlassDispersion`, `KerrLensOfTheHelper_ReproducesTheRadialModelsRmsRadius`), `cellMode` keys / defaults (`passes` 2, `slices` 12, 10 continuation steps, `lensModel` factor 4).

Verdict: one sign error (tilted-plate GDD), one stale API example; otherwise accurate, only minor defaults missing.

## active-elements.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :14-21 table "element / what it does" | Active elements (classes implementing `IActiveElement`) also include `modulator`, `mask`, `aperture`, `saturableAbsorber`, `oscillator`, dispersive surfaces (`grating`, `prism`, tilted `interface`), surface optics (aberration), beam tilt, `resonator`. The table lists seven families; `modulator` (own page) and `mask` are missing. | grep `: IActiveElement` (Crystals, Gain, Lasers, Optics, Transverse, Engine/MultiPort.cs) | missing |
| 2 | :85-97 Python user element: `phase = -0.5 * gddFs2 * w**2 - todFs3 * w**3 / 6` then `ax * exp(1j*phase)` with `gddFs2: -4700` | Works in example 12 (the shaper compresses to 25 fs), so the spectrum handed to the callback uses the opposite phase sign to the interface terms (`SpecCoating` multiplies by exp(+iφ) for positive GDD). The page states no sign convention for the array in `ElementContext`. | `docs/examples/GettingStarted/user-element/dazzler.py:29-33`; `Physics/Interfaces.cs:205-217`; `docs/concepts.md:13` (exp(−iωt)) | unclear (convention unstated) |
| 3 | :41-43 `beam` kinds for an aux beam: "also superGaussian (+ order), flatTop" | `order` default is 6 for `auxBeamShape`; `flatTop` radius is the edge radius; keys `w0M`, `decenterXM`, `decenterYM` exist. | `Schema/BuiltInElements.cs:286-290` | missing (minor) |
| 4 | :43 `delayS` "(positive = later)" | Schema: "Delay relative to the main beam" default 0; positive = later is not stated in the declaration. `fromPort: true` (aux from a link) is never described on this page, only linked from :29. | `Schema/BuiltInElements.cs:279-285` | missing (minor) |
| 5 | :32 anchor | The schema's `auxBeam` doc reference is `active-elements.md#auxiliary-beams`, but the section is headed "Aux beams" (anchor `#aux-beams`): the link generated into `options.md` is broken. | `Schema/BuiltInElements.cs:279` | stale |
| 6 | :99-100 `ElementContext` members | Python has also `n` (length) and `set()`; page lists `time_x()`; `n` missing. | `python/src/ultrafast/core.py:417` | missing (minor) |
| 7 | :139 "compile it from JSON in `Compiler.CompileElement`" | `CompileElement` is `private static` in `Compiler`; a third party cannot add an element without editing the core. `CompilerServices` is public. | `Engine/Compiler.cs:569, 1819` | unclear |
| 8 | :116-117 "`lengthM` (optional) is only used for the cumulative length" | Declared as "Length (informational)". Consistent. | `Schema/BuiltInElements.cs:254` | (none) |

Verified OK (not rows): aux keys (`pulse`, `from`, `energyJ`, `delayS`, `fromPort`), energy budget "from <role> (aux)", `record` default all, probe/`byproducts` figure, `IActiveElement` and `ActiveContext`
members, `UserElementContext` fields, C API names (`uf_run_set_element_callback`, `uf_run_set_field_callback`, `uf_element_ctx`), `Simulation.Run(system, run, reg)`, `ModelRegistry.CreateDefault`.

Verdict: accurate; table of active elements incomplete, user-element phase-sign convention unstated.

## modulator.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :54, :81, :92 "(spikes recorded in the development history (`docs/design/history.md`, internal))", timings :54, :94-96 (10 ms / 46 ms, 32 s, 3.6 GB, 3.3 s, 20 s ...) | No test or script pins these numbers; user-facing reference points to an internal file; timings are machine-specific. | grep tests, `docs/design/history.md` | unclear (unverifiable, internal reference in a public page) |
| 2 | :7 "It needs a cartesian transverse grid" | Without a carried profile the element is **ignored with a warning**; with a radial grid it throws. Page only says "needs". | `Optics/ModulatorElement.cs:150-153` | unclear |
| 3 | :108-109 validation rows for `CwRun_EqualsThePulse_AtTheCarrier` (2e-5) and `OneStoredFrequency_*` (1e-7; 3e-6) | The tests assert 1e-7 of the peak (cw vs 1 ns pulse) and "one stored frequency sits at the **energy centroid**" (within one grid bin), not "at the carrier". The tolerance numbers on the page differ from the test attributes. | `tests/.../ModulatorTests.cs:141, 348` | stale (tolerances) |
| 4 | :23, :26 NPZ keys and `file` | `phase_rad`, `opd_m`, `amplitude` read case-insensitively from the file; a 2-D shape fixes `cellsX/cellsY` only when not given. Matches; but the **map array orientation** `[cellsX][cellsY]` is stated only for arrays, not for NPZ shape order. | `Engine/CompilerModulator.cs:36-60` | unclear (minor) |
| 5 | :34-35 `phaseWrapRad` 0 = off, `quantisationLevels` | If `quantisationLevels > 0` and `phaseWrapRad == 0` the phase is **also wrapped to 2π** (not just rounded). Page says "(2π if that is 0)" for the levels but not that the wrap then applies. | `ModulatorElement.cs:65-72` | unclear |

Verified OK (not rows): all 20+ test names exist (`ModulatorTests.cs`, `GpuTests.cs`), blaze efficiency [sin(π/N)/(π/N)]² (N = 8: 94.96 %), 6 × 6 sub-sampling, "warns below 2 grid cells", warning at π/2 and 0.05,
`zeroOrderLeak` formula, cell-centre formula, order of operations (wrap → quantise → blur), `chromatic` rules, defaults, parameter table keys against the schema.

Verdict: accurate; the main weaknesses are unverifiable timings and spike numbers, and two test tolerances quoted differently from the tests.

## spatial-properties.md

| # | claim (page:line) | what the code does | evidence | severity |
|---|---|---|---|---|
| 1 | :3 "Any element can carry quantities that vary with position" | The keys are declared on every element (`elementCommon`) but only read by `bulk`/`gap`-type (nonlinear medium) elements and `gain`/`amplifier`. `crystal`, `fiber`, `plate` / `ndFilter` (own bulk path aside) and others silently ignore them. | `Engine/Compiler.cs:1149, 436`; `Gain/GainElements.cs:805`; no `Properties` in `CompilerFiber.cs`, `CrystalElement.cs`; `Schema/BuiltInOptions.cs:257-259` | wrong (overclaim) |
| 2 | :27 Gaussian / super-Gaussian row | Kinds `gaussian` (default order 1), `superGaussian` (default order **4**), `constant`; keys `centerXM`, `centerYM`, `background`. The page lists `centerXM` only and shows no `superGaussian` kind or `constant` kind. Formula amplitude·exp(−2(ρ/R)^(2·order)) + background is correct. | `Models/SpatialFields.cs:50-59, 87-107` | missing |
| 3 | :28 table kind | Axes are given by letters (`r`, `x`, `y`, `z`; also `x`, `y`, `xz`); the axis arrays use the same names; `values` flattened with the **last axis fastest**; axes must be strictly ascending; `outside` accepts a number only (otherwise clamp). Layout and the ascending rule are not stated. | `SpatialFields.cs:112-140, 170-195` | missing |
| 4 | :41 "(before 1.0 only n2 was scaled, so air kept its full orientational response)" | 1.0.0 is not released (CLAUDE.md: "1.0.0 (unreleased)"); a pre-release behaviour change does not belong in the reference. | `CHANGELOG.md` top section | stale |
| 5 | :47-48 "gain elements ... accept the same three keys" | In a 1-D gain element all three are ignored with a note ("need a transverse model"). | `Gain/GainElements.cs:512` | unclear |
| 6 | :44-45 "≈ 0.2 rad of phase per step" | 0.2 rad / StaticPhaseRate is applied for linear terms in the 1-D stepper; the spatial propagator step rule was not located. | `Physics/Nonlinear.cs:872` | unclear |
| 7 | :31-32 callbacks / z dependence | `expression` z-dependence is detected by a regex on the text (`z` as a token), `table` by its axes, `callback` always z-dependent: so a callback is evaluated at every step even if it ignores z. Page states the rule only abstractly. | `SpatialFields.cs:64-70, 190` | missing (minor) |
| 8 | :54 tests | `SpatialPropertyTests` lives in `tests/.../GainTests.cs`, not its own file; the 3 % / 0.3 % tolerances match. | `GainTests.cs:14-81` | (none) |

Verified OK (not rows): n2Scale scales Kerr, Raman, cubic anisotropy and gas rotational Raman but not user responses; 1-D averages (intensity-weighted absorption, intensity²-weighted n2Scale); indexProfile ignored in 1-D with a note; test tolerances and test names.

Verdict: formulas and 1-D behaviour are right; the opening claim ("any element") is wrong and the field-kind documentation is incomplete.
