using System;

namespace UltrafastEngine.Schema
{
    public static partial class BuiltInOptions
    {
        private static void Instruments(Action<ComponentSpec> add)
        {
            const string D = "reference/instruments.md#";
            add(new ComponentSpec("instrumentCommon", "core", "Keys every instrument accepts.")
                .Str("kind", "Instrument kind", required: true)
                .Str("name", "Name in the results (unique; repeated names get _2, _3)", "kind_at")
                .Str("at", "\"input\", an element id (after that element) or \"end\"", "\"end\"")
                .Obj("sampling", "What part of the beam the instrument sees: near / far field, pinhole, fibre, slit (non-imaging instruments)", "sampling")
                .Model("beam", "beam", "Beam model for 1-D locations (cameras, sampling): the instrument images this ideal Gaussian"));
            add(new ComponentSpec("sampling", "", "Near / far field and apertures for non-imaging instruments.").DocAt(D + "sampling")
                .Enum("plane", new[] { "near", "far" }, "Near field, or the focal plane of a lens", "near")
                .Num("focalLengthM", "m", "Far field: focal length", "0.2")
                .Num("propagateM", "m", "Near field: free-space propagation before the aperture", "0")
                .Obj("aperture", "The aperture", "samplingAperture"));
            add(new ComponentSpec("samplingAperture", "", "Sampling aperture.")
                .Enum("kind", new[] { "integrated", "pinhole", "iris", "fibre", "fiber", "slit", "point" }, "Aperture type", "pinhole")
                .Num("radiusM", "m", "Radius (pinhole, iris, fibre core)", "50e-6 (fibre 25e-6)").Num("widthM", "m", "Slit width", "2 x radius")
                .Num("centerXM", "m", "Aperture centre x", "0").Num("centerYM", "m", "Aperture centre y", "0").Num("na", "", "Fibre numerical aperture", "0.22"));

            add(new ComponentSpec("instrument", "energyMeter", "Pulse energy and average power (optionally behind a polariser / band).").Alias("powerMeter").DocAt(D + "energymeter")
                .Arr("rangeM", "m", "[min, max] wavelength band").Num("polarizerDeg", "deg", "Analyser angle").Num("repRateHz", "Hz", "Repetition rate for average power"));
            add(new ComponentSpec("instrument", "spectrometer", "Spectrum vs wavelength with resolution.").DocAt(D + "spectrometer")
                .Num("resolutionM", "m", "Gaussian resolution FWHM", "0").Arr("rangeM", "m", "[min, max] wavelength range").Int("points", "Output points")
                .Num("polarizerDeg", "deg", "Analyser angle").Enum("normalize", new[] { "none", "peak", "area" }, "Normalisation of the output", "none").Bool("db", "Also report dB", "false"));
            add(new ComponentSpec("instrument", "scope", "Detected power vs time with a rise time / bandwidth.").Alias("photodiode").DocAt(D + "scope")
                .Num("riseTimeS", "s", "10-90 % rise time").Num("bandwidthHz", "Hz", "Bandwidth (alternative)").Int("points", "Output points"));
            add(new ComponentSpec("instrument", "autocorrelator", "Intensity or interferometric autocorrelation.").DocAt(D + "autocorrelator")
                .Enum("type", new[] { "intensity", "interferometric" }, "AC type", "intensity").Num("rangeS", "s", "Delay range").Int("points", "Output points").Int("maxN", "Grid cap for the computation"));
            add(new ComponentSpec("instrument", "frog", "SHG or PG FROG trace.").DocAt(D + "frog")
                .Enum("type", new[] { "shg", "pg" }, "FROG geometry", "shg").Int("points", "Delay and frequency points", "256"));
            add(new ComponentSpec("instrument", "dscan", "Dispersion scan: SHG spectrum vs glass insertion or GDD.").DocAt(D + "dscan")
                .Str("material", "Wedge material (with insertionM)").Arr("insertionM", "m", "[min, max] insertion").Arr("gddRangeS2", "s^2", "[min, max] GDD (alternative)")
                .Int("steps", "Scan steps", "41"));
            add(new ComponentSpec("instrument", "contrast", "Intensity in dB, pre/post-pulse contrast, pedestal fraction (ideal).").DocAt(D + "contrast")
                .Num("resolutionS", "s", "Temporal resolution").Num("windowS", "s", "Pre/post window", "5 x FWHM")
                .Num("dynamicRangeDb", "dB", "Noise floor", "120").Int("points", "Output points", "8192"));
            add(new ComponentSpec("instrument", "crossCorrelator", "Third-order (SHG-gated) cross-correlator: high-dynamic-range contrast with noise floor, resolution, scan grid, polarisation.").Alias("thirdOrderCorrelator").DocAt(D + "crosscorrelator")
                .Arr("rangeS", "s", "[min, max] delay scan (relative to the peak)", "the simulated window").Num("stepS", "s", "Coarse step").Int("points", "Coarse points (when no step)", "2001")
                .Num("fineStepS", "s", "Fine step near zero delay").Num("fineRangeS", "s", "Half-range of the fine steps")
                .Num("dynamicRangeDb", "dB", "Noise floor below the peak", "110").Num("noiseFloor", "", "Noise floor (relative; alternative)")
                .Bool("noise", "Fluctuating detector background (else a flat floor)", "true").Int("seed", "Noise seed", "1")
                .Num("resolutionS", "s", "Extra temporal resolution (Gaussian FWHM)", "0").Num("shgAcceptanceFwhmM", "m", "SHG crystal acceptance (SH wavelength FWHM)")
                .P("polarization", ParamType.Any, "", "\"total\", \"x\", \"y\" or an analyser angle in degrees", "\"total\"")
                .Arr("probeDelaysS", "s", "Delays at which to report the contrast in dB").Num("windowS", "s", "Pre/post window around the peak", "5 x FWHM"));
            add(new ComponentSpec("instrument", "polarimeter", "Stokes parameters, integrated and vs wavelength.").DocAt(D + "polarimeter").Int("points", "Output points"));
            add(new ComponentSpec("instrument", "pulseSummary", "Every scalar pulse metric.").DocAt(D + "pulsesummary"));
            add(new ComponentSpec("instrument", "compressor", "Ideal compressor: GDD/TOD that maximise peak power.").DocAt(D + "compressor")
                .Bool("optimizeTod", "Also optimise TOD", "true").Int("maxN", "Grid cap").Int("points", "Output points"));
            add(new ComponentSpec("instrument", "conversion", "Energies and photon numbers of every field leaving an active element.").DocAt(D + "conversion"));
            add(new ComponentSpec("instrument", "phaseMatchingCurve", "Low-conversion phase-matching curve of a crystal vs angle, wavelength, temperature or period.").DocAt(D + "phasematchingcurve")
                .Enum("scan", new[] { "angle", "wavelength", "temperature", "period" }, "Scan variable", "angle")
                .Num("rangeDeg", "deg", "Angle range", "1").Num("rangeM", "m", "Wavelength or period range").Num("rangeK", "K", "Temperature range", "20").Int("points", "Points", "201"));
            add(new ComponentSpec("instrument", "delayScan", "Re-runs a crystal for aux delays: output energy vs delay.").DocAt(D + "delayscan")
                .Num("fromS", "s", "First delay").Num("toS", "s", "Last delay").Int("points", "Points"));
            add(new ComponentSpec("instrument", "beamProfile", "Fluence vs radius (or x/y cuts), radius, M^2, true peak fluence.").DocAt(D + "beamprofile"));
            add(new ComponentSpec("instrument", "camera", "A camera: imaging plane (near / far / relay) + sensor.").Alias("nearFieldCamera", "farFieldCamera").DocAt(D + "camera")
                .Obj("imaging", "What the sensor sees", "imaging").Obj("sensor", "The detector", "sensor")
                .Str("plane", "Shortcut for imaging.plane")
                .Arr("bandM", "m", "[min, max] band-pass filter")
                .Include("sensorFlat"));
            add(new ComponentSpec("imaging", "", "Camera imaging.")
                .Enum("plane", new[] { "near", "far", "relay" }, "near: the beam at the location (+ propagateM, relay); far: focal plane of focalLengthM", "near")
                .Num("propagateM", "m", "Free-space propagation before imaging (negative: back to a waist)", "0")
                .Num("focalLengthM", "m", "Far field: focal length", "1")
                .Num("magnification", "", "Imaging magnification onto the sensor", "1")
                .ListOf("relay", "relayStep", "Thin lenses {focalLengthM} and gaps {gapM}, in order"));
            add(new ComponentSpec("relayStep", "", "A relay step: a thin lens or a free-space gap.").Num("focalLengthM", "m", "Thin lens").Num("gapM", "m", "Free-space gap"));
            add(new ComponentSpec("sensor", "", "A camera sensor.").Include("sensorFlat"));
            add(new ComponentSpec("group", "sensorFlat", "Sensor keys (inside sensor, or at the instrument's top level).")
                .P("pixels", ParamType.Any, "", "n or [nx, ny]", "256").Num("pixelSizeM", "m", "Pixel pitch on the sensor", "auto: ~5 beam radii across")
                .Model("qe", "filter", "Quantum efficiency vs wavelength (a filter model: constant, table, gaussian, expression)")
                .Int("bitDepth", "ADC bits (0: ideal)", "0").Num("fullScaleJPerM2", "J/m^2", "Fluence at full scale", "1.1 x peak")
                .Num("readNoiseCounts", "counts", "Read noise rms", "0").Num("backgroundCounts", "counts", "Dark / background offset", "0")
                .Num("hotPixelFraction", "", "Fraction of stuck pixels", "0").Int("noiseSeed", "Random seed", "1")
                .Num("centerXM", "m", "Sensor centre x", "0").Num("centerYM", "m", "Sensor centre y", "0")
                .Num("propagateM", "m", "Free-space propagation before imaging (v1 key)").Num("magnification", "", "Magnification (v1 key)").Num("focalLengthM", "m", "Far-field focal length (v1 key)"));
            add(new ComponentSpec("instrument", "wavefrontSensor", "Shack-Hartmann-like wavefront sensor: map, Zernike terms, curvature.").DocAt(D + "wavefrontsensor")
                .P("pixels", ParamType.Any, "", "Lenslets", "64").Num("wavelengthM", "m", "Reconstruction wavelength", "spectral centroid")
                .Num("threshold", "", "Fluence threshold relative to the peak", "0.01").Num("apertureRadiusM", "m", "Pupil radius", "1/e^2 radius")
                .Num("halfWidthM", "m", "Map half-width").Int("zernikeTerms", "Zernike terms (max 36)", "15")
                .Num("propagateM", "m", "Propagation before the sensor").Arr("bandM", "m", "[min, max] band"));
            add(new ComponentSpec("instrument", "wigner", "Wigner distribution or spectrogram (virtual).").DocAt(D + "virtual-instruments")
                .Int("points", "Time rows", "256").Num("gateFwhmS", "s", "> 0: spectrogram gate").Int("maxN", "Grid cap"));
            add(new ComponentSpec("instrument", "fieldProbe", "Temporal phase, instantaneous wavelength, group delay, CEP (virtual).").DocAt(D + "virtual-instruments")
                .Num("threshold", "", "Relative intensity threshold", "1e-3").Bool("realField", "Also the real field on the optical cycle", "false").Int("maxN", "Grid cap"));
            add(new ComponentSpec("instrument", "spaceTime", "I(r, t) and S(r, lambda) across the beam (virtual).").DocAt(D + "virtual-instruments")
                .Int("points", "Time samples", "512").Num("maxRadiusM", "m", "Largest radius", "2.5 w").Enum("axis", new[] { "x", "y" }, "Cartesian cut axis", "x"));
        }

