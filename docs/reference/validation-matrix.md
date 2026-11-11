# Validation matrix

<!-- Generated from the test attributes: dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll --matrix docs/reference/validation-matrix.md -- do not edit. -->

Every C# test of the engine: **585 tests** in 61 classes. **370** of them check the engine against something outside it -- an analytic result, a published value or another code -- named in the *reference* column with the agreement required. **20** are cross-checks between two parts of the engine (a reference starting with "cross-"): they catch inconsistency, not a shared error. The others test behaviour and bookkeeping. The narrative overview is [Testing and validation](../testing.md); a test declares its reference with `[Test(Reference = "...", Tolerance = "...")]`.

| area | class | tests | with an external reference |
|---|---|---|---|
| [AO plugin: deformable mirror, SLM, spectral shaper and AOPDF elements](#aodevicetests) | `AoDeviceTests` | 26 | 26 |
| [Beam sources and masks: Hermite-Gauss, Laguerre-Gauss, Bessel, Airy, measured near fields, axicon, zone plate, vortex, slits](#beamsourcetests) | `BeamSourceTests` | 15 | 14 |
| [Gaussian beam tracking: thermal lenses and spatio-temporal couplings](#beamtrackingtests) | `BeamTrackingTests` | 17 | 16 |
| [Branched systems: splitter family, Gouy and crystal phases, mode overlap, profiles meeting, crystal ports](#branchedelementstests) | `BranchedElementsTests` | 12 | 8 |
| [Branched systems: ports, graph compiler, interference, loops](#branchingtests) | `BranchingTests` | 18 | 8 |
| [Material catalogue: values pinned, provenance, user catalogues](#cataloguesnapshottests) | `CatalogueSnapshotTests` | 1 | 1 |
| [Material catalogue: values pinned, provenance, user catalogues](#cataloguetests) | `CatalogueTests` | 13 | 3 |
| [Resonators, partial coherence, thermal stress birefringence, amplifier thermal lens](#cavityandcoherencetests) | `CavityAndCoherenceTests` | 9 | 8 |
| [Multi-pass cell modes (Kerr-matched Herriott and quasi-concentric cells)](#cellmodetests) | `CellModeTests` | 7 | 7 |
| [χ(2) crystals: SHG, OPA, QPM, cascading, phase matching, aux beams](#chi2tests) | `Chi2Tests` | 14 | 7 |
| [Components: beam tracking, ND filters, plates and ghosts, polarisers, Faraday rotators, waveplates, chirped mirrors, aberrations, pedestals](#componenttests) | `ComponentTests` | 21 | 15 |
| [CPU FFTs and compute back-ends](#computetests) | `ComputeTests` | 5 | 1 |
| [Crystal chains and phase-matching limits: SHG to THG, length against bandwidth](#crystalchaintests) | `CrystalChainTests` | 16 | 16 |
| [Delayed light: the tail ledger, ghosts, echoes, coating ripple, scatter, detectors](#delayedlighttests) | `DelayedLightTests` | 34 | 12 |
| [Cameras, wavefront sensor, Wigner and space-time probes](#diagnosticstests) | `DiagnosticsTests` | 5 | 4 |
| [Diffraction by apertures](#diffractiontests) | `DiffractionTests` | 8 | 6 |
| [Fibres: solitons, dispersion, loss and gain, Raman, noise and coherence](#fibertests) | `FiberTests` | 9 | 9 |
| [Cw sources, nonlinear absorbing boundary, Gerchberg-Saxton, near-field maps](#fieldtoolstests) | `FieldToolsTests` | 6 | 5 |
| [Fitting plugin: conditions, dependence, surfaces](#fittingconditionstests) | `FittingConditionsTests` | 15 | 9 |
| [Foundations: JSON, expressions, NPZ, FFT, dispersion maths, materials, Fresnel, thin films, Treacy](#foundationtests) | `FoundationTests` | 9 | 6 |
| [FROG geometries: PG, SD, XFROG, kind inference](#frogkindstests) | `FrogKindsTests` | 5 | 5 |
| [Gain media, pumping, thermal lens and position-dependent properties](#gaintests) | `GainTests` | 10 | 6 |
| [Gases: rotational Raman, indices, the refractiveindex.info database](#gastests) | `GasTests` | 6 | 5 |
| [GPU back-ends (CUDA / OpenCL) against the CPU](#gputests) | `GpuTests` | 8 | 5 |
| [Grid, carrier and solver accuracy](#gridsolvertests) | `GridSolverTests` | 9 | 9 |
| [HITRAN plugin: line shapes, files, partition sums, absorption in air](#hitrantests) | `HitranTests` | 18 | 10 |
| [Component sheet: instruments and chi(2) crystals at 515 nm / 1030 nm / 2 um](#instrumentcrystalcomponenttests) | `InstrumentCrystalComponentTests` | 4 | 4 |
| [Instrument device models: resolution, acceptance, detectors, filters](#instrumentdevicetests) | `InstrumentDeviceTests` | 8 | 3 |
| [Instrument fixes: band edges, IAC peak, cross-correlator resolution and arms](#instrumentfixestests) | `InstrumentFixesTests` | 5 | 5 |
| [Instruments: autocorrelators, spectrometer, compressor, polarimeter, d-scan](#instrumenttests) | `InstrumentTests` | 4 | 3 |
| [Photo-ionisation and plasma](#ionizationtests) | `IonizationTests` | 10 | 10 |
| [Lasers: Haus master equation, absorbers, cavities, apertures, carrier-resolved χ(2)](#lasertests) | `LaserTests` | 12 | 7 |
| [Component sheet: linear elements and splitters at 515 nm / 1030 nm / 2 um, 1 ps / 10 fs, Sequential = Branched](#linearcomponenttests) | `LinearComponentTests` | 5 | 4 |
| [Loss budget and overrides](#lossandoverridetests) | `LossAndOverrideTests` | 4 | 1 |
| [Materials and crystals: d_eff of explicit cuts, delayed-response split, CS2, cubic chi(3)](#materialscrystalstests) | `MaterialsCrystalsTests` | 14 | 14 |
| [Measured input spectra, comparisons and satellite room](#measuredspectrumtests) | `MeasuredSpectrumTests` | 13 | 0 |
| [Sampling apertures, measured data and fits](#measurementtests) | `MeasurementTests` | 13 | 2 |
| [Modelled instruments: IAC, FTS, FROG from elements; study node cache and array outputs](#modelledinstrumentstests) | `ModelledInstrumentsTests` | 4 | 3 |
| [Modulator: pixelated phase and amplitude devices (SLM, DM surface), quantisation, fill factor, chromatic rules](#modulatortests) | `ModulatorTests` | 24 | 24 |
| [Multi-trace FROG retrieval (double FROG)](#multitraceretrievaltests) | `MultiTraceRetrievalTests` | 1 | 1 |
| [Nonlinear ellipse rotation and the vector Kerr / Raman tensor](#nertests) | `NerTests` | 10 | 10 |
| [Non-collinear OPA and DFG: angle from the arms, output tilts, parametric gain](#noncollinearopatests) | `NoncollinearOpaTests` | 6 | 6 |
| [Regressions from the open-bug sweep](#openbugregressiontests) | `OpenBugRegressionTests` | 8 | 5 |
| [Physics review 2026-10-30: dispersed rays in glass, reversed cavity passes, stress depolarisation, mode truncation](#physicsreviewtests) | `PhysicsReviewTests` | 8 | 4 |
| [Plugins: registration, installation, versions, cameras](#plugintests) | `PluginTests` | 8 | 2 |
| [Polarisation: waveplates, Brewster windows, basis transport](#polarizationtests) | `PolarizationTests` | 3 | 2 |
| [Nonlinear envelope propagation](#propagationtests) | `PropagationTests` | 11 | 7 |
| [Instrument axes, resolution and measured-data resolution](#resolutiontests) | `ResolutionTests` | 7 | 1 |
| [Pulse retrieval (FROG, d-scan)](#retrievaltests) | `RetrievalTests` | 2 | 2 |
| [Regressions from the review: M^2, second moments, packets at ports, steady-state loops](#reviewregressiontests) | `ReviewRegressionTests` | 8 | 8 |
| [Schemas, validation and migrations](#schematests) | `SchemaTests` | 5 | 0 |
| [Single-shot time-to-space cross-correlator (window, resolution, position to delay)](#singleshotcorrelatortests) | `SingleShotCorrelatorTests` | 8 | 8 |
| [Small-scale self-focusing](#smallscaletests) | `SmallScaleTests` | 4 | 3 |
| [Position-dependent properties of spatial elements](#spatialpropertytests) | `SpatialPropertyTests` | 4 | 2 |
| [Large spatial runs: frequency streaming, profile retention, cylindrical lens, full-panel budget](#spatialstreamingtests) | `SpatialStreamingTests` | 6 | 6 |
| [Spatial (transverse) models](#spatialtests) | `SpatialTests` | 6 | 5 |
| [FFTs, SIMD kernels and studies](#speedstudytests) | `SpeedStudyTests` | 6 | 2 |
| [Coherent tile source: N sub-beams with piston, tilt and delay; combined far field, grating lobes, phase and delay errors](#tilesourcetests) | `TileSourceTests` | 11 | 11 |
| [Fitting plugin: transforms, backward propagation, fits](#transformtests) | `TransformTests` | 20 | 3 |
| [The test framework and the generated validation matrix](#validationmatrixtests) | `ValidationMatrixTests` | 2 | 0 |
| [Zemax plugin: export and replay](#zemaxtests) | `ZemaxTests` | 5 | 1 |

## AoDeviceTests

AO plugin: deformable mirror, SLM, spectral shaper and AOPDF elements.

| test | reference | tolerance |
|---|---|---|
| `Aopdf_AcousticWindow_CutsTheSpectrumWhereTheProgrammedDelayLeavesIt` | finite acoustic window: with GDD phi2 the programmed group delay is phi2 (omega - omega0); components with \|phi2 (omega - omega0)\| > window/2 are not diffracted, so the transmitted fraction of a Gaussian spectrum (FWHM d_omega = 4 ln2 / t0) is erf(a / (sigma sqrt 2)) with a = window / (2 phi2) and sigma = d_omega / (2 sqrt(2 ln 2)) | 0.3 % of the energy fraction |
| `Aopdf_Delay_MovesThePulse_AndEfficiencyScalesTheEnergy` | an aopdf with delayFs moves the pulse by that group delay (shift theorem); efficiency scales the energy | centroid 0.05 fs, energy 1e-9 |
| `Aopdf_GddAndTod_HaveTheSameSignAsTheCoreDispersionElement` | sign convention (docs/concepts.md): the aopdf's GDD and TOD are the same spectral phase as the core dispersion element's (+GDD puts red first), so the output pulses agree | peak power and FWHM 1e-3 (measured 8e-5 for GDD + TOD, 1e-6 for GDD alone: the two expand about carriers that differ by the grid snapping); centroids within 0.1 fs of each other and 6 fs from the undispersed pulse (opposite signs would differ by order 1 and 12 fs) |
| `Aopdf_Gdd_BroadensTheGaussianAsTheTextbookFormula` | linear chirp: a transform-limited Gaussian (FWHM t0) through GDD phi2 has FWHM t0 sqrt(1 + (4 ln2 phi2 / t0^2)^2) (Diels and Rudolph, Ultrashort laser pulse phenomena, eq. 2.4.23) | 0.5 % |
| `Aopdf_Replica_AddsASecondPulse_AtTheStatedDelay` | spurious replica H (1 + a exp(i omega tau)): a second pulse at tau with amplitude a (energy a^2), by the shift theorem | energy ratio 1e-3, position 0.2 fs |
| `DriveBits_QuantiseTheCommandsToTwoToTheBMinusOneSteps` | drive resolution: a b-bit drive over +-stroke has 2^b - 1 steps, level k = -stroke + k step with step = 2 stroke / (2^b - 1); a tilt ramp driven with driveBits 3 equals the same ramp rounded by hand to those levels and driven continuously | 1e-12 (focus centroid) |
| `Erf_MatchesTabulatedValues` | erf by series and continued fraction against tabulated values (Abramowitz and Stegun table 7.1): erf(0.5) = 0.5204998778130465, erf(1) = 0.8427007929497149, erf(2) = 0.9953222650189527, erf(3.5) = 0.9999992569016276 | 1e-12 |
| `HexLayout_HasSixNeighboursAtOnePitch` | hex layout: odd rows shifted by half a pitch, row spacing pitch sqrt(3)/2, so every interior actuator has six neighbours at one pitch | 1e-12 |
| `Planner_Aopdf_FixedWindow_WarnsAboutWrap` | fixed window (grid.auto false): a programmed GDD x bandwidth stretch of about 1 ns on a 10 ps window wraps, so the planner warns | a warning that says the grid is too small |
| `Planner_Aopdf_Gdd_WidensTheWindow` | GDD x bandwidth: a transform-limited Gaussian of FWHM tau and GDD phi'' has FWHM tau sqrt(1 + (4 ln2 phi''/tau^2)^2) (Diels and Rudolph, Ultrashort Laser Pulse Phenomena, ch. 1); the auto-sized window must hold it | window >= 2 x the stretched FWHM, stretched FWHM within 2 % of the formula |
| `Planner_SpectralShaper_FlatMask_AddsNoRoom` | a flat mask makes no side pulses: the planner adds no room (same grid as an aopdf without programmed phase) | identical grid |
| `Planner_SpectralShaper_WidensTheWindow_ForItsSidePulses` | Weiner (2000): side pulses of a pixelated mask lie at 2 pi / pixel bandwidth; the planner must hold them without grid.windowS (window >= 2 x 1.25 x T, T = 1.6 ps here) | window at least 2 x 1.25 x T; no warning; the +T side pulse is in the output |
| `Slm_BinaryPhaseGrating_OrdersSitAtWavelengthTimesFocalLengthOverPeriod` | binary phase grating (4 pixels at phase 0, 4 at pi) through two grey levels: diffraction orders at +-lambda f / (8 pixels) carrying the sampled-staircase Fourier weight, none at the zero order (a pi step has c_0 = 0) | 0.6 % of the order efficiency; zero order below 1e-4 |
| `Slm_FillFactor_SetsTheTransmission_AndTheReplicaAtWavelengthTimesFocalLengthOverPitch` | pixel replicas: a panel of pixels with fill factor 3/4 (4 grid cells per pixel, edges on cell edges) transmits 3/4 of the amplitude-squared... power 3/4 and has the discrete Fourier series c_0 = 3/4, \|c_1\| = 1/4, so the replica at lambda f / pitch carries 1/9 of the zero order (same geometry as the core modulator comb test) | 1e-6 transmission, 1 % order ratio |
| `Slm_FullPanel_1920x1080_8Bit_RunsWithinTheScaleBudget` (slow) | S2 scale spike (docs/design/notes/spikes.md): a 1920 x 1080 panel on a 2048 x 1280 grid, one frequency, ran in 1.9 s; here the panel is the slm element written with 8-bit grey levels (a phase-only lens-and-grating pattern from a callback); a Gaussian overfilling the panel passes the Gaussian power on the panel | budget 13 s (the S2 limit; 45 s when the CI variable is set, shared runners took 21-23 s); transmission 1e-3 of the erf product |
| `Slm_GreyLevelBlaze_FirstOrderEfficiency_FollowsSincOfOneOverN` | N-level blazed grating written with grey levels through the slm element: first-order efficiency of the sampled staircase (4 samples per pixel) and the continuum formula [sin(pi/N) / (pi/N)]^2 (Goodman, Fourier optics, sec. 5.2) | 0.6 % of the efficiency (sampled staircase), 2 % (continuum) |
| `Slm_NearestLevel_UsesTheCircularDistance_WhenTheCurveExceedsTwoPi` | nearest grey level when the curve spans more than 2 pi (0 .. 2.5 pi in 100 steps): images of the target modulo 2 pi inside the range are all candidates (circular distance) | exact |
| `Slm_PhaseTarget_IsMappedToTheNearestGreyLevel_OfANonlinearCurve` | phase-target input: the target is wrapped to 2 pi and mapped to the nearest grey level of the (here S-shaped) curve; the result is the same as writing those grey levels directly | 1e-12 (identical fields) |
| `Slm_WrappedRamp_AtOtherWavelengths_FollowsTheLiquidCrystalDispersion` | liquid-crystal dispersion: the 2 pi reset written at the design wavelength is 2 pi s(lambda) at lambda, s = lambda_d dn(lambda) / (lambda dn(lambda_d)); the first order of the wrapped ramp is the sampled Fourier coefficient with the phases scaled by s | 1.5 % of the efficiency |
| `SpectralShaper_GratingDispersion_MatchesLittrowReference` | Littrow condition (Hecht, Optics): for sin(theta) = m lambda N / 2 a grating in the focal plane of an f lens gives dx/dlambda = 2 f tan(theta) / lambda; the shaper built from the grating data equals the one given that dispersion | output power identical to 1e-12 of its peak |
| `SpectralShaper_LinearPixelPhase_DelaysThePulse` | shift theorem on the pixelated mask: pixels carrying the linear phase tau (omega_j - omega0) delay the pulse by tau (the main pulse of a staircase; Weiner) | centroid of the main pulse within 2 fs of tau (the replica comb is outside the window of the centroid) |
| `SpectralShaper_OffsetMovesTheArray_AwayFromTheSpectrum` | the mask array moves with offsetM: with unit amplitude on every pixel and outsideTransmission 0, an array centred on the spectrum passes all the energy (the pulse is far narrower than the array), the same array shifted by 1.5 array widths passes none | pass 1e-4, blocked 1e-6 |
| `SpectralShaper_PixelGaps_MakeSidePulses_AtTwoPiOverThePixelBandwidth` | Weiner (Rev. Sci. Instrum. 71, 1929 (2000)): a Fourier-plane mask with pixel gaps (150 fs pulse: its bandwidth is narrow enough for the pixel width in angular frequency to be constant to 0.4 %, the map is exact in wavelength) is a comb in frequency of period d_omega (one pixel); active fraction f gives side pulses at +-2 pi / d_omega with energy [sin(pi f) / (pi f)]^2 of the main pulse; a Gaussian spot of 1/e^2 radius w0 multiplies the first harmonic by exp(-(pi w0 / pitch)^2) | 1 % of the side-pulse energy ratio, 1 fs position |
| `StrokeAndDriveBits_ClipAndQuantiseCommands` | stroke limit and drive resolution: commands clip at +-stroke; a b-bit drive over +-stroke has 2^b - 1 steps | exact (1e-12) |
| `TiltRamp_OnActuators_MovesTheFocus_ByFocalLengthTimesWavefrontTilt` | influence function: the surface at a neighbouring actuator equals the stated coupling (definition), and a tilt ramp on the actuators gives a wavefront tilt 2 cos(aoi) dh/dx that moves the focus of a lens by f theta (geometric optics) | 2 % of the shift (the lattice sum of the influence functions sets dh/dx, computed from the same functions) |
| `UnknownElement_NamesTheAoPlugin` | the unknown-element message names the ao plugin for its four types | message text |

## BeamSourceTests

Beam sources and masks: Hermite-Gauss, Laguerre-Gauss, Bessel, Airy, measured near fields, axicon, zone plate, vortex, slits.

| test | reference | tolerance |
|---|---|---|
| `AiryBeam_MainLobeAcceleratesAlongTheParabola` | Siviloglou & Christodoulides, Opt. Lett. 32, 979 (2007): parabolic trajectory x = z^2 / (4 k^2 x0^3) | 3 % |
| `Axicon_TurnsAGaussianIntoABesselCore` | Axicon on a Gaussian: J0(k r sin(angle)) core, first zero 2.405 / (k sin(angle)) (McLeod 1954) | 2 % |
| `BesselGauss_CoreDoesNotSpreadOverSeveralRayleighRangesOfItsSpot` | Durnin, JOSA A 4, 651 (1987); Bessel-Gauss (Gori et al. 1987): central lobe 2.405 / k_r unchanged while z << w / angle | 2 % |
| `BinaryPhaseZonePlate_LosesTheEnergyOfItsSampledEdges_AsTheGridStep` | cell-averaged pi edges: a cell split into fractions f and 1-f of +1 and -1 transmits \|2f-1\|^2, mean 1/3 over a uniform edge position | 25 % of the loss, which falls as the grid step |
| `DoubleSlit_FringesHaveThePeriodOfYoungsExperiment` | Young's double slit: fringe period lambda z / d, transmission 2 b / W | 2 % |
| `FileMask_AppliesAmplitudeAndPhase` | T = amplitude^2 for a uniform map; a thin plate's phase scales with frequency | -- |
| `HermiteGauss_RadiiFollowTheModeOrders_AtTheWaistAndAfterARayleighRange` | Hermite-Gauss modes: second-moment radius w(z) sqrt(2m + 1) (Siegman, Lasers, ch. 16) | -- |
| `LaguerreGauss_VortexKeepsItsDarkAxis_AndItsRingScalesWithTheBeam` | Laguerre-Gauss modes: second-moment radius w sqrt(2p + \|l\| + 1), helical phase keeps a dark axis, ring at w sqrt(\|l\| / 2) | -- |
| `MaskFile_OfOneRow_IsConstantAlongY` | a mask file of one row is constant along y: a transmission of 0.5 everywhere passes 0.25 of the energy on a grid with ny = 1 and ny = 4 | 1e-9 |
| `Mask_WithoutAProfile_WarnsAndPassesThePulse` | -- | -- |
| `MeasuredNearField_FromAnNpz_PropagatesLikeTheBeamItSampled` | Gaussian beam w(z) from a sampled near field; known background subtracted | -- |
| `MeasuredScene_OfOneRow_IsConstantAlongY_OnGridsWithAnyNy` | a measured scene of one row (what a '1-D' scene saved at y = (j - ny/2) dx with ny = 1 is: its row is at y = -dx/2) reproduces on a grid with ny = 1, 4 and 128 the same x profile as a full map: radius w of the Gaussian and w sqrt(2) after one Rayleigh length | radius 1 %, w(z_R) 2 % |
| `SpecialFunctions_MatchTabulatedValues` | Abramowitz & Stegun table 10.11: Ai(0), Ai(1), Ai(-1), Ai(5.5), first zero; H_3, L_2^1 closed forms | 1e-8 |
| `VortexPlate_ConservesEnergy_AndDarkensTheAxis_AndNeedsACartesianGrid` | Spiral phase plate: a vortex of charge l has zero amplitude on the axis at every z | -- |
| `ZonePlate_FocusesAtItsFocalLength_AmplitudeAndBinaryPhase` | Fresnel zone plate: amplitude on the axis 2 N_open U0, intensity 4 N_open^2 I0; binary phase plate 16 N_open^2 I0 (Born & Wolf 8.8) | 5 % |

## BeamTrackingTests

Gaussian beam tracking: thermal lenses and spatio-temporal couplings.

| test | reference | tolerance |
|---|---|---|
| `AngularDispersionOnTheProfile_TwoGratingsOnAGrid_GiveTheTrackersChirpAndTreacysGdd` | Treacy GDD and the tracker's spatial chirp, from a field propagated on a grid | 3 % |
| `Duct_ManyThinSlices_ConvergeToTheDuct_WithSecondOrderError` | slices of a graded-index medium: N thin lenses + gaps converge to the duct as 1/N² | 1e-3 at N = 40, ratio 4 for 2N |
| `Duct_Matrix_IsTheQuadraticIndexResult_AndTheThinLensLimit` | quadratic-index medium n(r) = n0 (1 - g² r²/2): ABCD cos(gL), sin(gL)/(n0 g) (Saleh & Teich, ch. 1) | 1e-12 |
| `Grating_AngularDispersionAndPulseFrontTilt_FollowTheGratingEquationAndHebling` | grating equation; Hebling, Opt. Quantum Electron. 28, 1759 (1996) | 0.2 % |
| `PrismPair_InAir_GivesTheNegativeDispersionOfItsAngularDispersion` | angular-dispersion GDD -k0 L (d theta / d omega)^2; Snell's law at the prism faces | 1 % |
| `Prism_AngularDispersion_MatchesSnellsLawThroughBothFaces` | Snell's law at both prism faces (finite difference of n(λ)) | 0.5 % |
| `PulseFrontTilt_FromSpatialChirpAndGdd_MatchesTheGaussianResult` | Akturk et al., Opt. Express 13, 8642 (2005): 2 φ2 E σ² / (w² + 2 E² σ²) | 0.5 % |
| `Regen_ThermalLens_OnTheTrackedBeam_EqualsTheSamePassesOfAMultipass` | N round trips of a regenerative amplifier = N x passesPerRoundTrip duct passes | 1e-12 |
| `SpaceTime_OnA1DBeam_ShowsTheTrackedPulseFrontTilt` | cross-model: spaceTime from the tracked beam vs the element's pulse-front tilt | -- |
| `SpatioTemporalSwitch_TrackReportsWithoutPhase_OffForgetsTheCouplings` | -- | -- |
| `ThermalLens_MeasuredFocalLength_FocusesTheTrackedBeamLikeAThinLens` | Gaussian-beam thin-lens law (Self, Appl. Opt. 22, 658 (1983)) | 1e-12, 0.2 % |
| `ThermalLens_SmallBeam_TrackedFocalLengthIsInnocenzis` | Innocenzi et al., Appl. Phys. Lett. 56, 1831 (1990): f = πK w_p² / (P_h dn/dT) | 5 % |
| `ThermalLens_ThickRod_TrackedBeamIsTheDuctResult` | ABCD matrix of a quadratic-index duct (gL = 0.6): not the thin lens at the centre | 1e-9 |
| `ThermalLens_TrackedBeam_AgreesWithTheSpatialGainModel` | cross-model: the radial spatial gain model | 7 % (aberrated lens) |
| `TwoGratings_GiveTreacysGdd_AndTheGratingPairsSpatialChirp` | Treacy grating pair (IEEE JQE 5, 454 (1969)), Kostenbauder ray-pulse matrices | 0.1 %, 0.2 % |
| `TwoGratings_ReproduceTreacysThirdOrderDispersion` | Treacy, IEEE JQE 5, 454 (1969): TOD of the exact grating-pair phase | 0.5 % |
| `ZeroDispersionStretcher_4f_LeavesNoGddAndNoCouplings` | Martinez zero-dispersion stretcher: GDD = 0 | < 10⁻⁶ of k0 f F² |

## BranchedElementsTests

Branched systems: splitter family, Gouy and crystal phases, mode overlap, profiles meeting, crystal ports.

| test | reference | tolerance |
|---|---|---|
| `BeamState_GouyPhase_FollowsTheArctangent` | Gouy phase of a Gaussian beam: -atan(z / z_R), from -z_R to +z_R is -pi / 2 | 1e-9 |
| `CarriedProfiles_Meet_WithTiltFringes` | two beams at an angle: fringe period lambda / theta | 2 % |
| `CrystalByproduct_IsAPort_AndTheBudgetCloses` | -- | -- |
| `Crystal_HarmonicPhase_DoublesThePathsPhase` | carrier phase of a harmonic: twice the fundamental's, plus the crystal's | 1e-6 |
| `DelayedTail_FollowsEachBranch` | linear superposition: the tail splits and is booked with the main field | 1e-9 |
| `Dichroic_OutputsAddUpAtEachWavelength` | -- | -- |
| `F2f_FringesFollowTheCep` | f-2f: the fringe phase at the overlap moves with the CEP one to one | 3 % |
| `LeakMirror_PassesItsTransmission_ToThePort` | -- | -- |
| `Michelson_EqualArms_AreDarkAtTheOutput_AndFringeWithPeriodHalfLambda` | Michelson with a symmetric splitter: equal arms send all the light back, none to the output | 1e-9 |
| `PolarizingSplitter_SplitsByPolarisation` | Malus: an input at 30 deg sends cos^2 to p and sin^2 to s | 1e-9 |
| `TiltWithNoTrackedBeam_IsAnError_NotAPerfectOverlap` | -- | -- |
| `TrackedBeams_MeetWithTheirModeOverlap` | overlap of Gaussians in a tilt: exp(-(k theta w)^2 / 8), and in an offset: exp(-d^2 / (2 w^2)) | 1e-6 |

## BranchingTests

Branched systems: ports, graph compiler, interference, loops.

| test | reference | tolerance |
|---|---|---|
| `ArmsFurtherApartThanTheWindow_StaySeparatePackets_AndDoNotInterfere` | -- | -- |
| `CrystalPort_FedFromABranch_MatchesTheSameBeamGivenAsAux` | cross-check: a branch-fed port against the same pulse as an aux beam | 1e-6 |
| `FabryPerot_LoopGivesTheAiryFunction` | Airy transmission of a Fabry-Perot, (1-R)^2 / (1 + R^2 - 2 R cos delta) | 1e-5 |
| `GraphErrors_AreReported_AllAtOnce` | -- | -- |
| `MachZehnder_FringesFollowThePathDifference` | Mach-Zehnder: output (1 - cos phi) / 2 of the input | 1e-9 |
| `MachZehnder_SpectralFringes_PinTheSignOfPhaseAgainstDelay` | spectral interference, \|1 - exp(i omega tau)\|^2 / 4 | 1e-6 |
| `MissingNext_IsAnAlert_NotASilentFallThrough` | -- | -- |
| `ModelledAutocorrelator_ThinCrystal_GivesTheIdealWidth` | intensity autocorrelation of a Gaussian: FWHM sqrt 2 tau | 1 % |
| `PathLedger_KeepsTheCarrierPhaseTheFieldDoesNotCarry` | carrier phase of glass, n omega L / c | 1e-9 |
| `Ring_RoundTripLongerThanTheWindow_GivesAPulseTrain` | ring ring-down: pass n carries R^(n-1) T^2 | 1e-9 |
| `SequentialFile_WithNext_IsRejected_WithAPointer` | -- | -- |
| `Sequential_SplitterKeepsOnePort_AndBooksTheOther` | -- | -- |
| `Sequential_VirtualBeam_InterferesAtTheSplitter` | 2x2 unitary: c = t a - r b | 1e-9 |
| `SteadyStateLoop_SettlesOnlyWhenShapeAndEnergyRepeat` | -- | -- |
| `Targets_ByNumberNameAndDict_AreTheSame` | -- | -- |
| `TwoSourcesOnOnePort_AreAnError_NamingThePort` | -- | -- |
| `UnusedPorts_RaiseAlerts_AndBecomeVirtual_UnlessDeclared` | -- | -- |
| `Validate_PrintsTheGraph` | -- | -- |

## CatalogueSnapshotTests

Material catalogue: values pinned, provenance, user catalogues.

| test | reference | tolerance |
|---|---|---|
| `Catalogue_ReproducesTheValuesPinnedBeforeTheMove` | the values of the catalogue files equal the old C# tables | 1e-15 relative |

## CatalogueTests

Material catalogue: values pinned, provenance, user catalogues.

| test | reference | tolerance |
|---|---|---|
| `CatalogueFiles_HaveSourceStatusAndUnitsInTheirKeys` | -- | -- |
| `CiddorAir_AtStandardConditions_EqualsTheDatabaseFormula` | Ciddor, Appl. Opt. 35, 1566 (1996) as published on refractiveindex.info (other/air/Ciddor: standard air, 15 C, 101.325 kPa, dry, 450 ppm CO2): n - 1 = 0.05792105 / (238.0185 - s^2) + 0.00167917 / (57.362 - s^2) | 1e-12 |
| `Crystal_ListsItsUnverifiedValues_AndVerifiedIndicesStayQuiet` | -- | -- |
| `EveryBuiltInName_ResolvesToACatalogueEntry_AndBuilds` | -- | -- |
| `GainMedium_ListsItsCatalogueValues_AndNotTheOnesTheElementOverrides` | -- | -- |
| `LinearRun_ReportsOnlyTheDispersionItUsed` | -- | -- |
| `PublishedIndices_AreReproduced` | Malitson, JOSA 55, 1205 (1965): n_d = 1.45846 at 587.56 nm; Schott N-BK7 data sheet: n_d = 1.5168 at 587.56 nm; Dodge 1984 MgF2 n_o = 1.3777 at 589.3 nm | 2e-5 / 1e-4 / 1e-4 |
| `Run_ListsTheApproximateValuesItUsed_AndNotTheOnesTheUserOverrode` | -- | -- |
| `StrictMaterials_AcceptsAUserValueMarkedVerified` | -- | -- |
| `StrictMaterials_RefusesUnverifiedValues_ListsThem_AndAcceptsOverridesAndVerifiedOnes` | -- | -- |
| `Tgg_Dispersion_FollowsTheMeasuredTable` | Schlarb and Sugg, Phys. Status Solidi B 182, K91 (1994): measured n of TGG at 20 C (refractiveindex.info main/Tb3Ga5O12/Schlarb) | 1e-3 |
| `UserCatalogue_ReplacesAQuantityWithANotice_AndCanAddAMaterial` | -- | -- |
| `VerifiedDispersionSets_RecordTheirCheck` | -- | -- |

## CavityAndCoherenceTests

Resonators, partial coherence, thermal stress birefringence, amplifier thermal lens.

| test | reference | tolerance |
|---|---|---|
| `AmplifierThermalLens_OnRequest_ActsOncePerPass` | N thin lenses in contact: 1 / f_total = N / f; Gaussian lens law (Self 1983) | 0.3 % |
| `PartialCoherence_GaussianSchellBeam_SpreadsWithItsM2` | Gaussian-Schell beam: w(z) = w0 sqrt(1 + (M^2 z / z_R)^2) (Starikov & Wolf 1982; Siegman M^2 law) | 2 % |
| `PartialCoherence_M2OfOne_IsTheCoherentBeam_AndNeedsACartesianGrid` | -- | -- |
| `Resonator_FoxLi_ConvergesToTheGaussianEigenmode` | cross-model: Fox-Li wave iteration vs the ABCD eigenmode | 3 % |
| `Resonator_FoxLi_PlaneMirrors_LossFallsAsNToTheMinusThreeHalves` (slow) | Fox & Li (1961), Vainshtein: loss per transit of plane circular mirrors falls as N^-3/2 at large N; loss near 20 % at N = 1 | 25 % |
| `Resonator_LensGuide_HasTheAbcdEigenmode_AndPutsItOnTheTrackedBeam` | ABCD eigenmode of a periodic lens guide: w0^2 = (lambda / pi) sqrt(L (4 f - L)) / 2 at the waist between lenses (Kogelnik & Li, Appl. Opt. 5, 1550 (1966)) | 1e-3 |
| `Resonator_LinearTwoMirrorCavity_MatchesTheMirrorFormulas` | two-mirror cavity: (A + D) / 2 = 2 g1 g2 - 1, g = 1 - L / R; w^2 at a mirror of a symmetric cavity = (lambda L / pi) sqrt(1 / (1 - g^2)) | 1e-3 |
| `Resonator_Unstable_WarnsAndHasNoMode` | stability \|(A + D) / 2\| < 1: L > 4 f is unstable | -- |
| `ThermalStress_DepolarisedFraction_MatchesKoechner_AndTendsToAQuarter` | Koechner, Solid-State Laser Engineering, ch. 7: D = (1 - sin C / C) / 4, C = n0^3 alpha C_B P_h / (lambda K) | 2 % |

## CellModeTests

Multi-pass cell modes (Kerr-matched Herriott and quasi-concentric cells).

| test | reference | tolerance |
|---|---|---|
| `EmptyCell_MatchesKogelnikLiForSymmetricAndAsymmetricCells` | Kogelnik & Li, Appl. Opt. 5, 1550 (1966): w0^2 = (lambda d / pi) sqrt(g1 g2 (1 - g1 g2)) / \|g1 + g2 - 2 g1 g2\|, w1^2 = (lambda d / pi) sqrt(g2 / (g1 (1 - g1 g2))) | 1e-9 |
| `EmptyCell_OfTheSchulteCell_HasThePublishedSpotSizes` | Schulte et al., Opt. Lett. 41, 4511 (2016): empty-cell mode of 540 mm / R = 350 mm: waist 220 um, 459 um on the mirrors (as read in docs/papers/schulte-2016-mpc-fused-silica) | 0.3 % |
| `HerriottSpacing_ClosesThePatternAfterTheChosenRoundTrips` | Herriott, Kogelnik & Kompfner, Appl. Opt. 3, 523 (1964): the pattern closes after N round trips when N theta = 2 pi k, cos(theta / 2) = g | 1e-9 |
| `KerrLensOfTheHelper_ReproducesTheRadialModelsRmsRadius` | Vlasov, Petrishchev & Talanov (1971) moment law: the thin-lens power 1/f = n2 I0 dz / w^2 (I0 = 2P / pi w^2) reproduces the rms-radius evolution of the engine's radial Kerr propagation (first order in P / P_cr); the on-axis parabolic lens (4x) does not | 2 % |
| `MatchedMode_ReproducesItselfAndTendsToTheEmptyCellModeAtLowPower` | self-consistency of the round trip: the matched q reproduces itself with the Kerr lenses it creates (residual < 1e-9); a vanishing power gives the empty-cell mode | 1e-9 |
| `WeakKerrPlateAtTheWaist_ShiftsTheRoundTripStabilityAsTheThinLensFormula` | first order in a thin Kerr lens: cos(theta') - cos(theta) = -(Phi_total / 2) z_R sin(theta), theta = 2 arccos(g) the round-trip Gouy phase of the symmetric cell, Phi = 2 n2 dz P / (pi w0^4) per pass | 0.3 % |
| `WhenNoStableMatchedModeExists_TheHelperReportsIt` | the matched mode ceases to exist when the Kerr lens pushes the round trip out of its stability range; the helper says so instead of returning a mode | - |

## Chi2Tests

χ(2) crystals: SHG, OPA, QPM, cascading, phase matching, aux beams.

| test | reference | tolerance |
|---|---|---|
| `ActiveInstruments_ConversionDelayScanAndPhaseMatchingCurve` | -- | -- |
| `AuxBeam_FromSavedResults_PumpsAnOpa` | -- | -- |
| `BeamProfileInstrument_ReportsRadiusAfterARadialElement` | Gaussian beam w(z) | 1 %, 2 % |
| `CarrierChange_ShgThenGlass_ReplansTheGridAndKeepsEnergy` | -- | -- |
| `CascadedChi2_PhaseMatchesEffectiveKerrFormula` | cascaded χ(2): φ = κ² P / Δk (L − sin(Δk L)/Δk) | 2 % |
| `GridPlanner_StronglyChirpedInputIsNotWrapped` | -- | -- |
| `GridResampler_ExactRoundTripToADerivedGrid` | -- | -- |
| `Opa_SmallSignalGain_MatchesCoshSquared_AndConservesPhotons` | OPA cosh² small-signal gain; Manley-Rowe | 0.1 % |
| `PhaseMatchCalculator_ReproducesLiteratureAngles` | SNLO: BBO type-I SHG 29.2° (800 nm), 23.4° (1030 nm) | 1 %, 5 %, 10 % |
| `Qpm_AveragedAndDomainResolvedModelsAgree` | -- | -- |
| `Shg_AngleDetuning_FollowsSincSquared` | sinc²(Δk L / 2) phase-mismatch law | 1 % |
| `Shg_PlaneWave_MatchesArmstrongTanhSquared` | Armstrong et al., Phys. Rev. 127, 1918 (1962): tanh² SHG | 1e-09, 0.02 % |
| `Shg_RadialModelAgreesWithGaussianOverlapAtLowConversion` | Gaussian-mode overlap of low-conversion SHG | 1 % |
| `UserElement_CSharpCallbackAppliesSpectralPhase` | -- | -- |

## ComponentTests

Components: beam tracking, ND filters, plates and ghosts, polarisers, Faraday rotators, waveplates, chirped mirrors, aberrations, pedestals.

| test | reference | tolerance |
|---|---|---|
| `Aberration_StrehlOnOneDBeamAndPhaseOnSpatialBeam` | Maréchal approximation S = exp(−(2π σ)²) | 1 % |
| `BeamTracking_FeedsNonlinearElementsWithoutABeam` | -- | -- |
| `BeamTracking_LensFocusMatchesGaussianOptics` | Gaussian optics (ABCD law) | 1e-12, 0.05 %, 1 % |
| `BeamTracking_OffAxisMirrorIsAstigmatic` | off-axis mirror: f_t = R cos θ / 2, f_s = R / (2 cos θ) | 0.05 % |
| `ChirpedMirror_GddRippleAndComplementaryPairs` | -- | -- |
| `CrossCorrelator_PostPulseArtefactAndNoiseFloor` | -- | -- |
| `FaradayRotator_RotatesNonReciprocallyWithVerdetDispersion` | Faraday rotation V(λ) B L, non-reciprocal | 1e-09, 0.01 %, 0.1 % |
| `Ghost_KerrMediumCreatesPrePulsePartner` | pre-pulse from Kerr phase: level ∝ B² (Didenko et al., Opt. Express 16, 3178 (2008)) | 25 % |
| `Ghosts_OfBulkCrystalAndLensEndFaces` | ghost delay 2 n_g L / c, level R² | 2 % |
| `NdFilter_AbsorptiveAndReflectiveOd` | T = 10^−OD | 0.2 % |
| `NdFilter_OdPerWavelengthShapesTheSpectrum` | T = 10^−OD(λ) | 1 % |
| `Pedestal_AseLevelAndPhaseNoiseConservesEnergy` | -- | -- |
| `Pedestal_Measured_FollowsTheRecordedTrace` | a recorded contrast trace as the input pedestal: the pulse's intensity follows the trace | 25 % (a random field: ~100 independent speckles in the averaging window, 10 % rms) |
| `Pedestal_ReplicaPostPulseAndPrePulse` | -- | -- |
| `Plate_GhostReplicaDelayAndIntensity` | ghost delay 2 n_g d / c, level R² | 0.2 %, 3 % |
| `Plate_GhostTiltedOutOfTheBeamByAWedge` | -- | -- |
| `ThickWaveplate_RetardanceFromMaterialDispersion` | retardance 2π Δn(λ) d / λ from the crystal's indices | 1e-09, 0.01 %, 0.1 %, 5 % |
| `ThickWaveplate_SplitsPulseByGroupDelayDifference` | group-delay difference Δn_g d / c | 5 % |
| `ThinFilmPolarizer_TransmitsPReflectsS` | thin-film transfer matrix at the design angle | -- |
| `VectorSpatial_NonlinearEllipseRotationAcrossTheBeam` | nonlinear ellipse rotation B sin 2χ / 3, local across a Gaussian beam | 2 %, 4 %, 5 % |
| `Zernike_NollOrderingAndOrthonormality` | Noll, J. Opt. Soc. Am. 66, 207 (1976): ordering and orthonormality | -- |

## ComputeTests

CPU FFTs and compute back-ends.

| test | reference | tolerance |
|---|---|---|
| `AutoBackend_WithoutDevices_RunsOnTheCpu` | -- | -- |
| `ComputeFftOption_SelectsTheProvider_AndResultsAgree` | -- | -- |
| `EnvelopeFastPath_GivesTheGeneralPathResult_ScalarAndVectorWithRaman` | -- | -- |
| `FftProviders_ManagedAlwaysWorks_NativeOnesPassTheSelfCheckOrSayWhy` | -- | -- |
| `Stockham_MatchesRadix2AndTheDft_ForEveryPowerOfTwo` | direct DFT | -- |

## CrystalChainTests

Crystal chains and phase-matching limits: SHG to THG, length against bandwidth.

| test | reference | tolerance |
|---|---|---|
| `AutoCut_OfAFedThirdHarmonicStage_IsPhaseMatched` | BBO type I THG of 1064 nm: theta = 31.3 deg (Dmitriev, Handbook of Nonlinear Optical Crystals) | 0.3 deg |
| `Conversion_GrowsAsLengthSquared_ThenLinearlyOnceWalkOffLimitsIt` | Weiner, Ultrafast Optics: efficiency grows as L^2 for L << tau / GVM, as L beyond it | 2 % |
| `CrossedAutocorrelator_OnACartesianGrid_HasTheGeometricSmearing` | autocorrelation with geometric smearing: FWHM^2 = 2 tau^2 + (w sqrt(ln 2) 2 sin(phi / 2) / c)^2 | 1 % |
| `CrossedBeams_OneDimensionAndCartesianGiveTheSameEnergy` | cross-check: 1-D coupling against a 2-D Cartesian grid | 1 % |
| `CrossedThg_AngleFromTheArms_EqualsTheStatedAngle` | the arms and the interaction state the same geometry | 1e-9 |
| `CrossedThg_NeedsADifferentCutThanCollinear` | a crossing angle changes the cut, or the beam would not phase match | exact |
| `CrossedThg_RecordsTheOutputAngles` | tangential momentum and Snell: w3 sin t3 = w1 sin t1 + w2 sin t2 | 1e-3 |
| `CrossingBeams_InteractOverTheirOverlapOnly` | Gaussians crossing at mid-crystal: effective length l sqrt(pi) erf(L / 2 l), l^2 = (wa^2 + wb^2) / slope^2 | 2 % |
| `DelayScanOfTheThirdHarmonic_IsTheCrossCorrelation` | third-order cross-correlation of Gaussians: FWHM = sqrt(tau^2 + (tau / sqrt 2)^2) | 1 % |
| `NoncollinearShg_CutFollowsTheAnalyticCondition` | type I phase matching of crossing equal beams: n_e(2w, theta) = n_o(w) cos(alpha / 2) | 0.01 deg |
| `NoncollinearShg_DetuningFollowsSincSquared` | sinc^2(dk L / 2) in the crossed geometry | 1 % |
| `NoncollinearShg_OfOneBeam_IsRefusedWithAPointer` | one beam cannot cross itself | exact |
| `SecondHarmonicBandwidth_IsSetByTheCrystal` | sinc^2 phase-matching bandwidth: 0.886 / (GVM L); GVM from the crystal's group indices | 3 % |
| `ShgThenSfg_ConservesPhotonsAtTheThirdHarmonic` | Manley-Rowe: one photon each of w and 2w per photon of 3w | 1e-3 |
| `ShgThenSfg_MainAndFedBeamsCanSwap` | the crystal does not care which beam is called main | 1e-3 |
| `YTiltOnAnArm_Warns` | a y tilt is not in the crystal's plane: it must say so | exact |

## DelayedLightTests

Delayed light: the tail ledger, ghosts, echoes, coating ripple, scatter, detectors.

| test | reference | tolerance |
|---|---|---|
| `CoatingRipple_AmplitudeRippleOnlyTakesLightAway` | amplitude-ripple energy 1 − √(2/π) σ + σ²/2 | 0.5 % |
| `CoatingRipple_FirstOrderPedestal_HasEnergySigmaSquaredWithinTheMaxDelay` | first-order phase-noise pedestal energy σ² | 1e-09, 30 % |
| `CoatingRipple_IsReproducibleFromTheSeed` | -- | -- |
| `DelayedScatter_OfAPlate_DefaultsToTheBulkBackscatterDelay` | -- | -- |
| `DelayedScatter_ReturnsTheFractionAfterTheGivenDelays_AndConservesEnergy` | -- | -- |
| `DelayedScatter_SpectralWeightFollowsALambdaPowerLaw` | λ^−n spectral weight | 25 % |
| `Detector_PhotonCounting_WeightsTheEnergySpectrumByLambda` | photon number ∝ λ x energy | 1e-09 |
| `Detector_Preset_FillsTheInstrumentAndTheReadout_FromTheRunSpecOrAFile` | -- | -- |
| `Detector_Saturation_ClipsThePeak_AndStitchingRestoresTheContrast` | -- | -- |
| `Detector_SpectralResponse_MultipliesTheSpectrumByTheTable` | -- | -- |
| `Detector_Stitching_RaisesTheFloorByTheInverseAttenuation` | -- | -- |
| `Ledger_CrossCorrelator_ReadsTheGhostInTheProbe` | -- | -- |
| `Ledger_CrossPhase_MatchesTheFullNonlinearSolutionToFirstOrder` | cross-check: the full nonlinear difference field | 10 % |
| `Ledger_EnergyBudgetCloses_WithTheTailBooked` | -- | -- |
| `Ledger_GhostOfANonlinearPlate_LandsAtItsDelay_InEverySolver` | Airy: first replica at 2 n_g d / c with intensity R^2 | 1 %, 20 % |
| `Ledger_GhostOfAPlate_MatchesTheBruteForceBigWindow` | cross-check: one large grid holding the whole window | 1e-05, 0.1 % |
| `Ledger_LightLeavingTheMainWindowComesBack` | -- | -- |
| `Ledger_Off_ChangesNothing` | -- | -- |
| `Ledger_SmoothElements_SkipTheExactStep_WithTheSameResult` | -- | -- |
| `Ledger_TailPassesThroughTheMaterialOfASpatialElement` | cross-check: the same plate, 1-D and radial | 2 % |
| `Ledger_WarnsWhenTheTailIsAFewPercentOfTheEnergy` | -- | -- |
| `MirrorEcho_AWedgedSubstrateSendsTheEchoOutOfTheBeam` | -- | -- |
| `MirrorEcho_DelayIsTwoGroupIndexThicknessOverC` | echo delay 2 n_g d / c (fused silica n_g = 1.4635 at 1030 nm) | 0.3 %, 1 %, 8 % |
| `MirrorEcho_LeakageFollowsTheCoatingTransmissionAndTheBackFace` | echo amplitude T r_b / r from Stokes reciprocity | 1 % |
| `MirrorEcho_NeedsTheTailOrTheWindow_AndTheLedgerCarriesIt` | -- | -- |
| `Plate_Ghost_FollowsTheWavelengthDependentCoatingTable` | ghost level R(λ)² | 1 %, 2 % |
| `SurfaceScatter_InAnEngineRun_BooksTheLossAndNeedsItsInputs` | -- | -- |
| `SurfaceScatter_LossIsTheTisOutsideTheMode_AndFollowsLambdaMinusTwo` | total integrated scatter TIS = (4π σ cos θ / λ)² | 1e-12, 0.1 % |
| `SurfaceScatter_WithADelay_ReturnsTheInModePartAfterTheKernel` | -- | -- |
| `Window_AutoGrid_KeepsItsBehaviour` | -- | -- |
| `Window_ExtendForSatellites_GrowsAManualWindow` | -- | -- |
| `Window_ManualGridTooSmall_NamesTheGhostItsDelayAndEnergy` | -- | -- |
| `Window_PedestalReplicaThatWraps_IsNamed` | -- | -- |
| `Window_Strict_TurnsTheWarningIntoAnError` | -- | -- |

## DiagnosticsTests

Cameras, wavefront sensor, Wigner and space-time probes.

| test | reference | tolerance |
|---|---|---|
| `Camera_On1DElement_ImagesTheBeamModelAndSaysSo` | Gaussian beam w(z) | 2 % |
| `Cameras_GaussianBeam_NearFarFieldAndWavefrontMatchGaussianOptics` | Gaussian optics: w(z), far field λf/(πw0), R(z) | 0.5 %, 1 % |
| `SpaceTime_LinearGaussian_HasNoSpatioTemporalCoupling` | -- | -- |
| `Wigner_MarginalIsThePowerAndFieldProbeRecoversTheChirp` | Wigner time marginal = \|A(t)\|²; known linear chirp | 1 % |
| `Zernike_FitRecoversKnownCoefficients` | known Zernike coefficients | -- |

## DiffractionTests

Diffraction by apertures.

| test | reference | tolerance |
|---|---|---|
| `AbsorbingBoundary_RemovesTheWrappedLight_AndTheLossBudgetCloses` | separable Fresnel integral on the axis | 2 % |
| `FarField_AiryAndSincZerosAreWhereFraunhoferPutsThem` | Fraunhofer: Airy zero 0.6098 λz/a, sinc² zero λz/2a | 2 % |
| `GaussianAndFlatTopThroughRoundIris_ProfilesMatchTheFresnelIntegral` | Fresnel diffraction integral (quadrature); T = 1 − exp(−2a²/w²) | 1 % of the peak |
| `HardAperture_EdgeCellsCarryTheirAreaFraction_AndCoarseIrisesWarn` | T = 1 − exp(−2a²/w²) | dx / 2a |
| `LinearSpatialGap_PropagatesTheProfileOnly_AndEqualsTheFullFieldPropagation` | -- | -- |
| `SquareBeamThroughSquareAperture_CutMatchesTheSeparableFresnelIntegral` | separable Fresnel integral of a rectangular aperture | 2 % of the peak |
| `SquareShapeOnARadialGrid_IsRejected_AndUnknownKindsAreErrors` | -- | -- |
| `UniformBeamThroughRoundIris_OnAxisFluenceFollowsFresnelZones` | Born & Wolf 8.8: on-axis 4 I0 sin²(πN/2) | 0.3 %, 1.5 % |

## FiberTests

Fibres: solitons, dispersion, loss and gain, Raman, noise and coherence.

| test | reference | tolerance |
|---|---|---|
| `Coherence_LostInLongPulseSupercontinuum` (slow) | Dudley, Genty & Coen, Rev. Mod. Phys. 78, 1135 (2006): coherence of MI supercontinuum | -- |
| `DispersionCurve_EqualsTaylorCoefficients` | D(λ) = −(2πc/λ²) β2 | 1 % |
| `DispersiveWave_ResonanceMatchesPhaseMatching` | dispersive-wave phase matching (Akhmediev & Karlsson 1995) | 0.3 %, 1 % |
| `FundamentalSoliton_PropagatesUnchanged_AndMetricsAreRight` | fundamental soliton sech shape (NLSE) | 1 % |
| `LossGainAndGainGoal` | exponential loss and gain in dB | 0.01 %, 0.1 % |
| `QuantumNoise_OnePhotonPerMode_AndCoherentSoliton` | one photon per mode: N ħω of energy | 5 % |
| `RamanModels_NormalisedAndHollenbeckPeaksAt13THz` | Hollenbeck & Cantrell, JOSA B 19, 2886 (2002): gain peak 13.2 THz | 1 %, 3 % |
| `SolitonSelfFrequencyShift_OnACoarseGrid_MatchesGordon` | Gordon, Opt. Lett. 11, 662 (1986): SSFS rate (perturbation integral over the Raman gain) | 2 % |
| `TaperedGamma_BIntegralFollowsTheIntegral` | B = ∫ γ(z) P dz | 1 % |

## FieldToolsTests

Cw sources, nonlinear absorbing boundary, Gerchberg-Saxton, near-field maps.

| test | reference | tolerance |
|---|---|---|
| `AbsorbingBoundary_InANonlinearElement_RemovesTheWrappedLight` | separable Fresnel integral on the axis: same pattern as the linear absorber | 3 % |
| `CwSource_InAKerrPlate_PicksUpOnlyAPhase` | B = k n2 I L of a constant intensity; no spectral change | 1e-3 |
| `CwSource_IsOneFrequency_AndThroughAnIrisGivesTheFresnelIntensity` | Fresnel integral on the axis, 4 I0 at N = 1 (Born & Wolf 8.8); power = I x area | 1.5 % |
| `GerchbergSaxton_Command_WritesAPhaseMapTheEngineReads` | -- | -- |
| `GerchbergSaxton_RecoversAKnownWavefront` | Gerchberg & Saxton, Optik 35, 237 (1972): a known phase recovered from two intensities | far field 1 %, phase rms 0.1 rad over the beam |
| `MeasuredPng_WithBackgroundCentreAndThreshold_GivesTheBeam` | Gaussian beam w = 2 sigma of a sampled image | -- |

## FittingConditionsTests

Fitting plugin: conditions, dependence, surfaces.

| test | reference | tolerance |
|---|---|---|
| `Conditions_HeldOutCondition_IsPredictedWithinTheNoise` | chi^2 per point of an unfitted condition near 1 when the model is right | chi^2/point < 1.5 with noise equal to the stated sigma |
| `Conditions_HeldOutCondition_TheModelCannotPredict_IsFlagged` | -- | -- |
| `Conditions_NeedingAtLeastOneTrainingCondition_Throws` | -- | -- |
| `Conditions_PerConditionParameter_GetsOneValuePerTrainingCondition` | synthetic: one spectrometer resolution per condition | 3 % |
| `Conditions_SharedParameterThatAConditionAlsoSets_IsRefused` | -- | -- |
| `Conditions_SingleCondition_IsUnchanged_NoConditionKeysInTheReport` | -- | -- |
| `Conditions_TwoInputPulses_RecoverTheSharedFilter` | synthetic: shared filter parameters generated by the same forward model | 1e-3 relative |
| `Dependence_Kinds_MatchTheirFormulas` | power law c0 u^c1 evaluated analytically | 1e-12 |
| `Dependence_NeedingMoreDistinctConditionsThanCoefficients_Throws` | -- | -- |
| `Dependence_Proportional_RecoversTheSlopeOfANonlinearPhaseFromFiveEnergies` | B-integral: the nonlinear phase of a thin Kerr plate grows linearly with the pulse energy | 1e-3 relative on the slope |
| `Network_ConstraintsHoldExactly_ForAnyWeights` | constraints built into the output layer hold exactly (squared weights are non-negative, a sigmoid lies in (lo, hi)) | exact (strict inequalities) |
| `Network_RecoversAHiddenSmoothMap_WithItsConstraintsHolding` | synthetic hidden map b(E) = 8 E^2 / (E^2 + 1.44) (rad, E in uJ): saturating, monotone, bounded | 0.15 rad at held-out energies (measured 0.11 and 0.01); constraints exact |
| `Surface_KnownSmoothSurface_IsRecoveredAcrossSpectrumAndCondition` | synthetic: five spectral knots, each linear in a condition variable | 0.01 absolute on the knots relative to the central one |
| `Surface_Penalties_VanishOnAPlane_AndGrowWithRoughness` | second differences of a straight line are zero | 1e-12 |
| `Surface_SmoothnessPenalty_ReducesTheErrorOfASurfaceFittedToNoisyData` | -- | -- |

## FoundationTests

Foundations: JSON, expressions, NPZ, FFT, dispersion maths, materials, Fresnel, thin films, Treacy.

| test | reference | tolerance |
|---|---|---|
| `DispersionMath_RecoversPolynomialPhase` | known polynomial phase | 1e-09, 1e-06, 0.01 % |
| `Expression_PrecedenceFunctionsAndErrors` | -- | -- |
| `Fft_RoundTripParsevalAndSignConvention` | Parseval's theorem | 1e-12, 0.1 % |
| `Fresnel_EnergyConservationNormalIncidenceAndBrewster` | Fresnel equations: R + T = 1, Brewster angle | 1e-12 |
| `GratingPair_GroupDelayMatchesTreacy` | Treacy, IEEE JQE 5, 454 (1969) | 0.01 % |
| `Json_RoundTripCommentsTrailingCommas` | -- | -- |
| `Materials_IndicesMatchReferenceValues` | Ciddor (1996) air at 633 nm; Schott N-BK7 and Malitson fused silica n_d | 2e-05, 1 %, 3 % |
| `Npz_WriteReadRoundTrip` | -- | -- |
| `ThinFilm_QuarterWaveArAndBraggMirror` | quarter-wave AR and Bragg mirror (analytic thin-film results) | 1e-10, 1e-09 |

## FrogKindsTests

FROG geometries: PG, SD, XFROG, kind inference.

| test | reference | tolerance |
|---|---|---|
| `Frog_PcgpaAndEpie_RecoverAChirpedPulse_FromSdAndPgTraces_WithTheSign` | known chirped pulse; PCGPA 3 % duration, 6 % GDD; ePIE median of 3 seeds 5 % duration, 8 % GDD each | see summary |
| `Frog_PgAndSd_TraceCentroid_TiltsWithTheChirpRate` | Kane and Trebino 1993, App. C: PG slope 2b/3, SD slope 4b/3 | 2 % |
| `Retrieve_WithoutAKind_DoesNotAssumeShg_ForAPolarisationGateTrace` | SHG-FROG traces are symmetric in the delay; PG traces of a chirped pulse are not | exact |
| `Xfrog_PcgpaAndEpie_RecoverAChirpedPulse_WithTheSign` | known chirped pulse, known reference | 3 % duration, 6 % GDD, sign exact |
| `Xfrog_TraceCentroid_TiltsWithTheChirpRate_AtTheSumFrequency` | first moment of a Gaussian product: slope b T^2 / (T^2 + T_R^2) | 2 % |

## GainTests

Gain media, pumping, thermal lens and position-dependent properties.

| test | reference | tolerance |
|---|---|---|
| `GainElement_FrantzNodvik_SaturatedEnergyGain` | Frantz & Nodvik, J. Appl. Phys. 34, 2346 (1963) | 1e-12, 1e-09, 0.03 % |
| `GainElement_SmallSignalGainSpec_IsReproduced` | G = exp((σ_e β − σ_a (1 − β)) N L) | 1e-09, 0.1 % |
| `GainMedia_LibraryPeaksAndMcCumber` | McCumber relation; published Yb:YAG σ_e(1030 nm) ≈ 2.1e-24 m² and emission peaks | 1e-09, 0.02 %, 0.15 %, 2 %, 15 % |
| `Pumping_CwThinCrystal_RateEquationSteadyState` | rate-equation steady state N2/Nt = σ_ap Φ / ((σ_ap + σ_ep) Φ + 1/τ) | 1 % |
| `Regen_AutoRoundTrips_EjectsAtTheEnergyMaximum_AndConservesEnergy` | -- | -- |
| `Regen_SmallSignal_GrowsAsGainSquaredTimesCavityTransmission` | small-signal regen growth (G0² (1 − L))^N | 0.2 % |
| `SpatialGain_RadialModel_ThermalLensAndPerPointInversion` | -- | -- |
| `SteadyState_PulseTrain_ConvergesAndMatchesTheFullModel` | -- | -- |
| `ThermalLens_TophatAndGaussianPump_MatchAnalyticFocalLengths` | f = 2πK w²/(P_h dn/dT) (top hat), πK w²/(P_h dn/dT) (Gaussian; Innocenzi 1990) | 1e-09 |
| `TimeOrderedSaturation_RedLeadingChirp_ShiftsTheSpectrumRed` | -- | -- |

## GasTests

Gases: rotational Raman, indices, the refractiveindex.info database.

| test | reference | tolerance |
|---|---|---|
| `Gases_IndicesOfNewSpecies` | published refractivities of the gases | 1e-09, 2 % |
| `RefractiveIndexInfo_AsAMaterialInARun` | -- | -- |
| `RefractiveIndexInfo_FormulasAndTablesMatchTheLibrary` | refractiveindex.info formulas and tables | 1e-12, 1e-09, 2 % |
| `RotationalRaman_AdiabaticLimitMatchesIsolatedMoleculeTheory` | isolated-molecule alignment, high-T limit: n2 = N Δα² / (4 c ε0² 15 kT) | 3 % |
| `RotationalRaman_LongPulseSpmUsesTheTotalN2_AndCircularSeesAQuarter` | rotational n2 of circular light = 1/4 of linear (tensor Raman, Hellwarth) | 3 %, 5 % |
| `RotationalRaman_RevivalsAtTheRotationalPeriod` | rotational revival period 1 / (2 B c) | 5 % |

## GpuTests

GPU back-ends (CUDA / OpenCL) against the CPU.

| test | reference | tolerance |
|---|---|---|
| `ConcurrentRuns_ShareTheDevice_AndMatchTheCpu` | -- | -- |
| `DevicesCommand_ListsTheCpuAndTheDevice_AndTheSelfTestPasses` | -- | -- |
| `Envelope_ScalarAndVector_MatchTheCpu_AdaptiveAndFixed` | cross-check: the CPU solver | 1e-09 |
| `Fft_BuiltInAndVendor_MatchTheManagedFft` | cross-check: the managed CPU FFT | -- |
| `SmallOrUnsupportedProblems_StayOnTheCpu_WithANote` | -- | -- |
| `Spatial_RadialAndCartesian_MatchTheCpu` | cross-check: the CPU solver | 1e-09 |
| `Spatial_UppeCoupling_MatchesTheCpu` | cross-check: the CPU solver | -- |
| `TilesAndModulator_IntoANonlinearPlate_MatchTheCpu` | cross-check (C6 of the S2 roadmap): the S2 primitives -- a coherent tile source and a modulator -- feeding a nonlinear spatial plate give the CPU result on the device | 1e-9 relative |

## GridSolverTests

Grid, carrier and solver accuracy.

| test | reference | tolerance |
|---|---|---|
| `CircularAperture_OnAnEllipticalBeam_IsTheTwoDimensionalIntegral` | elliptical Gaussian through a circular aperture: 2-D quadrature; small aperture T = 2 a^2 / (wx wy); round beam 1 - exp(-2 a^2 / w^2) | 1e-6 (quadrature), 1e-5 (small-aperture limit) |
| `DelayedResponseLongerThanTheWindow_IsWarnedAbout_AndTheAutoGridHoldsIt` | periodic time grid: the response to a pulse must fit the window (convergence against a window four times larger) | total variation of the spectra 0.02 |
| `FiberDispersionTable_Coarse_ReproducesBeta2_BetweenThePoints` | definition D = -2 pi c beta2 / lambda^2 (Agrawal, Nonlinear Fiber Optics); beta2(w) = b3 (w - wz) + b4 (w - wz)^2 / 2 | 2e-4 relative to beta2 (a linear interpolation of the 53-point table gave ~1e-3) |
| `Fiber_ElementLevelSelfSteepeningFalse_SwitchesItOff_AndBulkToo` | self-steepening switch: with it off the pulse stays symmetric in time (centroid shift 0); with it on the centroid moves (Agrawal, Nonlinear Fiber Optics, sec. 4.4) | off: centroid shift < 1 % of the on value |
| `Grating_TrackedAngularDispersion_IsTheGratingEquation_OnAnyGrid` | grating equation: d theta / d lambda = m / (period cos beta) (Treacy, IEEE JQE 5, 454 (1969), eq. 2) | 1e-6 relative |
| `PulseBroadenedToZeroFrequency_IsReportedAndBooked` | energy conservation of the envelope model: a pulse broadened to zero frequency loses energy there, and says so | exact booking |
| `Pulse_IsCentredOnTheRequestedCarrier_NotTheSnappedOne` | definition of the carrier: the input spectrum is centred on the requested wavelength | 1e-6 relative |
| `PureSpm_PhaseIsGammaPL_OnEveryGrid_At515_1030_And2000nm` | pure SPM in a dispersion-free fibre: A_out = A_in exp(i gamma \|A_in\|^2 L) | 2e-5 relative |
| `Shg_StronglyPhaseMismatchedThickCrystal_FollowsTheSincLaw_AtTheDefaultStepper` | plane-wave SHG at low conversion: eta(L) ~ sin^2(dk L / 2) (Armstrong et al., Phys. Rev. 127, 1918 (1962), eq. 2.14 limit) | 4 % (dispersion of the 5 ps pulse and finite bandwidth) |

## HitranTests

HITRAN plugin: line shapes, files, partition sums, absorption in air.

| test | reference | tolerance |
|---|---|---|
| `AgainstHapi_RealLines_AgreeWithTheReferenceImplementation` | HAPI (HITRAN reference code), H2O / CO2 / O2 at 250-350 K | 0.7 % integrated, 3.9 % worst point |
| `CsvFile_UsesHapiColumnNames` | -- | -- |
| `Doppler_WidthMatchesTheKineticFormula` | Doppler HWHM ν/c √(2kT ln2 / m) | 0.01 %, 1 %, 2 % |
| `EnvironmentAir_IsOffByDefault_AndPlainCiddorReproducesTheDefaultAir` | -- | -- |
| `Faddeeva_IsContinuousAcrossTheAsymptoticSwitch` | -- | -- |
| `Faddeeva_MatchesKnownValues` | Faddeeva function: w(0) = 1, erfcx, Dawson values | 1e-10, 1e-09 |
| `GapAir_OverridesTheEnvironmentForOneGap` | -- | -- |
| `LineStrength_ScalesWithDensityAndTemperature` | -- | -- |
| `LineStrength_UsesThePartitionTable_WhenThereIsOne` | HITRAN strength scaling with Q(T) (Rothman et al.) | 1e-09, 0.2 % |
| `LorentzLine_PeakAbsorptionAndDispersion_MatchTheAnalyticResult` | Lorentz line: peak n S / (π γ), dispersion extremum | 1 % |
| `ParFile_RoundTripsAHitranRecord` | -- | -- |
| `PartitionFile_NextToTheLineFile_IsFoundAndNamed` | -- | -- |
| `PartitionTable_InterpolatesLinearly_AndRefusesToExtrapolate` | -- | -- |
| `VoigtLine_DispersionIsTheHilbertTransformOfTheAbsorption` | Kramers-Kronig (numerical Hilbert transform) | -- |
| `WaterLines_InAGap_AbsorbAsBeerLambertOnTheGrid` | Beer-Lambert: 1 − exp(−α L) | 1 % |
| `WaterLines_InsideASolver_ReachTheTailLikeALinearGap` | cross-check: the same gap, linear, on the exact step | 2 % |
| `WaterLines_TailLedger_ReproducesTheBruteForce2nsWindow` | cross-check: one 2 ns window | 1e-05, 0.1 %, 2 % |
| `WaterVapour_FractionFollowsTheSaturationPressure` | saturation vapour pressure of water, 2339 Pa at 20 °C | 1e-12, 0.5 % |

## InstrumentCrystalComponentTests

Component sheet: instruments and chi(2) crystals at 515 nm / 1030 nm / 2 um.

| test | reference | tolerance |
|---|---|---|
| `Autocorrelator_Sech2_HasTheRatio1543_AtEveryCarrier` | Intensity autocorrelation of sech^2: FWHM ratio 1.5427 (Diels & Rudolph, Table 9.1) | 0.3 % |
| `BboShg_ConservesEnergy_AndEachPortHasItsCarrier_AtEveryCarrier` | Manley-Rowe / energy conservation of lossless SHG; out port = SH at lambda / 2, fundamental at lambda | 1e-6 energy, 0.2 % carrier |
| `CrossCorrelator_Gaussian_HasWidthRootThreeHalves_AndTheFloor` | Third-order correlation of a Gaussian: integral I(t) I^2(t - tau) dt has FWHM sqrt(3/2) tau_p; flat floor at the dynamic range | 0.5 %; 0.01 dB |
| `Frog_FrequencyMarginal_IsTheSpectrumsAutoconvolution` | SHG-FROG frequency marginal = autoconvolution of the spectrum: centre 2 w0, rms sqrt(2) sigma_w | 1e-4 centre, 1 % rms |

## InstrumentDeviceTests

Instrument device models: resolution, acceptance, detectors, filters.

| test | reference | tolerance |
|---|---|---|
| `Autocorrelator_ResolutionAndAcceptance` | AC width √2 FWHM, quadrature resolution sum | 2 %, 3 % |
| `Detector_CalibrationNoiseFloorAndBits_AreReproducible` | -- | -- |
| `EveryInstrumentEnum_RunsWithEachValue` | -- | -- |
| `Filter_InFrontOfTheInstrument_ActsOnTheMeasuredField` | -- | -- |
| `Frog_SpectralResolutionAndAcceptance_NarrowAndSmoothTheTrace` | SH bandwidth of a 60 fs pulse (~4.4 nm) | 3 %, 5 % |
| `Polarimeter_ExtinctionRatioScalesStokes` | Malus: S1 = (ER − 1) / (ER + 1) | 1e-09 |
| `Settings_ListEveryParameterWithItsValueOrDefault` | -- | -- |
| `Spectrometer_NormalizeModes_IncludingLegacyBooleans` | -- | -- |

## InstrumentFixesTests

Instrument fixes: band edges, IAC peak, cross-correlator resolution and arms.

| test | reference | tolerance |
|---|---|---|
| `CrossCorrelator_GateArmPlate_GivesGhostLevelsRToThe2k_AtTheSecondHarmonicSpacing` | Fresnel: ghost k of a plate has intensity R^(2k); spacing 2 n_g(lambda/2) d / c | 3 %, spacing 0.1 % |
| `CrossCorrelator_WithResolution_TraceFloorAndReportAreConsistent` | linear smoothing: relative height of a distant pre-pulse is unchanged | 0.3 dB |
| `EnergyMeter_BandEdges_AreWeightedByTheFractionOfTheBin` | Gaussian spectrum: band fraction = CDF(w_hi) - CDF(w_lo) | 0.5 % |
| `InterferometricAutocorrelator_LongPulse_KeepsZeroDelay_AndSaysTheFringesAreUndersampled` | Diels and Rudolph: IAC peak to background 8:1 | 0.2 % |
| `InterferometricAutocorrelator_PeakToBackground_IsEight_ForEveryCarrierAndBandwidth` | Diels and Rudolph: IAC peak to background 8:1 | 0.2 % |

## InstrumentTests

Instruments: autocorrelators, spectrometer, compressor, polarimeter, d-scan.

| test | reference | tolerance |
|---|---|---|
| `Autocorrelator_DeconvolutionFactors` | autocorrelation widths: √2 x FWHM (Gaussian), sech² deconvolution; interferometric AC peak-to-background 8:1 | 1 %, 2 % |
| `Compressor_AfterStrongSpm_ReportsTheFwhmOnTheFullGrid` | Escoto et al., JOSA B 39, 1694 (2022), Eq. 2: C = 0.59 B + 1 within 5 % to B = 40 | 0.5 %, 5 % |
| `DScan_FindsGlassThatCompensatesTheChirp` | GDD of 1.5 mm fused silica at 800 nm | 2 %, 5 % |
| `Spectrometer_CompressorPolarimeterEnergy` | -- | -- |

## IonizationTests

Photo-ionisation and plasma.

| test | reference | tolerance |
|---|---|---|
| `Keldysh_MultiphotonOrderForFusedSilica` | Keldysh: K = ceil(U_g / ħω) | 8 % |
| `Plasma_ElectronDensityBlueshiftAndAbsorption` | ρ = ∫ W dt; Drude blue shift | 3 % |
| `Plasma_EnvelopeAgreesWithCarrierResolved_ForA10FsMidInfraredPulse` | cross-check: the envelope model against the carrier-resolved field model, same operator | blueshift 2 %, energy 1 %, density 1e-4 |
| `Plasma_FixedDensity_FollowsTheFrequencyResolvedDrudeIndex_InEnvelopeAndCarrierMode` | Drude response of free electrons: Δn(ω) = -ω_p² / (2 n0 ω (ω + iν)) (Couairon & Mysyrowicz, Phys. Rep. 441, 47 (2007)) | 1e-8 (solver tolerance 1e-11), both field models |
| `Plasma_FixedDensity_SpatialModel_AppliesTheSameDrudeFilter` | the same Drude filter in the radial spatial model (a uniform plasma commutes with diffraction): energy transmission | 1e-7 |
| `Plasma_FrequencyResolved_TakesTheDriftEnergyOfTheElectronsBornInTheField` | drift energy of electrons born at rest in a linearly polarised field: U_p = e² E0² / (4 m ω²) per electron (Corkum, Phys. Rev. Lett. 71, 1994 (1993)) | 2 % |
| `Plasma_FrequencyResolved_TendsToTheCarrierValue_ForNarrowSpectra` | narrow-band limit: the frequency-resolved operator tends to the carrier value as the bandwidth goes to zero; R(ω0) = 1 | 1e-15; difference halves with the bandwidth |
| `Ppt_MultiphotonAndTunnelLimits` | PPT rate: multiphoton I^K and tunnelling (ADK) limits | 3 %, 15 % |
| `SpecialFunctions_DawsonAndElliptic` | tabulated Dawson and elliptic-integral values | 1e-12, 1e-07, 1e-06 |
| `Uppe_AgreesWithEnvelopeForParaxialBeams_AndPlasmaClampsTheFocus` (slow) | cross-check: the envelope model for paraxial beams | 1e-06, 1 % |

## LaserTests

Lasers: Haus master equation, absorbers, cavities, apertures, carrier-resolved χ(2).

| test | reference | tolerance |
|---|---|---|
| `Aperture_OnACarriedProfile_HardAndSoft` | -- | -- |
| `Ase_SeedsNoise_AndTheBudgetCloses` | -- | -- |
| `Cavity_SpatialLensGuide_ReproducesTheEigenmode` | ABCD eigenmode z_R = f, w0 = √(λf/π) | 8 % |
| `Cavity_YbSesamOscillator_SolitonAndQmlDiagnostics` | -- | -- |
| `Chi2Response_CarrierResolvedShg_MatchesLowConversionFormula` | low-conversion SHG P = K P² | 0.1 %, 5 % |
| `Diagnostics_CountPulsesAndBackground` | -- | -- |
| `ExpressionResponse_SeesPositionAndFluence` | -- | -- |
| `FastAbsorbers_LoseLessAtHighPower` | unsaturated fast absorber 1 − exp(−q0) | 1 % |
| `Haus_CwLaser_GainClampsAtTheLoss` | cw laser: E = E_sat (g0 / L − 1) | 0.2 % |
| `Haus_SolitonModeLocking_ObeysTheAreaTheorem` | soliton area theorem (Haus master equation) | 1e-12, 10 % |
| `Sesam_SlowAbsorber_SaturatesAsTheEnergyIntegral` | slow absorber: q0 (1 − e^−S) / S | 1e-09, 2 % |
| `ThermalLens_ExportsAsGradient1` | GRIN thin lens n_r2 = −1 / (2 f L) | 1e-09 |

## LinearComponentTests

Component sheet: linear elements and splitters at 515 nm / 1030 nm / 2 um, 1 ps / 10 fs, Sequential = Branched.

| test | reference | tolerance |
|---|---|---|
| `FedBeam_OfAnotherColour_KeepsItsCarrier` | Linear superposition: a lossless splitter sends (1 - R) a + R b of each colour to port c; different colours do not interfere | 1e-6 |
| `GratingPair_WithoutAPropagatingOrder_IsAnError` | -- | -- |
| `GridResampler_ShiftBeyondTheBand_IsExact` | Band-limited resampling between exactly related grids is exact | 1e-9 |
| `LeakMirror_ReportsTheGddOfThePortThatContinues` | leakMirror gddFs2 / leakGddFs2 (the phase applied to each port) | 1e-9 |
| `LinearSheet_EveryElement_PassesAtEveryCarrierAndBandwidth` | Sellmeier (Malitson 1965) x Ciddor (1996); Treacy, IEEE JQE 5, 454 (1969); Taylor phase; Stokes relations | GDD 1e-6 (glass 1e-5, air 1e-4), energy 1e-9, Branched = Sequential 1e-12 |

## LossAndOverrideTests

Loss budget and overrides.

| test | reference | tolerance |
|---|---|---|
| `LossBudget_ClipFresnelScatterAddUp` | clip 1 − exp(−2a²/w²), Fresnel 4 % | 1e-12, 1e-09, 3 % |
| `Overrides_MaterialIndexOverrideBeatsTracedData` | -- | -- |
| `Overrides_PriorityAndProvenance` | -- | -- |
| `SystemFile_RoundTripAndValidation` | -- | -- |

## MaterialsCrystalsTests

Materials and crystals: d_eff of explicit cuts, delayed-response split, CS2, cubic chi(3).

| test | reference | tolerance |
|---|---|---|
| `BaF2_RefractiveIndex_AtSodiumD_Matches` | BaF2 at 20 C, sodium D: n = 1.4744 (Li, J. Phys. Chem. Ref. Data 9, 161 (1980)) | 0.001 |
| `Cs2_Gdd_MatchesTheAnalyticSecondDerivative` | closed-form second derivative of the one-pole Sellmeier formula: GVD = lambda^3 n'' / (2 pi c^2) | 0.3 % |
| `Cs2_LongPulse_EllipseRotation_UsesTheOrientationalTensor` | NER rotation = B sin(2chi) ((1-fR)/3 + fR b/2) with b = 3/2 for the orientational part (Maker-Terhune-Savage 1964; Hellwarth tensor) | 2 % |
| `Cs2_Pulse_PeakPhase_FollowsTheRetardedOrientationalResponse_AndCircularSeesLessThanLinear` | B = k0 n2 I0 L; peak phase = B [(1-fR) + fR d] with d = int h(s) I(-s) ds / I0 (retarded response of a Gaussian pulse); circular: (1-fR) 2/3 + fR d/4 (electronic Kerr + purely orientational, Boyd Nonlinear Optics 3rd ed. sec. 4.4) | 0.5 % |
| `Cs2_RefractiveIndex_AtSodiumD_MatchesKedenburgAndTheHandbook` | CS2 at 20 C, sodium D line: n = 1.6276 (Kedenburg et al. 2012; refractiveindex.info, CC0); CRC Handbook n_D(20 C) = 1.6319 for the liquid | 0.003 |
| `CubicKerr_RotatingCrystalAndInputTogether_ChangesNothing` | covariance: crystal azimuth and input angle rotate together | 1e-9 |
| `CubicKerr_SpmIndex_DependsOnTheAngleToTheCubicAxis` | cubic chi(3): index change along beta from [100] is n2 [1 - (sigma/2) sin^2(2 beta)] | 0.3 % |
| `CubicKerr_WithATransverseModel_IsRejected` | cubic anisotropy needs the envelope model: a spatial model throws instead of ignoring it | exact |
| `CubicKerr_WithZeroAnisotropy_EqualsTheIsotropicKerr` | sigma = 0 is the isotropic Maker-Terhune Kerr tensor: identical output | 1e-12 |
| `CubicKerr_Xpw_FollowsSin4BetaSquared_ForTheCutAlong001` | cubic chi(3) tensor (Boyd, Nonlinear Optics, sec. 4.2; Minkovski et al., Opt. Lett. 29, 2237 (2004)): XPW fraction (B sigma sin 4beta / 4)^2 / sqrt(3) | 3 % (O(B) corrections) |
| `CubicKerr_Xpw_In011Cut_FollowsTheTensorProjection` | cubic chi(3) tensor in the [011] cut: P_perp ~ sin b cos b (sin^2 b / 2 - cos^2 b) | 3 % |
| `KdpElement_ExplicitCut_RecordsDeffThatVariesSmoothlyWithPhi` | KDP type-I, explicit cut: recorded d_eff is smooth in phi (d36 \|sin 2phi\| E_z) | 3 % |
| `Kdp_TypeI_Deff_FollowsD36Sin2Phi_ForEveryPhi_AtFixedTheta` | KDP (-42m) type-I o o -> e: d_eff = d36 \|sin 2phi\| E_z (exact E-field vectors); textbook d36 sin(theta) sin(2 phi) within the walk-off angle (Dmitriev, Handbook of NLO crystals) | 1e-6 relative (exact); 4 % (textbook) |
| `OrientationalResponse_HasUnitIntegral_AndTheAnalyticPeakTime` | response normalisation: unit integral, causal, peak at rise*decay*ln(decay/rise)/(decay-rise) | 1e-3 |

## MeasuredSpectrumTests

Measured input spectra, comparisons and satellite room.

| test | reference | tolerance |
|---|---|---|
| `Comparison_BeyondACroppedReading_IsComparedWithZero` | -- | -- |
| `Comparison_CoarserRecording_IsMatchedByBlurringTheSimulation` | -- | -- |
| `Comparison_RecordingWiderThanTheSimulation_ComparesOnlyTheOverlap` | -- | -- |
| `InputSpectrum_AutoBackground_IgnoresTheCutOffEdge` | -- | -- |
| `InputSpectrum_BadBackground_IsAClearError` | -- | -- |
| `InputSpectrum_Csv_WithBackgroundKey` | -- | -- |
| `InputSpectrum_FlatFloor_ShortensTheTransformLimit_AndAutoBackgroundRestoresIt` | -- | -- |
| `InputSpectrum_NonFiniteRows_AreDroppedWithAWarning` | -- | -- |
| `InputSpectrum_OutOfOrderRows_AreSortedAndReported` | -- | -- |
| `InputSpectrum_RepeatedWavelength_NoLongerGivesNaN` | -- | -- |
| `InputSpectrum_Threshold_ZeroesNoiseBelowIt` | -- | -- |
| `SatelliteRoom_SumsCoherentlyAtEqualDelays_AndHonoursTheLevel` | -- | -- |
| `Window_ManyPlatesWithGhosts_DoesNotGrowWithEveryOrderOfEveryPlate` | -- | -- |

## MeasurementTests

Sampling apertures, measured data and fits.

| test | reference | tolerance |
|---|---|---|
| `Comparison_DataEqualToTheSimulation_GivesZeroResidual` | -- | -- |
| `Comparison_MissingFile_IsAWarningNotAFailure` | -- | -- |
| `Comparison_Normalisations_AndBackground` | -- | -- |
| `FitPaths_ReadAndWriteTheFiles` | -- | -- |
| `Fit_ParameterTheDataDoNotSee_IsFlagged` | -- | -- |
| `Fit_RecoversEnergyAndChirp_FromSpectrumAndAutocorrelation` | known truth: 260 nJ, +120 fs² | 1 % |
| `MeasuredData_CsvUnitsAndColumns` | -- | -- |
| `MeasuredData_NpzRoundTrip` | -- | -- |
| `PseudoInverse_FindsTheNullDirection` | -- | -- |
| `Sampling_FarFieldFibre_On1DBeam_FavoursTheBlue` | -- | -- |
| `Sampling_GaussianApertures_TransmitTheAnalyticFraction` | Gaussian through a pinhole / slit: analytic transmitted fraction | 1e-06, 2 %, 5 % |
| `Sampling_IgnoredByImagingInstruments_AndBadPlaneRejected` | -- | -- |
| `Sampling_Integrated_IsTheWholeBeam` | -- | -- |

## ModelledInstrumentsTests

Modelled instruments: IAC, FTS, FROG from elements; study node cache and array outputs.

| test | reference | tolerance |
|---|---|---|
| `Frog_FromElements_HasTheAutocorrelationMarginal` | delay marginal of an SHG FROG trace: the autocorrelation, FWHM sqrt 2 tau | 2 % |
| `Fts_FromElements_RecoversTheSpectrum` | Wiener-Khinchin: the interferogram's transform is the spectrum | 0.2 % centroid, 2 % width |
| `Iac_FromElements_HasTheEightToOneRatio` | interferometric autocorrelation: peak to background 8:1 | 1 % |
| `NodeCache_RerunsOnlyWhatFollowsTheChange_WithTheSameResult` | -- | -- |

## ModulatorTests

Modulator: pixelated phase and amplitude devices (SLM, DM surface), quantisation, fill factor, chromatic rules.

| test | reference | tolerance |
|---|---|---|
| `BadInput_IsRefusedWithAMessage` | validation: a map of the wrong shape, both phase and path, and a quantisation of one level are refused with a message | exact |
| `CrosstalkKernel_BlursTheWrittenMap_ByDiscreteConvolution` | cross-talk kernel w applied separably to the written map: a single raised cell of phase 1 with w = [1, 2, 1] / 4 gives 1/4, 1/2, 1/4 (discrete convolution); the Gaussian sigma equals the explicit kernel of its weights | 1e-12 |
| `CwRun_EqualsThePulse_AtTheCarrier` | a continuous-wave source and a pulse at the carrier see the same (monochromatic) device: the cw focal fluence pattern equals that of a 1 ns pulse | 1e-7 of the peak (measured 1.3e-8): a 1 ns pulse has a 0.4 GHz bandwidth, so the optical-path phase differs from the carrier's by ~1e-6 of its value across the band; the cw line sits on the requested carrier |
| `EdgeCellsAndGaps_AreAreaAverages` | area average over a grid cell: a cell half on the array and half outside gets (t_cell + t_outside) / 2; gaps transmit gapTransmission, so a fill factor 1/2 with gap amplitude g passes 1/2 + g^2 / 2 of the power when every grid cell lies in a cell or a gap | 1e-12 edge cell, 1e-6 power |
| `FillFactor_SetsTheTransmission_AndTheReplicaOrders` | comb of active cells (fill 3/4 of a 4-sample pitch, edges on grid-cell edges): transmission 3/4; discrete Fourier series c_0 = 3/4, \|c_1\| = 1/4, so the first replica / zero order = 1/9; replicas at lambda f / pitch | 1e-6 transmission, 1 % order ratio |
| `FixedPhase_RampDeflectionScalesWithWavelength` | fixedPhase: the written phase does not scale with frequency, so a ramp k0 theta x deflects by theta lambda / lambda0 (diffractive / geometric-phase device) | 1 % |
| `IndexRule_ScalesThePhaseByTheDispersion_AndSetsTheBlazeEfficiency` | chromatic index: phase(omega) = phi_d alpha with alpha = omega dn(omega) / (omega_d dn(omega_d)) (liquid-crystal SLM, extended Cauchy dn = A + B / lambda^2); the same device as fixedPhase with the map multiplied by alpha; an n-level wrapped blaze then has the first-order efficiency of a staircase of depth 2 pi alpha, \|c_1\|^2 = \|(1/N) sum_k exp(i 2 pi (alpha level_k - k / N))\|^2 (direct Fourier coefficient, Goodman sec. 5.2) | 1e-12 of the peak for the equivalence; 1 % efficiency |
| `IndexRule_WithConstantDeltaN_IsTheOpdRule` | chromatic index with a constant index modulation dn: phase = phi omega dn / (omega_d dn) = phi omega / omega_d, the opd rule | 1e-12 of the peak |
| `Maps_InlineFileAndCallback_AreIdentical` | the three ways to give a map (inline array, NPZ file, callback "@name" evaluated at the cell centres) are the same device: identical focal fields | 1e-12 |
| `OffsetArray_ClipsTheBeam_LikeAnAperture` | arrays outside the device block the light: outsideTransmission = 0 and an array covering half the beam transmits the Gaussian power inside it | 0.5 % |
| `OneStoredFrequency_IsAtTheCarrier_AndTheCwPatternMatches` | one stored frequency (spectralSamples = 1) sits at the energy centroid of the spectrum (the carrier of a 1 ns Gaussian, within one grid bin); the cw source puts its single line on the requested carrier (the lattice is shifted; it was snapped, 1.2e-6 away, until 2.2.1), so the two monochromatic focal patterns are the same | stored frequencies within 1e-9 of the carrier; pattern 1e-9 of the peak (measured 1e-16) |
| `PhaseAtDesign_IsTheOpdRule` | naming (ao-twin-2026-11.md): "phaseAtDesign" is a phase given at the design wavelength and scaled with omega, the opd rule | exact |
| `PhaseRamp_MovesTheFocus_ByFocalLengthTimesAngle` | a pure phase tilt k theta x moves the focus of a lens by f theta (geometric optics, Goodman, Fourier optics ch. 5) | 1 % |
| `QuadraticPhase_FocusesLikeAThinLens` | a quadratic phase -k x^2 / (2 f) is a thin lens: a Gaussian waist w at the device focuses at z = f to lambda f / (pi w) (Siegman, Lasers, ch. 17), as the lens element does | 2 % of the analytic radius, 1 % of the lens element's |
| `Quantisation_WrapsTheTopHalfLevel_ToLevelZero` | a quantised continuous phase at a wavelength off the design one: the level just below 2 pi wraps to level 0, not to a full turn (which would differ off-design) | exact |
| `QuantisedBlaze_FirstOrderEfficiency_FollowsSincOfOneOverN` | N-level blazed grating, first-order efficiency [sin(pi/N) / (pi/N)]^2 (Goodman, Fourier optics, sec. 5.2; N = 8: 94.96 %); the sampled field (4 grid cells per device cell) gives sin(pi/4)... exact value of the sampled staircase, within 1.5 % of the continuum for N = 4 | 1.5 % of the efficiency |
| `Rotation_TurnsTheCellGrid_AboutTheArrayCentre` | geometry: a device rotated by +theta maps its cell centre (u, v) to the lab point R(theta) (u, v); a 2 x 2 map rotated by 90 deg puts cell (1, 0) at (+1, +1) um | 1e-12 |
| `SpectralSamplingError_BoundsTheTrueErrorOfTheFocalMap` | the reported spectralSamplingError (a whole-bin interpolation residual) bounds the true error of the focal fluence map with K stored frequencies, measured against K = 64 (spike S2) | bound holds at K = 8, 16, 32; the true error falls with K |
| `SpectralSamplingError_FallsWithTheStoredFrequencies_AndWarnsWhenTooFew` | the interpolation error of the stored frequencies is 0 for a phase-free device and falls as the number of stored frequencies grows (compare K and 2K); too few frequencies warn | error ratio 2.5-6 per doubling of K (1/K^2); below the warning level 0.05 at K = 32; warning at K = 3 |
| `Trials_WithACallbackMap_EqualRunsFromMapFiles` | a callback map is read when the run compiles, so N trials of a loop that changes the callback equal N runs from N map files | 1e-12 of the peak |
| `WavefrontSensor_SlopeMaps_OfADefocusedBeam_AreLinear` | wavefront sensor slope maps of a diverging beam (radius of curvature R): W = r^2 / (2 R lambda), so lambda dW/dx = x / R and lambda dW/dy = y / R | 2 % of 1/R |
| `WavefrontSensor_SlopeMaps_ReportTheTilt` | wavefront sensor slope maps: a tilt of the beam by theta has lambda dW/dx = theta everywhere on the beam and lambda dW/dy = 0 | 1 % |
| `WrappedRamp_DispersesLikeAGrating_UnwrappedRampDoesNot` | a wrapped optical path is a grating: the first order leaves at lambda / period, so its position scales with lambda; an unwrapped ramp is a prism that deflects every colour by the same angle | 1.5 % |
| `ZeroOrderLeak_IsPassive_AndSetsTheZeroOrder` | zeroOrderLeak eta: t = a[(1-eta) e^(i phi) + eta] is passive: a flat map transmits exactly 1; a 2-level 0/pi grating with c0 = 0 has zero-order power eta^2 of the light | 1e-9 transmission, 1 % order power |

## MultiTraceRetrievalTests

Multi-trace FROG retrieval (double FROG).

| test | reference | tolerance |
|---|---|---|
| `DoubleFrog_SixStarts_AllGiveTheRightTimeDirectionForBothPulses` | Kida et al. 2014: double FROG removes the SHG-FROG time-direction ambiguity of both pulses | 6 % GDD, 5 % duration, sign exact |

## NerTests

Nonlinear ellipse rotation and the vector Kerr / Raman tensor.

| test | reference | tolerance |
|---|---|---|
| `AnisotropicFraction_OutsideZeroToOneAndAHalf_IsRejected` | validation: physically meaningful range 0 <= b <= 3/2 | exact |
| `BIntegral_CircularIsTwoThirdsOfLinear_EllipticalInBetween` | isotropic χ(3): circular light sees 2/3 of the linear phase | 0.5 % |
| `CircularLight_SeesLessRamanThanLinear` | tensor Raman: (1 − f_R) 2/3 + f_R (1 − b/2) | 1e-09, 1 % |
| `Circular_AtOneAndAHalfTimesTheEnergy_MatchesLinear` | isotropic χ(3): 3/2 energy compensates 2/3 | 0.5 % |
| `ExpressionResponse_HonoursAnisotropicFraction_IncludingTheOrientationalValue` | tensor Raman (Hellwarth) for an expression response; purely orientational b = 3/2 gives circular/linear 1/4 (Boyd, Nonlinear Optics 3rd ed., sec. 4.4) | 1 % |
| `LinAgrawal_AnisotropicPart_IsFcHaPlusFbHb` | Lin & Agrawal, Opt. Lett. 31, 3086 (2006) | 1e-12, 1 % |
| `LinAgrawal_WithExplicitZeroAnisotropy_IsIsotropic` | tensor Raman: an explicit anisotropicFraction 0 makes linAgrawal isotropic: circular / linear = (1 - fR) 2/3 + fR | 1 % |
| `Rotation_InstantaneousKerr_IsBsin2chiOverThree` | nonlinear ellipse rotation B sin 2χ / 3 (Maker, Terhune & Savage 1964) | 1 % |
| `Rotation_WithAnisotropicRaman_UsesTheAnisotropicShare` | steady-state Raman tensor (Hellwarth) | 2 % |
| `SpatialModel_OnAxisRotationWithAnisotropicRaman_MatchesTheOneDModel` (slow) | cross-model: the 1-D vector model | 5 % |

## NoncollinearOpaTests

Non-collinear OPA and DFG: angle from the arms, output tilts, parametric gain.

| test | reference | tolerance |
|---|---|---|
| `AngleFromTheArms_WithoutASignalInput_IsRefused` | the idler must leave with a signal and a pump to set the direction | exact |
| `NoncollinearDfg_ConservesPhotons` | Manley-Rowe: one signal and one idler photon per pump photon | 1e-4 |
| `Nopa_CutFromTheArms_EqualsTheStatedAngle` | the arms, externalAngleDeg and noncollinearDeg state the same geometry | 1e-6 |
| `Nopa_OutputTilts_ConserveTransverseMomentum` | transverse momentum at the exit face: w_p sin t_p = w_s sin t_s + w_i sin t_i | 1e-6 |
| `Nopa_PlaneWaveGain_FollowsTheMismatchedParametricGain` | Cerullo & De Silvestri, Rev. Sci. Instrum. 74, 1 (2003): G = 1 + (Gamma/g)^2 sinh^2(g L), g^2 = Gamma^2 - (dk/2)^2 | 2 % |
| `SequentialAndBranched_GiveTheSameNopa` | one crystal as a Sequential and as a Branched system | 1e-9 |

## OpenBugRegressionTests

Regressions from the open-bug sweep.

| test | reference | tolerance |
|---|---|---|
| `CriticalPowerRatio_UsesMarburgersConstant_InBulk` | Marburger 1975: P_cr = 3.77 lambda^2 / (8 pi n0 n2) for a Gaussian beam | 1e-6 |
| `LeakMirror_GddIsAboutTheFedBeamsOwnCarrier` | a leakMirror's GDD is a quadratic phase about the beam's own carrier: no group-delay offset for another colour | 0.2 ps |
| `MeasuredSpectrum_OutsideTheGridBand_IsAnError_NotRenormalisedAtTheEdge` | -- | -- |
| `Probe_SeeingTwoPackets_GivesEachItsOwnNamedArrays` | -- | -- |
| `ReportedGdd_IsAtTheRequestedCarrier_NotTheSnappedGridCentre` | a Taylor dispersion element has its GDD at its reference wavelength: 1000 fs^2 and 2000 fs^3 about the requested carrier | 1e-9 |
| `SequentialSplitter_FedBeamOfAnotherColour_KeepsItsOwnCarrier` | linear superposition: the splitter passes each colour at its own frequency | 1 % |
| `SequentialSplitter_FedBeamOutsideTheGrid_IsReportedAndBooked_NotRenormalised` | -- | -- |
| `TiltedPlate_WithoutBeam_HasTheRefractedColoursGdd_AndMatchesTheTrackedBeam` | plane-wave phase of a tilted plate: k_z d with k_z = (w/c) sqrt(n^2 - sin^2 theta), plus the air path to the plane normal to the beam | 1e-4 |

## PhysicsReviewTests

Physics review 2026-10-30: dispersed rays in glass, reversed cavity passes, stress depolarisation, mode truncation.

| test | reference | tolerance |
|---|---|---|
| `Coherence_TooFewModes_Warns_AndKeepsTheEnergy` | -- | -- |
| `CurvedBeam_ActsOnTheTrackedSpatialChirp_WithTheProfilesSign` | -- | -- |
| `DispersedBeamInGlass_ActsAsTheReducedDistance` | paraxial reduced distance: a glass path L acts as L / n for angular dispersion | 1 % |
| `FoxLi_WithNoAperture_DoesNotClaimAMode` | -- | -- |
| `LinearCavity_ReturnPass_ReversesAnAnamorphicElement` | Linear-cavity round trip with element matrices reversed on the return (A, B, C, D) -> (D, B, C, A) | 0.3 % |
| `ProfileMode_APrismTiltsTheProfile_ByItsExitAngle` | Snell's law at both faces, exit-angle dispersion by finite difference | 1 % |
| `ProfileMode_WithoutACarriedProfile_Warns` | -- | -- |
| `StressDepolarization_DependsOnTheBeam` | gaussian average: D = x^2 / (1 + x^2) / 4, x = C w^2 / (2 r0^2); filled rod (1 - sin C / C) / 4 (Koechner) | 1e-6 |

## PluginTests

Plugins: registration, installation, versions, cameras.

| test | reference | tolerance |
|---|---|---|
| `Camera_UnifiedImaging_MatchesTheOldCameras` | far-field radius λf/(πw0) | 1e-09, 0.5 %, 3 % |
| `FirstPartyPlugins_AreRegistered_AndDiscoveryReusesThem` | -- | -- |
| `Install_AndRemove_InTheUserFolder` | -- | -- |
| `Plugin_RegistersElementAndInstrument_AndTheyRun` | -- | -- |
| `RepeatedInstruments_GetUniqueNames` | -- | -- |
| `TooNewPlugin_IsReportedNotLoaded` | -- | -- |
| `TwinPlugin_RegistersItsCommand_AndReportsAMissingInterpreter` | the twin plugin registers `ultrafast twin`; when the interpreter cannot be started the command says so and exits with code 2 | -- |
| `VersionComparison_OrdersPreReleases` | -- | -- |

## PolarizationTests

Polarisation: waveplates, Brewster windows, basis transport.

| test | reference | tolerance |
|---|---|---|
| `BasisTracker_PeriscopeRotatesTheTransportedBasis` | -- | -- |
| `BrewsterWindow_TransmitsPOnly` | Fresnel equations at Brewster's angle | 1e-10, 1e-09 |
| `Waveplates_RotateAndCirculariseAsExpected` | Jones calculus | 1e-09, 1e-06, 0.1 % |

## PropagationTests

Nonlinear envelope propagation.

| test | reference | tolerance |
|---|---|---|
| `ChirpedMirrorCoating_GddAccumulatesPerBounce` | GDD and reflectance add per bounce | 1e-09, 0.1 % |
| `FieldMode_AgreesWithEnvelopeForSpm` | cross-model: the envelope model | 0.1 %, 2 % |
| `MaterialAttenuation_BeerLambert` | Beer-Lambert law | 1e-09 |
| `Raman_RedShiftsTheSpectrum` | -- | -- |
| `SelfSteepening_MovesPeakToTrailingEdgeAndBroadensBlueSide` | -- | -- |
| `Soliton_FundamentalSolitonKeepsItsShape` | fundamental soliton of the NLSE | 1 % |
| `Spm_GaussianBroadeningMatchesAnalyticAndBIntegral` | SPM: analytic phase γ P L, spectral broadening of a Gaussian (Agrawal) | 1e-06, 1 % |
| `Steppers_AdaptiveAgreesWithFixedRk4Ip` (slow) | -- | -- |
| `Stretcher_SampledGroupDelayStretchesAndReportsArrival` | linear chirp τ = τ0 + GDD Δω | 1e-09, 0.1 %, 1 %, 2 % |
| `TimeResponse_CallbackAndExpressionReproduceBuiltInKerr` | -- | -- |
| `TimeResponse_LabTimeModulationCreatesBesselSidebands` | phase modulation: Bessel sidebands J_n(m) | -- |

## ResolutionTests

Instrument axes, resolution and measured-data resolution.

| test | reference | tolerance |
|---|---|---|
| `Axis_CoarseDelaySteps_OnTheAutocorrelator_AndOutsideReadsZero` | -- | -- |
| `Axis_DbArraysFollowTheirLinearCurves` | -- | -- |
| `Axis_ExplicitPixels_WithPixelBins` | -- | -- |
| `Axis_Resolution_EqualsTheSpectrometerResolution` | -- | -- |
| `InputSpectrum_Deconvolution_RemovesTheRecordingResolution` | Gaussian widths add in quadrature (√8 nm recording of 2 nm) | 2 %, 6 % |
| `InputSpectrum_GaussianTails_ReplaceTheTruncatedWings` | -- | -- |
| `MeasuredData_ResolutionMismatch_IsReported` | -- | -- |

## RetrievalTests

Pulse retrieval (FROG, d-scan).

| test | reference | tolerance |
|---|---|---|
| `DScan_RecoversPhaseAndFeedsARun` | known spectral phase | 5 %, 6 % |
| `Frog_PcgpaAndEpieRecoverAChirpedPulse` | known chirped pulse | 3 %, 5 % |

## ReviewRegressionTests

Regressions from the review: M^2, second moments, packets at ports, steady-state loops.

| test | reference | tolerance |
|---|---|---|
| `CurvedBeam_KeepsUnitBeamQuality` | Gaussian beam: curvature does not change M^2 | 1e-6 |
| `HermiteGauss_HasItsAnalyticBeamQualityOnEachAxis` | Hermite-Gauss M^2 = 2m + 1 | 1e-6 |
| `PacketsTooFarApartForOneWindow_AllCountAsOutput` | energy conservation: 1/4 + 1/4 leaves by c | 1e-12 |
| `RingDownRing_IsAllPassAtEveryLength` | all-pass ring: \|r + t^2 q / (1 + r q)\|^2 = 1 | 1e-6 |
| `SeparatePacketsAtACrystalPort_AreBooked` | energy conservation at a crystal port | 1e-6 of the input |
| `SteadyStateLoop_DoesNotClaimConvergenceWhileTheLightDecays` | a passive loop decays, so it has no steady state | exact |
| `TiltAndDecentre_DoNotDegradeBeamQualityOrSize` | Siegman, Proc. SPIE 1224, 2 (1990): M^2 from central moments | 1e-6 |
| `Vortex_HasItsAnalyticBeamQuality` | Laguerre-Gauss M^2 = 2p + \|l\| + 1 | 1e-6 |

## SchemaTests

Schemas, validation and migrations.

| test | reference | tolerance |
|---|---|---|
| `CommittedSchemasAndReference_AreUpToDate` | -- | -- |
| `ExamplesUseOnlyDeclaredOptions` | -- | -- |
| `Migration_V1Cameras_BecomeTheUnifiedCamera` | -- | -- |
| `Schemas_AreGeneratedForEveryElementAndInstrument` | -- | -- |
| `UnknownKey_IsReportedWithSuggestion` | -- | -- |

## SingleShotCorrelatorTests

Single-shot time-to-space cross-correlator (window, resolution, position to delay).

| test | reference | tolerance |
|---|---|---|
| `BboAt40Degrees_FollowsEquations1And2_AndTheQuotedResolution` | Ma et al. 2018 eqs. (1), (2) with the angles a, b of the inputs to the signal from k_T + k_S = k_sig (vector sum written out independently in the test), BBO 1053 nm + 526.5 nm at 40 deg: about 1 ps resolution for 1-2 mm (p. 2) | 1e-9 (indices at the grid carrier); 0.5-1.1 ps |
| `ExternalAngle_WithASecondHarmonicSamplingBeam_GivesTheAirSlopeOfThePerpendicularFace` | tangential continuity at the entrance face: slope = (sin phi_T + sin phi_S) / c with omega_T sin phi_T = omega_S sin phi_S, phi_T + phi_S = the external angle (second-harmonic sampling beam) | 1e-9 |
| `PrePulse_AppearsAtThePositionOfItsDelay_WithItsRelativeIntensity` | time-to-space mapping: a pre-pulse of relative intensity e at -T before the main pulse appears at x = -T / slope with height e (third-order correlation, Ma et al. 2018 Fig. 1) | 1 pixel; 2 % |
| `ShortPulse_TraceWidthIsTheGeometricResolution` | a delta-like correlation read through a crystal of length L shows the rectangle of width dtau = L \|n_T cos a - n_S cos b\| / c of eq. (2) | 3 % |
| `SignalArea_EqualsTheProductOfTheTwoPulseEnergies_WhenTheWindowHoldsTheCorrelation` | the correlation integral conserves area: sum S dtau = (integral I_PT dt)(integral I_S dt); here the sampling pulse is the pulse itself, so E^2 | 1e-3 |
| `SpatialSfgOfTwoCrossedBeams_PutsAReplicaAtTheInstrumentsPosition` (slow) | time-to-space mapping in the engine's own spatial model: two beams crossing at 20 deg (external) in BBO on a Cartesian grid; a replica at delay T lands at x = T c / (2 sin(10 deg)) (tangential continuity), the slope of the instrument | 3 % |
| `SymmetricEqualIndexGeometry_GivesTwoNSinHalfAngleOverC_AndNoSmearing` | Ma et al. 2018 eq. (1) for two beams of equal index n crossing at the full angle g: T = 2 W n sin(g/2) / c; eq. (2): dtau = 0 | 1e-9 |
| `WithoutSmearing_TheTraceIsTheCrossCorrelatorsTrace` | with no crystal smearing and fine pixels the single-shot trace is the delay trace of the crossCorrelator (same third-order correlation) | 1 % |

## SmallScaleTests

Small-scale self-focusing.

| test | reference | tolerance |
|---|---|---|
| `SmallScale_ReportsPeakPowerOverCritical` | P_cr = 3.77 λ² / (8π n0 n2) (Marburger) | 2 % |
| `SmallScale_RippleGrowth_MatchesBespalovTalanov` | Bespalov & Talanov, JETP Lett. 3, 307 (1966): cosh(g z) | 6 % |
| `SmallScale_SeededNoise_IsReproducible_AndScalesWithItsAmplitude` | -- | -- |
| `SmallScale_WithoutKerr_TheRippleDiffractsAsTheTalbotEffect` | Talbot: 2 ε \|cos(q L)\| | 8 % |

## SpatialPropertyTests

Position-dependent properties of spatial elements.

| test | reference | tolerance |
|---|---|---|
| `AbsorptionProfile_GaussianAlpha_ExactSpatialTransmissionAndBeamAveraged1D` | T = (1 − exp(−α0 L)) / (α0 L) for a matched Gaussian | 0.3 %, 1 % |
| `FieldKinds_ExpressionTableAndCallback_Agree` | -- | -- |
| `IndexProfile_ParabolicGrin_FocusesLikeAThinLens` | GRIN thin lens f = 1 / (n0 g² L); waist λf/(πw0) | 3 % |
| `N2Scale_ScalesTheKerrPhase` | -- | -- |

## SpatialStreamingTests

Large spatial runs: frequency streaming, profile retention, cylindrical lens, full-panel budget.

| test | reference | tolerance |
|---|---|---|
| `CylindricalLens_FocusesOneAxisOnly` | cylindrical lens (focalLengthXM = f, focalLengthYM = 1e30 m): x focuses to lambda f / (pi w), y diffracts freely to w sqrt(1 + (z / z_R)^2) with z_R = pi w^2 / lambda (Siegman, Lasers, ch. 17) | 2 % |
| `FullPanel_1920x1080_SingleFrequency_RunsWithinTheSpikeBudget` (slow) | S2 scale spike (docs/design/notes/spikes.md): a 1920 x 1080 single-frequency run (modulator + 0.2 m gap + beam profile) took 3.3 s and 1.05 GB peak on the owner's laptop; a Gaussian overfilling the panel passes the Gaussian power on the panel (erf in x and y) | budget 4x the spike time (13 s; 45 s when the CI variable is set); keepProfiles used holds at least one profile less than all; transmission 1e-3 |
| `KeepProfilesUsed_DropsUnreadProfiles_AndChangesNoResult` | outputs.keepProfiles = "used" drops the profiles no instrument reads; the instruments and the output are unchanged | exact |
| `OneStoredFrequency_IsTheCentroid_OrTheNearestLitBin` | one stored frequency: the bin nearest the energy centroid; for a two-colour spectrum with no light at the centroid, the nearest bin that carries light (review finding M1) | exact |
| `PrefixCachedStudy_WithKeepProfilesUsed_EqualsTheUncachedStudy` | a prefix-cached study resumes from the nominal run's pulses, so with keepProfiles "used" the nominal run still keeps every profile: the cached sweep equals the uncached one (review finding H2, 2026-11-10) | 1e-12 |
| `Streaming_InFrequencyBlocks_EqualsAllAtOnce` | linear propagation is independent per stored frequency, so propagating them in blocks (streaming) gives the all-at-once result | 1e-12 of the peak |

## SpatialTests

Spatial (transverse) models.

| test | reference | tolerance |
|---|---|---|
| `CarriedProfile_ThroughFreeSpace_ConservesEnergyAfterSpectralBroadening` | -- | -- |
| `Diffraction_GaussianBeamRadiusMatchesAnalytic_Cartesian` | Gaussian beam w(z) = w0 √(1 + (z/z_R)²) | 1 % |
| `Diffraction_GaussianBeamRadiusMatchesAnalytic_Radial` | Gaussian beam w(z) = w0 √(1 + (z/z_R)²) | 0.5 % |
| `LensAndGap_FocusAGaussianBeamAsGaussianOpticsPredicts` | Gaussian optics: waist λf/(πw) | 2 % |
| `RadialBasis_QuadratureAndTransformAreExact` | ∫ Gaussian = π w²/2; transform round trip | 1e-06 |
| `ThinKerrLens_PhaseProfileMatchesAnalytic` | thin Kerr lens: k0 n2 I0 L exp(−2r²/w²) | 0.2 % |

## SpeedStudyTests

FFTs, SIMD kernels and studies.

| test | reference | tolerance |
|---|---|---|
| `ComputeBackend_SelectedFromTheRunSpec` | -- | -- |
| `Fft_MixedRadixAndBluesteinMatchTheDft` | direct DFT | -- |
| `Simd_KernelsAgreeWithScalar` | -- | -- |
| `Sobol_SequenceIsStratified` | -- | -- |
| `Study_OptimizeAndSensitivities` | -- | -- |
| `Study_ScanWithPrefixCacheMatchesFullRuns` | B = γ P L | 1e-09, 1 % |

## TileSourceTests

Coherent tile source: N sub-beams with piston, tilt and delay; combined far field, grating lobes, phase and delay errors.

| test | reference | tolerance |
|---|---|---|
| `BadTileSpec_IsRefused` | validation: per-tile arrays of the wrong length and missing sizes are refused | exact |
| `DelayMismatch_CostsTheTemporalOverlap_OfTheTwoPulses` | delay mismatch of two in-phase Gaussian pulses (intensity FWHM T): the energy on axis is 2E + 2 Int A(t) A(t - tau) dt, so relative to tau = 0 it is (1 + C) / 2 with the field overlap C = exp(-ln2 (tau / T)^2) | 3 % |
| `FourTiles_OnAxisPistonLoss_EqualsABranchedBalancedTreeRecombinedAtOnePoint` | N = 4 coherent beams of equal power recombined at one point: a balanced tree of 50/50 splitters (a Mach-Zehnder of Mach-Zehnders) with piston phase phi_k in each arm returns \|sum exp(i phi_k)\|^2 / 16 of the input power at its bright port (all arms equal: the whole input), exactly; the tile source gives the same fraction of its in-phase on-axis fluence. A Branched system recombines at a point and so cannot give the array's far field: this is the on-axis value only | Branched 1e-6 of the formula (the carrier is snapped to the grid), tiles against Branched 1.5 % |
| `InPhaseTiles_CombinePeakIntensity_ByN` | N equal, in-phase, coherent beams: on-axis far-field peak = N x the peak of one beam carrying all the energy (N^2 for equal power per beam); Goodman ch. 4 (array theorem) | 2 % |
| `Layouts_HexRingAndExplicit_PlaceTheTiles` | geometry: a hex layout of r rings has 1 + 3 r (r + 1) tiles, each at distance pitch from its nearest neighbour; a ring of n tiles lies on radius R at angles 2 pi i / n; explicit positionsM are used as given | 1e-12 |
| `PistonErrors_CostTheRealisedCoherenceSum` | piston errors: the on-axis peak of the realised phases is \|sum exp(i phi)\|^2 / N^2 of the in-phase peak (exact for the draw); its expectation is 1/N + (1 - 1/N) exp(-sigma^2) | 1.5 % |
| `TileDelay_HasTheSignOfADelayElement` | sign convention: delaysS = tau > 0 makes the tile's pulse arrive later: its field moves by +tau in the frame, as a delay element of delayS = tau moves the arrival time by +tau (E = Re{A e^{-i omega t}}: spectral factor e^{+i omega tau}) | 3 % of tau |
| `Tiles_GiveGratingLobes_AtWavelengthOverPitch` | array theorem: the far field of tiles on a pitch p has lobes at lambda / p (grating lobes), spacing F lambda / p on the focal plane | 1.5 % |
| `TiltedTile_FocusesAtFocalLengthTimesAngle` | a tile tilted by theta (tileTiltXRad) focuses at F theta (Goodman, Fourier optics ch. 5) | 1 % |
| `TophatTile_HasTheSlitFarField` | a tophat tile on a line grid is a slit of width 2a: its far field is sinc^2 with the first zero at F lambda / (2 a) (Fraunhofer, Goodman sec. 4.4); a = 8.5 grid steps so that the slit holds exactly 17 samples | 1 grid step of the focal plane |
| `UnequalAmplitudes_CostTheCombiningEfficiency` | two in-phase tiles with amplitudes a_1, a_2 (unit total energy): the on-axis far-field peak scales as \|a_1 + a_2\|^2 / (a_1^2 + a_2^2); [1, 0.5] gives 0.9 of the equal-amplitude peak | 1 % |

## TransformTests

Fitting plugin: transforms, backward propagation, fits.

| test | reference | tolerance |
|---|---|---|
| `Backward_ActiveElements_NeedAMode` | -- | -- |
| `Backward_LinearSystem_RecoversTheInputPulse` | -- | -- |
| `Backward_NonlinearPlate_UndoesSelfPhaseModulation` | -- | -- |
| `Fit_CmaEs_FindsTheGlobalMinimum` | -- | -- |
| `Fit_DScan_RecoversGddAndTod` | -- | -- |
| `Fit_InstrumentResolution_IsAFitParameter` | -- | -- |
| `Fit_Mcmc_GivesCredibleIntervalsAroundTheTruth` | known truth inside the 95 % intervals | -- |
| `Fit_Prior_ConstrainsAnUnmeasuredParameter_AndShowsInTheInfluenceMap` | -- | -- |
| `Fit_SmoothSplinePhase_FromAFrog` | -- | -- |
| `Fit_TransformFlags_RecoverAFilterFromASpectrum` | -- | -- |
| `Transform_EveryOperator_InvertsToTheInput` | -- | -- |
| `Transform_GaussianFilter_AndEnergy_GoToTheLossBudget` | -- | -- |
| `Transform_JonesRotation_TurnsXIntoY` | -- | -- |
| `Transform_MinimumPhase_IsCausal` | minimum-phase filter of a Lorentzian: one-sided exponential | -- |
| `Transform_Pedestal_AddsAnIncoherentBackground` | -- | -- |
| `Transform_Replica_IsAPostPulseAtTheRightLevel` | -- | -- |
| `Transform_Schema_AcceptsFitObjects_AndFlagsTypos` | -- | -- |
| `Transform_SpectralPhase_EqualsDispersionElement` | -- | -- |
| `Transform_Spline_InterpolatesKnots` | -- | -- |
| `Transform_Wavefront_ActsOnTheSpatialProfile` | Maréchal exp(−(2π σ)²) | 1e-12, 1e-09 |

## ValidationMatrixTests

The test framework and the generated validation matrix.

| test | reference | tolerance |
|---|---|---|
| `EveryTestClass_NamesItsArea` | -- | -- |
| `ValidationMatrix_IsUpToDate` | -- | -- |

## ZemaxTests

Zemax plugin: export and replay.

| test | reference | tolerance |
|---|---|---|
| `GaussianBeamProfile_ReconstructsAWaistInsideTheGap` | -- | -- |
| `Mpc_PassesBeamsMirrorsAndCoatings` | -- | -- |
| `RecordReplay_ReproducesTheSampleExactly` | -- | -- |
| `Slab_InterfacesAoiCoatingAndFresnel` | -- | -- |
| `Treacy_GroupPathMatchesTreacyAndBeatsPhasePath` | Treacy, IEEE JQE 5, 454 (1969) | 0.5 % |
