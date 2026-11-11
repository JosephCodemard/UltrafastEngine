// AoPlugin.cs -- the ao plugin entry point: registers the elements and declares their keys.
//   elements: deformableMirror, slm (Devices.cs; compile to the core `modulator`), spectralShaper, aopdf (TemporalShapers.cs; linear terms)
//   The algorithms (wavefront sensing, control, holograms, sensorless optimisers, tiled-aperture combining) are Python: ultrafast_ao and
//   ultrafast.algorithms. Docs: docs/plugins/ao.md.
using UltrafastEngine.Schema;

namespace UltrafastEngine.Plugins.Ao
{
    public sealed class AoPlugin : IPlugin
    {
        public const string PluginVersion = "2.2.0";
        public string Id => "ao";
        public string Name => "Adaptive optics, beam and pulse shaping";
        public string Version => PluginVersion;
        public string Description => "Parametric deformable mirror, spatial light modulator, spectral shaper and AOPDF elements.";

        /// <summary>Registers the plugin in-process (hosts that cannot load plugin folders, e.g. the NativeAOT library).</summary>
        public static void RegisterStatic()
        {
            try { PluginManager.Register(new AoPlugin(), "built-in"); } catch { }
        }

        public void Register(PluginContext ctx)
        {
            ctx.RegisterElement("deformableMirror", DeformableMirror.Compile, new ComponentSpec("element", "deformableMirror",
                "A parametric deformable mirror on the carried transverse profile: actuator layout, Gaussian influence function, stroke limit, drive quantisation. Compiles to a modulator with an optical-path (achromatic) map. Generic: no vendor data.").DocAt("plugins/ao.md#deformablemirror")
                .Enum("layout", new[] { "square", "hex", "ring", "explicit" }, "Actuator layout", "square")
                .Int("actuatorsX", "Actuators along x (square), per row (hex)", null).Int("actuatorsY", "Actuators along y (square), rows (hex)", "actuatorsX")
                .Arr("ringCounts", "", "ring layout: actuators on ring k (radius k pitch); the first entry is the centre actuator count")
                .Arr("positionsXM", "m", "explicit layout: actuator x positions").Arr("positionsYM", "m", "explicit layout: actuator y positions")
                .Num("pitchM", "m", "Actuator pitch", null, required: true)
                .P("commandsM", ParamType.Any, "m", "Drive coefficient per actuator (the surface is the sum of the influence functions: a uniform drive gives about 1.7 x the drive in the interior at coupling 0.15, not a uniform piston), positive away from the incoming beam (lengthens the path): a list in layout order, or [actuatorsX][actuatorsY] for the square layout", "0")
                .Num("influenceCoupling", "", "Surface value at a neighbouring actuator when one actuator is driven (Gaussian influence function exp(ln(c) (r/pitch)^2))", "0.15")
                .Num("strokeM", "m", "Mechanical stroke limit (commands are clipped to +-strokeM)", "unlimited").Int("driveBits", "Drive resolution in bits over +-strokeM (needs strokeM); 0 = continuous", "0")
                .Num("incidenceAngleRad", "rad", "Angle of incidence; the wavefront changes by 2 cos(aoi) times the surface", "0")
                .Num("clearApertureRadiusM", "m", "Radius of the circular clear aperture (0 = the actuator footprint)", "0").Num("marginPitches", "", "Border around the outermost actuators, in pitches", "0.5")
                .Num("reflectivity", "", "Power reflectivity", "1").Int("samplesPerActuator", "Surface cells per actuator pitch along x and y", "4")
                .Num("designWavelengthM", "m", "Wavelength at which the optical path becomes a phase", "the carrier"));

            ctx.RegisterElement("slm", Slm.Compile, new ComponentSpec("element", "slm",
                "A parametric phase-only liquid-crystal spatial light modulator: pixels, fill factor, grey levels through a voltage-to-phase curve, dispersive birefringence, fringing cross-talk. Compiles to a modulator. Generic: no vendor data.").DocAt("plugins/ao.md#slm")
                .Int("pixelsX", "Pixels along x", "from the map").Int("pixelsY", "Pixels along y", "from the map").Num("pitchM", "m", "Pixel pitch", null, required: true).Num("pitchYM", "m", "Pixel pitch along y", "pitchM")
                .Num("offsetXM", "m", "Centre of the panel in x", "0").Num("offsetYM", "m", "Centre of the panel in y", "0").Num("rotationRad", "rad", "Rotation of the panel", "0")
                .Num("fillFactor", "", "Active fraction of the pitch (0-1]", "1").Num("gapTransmission", "", "Amplitude transmission of the gaps", "0").Num("outsideTransmission", "", "Amplitude transmission outside the panel", "0")
                .Int("greyLevels", "Number of grey levels", "256")
                .Enum("lutKind", new[] { "linear", "tanh", "table" }, "Voltage-to-phase curve: linear (maxPhaseRad k / levels), tanh (S-shaped, lutSteepness) or table (lutPhaseRad)", "linear")
                .Num("maxPhaseRad", "rad", "Phase range of the curve at the design wavelength", "2 pi").Num("lutSteepness", "", "tanh curve steepness", "2").Arr("lutPhaseRad", "rad", "table curve: phase of each grey level (non-decreasing)")
                .P("greyLevel", ParamType.Any, "", "Grey level of each pixel [pixelsX][pixelsY]").P("phaseTargetRad", ParamType.Any, "rad", "Wanted phase [pixelsX][pixelsY]: wrapped to 2 pi and mapped to the nearest grey level of the curve")
                .Str("file", "NPZ with grey_level or phase_target_rad [pixelsX][pixelsY]; relative to the system file")
                .Num("designWavelengthM", "m", "Wavelength at which the curve holds", "the carrier")
                .Enum("dispersion", new[] { "liquidCrystal", "none" }, "liquidCrystal: phase scales with omega dn(omega) (the written 2 pi reset is only 2 pi at the design wavelength); none: an achromatic optical path", "liquidCrystal")
                .Arr("deltaIndexCauchy", "", "Birefringence dn = A + B/lambda^2 + C/lambda^4 as [A, B (m^2), C (m^4)]; default a GENERIC nematic preset (unverified)")
                .Num("crosstalkSigmaCells", "", "Gaussian fringing-field cross-talk, standard deviation in pixels", "0").Arr("crosstalkKernel", "", "Explicit cross-talk kernel (odd length, non-negative)")
                .Num("zeroOrderLeak", "", "Amplitude fraction that passes unmodulated", "0"));

            ctx.RegisterElement("spectralShaper", TemporalShapers.CompileSpectralShaper, new ComponentSpec("element", "spectralShaper",
                "A pixelated spectral phase / amplitude mask in the Fourier plane of a 4f line (1-D SLM pulse shaper), as a linear spectral transfer function. The programmed group delay is kept in the transfer (the pulse moves inside the time window).").DocAt("plugins/ao.md#spectralshaper")
                .Int("pixels", "Number of pixels", null, required: true).Num("pixelPitchM", "m", "Pixel pitch", null, required: true)
                .Num("fillFactor", "", "Active fraction of the pitch (0-1]", "1").Num("gapTransmission", "", "Amplitude transmission of the gaps", "0").Num("outsideTransmission", "", "Amplitude transmission beyond the array", "0")
                .Num("centerWavelengthM", "m", "Wavelength at the centre of the array", "the carrier").Num("offsetM", "m", "Position of the array centre relative to the centre wavelength's focus", "0")
                .Num("dispersionMPerM", "", "Focal-plane position per wavelength (m of position per m of wavelength; 1e6 = 1 mm/nm)", "from the grating")
                .Num("gratingLinesPerMm", "1/mm", "Grating line density").Num("incidenceAngleRad", "rad", "Angle of incidence on the grating", "0").Num("order", "", "Diffraction order", "1").Num("focalLengthM", "m", "Focal length of the 4f lens")
                .Num("spotRadiusM", "m", "1/e^2 field radius of the focused monochromatic spot", "lambda f / (pi inputBeamRadiusM)").Num("inputBeamRadiusM", "m", "Beam radius at the grating (sets the spot size)")
                .P("phaseRad", ParamType.Any, "rad", "Phase per pixel", "0").P("amplitude", ParamType.Any, "", "Amplitude transmission per pixel", "1")
                .Num("phaseWrapRad", "rad", "Wrap the phase to [0, phaseWrapRad); 0 = no wrapping", "2 pi").Int("greyLevels", "Phase levels over 2 pi; 0 = continuous", "0"));

            ctx.RegisterElement("aopdf", TemporalShapers.CompileAopdf, new ComponentSpec("element", "aopdf",
                "An acousto-optic programmable dispersive filter: programmed spectral phase (delay, GDD, TOD, FOD or a table), finite acoustic window, diffraction efficiency, amplitude table and a spurious replica.").DocAt("plugins/ao.md#aopdf")
                .Num("centerWavelengthM", "m", "Wavelength at which the polynomial phase is expanded", "the carrier")
                .Num("delayFs", "fs", "Group delay at the centre", "0").Num("gddFs2", "fs^2", "Group-delay dispersion", "0").Num("todFs3", "fs^3", "Third-order dispersion", "0").Num("fodFs4", "fs^4", "Fourth-order dispersion", "0")
                .Arr("spectralPhaseRad", "rad", "Extra spectral phase table over tableWavelengthsM").Arr("amplitudeTable", "", "Amplitude transmission table over tableWavelengthsM").Arr("tableWavelengthsM", "m", "Wavelengths of the tables")
                .Num("windowS", "s", "Acoustic window: components whose programmed group delay is outside +-window/2 are not diffracted (0 = unlimited)", "0")
                .Num("efficiency", "", "Diffraction efficiency (power)", "1").Num("replicaAmplitude", "", "Spurious replica split off the main pulse: H x (1 + a exp(i omega tau)) / sqrt(1 + a^2)", "0").Num("replicaDelayS", "s", "Delay of the spurious replica", "0"));
        }
    }
}