        private static void Models(Action<ComponentSpec> add)
        {
            const string D = "reference/models.md#";
            add(new ComponentSpec("modelCommon", "core", "Keys every model object accepts.")
                .Str("kind", "Model kind", required: true).Str("name", "Callback / library name").Str("source", "Provenance label").Str("reference", "Literature reference"));
            add(new ComponentSpec("model:beam", "gaussian", "Gaussian beam w(z) = w0 sqrt(1 + ((z - zWaist)/zR)^2).").DocAt(D + "beam")
                .Num("w0M", "m", "Waist 1/e^2 radius").Num("w0xM", "m", "Waist in x").Num("w0yM", "m", "Waist in y")
                .Num("zWaistM", "m", "Waist position from the element start", "0").Num("zWaistXM", "m", "Waist position x").Num("zWaistYM", "m", "Waist position y")
                .Num("m2", "", "Beam quality", "1").Num("m2x", "", "M^2 in x").Num("m2y", "", "M^2 in y")
                .Num("wavelengthM", "m", "Wavelength (filled in from the carrier)").Num("n", "", "Index of the medium (filled in from the material)")
                .Num("zRayleighM", "m", "Rayleigh range (instead of wavelength)").Num("zRayleighXM", "m", "Rayleigh range x").Num("zRayleighYM", "m", "Rayleigh range y"));
            add(new ComponentSpec("model:beam", "constant", "Constant beam size.").Num("radiusM", "m", "1/e^2 radius").Num("radiusXM", "m", "x radius").Num("radiusYM", "m", "y radius").Num("areaM2", "m^2", "Effective area"));
            add(new ComponentSpec("model:beam", "sampled", "Beam radii sampled along z (e.g. from OpticStudio).").Arr("zM", "m", "Positions").Arr("radiusM", "m", "Radii").Arr("radiusXM", "m", "x radii").Arr("radiusYM", "m", "y radii"));
            add(new ComponentSpec("model:index", "sellmeier", "Sellmeier: n^2 = A + sum B lambda^2 / (lambda^2 - C) (lambda in um, C in um^2).").DocAt(D + "kinds-per-category")
                .Arr("B", "", "B coefficients").Arr("C", "um^2", "C coefficients").Num("A", "", "Constant term", "1"));
            add(new ComponentSpec("model:index", "constant", "Constant index.").Num("value", "", "Index"));
            add(new ComponentSpec("model:index", "refractiveindex", "A refractiveindex.info page (bundled, imported, a file, or ULTRAFAST_RII_PATH).").DocAt("reference/materials.md#refractiveindexinfo-pages")
                .Str("page", "Page name (\"main/SiO2/Malitson\") or file").Str("file", "YAML file (alternative)")
                .Num("pressureBar", "bar", "Gas pressure (density scaling from the page's SPECS)").Num("temperatureK", "K", "Temperature")
                .Num("dnDT", "1/K", "Thermo-optic coefficient (solids)", "0").Bool("relativeToAir", "The page's n are relative to air", "false"));
            add(new ComponentSpec("model:attenuation", "refractiveindex", "Absorption from the k data of a refractiveindex.info page.")
                .Str("page", "Page name or file").Str("file", "YAML file (alternative)").Num("pressureBar", "bar", "Gas pressure").Num("temperatureK", "K", "Temperature"));
            add(new ComponentSpec("model:filter", "constant", "Constant transmission.").Num("value", "", "Power transmission").Num("transmission", "", "Power transmission").Num("phaseRad", "rad", "Phase"));
            add(new ComponentSpec("model:filter", "gaussian", "Gaussian band-pass.").Num("centerWavelengthM", "m", "Centre").Num("fwhmM", "m", "FWHM").Num("peak", "", "Peak transmission", "1"));
            add(new ComponentSpec("model:filter", "superGaussian", "Super-Gaussian band-pass.").Num("centerWavelengthM", "m", "Centre").Num("fwhmM", "m", "FWHM").Num("peak", "", "Peak transmission", "1").Int("order", "Order", "2"));
            add(new ComponentSpec("model:filter", "table", "Tabulated transmission / phase vs wavelength.")
                .Arr("wavelengthsM", "m", "Wavelengths").Arr("wavelengthsNm", "nm", "Wavelengths in nm").Arr("wavelengthsUm", "um", "Wavelengths in um")
                .Arr("transmission", "", "Power transmission").Arr("phaseRad", "rad", "Phase").Arr("groupDelayS", "s", "Group delay (alternative to the phase)"));
        }
    }
}
