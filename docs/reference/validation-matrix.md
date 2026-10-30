# Validation matrix

<!-- Generated from the test attributes: dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll --matrix docs/reference/validation-matrix.md -- do not edit. -->

Every C# test of the engine: **309 tests** in 35 classes. **130** of them check the engine against something outside it -- an analytic result, a published value or another code -- named in the *reference* column with the agreement required; the others test behaviour, bookkeeping and consistency between parts of the engine. The narrative overview is [Testing and validation](../testing.md); a test declares its reference with `[Test(Reference = "...", Tolerance = "...")]`.

| area | class | tests | with an external reference |
|---|---|---|---|
| [Beam sources and masks: Hermite-Gauss, Laguerre-Gauss, Bessel, Airy, measured near fields, axicon, zone plate, vortex, slits](#beamsourcetests) | `BeamSourceTests` | 12 | 11 |
| [Gaussian beam tracking: thermal lenses and spatio-temporal couplings](#beamtrackingtests) | `BeamTrackingTests` | 9 | 8 |
| [χ(2) crystals: SHG, OPA, QPM, cascading, phase matching, aux beams](#chi2tests) | `Chi2Tests` | 14 | 6 |
| [Components: beam tracking, ND filters, plates and ghosts, polarisers, Faraday rotators, waveplates, chirped mirrors, aberrations, pedestals](#componenttests) | `ComponentTests` | 20 | 11 |
| [CPU FFTs and compute back-ends](#computetests) | `ComputeTests` | 5 | 1 |
| [Delayed light: the tail ledger, ghosts, echoes, coating ripple, scatter, detectors](#delayedlighttests) | `DelayedLightTests` | 32 | 7 |
| [Cameras, wavefront sensor, Wigner and space-time probes](#diagnosticstests) | `DiagnosticsTests` | 5 | 3 |
| [Diffraction by apertures](#diffractiontests) | `DiffractionTests` | 8 | 6 |
| [Fibres: solitons, dispersion, loss and gain, Raman, noise and coherence](#fibertests) | `FiberTests` | 8 | 7 |
| [Foundations: JSON, expressions, NPZ, FFT, dispersion maths, materials, Fresnel, thin films, Treacy](#foundationtests) | `FoundationTests` | 9 | 6 |
| [Gain media, pumping, thermal lens and position-dependent properties](#gaintests) | `GainTests` | 10 | 5 |
| [Gases: rotational Raman, indices, the refractiveindex.info database](#gastests) | `GasTests` | 6 | 3 |
| [GPU back-ends (CUDA / OpenCL) against the CPU](#gputests) | `GpuTests` | 7 | 4 |
| [HITRAN plugin: line shapes, files, partition sums, absorption in air](#hitrantests) | `HitranTests` | 17 | 8 |
| [Instrument device models: resolution, acceptance, detectors, filters](#instrumentdevicetests) | `InstrumentDeviceTests` | 8 | 1 |
| [Instruments: autocorrelators, spectrometer, compressor, polarimeter, d-scan](#instrumenttests) | `InstrumentTests` | 3 | 2 |
| [Photo-ionisation and plasma](#ionizationtests) | `IonizationTests` | 5 | 5 |
| [Lasers: Haus master equation, absorbers, cavities, apertures, carrier-resolved χ(2)](#lasertests) | `LaserTests` | 12 | 5 |
| [Loss budget and overrides](#lossandoverridetests) | `LossAndOverrideTests` | 4 | 0 |
| [Measured input spectra, comparisons and satellite room](#measuredspectrumtests) | `MeasuredSpectrumTests` | 13 | 0 |
| [Sampling apertures, measured data and fits](#measurementtests) | `MeasurementTests` | 13 | 2 |
| [Nonlinear ellipse rotation and the vector Kerr / Raman tensor](#nertests) | `NerTests` | 7 | 6 |
| [Plugins: registration, installation, versions, cameras](#plugintests) | `PluginTests` | 7 | 0 |
| [Polarisation: waveplates, Brewster windows, basis transport](#polarizationtests) | `PolarizationTests` | 3 | 2 |
| [Nonlinear envelope propagation](#propagationtests) | `PropagationTests` | 11 | 5 |
| [Instrument axes, resolution and measured-data resolution](#resolutiontests) | `ResolutionTests` | 7 | 0 |
| [Pulse retrieval (FROG, d-scan)](#retrievaltests) | `RetrievalTests` | 2 | 2 |
| [Schemas, validation and migrations](#schematests) | `SchemaTests` | 5 | 0 |
| [Small-scale self-focusing](#smallscaletests) | `SmallScaleTests` | 4 | 3 |
| [Position-dependent properties of spatial elements](#spatialpropertytests) | `SpatialPropertyTests` | 4 | 2 |
| [Spatial (transverse) models](#spatialtests) | `SpatialTests` | 6 | 5 |
| [FFTs, SIMD kernels and studies](#speedstudytests) | `SpeedStudyTests` | 6 | 1 |
| [Fitting plugin: transforms, backward propagation, fits](#transformtests) | `TransformTests` | 20 | 2 |
| [The test framework and the generated validation matrix](#validationmatrixtests) | `ValidationMatrixTests` | 2 | 0 |
| [Zemax plugin: export and replay](#zemaxtests) | `ZemaxTests` | 5 | 1 |

## BeamSourceTests

Beam sources and masks: Hermite-Gauss, Laguerre-Gauss, Bessel, Airy, measured near fields, axicon, zone plate, vortex, slits.

| test | reference | tolerance |
|---|---|---|
| `AiryBeam_MainLobeAcceleratesAlongTheParabola` | Siviloglou & Christodoulides, Opt. Lett. 32, 979 (2007): parabolic trajectory x = z^2 / (4 k^2 x0^3) | 3 % |
| `Axicon_TurnsAGaussianIntoABesselCore` | Axicon on a Gaussian: J0(k r sin(angle)) core, first zero 2.405 / (k sin(angle)) (McLeod 1954) | 2 % |
| `BesselGauss_CoreDoesNotSpreadOverSeveralRayleighRangesOfItsSpot` | Durnin, JOSA A 4, 651 (1987); Bessel-Gauss (Gori et al. 1987): central lobe 2.405 / k_r unchanged while z << w / angle | 2 % |
| `DoubleSlit_FringesHaveThePeriodOfYoungsExperiment` | Young's double slit: fringe period lambda z / d, transmission 2 b / W | 2 % |
| `FileMask_AppliesAmplitudeAndPhase` | T = amplitude^2 for a uniform map; a thin plate's phase scales with frequency | -- |
| `HermiteGauss_RadiiFollowTheModeOrders_AtTheWaistAndAfterARayleighRange` | Hermite-Gauss modes: second-moment radius w(z) sqrt(2m + 1) (Siegman, Lasers, ch. 16) | -- |
| `LaguerreGauss_VortexKeepsItsDarkAxis_AndItsRingScalesWithTheBeam` | Laguerre-Gauss modes: second-moment radius w sqrt(2p + \|l\| + 1), helical phase keeps a dark axis, ring at w sqrt(\|l\| / 2) | -- |
| `Mask_WithoutAProfile_WarnsAndPassesThePulse` | -- | -- |
| `MeasuredNearField_FromAnNpz_PropagatesLikeTheBeamItSampled` | Gaussian beam w(z) from a sampled near field; known background subtracted | -- |
| `SpecialFunctions_MatchTabulatedValues` | Abramowitz & Stegun table 10.11: Ai(0), Ai(1), Ai(-1), Ai(5.5), first zero; H_3, L_2^1 closed forms | 1e-8 |
| `VortexPlate_ConservesEnergy_AndDarkensTheAxis_AndNeedsACartesianGrid` | Spiral phase plate: a vortex of charge l has zero amplitude on the axis at every z | -- |
| `ZonePlate_FocusesAtItsFocalLength_AmplitudeAndBinaryPhase` | Fresnel zone plate: amplitude on the axis 2 N_open U0, intensity 4 N_open^2 I0; binary phase plate 16 N_open^2 I0 (Born & Wolf 8.8) | 5 % |

## BeamTrackingTests

Gaussian beam tracking: thermal lenses and spatio-temporal couplings.

| test | reference | tolerance |
|---|---|---|
| `Grating_AngularDispersionAndPulseFrontTilt_FollowTheGratingEquationAndHebling` | grating equation; Hebling, Opt. Quantum Electron. 28, 1759 (1996) | 0.2 % |
| `Prism_AngularDispersion_MatchesSnellsLawThroughBothFaces` | Snell's law at both prism faces (finite difference of n(λ)) | 0.5 % |
| `PulseFrontTilt_FromSpatialChirpAndGdd_MatchesTheGaussianResult` | Akturk et al., Opt. Express 13, 8642 (2005): 2 φ2 E σ² / (w² + 2 E² σ²) | 0.5 % |
| `SpatioTemporalSwitch_TrackReportsWithoutPhase_OffForgetsTheCouplings` | -- | -- |
| `ThermalLens_MeasuredFocalLength_FocusesTheTrackedBeamLikeAThinLens` | Gaussian-beam thin-lens law (Self, Appl. Opt. 22, 658 (1983)) | 1e-12, 0.2 % |
| `ThermalLens_SmallBeam_TrackedFocalLengthIsInnocenzis` | Innocenzi et al., Appl. Phys. Lett. 56, 1831 (1990): f = πK w_p² / (P_h dn/dT) | 5 % |
| `ThermalLens_TrackedBeam_AgreesWithTheSpatialGainModel` | cross-model: the radial spatial gain model | 7 % (aberrated lens) |
| `TwoGratings_GiveTreacysGdd_AndTheGratingPairsSpatialChirp` | Treacy grating pair (IEEE JQE 5, 454 (1969)), Kostenbauder ray-pulse matrices | 0.1 %, 0.2 % |
| `ZeroDispersionStretcher_4f_LeavesNoGddAndNoCouplings` | Martinez zero-dispersion stretcher: GDD = 0 | < 10⁻⁶ of k0 f F² |

## Chi2Tests

χ(2) crystals: SHG, OPA, QPM, cascading, phase matching, aux beams.

| test | reference | tolerance |
|---|---|---|
| `ActiveInstruments_ConversionDelayScanAndPhaseMatchingCurve` | -- | -- |
| `AuxBeam_FromSavedResults_PumpsAnOpa` | -- | -- |
| `BeamProfileInstrument_ReportsRadiusAfterARadialElement` | -- | -- |
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
| `Ghost_KerrMediumCreatesPrePulsePartner` | -- | -- |
| `Ghosts_OfBulkCrystalAndLensEndFaces` | ghost delay 2 n_g L / c, level R² | 2 % |
| `NdFilter_AbsorptiveAndReflectiveOd` | T = 10^−OD | 0.2 % |
| `NdFilter_OdPerWavelengthShapesTheSpectrum` | -- | -- |
| `Pedestal_AseLevelAndPhaseNoiseConservesEnergy` | -- | -- |
| `Pedestal_ReplicaPostPulseAndPrePulse` | -- | -- |
| `Plate_GhostReplicaDelayAndIntensity` | ghost delay 2 n_g d / c, level R² | 0.2 %, 3 % |
| `Plate_GhostTiltedOutOfTheBeamByAWedge` | -- | -- |
| `ThickWaveplate_RetardanceFromMaterialDispersion` | retardance 2π Δn(λ) d / λ from the crystal's indices | 1e-09, 0.01 %, 0.1 %, 5 % |
| `ThickWaveplate_SplitsPulseByGroupDelayDifference` | group-delay difference Δn_g d / c | 5 % |
| `ThinFilmPolarizer_TransmitsPReflectsS` | thin-film transfer matrix at the design angle | -- |
| `VectorSpatial_NonlinearEllipseRotationAcrossTheBeam` | -- | -- |
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
| `Detector_PhotonCounting_WeightsTheEnergySpectrumByLambda` | -- | -- |
| `Detector_Preset_FillsTheInstrumentAndTheReadout_FromTheRunSpecOrAFile` | -- | -- |
| `Detector_Saturation_ClipsThePeak_AndStitchingRestoresTheContrast` | -- | -- |
| `Detector_SpectralResponse_MultipliesTheSpectrumByTheTable` | -- | -- |
| `Detector_Stitching_RaisesTheFloorByTheInverseAttenuation` | -- | -- |
| `Ledger_CrossCorrelator_ReadsTheGhostInTheProbe` | -- | -- |
| `Ledger_CrossPhase_MatchesTheFullNonlinearSolutionToFirstOrder` | cross-check: the full nonlinear difference field | 10 % |
| `Ledger_EnergyBudgetCloses_WithTheTailBooked` | -- | -- |
| `Ledger_GhostOfAPlate_MatchesTheBruteForceBigWindow` | cross-check: one large grid holding the whole window | 1e-05, 0.1 % |
| `Ledger_LightLeavingTheMainWindowComesBack` | -- | -- |
| `Ledger_Off_ChangesNothing` | -- | -- |
| `Ledger_SmoothElements_SkipTheExactStep_WithTheSameResult` | -- | -- |
| `Ledger_WarnsWhenTheTailIsAFewPercentOfTheEnergy` | -- | -- |
| `MirrorEcho_AWedgedSubstrateSendsTheEchoOutOfTheBeam` | -- | -- |
| `MirrorEcho_DelayIsTwoGroupIndexThicknessOverC` | echo delay 2 n_g d / c (fused silica n_g = 1.4635 at 1030 nm) | 0.3 %, 1 %, 8 % |
| `MirrorEcho_LeakageFollowsTheCoatingTransmissionAndTheBackFace` | -- | -- |
| `MirrorEcho_NeedsTheTailOrTheWindow_AndTheLedgerCarriesIt` | -- | -- |
| `Plate_Ghost_FollowsTheWavelengthDependentCoatingTable` | -- | -- |
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
| `Camera_On1DElement_ImagesTheBeamModelAndSaysSo` | -- | -- |
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
| `LossGainAndGainGoal` | -- | -- |
| `QuantumNoise_OnePhotonPerMode_AndCoherentSoliton` | one photon per mode: N ħω of energy | 5 % |
| `RamanModels_NormalisedAndHollenbeckPeaksAt13THz` | Hollenbeck & Cantrell, JOSA B 19, 2886 (2002): gain peak 13.2 THz | 1 %, 3 % |
| `TaperedGamma_BIntegralFollowsTheIntegral` | B = ∫ γ(z) P dz | 1 % |

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

## GainTests

Gain media, pumping, thermal lens and position-dependent properties.

| test | reference | tolerance |
|---|---|---|
| `GainElement_FrantzNodvik_SaturatedEnergyGain` | Frantz & Nodvik, J. Appl. Phys. 34, 2346 (1963) | 1e-12, 1e-09, 0.03 % |
| `GainElement_SmallSignalGainSpec_IsReproduced` | -- | -- |
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
| `Gases_IndicesOfNewSpecies` | -- | -- |
| `RefractiveIndexInfo_AsAMaterialInARun` | -- | -- |
| `RefractiveIndexInfo_FormulasAndTablesMatchTheLibrary` | refractiveindex.info formulas and tables | 1e-12, 1e-09, 2 % |
| `RotationalRaman_AdiabaticLimitMatchesIsolatedMoleculeTheory` | isolated-molecule alignment, high-T limit: n2 = N Δα² / (4 c ε0² 15 kT) | 3 % |
| `RotationalRaman_LongPulseSpmUsesTheTotalN2_AndCircularSeesAQuarter` | -- | -- |
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
| `LineStrength_UsesThePartitionTable_WhenThereIsOne` | -- | -- |
| `LorentzLine_PeakAbsorptionAndDispersion_MatchTheAnalyticResult` | Lorentz line: peak n S / (π γ), dispersion extremum | 1 % |
| `ParFile_RoundTripsAHitranRecord` | -- | -- |
| `PartitionFile_NextToTheLineFile_IsFoundAndNamed` | -- | -- |
| `PartitionTable_InterpolatesLinearly_AndRefusesToExtrapolate` | -- | -- |
| `VoigtLine_DispersionIsTheHilbertTransformOfTheAbsorption` | Kramers-Kronig (numerical Hilbert transform) | -- |
| `WaterLines_InAGap_AbsorbAsBeerLambertOnTheGrid` | Beer-Lambert: 1 − exp(−α L) | 1 % |
| `WaterLines_TailLedger_ReproducesTheBruteForce2nsWindow` | cross-check: one 2 ns window | 1e-05, 0.1 %, 2 % |
| `WaterVapour_FractionFollowsTheSaturationPressure` | saturation vapour pressure of water, 2339 Pa at 20 °C | 1e-12, 0.5 % |

## InstrumentDeviceTests

Instrument device models: resolution, acceptance, detectors, filters.

| test | reference | tolerance |
|---|---|---|
| `Autocorrelator_ResolutionAndAcceptance` | -- | -- |
| `Detector_CalibrationNoiseFloorAndBits_AreReproducible` | -- | -- |
| `EveryInstrumentEnum_RunsWithEachValue` | -- | -- |
| `Filter_InFrontOfTheInstrument_ActsOnTheMeasuredField` | -- | -- |
| `Frog_SpectralResolutionAndAcceptance_NarrowAndSmoothTheTrace` | SH bandwidth of a 60 fs pulse (~4.4 nm) | 3 %, 5 % |
| `Polarimeter_ExtinctionRatioScalesStokes` | -- | -- |
| `Settings_ListEveryParameterWithItsValueOrDefault` | -- | -- |
| `Spectrometer_NormalizeModes_IncludingLegacyBooleans` | -- | -- |

## InstrumentTests

Instruments: autocorrelators, spectrometer, compressor, polarimeter, d-scan.

| test | reference | tolerance |
|---|---|---|
| `Autocorrelator_DeconvolutionFactors` | autocorrelation widths: √2 x FWHM (Gaussian), sech² deconvolution; interferometric AC peak-to-background 8:1 | 1 %, 2 % |
| `DScan_FindsGlassThatCompensatesTheChirp` | GDD of 1.5 mm fused silica at 800 nm | 2 %, 5 % |
| `Spectrometer_CompressorPolarimeterEnergy` | -- | -- |

## IonizationTests

Photo-ionisation and plasma.

| test | reference | tolerance |
|---|---|---|
| `Keldysh_MultiphotonOrderForFusedSilica` | Keldysh: K = ceil(U_g / ħω) | 8 % |
| `Plasma_ElectronDensityBlueshiftAndAbsorption` | ρ = ∫ W dt; Drude blue shift | 3 % |
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
| `FastAbsorbers_LoseLessAtHighPower` | -- | -- |
| `Haus_CwLaser_GainClampsAtTheLoss` | cw laser: E = E_sat (g0 / L − 1) | 0.2 % |
| `Haus_SolitonModeLocking_ObeysTheAreaTheorem` | soliton area theorem (Haus master equation) | 1e-12, 10 % |
| `Sesam_SlowAbsorber_SaturatesAsTheEnergyIntegral` | slow absorber: q0 (1 − e^−S) / S | 1e-09, 2 % |
| `ThermalLens_ExportsAsGradient1` | -- | -- |

## LossAndOverrideTests

Loss budget and overrides.

| test | reference | tolerance |
|---|---|---|
| `LossBudget_ClipFresnelScatterAddUp` | -- | -- |
| `Overrides_MaterialIndexOverrideBeatsTracedData` | -- | -- |
| `Overrides_PriorityAndProvenance` | -- | -- |
| `SystemFile_RoundTripAndValidation` | -- | -- |

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

## NerTests

Nonlinear ellipse rotation and the vector Kerr / Raman tensor.

| test | reference | tolerance |
|---|---|---|
| `BIntegral_CircularIsTwoThirdsOfLinear_EllipticalInBetween` | isotropic χ(3): circular light sees 2/3 of the linear phase | 0.5 % |
| `CircularLight_SeesLessRamanThanLinear` | -- | -- |
| `Circular_AtOneAndAHalfTimesTheEnergy_MatchesLinear` | isotropic χ(3): 3/2 energy compensates 2/3 | 0.5 % |
| `LinAgrawal_AnisotropicPart_IsFcHaPlusFbHb` | Lin & Agrawal, Opt. Lett. 31, 3086 (2006) | 1e-12, 1 % |
| `Rotation_InstantaneousKerr_IsBsin2chiOverThree` | nonlinear ellipse rotation B sin 2χ / 3 (Maker, Terhune & Savage 1964) | 1 % |
| `Rotation_WithAnisotropicRaman_UsesTheAnisotropicShare` | steady-state Raman tensor (Hellwarth) | 2 % |
| `SpatialModel_OnAxisRotationWithAnisotropicRaman_MatchesTheOneDModel` (slow) | cross-model: the 1-D vector model | 5 % |

## PluginTests

Plugins: registration, installation, versions, cameras.

| test | reference | tolerance |
|---|---|---|
| `Camera_UnifiedImaging_MatchesTheOldCameras` | -- | -- |
| `FirstPartyPlugins_AreRegistered_AndDiscoveryReusesThem` | -- | -- |
| `Install_AndRemove_InTheUserFolder` | -- | -- |
| `Plugin_RegistersElementAndInstrument_AndTheyRun` | -- | -- |
| `RepeatedInstruments_GetUniqueNames` | -- | -- |
| `TooNewPlugin_IsReportedNotLoaded` | -- | -- |
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
| `ChirpedMirrorCoating_GddAccumulatesPerBounce` | -- | -- |
| `FieldMode_AgreesWithEnvelopeForSpm` | cross-model: the envelope model | 0.1 %, 2 % |
| `MaterialAttenuation_BeerLambert` | Beer-Lambert law | 1e-09 |
| `Raman_RedShiftsTheSpectrum` | -- | -- |
| `SelfSteepening_MovesPeakToTrailingEdgeAndBroadensBlueSide` | -- | -- |
| `Soliton_FundamentalSolitonKeepsItsShape` | fundamental soliton of the NLSE | 1 % |
| `Spm_GaussianBroadeningMatchesAnalyticAndBIntegral` | SPM: analytic phase γ P L, spectral broadening of a Gaussian (Agrawal) | 1e-06, 1 % |
| `Steppers_AdaptiveAgreesWithFixedRk4Ip` (slow) | -- | -- |
| `Stretcher_SampledGroupDelayStretchesAndReportsArrival` | -- | -- |
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
| `InputSpectrum_Deconvolution_RemovesTheRecordingResolution` | -- | -- |
| `InputSpectrum_GaussianTails_ReplaceTheTruncatedWings` | -- | -- |
| `MeasuredData_ResolutionMismatch_IsReported` | -- | -- |

## RetrievalTests

Pulse retrieval (FROG, d-scan).

| test | reference | tolerance |
|---|---|---|
| `DScan_RecoversPhaseAndFeedsARun` | known spectral phase | 5 %, 6 % |
| `Frog_PcgpaAndEpieRecoverAChirpedPulse` | known chirped pulse | 3 %, 5 % |

## SchemaTests

Schemas, validation and migrations.

| test | reference | tolerance |
|---|---|---|
| `CommittedSchemasAndReference_AreUpToDate` | -- | -- |
| `ExamplesUseOnlyDeclaredOptions` | -- | -- |
| `Migration_V1Cameras_BecomeTheUnifiedCamera` | -- | -- |
| `Schemas_AreGeneratedForEveryElementAndInstrument` | -- | -- |
| `UnknownKey_IsReportedWithSuggestion` | -- | -- |

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
| `Study_ScanWithPrefixCacheMatchesFullRuns` | -- | -- |

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
| `Transform_Wavefront_ActsOnTheSpatialProfile` | -- | -- |

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
