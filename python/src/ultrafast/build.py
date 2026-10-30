# mypy: disable-error-code="return-value"
"""Helpers to write system files and run specs from Python (plain dicts, so they can be inspected,
edited, saved with :func:`save` and versioned). Every helper simply returns the JSON the engine reads;
see docs/reference/system-file.md and docs/reference/run-spec.md for all fields.

>>> from ultrafast import build as b
>>> sysd = b.system("plate", [b.bulk("plate", "FusedSilica", 5e-3, beam=b.gaussian_beam(100e-6, 2.5e-3))])
>>> spec = b.run_spec(b.pulse(1.03e-6, 200e-15, energy=10e-6), instruments=[b.instrument("spectrometer")])
"""
from __future__ import annotations

import json
import math
from typing import TYPE_CHECKING, Any, Dict, Iterable, List, Mapping, Optional, Sequence

if TYPE_CHECKING:  # TypedDicts for editors (python/tools/gen_types.py)
    from . import types as T

C = 299792458.0


def _clean(d: Dict[str, Any]) -> Dict[str, Any]:
    return {k: v for k, v in d.items() if v is not None}


# ------------------------------------------------------------------ system file
def system(name: str, elements: Sequence[Mapping[str, Any]], materials: Optional[Mapping[str, Any]] = None, coatings: Optional[Mapping[str, Any]] = None,
           wavelengths_m: Optional[Sequence[float]] = None, overrides: Optional[list] = None, environment: Optional[Mapping[str, Any]] = None,
           description: Optional[str] = None, substrates: Optional[Mapping[str, Any]] = None) -> "T.System":
    """A system file. ``substrates`` defines mirror substrates by name (see :func:`substrate`); ``environment`` is the lab
    air (:func:`environment`)."""
    d = {"format": "ultrafast-system", "version": 2, "name": name}
    if description:
        d["description"] = description
    if wavelengths_m is not None:
        d["sampling"] = {"wavelengthsM": list(map(float, wavelengths_m))}
    if environment:
        d["environment"] = environment
    if materials:
        d["materials"] = materials
    if coatings:
        d["coatings"] = coatings
    if substrates:
        d["substrates"] = substrates
    d["elements"] = list(elements)
    if overrides:
        d["overrides"] = list(overrides)
    return d


def bulk(id: str, material: str, length_m: float, beam: Optional[Mapping[str, Any]] = None, nonlinear: Optional[bool] = None, **extra) -> "T.BulkElement":
    return _clean({"id": id, "type": "bulk", "material": material, "lengthM": length_m, "beam": beam, "nonlinear": nonlinear, **extra})


def fiber(id: str, length_m: float, betas: Optional[Sequence[float]] = None, gamma: Any = None, *,
          betas_ps_km: Optional[Sequence[float]] = None, loss_db_per_km: Optional[float] = None, gain_db_per_m: Optional[float] = None,
          raman: Any = None, **extra) -> "T.FiberElement":
    """Single-mode fibre (docs/reference/fibers.md): Taylor ``betas`` [beta2, beta3, ...] in s^k/m (or ``betas_ps_km``
    in ps^k/km), ``gamma`` in 1/(W m) (or an expression in z), loss, gain, ``raman`` = "silica" | "linAgrawal" |
    "hollenbeck" | "none". ``extra``: ``dispersion``, ``material``, ``n2``, ``effectiveAreaM2``, ``gain`` (spectrum), ..."""
    return _clean({"id": id, "type": "fiber", "lengthM": length_m, "betas": list(betas) if betas is not None else None,
                   "betasPsKm": list(betas_ps_km) if betas_ps_km is not None else None, "gamma": gamma,
                   "lossDbPerKm": loss_db_per_km, "gainDbPerM": gain_db_per_m, "raman": raman, **extra})


def gap(id: str, length_m: float, medium: str = "Air", air: Optional[Mapping[str, Any]] = None, **extra) -> "T.GapElement":
    """Free space (air by default). ``air`` (see :func:`air`) gives this gap its own temperature, pressure, humidity or gas
    lines instead of the system's ``environment.air``."""
    return _clean({"id": id, "type": "gap", "material": medium, "lengthM": length_m, "air": air, **extra})


def interface(id: str, before: str = "Air", after: str = "Air", aoi_deg: float = 0.0, coating=None, mode: str = "refract", **extra) -> "T.InterfaceElement":
    return _clean({"id": id, "type": "interface", "mode": mode, "before": before, "after": after, "aoiDeg": aoi_deg, "coating": coating, **extra})


def mirror(id: str, coating=None, aoi_deg: float = 0.0, medium: str = "Air", substrate: Any = None, ripple: Optional[Mapping[str, Any]] = None,
           delayed_scatter: Optional[Mapping[str, Any]] = None, **extra) -> "T.MirrorElement":
    """A reflecting surface. ``substrate``: a name ("FS-12.7mm", or one defined with ``system(substrates=...)``) or :func:`substrate`
    -- light leaking through the coating returns as an echo delayed by 2 n_g d cos(theta) / c. ``ripple``: :func:`coating_ripple`.
    ``delayed_scatter``: :func:`delayed_scatter`. ``aoi_deg`` defaults to 0 (a Herriott cell: a few mrad)."""
    return _clean({"id": id, "type": "mirror", "before": medium, "aoiDeg": aoi_deg, "coating": coating, "substrate": substrate, "ripple": ripple,
                   "delayedScatter": delayed_scatter, **extra})


def dispersion(id: str, gdd_s2: float = 0.0, tod_s3: float = 0.0, fod_s4: float = 0.0, **extra) -> "T.DispersionElement":
    return _clean({"id": id, "type": "dispersion", "gddS2": gdd_s2, "todS3": tod_s3, "fodS4": fod_s4, **extra})


def grating_pair(id: str, lines_per_mm: float, aoi_deg: float, separation_m: float, order: int = -1, passes: int = 2,
                 efficiency: float = 1.0) -> "T.GratingPairElement":
    return {"id": id, "type": "gratingPair", "linesPerMm": lines_per_mm, "aoiDeg": aoi_deg, "separationM": separation_m,
            "order": order, "passes": passes, "efficiency": efficiency}


def chirped_mirror(id: str, gdd_s2: float, bounces: int = 1, reflectance: float = 0.999, tod_s3: float = 0.0, **extra) -> "T.ChirpedMirrorElement":
    """Dispersive mirror (GDD/TOD per bounce). ``extra``: e.g. ``rippleGddFs2=30, ripplePeriodNm=25, complementaryPairs=True``."""
    return _clean({"id": id, "type": "chirpedMirror", "gddS2": gdd_s2, "todS3": tod_s3, "bounces": bounces, "reflectance": reflectance, **extra})


def aperture(id: str, radius_m: Optional[float] = None, beam_radius_m: Optional[float] = None, *, half_width_x_m: Optional[float] = None,
             half_width_y_m: Optional[float] = None, decenter_x_m: Optional[float] = None, decenter_y_m: Optional[float] = None) -> "T.ApertureElement":
    """A hard aperture: round (``radius_m``) or rectangular (``half_width_x_m``, ``half_width_y_m``), optionally decentred. On a carried
    transverse profile it masks the field (edge cells transmit their area fraction) and the light diffracts in the spatial elements after it
    (docs/reference/transverse.md#diffraction-and-apertures); without one it clips the beam model of radius ``beam_radius_m``."""
    if radius_m is None and half_width_x_m is None:
        raise ValueError("aperture needs radius_m or half_width_x_m")
    return _clean({"id": id, "type": "aperture", "radiusM": radius_m, "beamRadiusM": beam_radius_m, "halfWidthXM": half_width_x_m,
                   "halfWidthYM": half_width_y_m, "decenterXM": decenter_x_m, "decenterYM": decenter_y_m})


def mask(id: str, kind: str, *, cone_angle_rad: Optional[float] = None, focal_length_m: Optional[float] = None, binary_phase: Optional[bool] = None,
         design_wavelength_m: Optional[float] = None, charge: Optional[int] = None, count: Optional[int] = None, width_m: Optional[float] = None,
         separation_m: Optional[float] = None, height_m: Optional[float] = None, file: Optional[str] = None, **extra) -> "T.MaskElement":
    """A thin mask on the carried transverse profile (docs/reference/transverse.md#masks): "axicon" (``cone_angle_rad``), "zonePlate"
    (``focal_length_m``, ``binary_phase``), "vortex" (``charge``), "slits" (``count``, ``width_m``, ``separation_m``, ``height_m``) or "file" (an NPZ
    with x_m, y_m or r_m, amplitude, optionally phase_rad). Vortex and slits need a cartesian grid."""
    return _clean({"id": id, "type": "mask", "kind": kind, "coneAngleRad": cone_angle_rad, "focalLengthM": focal_length_m, "binaryPhase": binary_phase,
                   "designWavelengthM": design_wavelength_m, "charge": charge, "count": count, "widthM": width_m, "separationM": separation_m,
                   "heightM": height_m, "file": file, **extra})


def grating(id: str, lines_per_mm: float, aoi_deg: float, order: int = -1, efficiency: Optional[float] = None, **extra) -> "T.GratingElement":
    """One diffraction grating. With the beam tracked (an ``inputBeam``) it disperses the beam: sin(theta_out) = sin(theta_in) + m lambda / period
    with signed angles in the unfolded beam (a second grating that undoes it has ``aoi_deg`` = the first's diffraction angle and the opposite
    order), anamorphic magnification, angular dispersion, pulse-front tilt, and the group-delay dispersion of the spatio-temporal couplings
    (docs/reference/components.md#spatio-temporal-couplings)."""
    return _clean({"id": id, "type": "grating", "linesPerMm": lines_per_mm, "aoiDeg": aoi_deg, "order": order, "efficiency": efficiency, **extra})


def waveplate(id: str, plate: str = "half", axis_deg: float = 0.0, design_wavelength_m: Optional[float] = None, *,
              material: Optional[str] = None, order: Optional[int] = None, thickness_m: Optional[float] = None,
              construction: Optional[str] = None, plate_thickness_m: Optional[float] = None) -> "T.WaveplateElement":
    """Waveplate: dispersionless design model, or with ``material`` (Quartz, MgF2, ...) a thick plate with full dispersion
    (multi-order ``order`` / ``thickness_m``, or ``construction="compound"`` zero-order)."""
    return _clean({"id": id, "type": "waveplate", "plate": plate, "axisDeg": axis_deg, "designWavelengthM": design_wavelength_m,
                   "material": material, "order": order, "thicknessM": thickness_m, "construction": construction, "plateThicknessM": plate_thickness_m})


def plate(id: str, material: str, thickness_m: float, aoi_deg: float = 0.0, ghosts: Any = False, coating: Any = None,
          wedge_deg: float = 0.0, beam: Optional[Mapping[str, Any]] = None, delayed_scatter: Optional[Mapping[str, Any]] = None,
          **extra) -> "T.PlateElement":
    """Plane-parallel plate / window: surfaces, bulk (dispersion, nonlinearity with a beam), optional ghost replicas. The ghosts
    use the reflectance of ``coating`` at each wavelength (a ``table`` coating or a stack), not a flat value. ``delayed_scatter``:
    bulk back-scatter returned as delayed light (:func:`delayed_scatter`; the default delay is 0 .. 2 n_g d / c)."""
    return _clean({"id": id, "type": "plate", "material": material, "thicknessM": thickness_m, "aoiDeg": aoi_deg or None,
                   "ghosts": ghosts or None, "coating": coating, "wedgeDeg": wedge_deg or None, "beam": beam,
                   "delayedScatter": delayed_scatter, **extra})


def nd_filter(id: str, od: Any, kind: str = "absorptive", material: Optional[str] = None, thickness_m: Optional[float] = None,
              wavelengths_m: Optional[Sequence[float]] = None, aoi_deg: float = 0.0, ghosts: Any = False, **extra) -> "T.NdFilterElement":
    """Neutral-density filter: OD (or OD per wavelength with ``wavelengths_m``), absorptive or reflective."""
    return _clean({"id": id, "type": "ndFilter", "od": list(od) if isinstance(od, (list, tuple)) else od, "kind": kind,
                   "material": material, "thicknessM": thickness_m, "wavelengthsM": list(wavelengths_m) if wavelengths_m else None,
                   "aoiDeg": aoi_deg or None, "ghosts": ghosts or None, **extra})


def thin_film_polarizer(id: str, output: str = "transmitted", axis_deg: float = 0.0, aoi_deg: float = 56.0, model: str = "stack", **extra) -> "T.ThinFilmPolarizerElement":
    """Thin-film polariser (physical quarter-wave stack or ideal); ``output`` "transmitted" (p) or "reflected" (s)."""
    return _clean({"id": id, "type": "thinFilmPolarizer", "output": output, "axisDeg": axis_deg, "aoiDeg": aoi_deg, "model": model, **extra})


def faraday_rotator(id: str, rotation_deg: float = 45.0, material: Optional[str] = None, length_m: Optional[float] = None, **extra) -> "T.FaradayRotatorElement":
    """Faraday rotator (non-reciprocal, Verdet ~ 1/(lambda^2 - lambda0^2)); with a crystal ``material`` and ``length_m``."""
    return _clean({"id": id, "type": "faradayRotator", "rotationDeg": rotation_deg, "material": material, "lengthM": length_m, **extra})


def wavefront(id: str, zernike_noll_waves: Optional[Sequence[float]] = None, radius_m: Optional[float] = None,
              random_rms_m: Optional[float] = None, correlation_length_m: Optional[float] = None, **extra) -> "T.WavefrontElement":
    """A wavefront error: Zernike terms (Noll order, waves) over ``radius_m`` and/or a random figure (cartesian grids)."""
    return _clean({"id": id, "type": "wavefront", "zernikeNollWaves": list(zernike_noll_waves) if zernike_noll_waves else None,
                   "radiusM": radius_m, "randomRmsM": random_rms_m, "correlationLengthM": correlation_length_m, **extra})


def substrate(material: str = "FusedSilica", thickness_m: Optional[float] = None, *, back_reflectance: Optional[float] = None,
              back_coating: Any = None, wedge_deg: Optional[float] = None, orders: Optional[int] = None,
              beam_radius_m: Optional[float] = None) -> dict:
    """A mirror substrate (for ``mirror(substrate=...)`` or ``system(substrates={name: ...})``). Light that leaks through the HR
    coating reflects from the back face (bare Fresnel unless ``back_reflectance`` -- a flat power reflectance, e.g. 1e-3 for an AR
    coating -- or ``back_coating`` is given) and returns ``orders`` times as an echo delayed by 2 n_g(lambda) d cos(theta_t) / c.
    Built-in names: FS-3mm, FS-6.35mm, FS-9.5mm, FS-12.7mm, FS-25.4mm, BK7-6.35mm, BK7-12.7mm (fused silica / N-BK7, bare back face)."""
    return _clean({"material": material, "thicknessM": thickness_m, "backReflectance": back_reflectance, "backCoating": back_coating,
                   "wedgeDeg": wedge_deg, "orders": orders, "beamRadiusM": beam_radius_m})


def coating_ripple(phase_rms_rad: float = 0.0, amplitude_rms: float = 0.0, max_delay_s: float = 5e-12, *, seed: Optional[int] = None,
                   components: Optional[int] = None) -> dict:
    """Random phase / amplitude ripple of a mirror coating (for ``mirror(ripple=...)``). The first-order pedestal holds a fraction
    ``phase_rms_rad`` squared of the energy within +-``max_delay_s`` of the pulse. Measured ripple: use a ``table`` coating."""
    return _clean({"phaseRmsRad": phase_rms_rad, "amplitudeRms": amplitude_rms, "maxDelayS": max_delay_s, "seed": seed, "components": components})


def delay_uniform(max_s: Optional[float] = None, min_s: float = 0.0) -> dict:
    """Delays uniform in [min_s, max_s] (default max: 2 n_g d cos(theta) / c of the plate, or of the mirror substrate)."""
    return _clean({"kind": "uniform", "minS": min_s, "maxS": max_s})


def delay_exponential(tau_s: float, max_s: Optional[float] = None) -> dict:
    """Delays with an exponential weight exp(-t / tau_s)."""
    return _clean({"kind": "exponential", "tauS": tau_s, "maxS": max_s})


def delay_table(delays_s: Sequence[float], weights: Sequence[float]) -> dict:
    """Delays from a table (weights are normalised)."""
    return {"kind": "table", "delaysS": list(map(float, delays_s)), "weights": list(map(float, weights))}


def delayed_scatter(fraction: Optional[float] = None, delay: Optional[Mapping[str, Any]] = None, spectral_exponent: Optional[float] = None, *,
                    seed: Optional[int] = None, components: Optional[int] = None, model: Optional[str] = None,
                    roughness_rms_m: Optional[float] = None, correlation_length_m: Optional[float] = None,
                    beam_radius_m: Optional[float] = None, surfaces: Optional[int] = None) -> dict:
    """Scatter as delayed light, not loss. Generic model: ``fraction`` of the power returns incoherently after ``delay``
    (:func:`delay_uniform`, :func:`delay_exponential`, :func:`delay_table`) with an intensity weight lambda^-``spectral_exponent``.
    Analytic surface model (``model="surface"``): rms roughness ``roughness_rms_m`` gives the total integrated scatter
    (4 pi sigma / lambda)^2 per surface; the share 1 - exp(-(l/w)^2) of it (correlation length l, beam radius w) stays in the mode and returns
    after ``delay`` (none: at once), the rest is lost (booked as scatter). ``surfaces`` defaults to 1 (mirror) / 2 (plate)."""
    return _clean({"fraction": fraction, "delay": delay, "spectralExponent": spectral_exponent, "seed": seed, "components": components, "model": model,
                   "roughnessRmsM": roughness_rms_m, "correlationLengthM": correlation_length_m, "beamRadiusM": beam_radius_m, "surfaces": surfaces})


def air(*, temperature_c: Optional[float] = None, pressure_pa: Optional[float] = None, humidity: Optional[float] = None,
        co2_ppm: Optional[float] = None, apply_to_gaps: Optional[bool] = None, kind: Optional[str] = None, **model) -> "T.Air":
    """Lab air (Ciddor index from temperature, pressure, relative humidity 0..1 and CO2), for ``environment(air=...)`` or one
    ``gap(air=...)``. ``apply_to_gaps=True`` uses it as the "Air" of every gap (off by default: existing results do not change).
    ``kind="hitran"`` (hitran plugin) adds line-by-line absorption and dispersion: see ``ultrafast_hitran.air``."""
    return _clean({"temperatureC": temperature_c, "pressurePa": pressure_pa, "humidity": humidity, "co2ppm": co2_ppm,
                   "applyToGaps": apply_to_gaps, "kind": kind, **model})


def environment(air: Optional[Mapping[str, Any]] = None, index_reference: Optional[str] = None) -> "T.Environment":
    """The system's ``environment``: the lab air and whether quoted data are relative to "air" or "vacuum"."""
    return _clean({"air": air, "indexReference": index_reference})


def grid(*, n: Optional[int] = None, window_s: Optional[float] = None, window_ps: Optional[float] = None, auto: Optional[bool] = None,
         max_n: Optional[int] = None, snap_carrier: Optional[bool] = None, spectral_margin: Optional[float] = None,
         min_span_fraction: Optional[float] = None, min_points_per_fwhm: Optional[float] = None, satellite_level: Optional[float] = None,
         extend_for_satellites: Optional[bool] = None, strict: Optional[bool] = None, tail: Any = None) -> "T.Grid":
    """The time grid. ``tail=True`` (or :func:`tail_grid`) adds the delayed-light ledger: a second, long, linear grid with the same
    time step that carries ghosts, echoes, scatter and gas-line tails while the nonlinear solver stays on the small main grid.
    ``extend_for_satellites=True`` grows a manual window to hold the satellites; ``strict=True`` makes a wrapping satellite an error."""
    return _clean({"n": n, "windowS": window_s, "windowPs": window_ps, "auto": auto, "maxN": max_n, "snapCarrier": snap_carrier,
                   "spectralMargin": spectral_margin, "minSpanFraction": min_span_fraction, "minPointsPerFwhm": min_points_per_fwhm,
                   "satelliteLevel": satellite_level, "extendForSatellites": extend_for_satellites, "strict": strict, "tail": tail})


def tail_grid(window_s: Optional[float] = None, warn_energy_fraction: Optional[float] = None, *, exact_steps: Optional[bool] = None,
              cross_phase: Optional[bool] = None) -> dict:
    """``grid(tail=tail_grid(window_s=2e-9))``: the long tail grid of the delayed-light ledger (default window: from the satellites,
    at least 8 x the main window). A warning is added when the tail holds more than ``warn_energy_fraction`` (0.01) of the energy.
    ``exact_steps=True`` takes the exact long-grid step for every linear element (default: smooth elements -- dispersion, flat loss, bare
    surfaces -- act on main and tail separately, much cheaper); ``cross_phase=False`` leaves out the first-order cross-phase of the tail
    by the main pulse in nonlinear elements."""
    return _clean({"windowS": window_s, "warnEnergyFraction": warn_energy_fraction, "exactSteps": exact_steps, "crossPhase": cross_phase})


def detector_response(wavelengths_m: Sequence[float], values: Sequence[float]) -> dict:
    """A spectral response table (e.g. the quantum efficiency) for ``detector(response=...)``: linear in wavelength, held at the ends."""
    return {"wavelengthsM": list(map(float, wavelengths_m)), "values": list(map(float, values))}


def detector(*, calibration: Optional[float] = None, noise: Optional[float] = None, noise_floor: Optional[float] = None,
             dynamic_range_db: Optional[float] = None, saturation_level: Optional[float] = None, bit_depth: Optional[int] = None,
             response: Optional[Mapping[str, Any]] = None, counting: Optional[str] = None, seed: Optional[int] = None,
             preset: Optional[str] = None) -> "T.Detector":
    """The read-out of an instrument (``instrument(..., detector=detector(...))``). ``response`` (:func:`detector_response`) is the spectral
    response of a spectral instrument; ``counting="photons"`` converts the energy spectrum to photon counts first. ``preset`` names an entry
    of ``run_spec(detectors=...)`` (or a JSON file) that the other keys override."""
    return _clean({"calibration": calibration, "noise": noise, "noiseFloor": noise_floor, "dynamicRangeDb": dynamic_range_db,
                   "saturationLevel": saturation_level, "bitDepth": bit_depth, "response": response, "counting": counting, "seed": seed, "preset": preset})


def detector_preset(*, instrument: Optional[Mapping[str, Any]] = None, detector: Optional[Mapping[str, Any]] = None) -> dict:
    """A reusable detector preset for ``run_spec(detectors={name: ...})``: ``instrument`` keys fill instrument parameters the instrument does
    not set (``saturationLevel``, ``segments`` from :func:`detector_segment`, ``noiseFloor``, ...); ``detector`` is the read-out. An instrument
    uses it with ``detector="name"``. Numbers are yours (a manual, a calibration): none are built in."""
    return _clean({"instrument": instrument, "detector": detector})


def detector_segment(from_s: float, to_s: float, attenuation: float = 1.0, noise_floor: Optional[float] = None) -> dict:
    """One delay range of a cross-correlator scan measured through an attenuation (a power factor): for the instrument's
    ``segments=[...]`` (stitching) together with ``saturation_level``-style keys ``saturationLevel`` and ``noiseFloor``."""
    return _clean({"fromS": from_s, "toS": to_s, "attenuation": attenuation, "noiseFloor": noise_floor})


def pedestal(kind: str, **params) -> dict:
    """A pedestal / contrast component for ``pulse(..., pedestal=[...])``: kind replica | prePulse | postPulse | ase |
    phaseNoise | exponential (docs/reference/run-spec.md#pedestal); parameters in the engine's spelling
    (``delayS``, ``relativeIntensity``, ``rmsRad``, ...)."""
    return {"kind": kind, **params}


def polarizer(id: str, axis_deg: float = 0.0, extinction_ratio: float = 1e5) -> "T.PolarizerElement":
    return {"id": id, "type": "polarizer", "axisDeg": axis_deg, "extinctionRatio": extinction_ratio}


def filter_element(id: str, model: Mapping[str, Any]) -> dict:
    return {"id": id, "type": "filter", "filter": model}


def probe(id: str) -> "T.ProbeElement":
    return {"id": id, "type": "probe"}


def lens(id: str, focal_length_m: float, material: Optional[str] = None, thickness_m: float = 0.0, medium: str = "Air") -> "T.LensElement":
    """Thin lens acting on a carried transverse profile (chromatic if a lens material is given)."""
    return _clean({"id": id, "type": "lens", "focalLengthM": focal_length_m, "material": material,
                   "thicknessM": thickness_m or None, "medium": medium})


def user(id: str, callback: str, params: Optional[Mapping[str, Any]] = None, length_m: float = 0.0) -> "T.UserElement":
    """User element: ``callback`` is the name registered with ``Run.element(name, fn)``."""
    return _clean({"id": id, "type": "user", "callback": callback if callback.startswith("@") else "@" + callback,
                   "params": params, "lengthM": length_m or None})


def transverse(model: str = "radial", points: Optional[int] = None, radius_m: Optional[float] = None,
               nx: Optional[int] = None, ny: Optional[int] = None, half_width_m: Optional[float] = None,
               transport: Optional[str] = None, initial_profile: Optional[Mapping[str, Any]] = None, *,
               absorbing_boundary: Any = None, spectral_samples: Optional[int] = None, **extra) -> dict:
    """Transverse (spatial) model for an element: "radial" (Hankel) or "cartesian" (x-y FFT). ``absorbing_boundary`` (True, or
    {"widthFraction": 0.15}) absorbs light leaving the grid in linear elements instead of reflecting / wrapping it (booked as "grid
    boundary"); ``spectral_samples`` (48) is the number of frequencies at which the profile is stored -- fewer is faster for narrowband
    pulses."""
    return _clean({"model": model, "points": points, "radiusM": radius_m, "nx": nx, "ny": ny, "halfWidthM": half_width_m,
                   "transport": transport, "initialProfile": initial_profile, "absorbingBoundary": absorbing_boundary,
                   "spectralSamples": spectral_samples, **extra})


def initial_profile(kind: str = "gaussian", *, order: Optional[int] = None, radius_m: Optional[float] = None, geometry: Optional[str] = None,
                    radius_x_m: Optional[float] = None, radius_y_m: Optional[float] = None, radius_of_curvature_m: Optional[float] = None,
                    mode_x: Optional[int] = None, mode_y: Optional[int] = None, mode_p: Optional[int] = None, mode_l: Optional[int] = None,
                    cone_angle_rad: Optional[float] = None, truncation: Optional[float] = None, file: Optional[str] = None, background: Any = None,
                    ripple: Optional[Mapping[str, Any]] = None, noise: Optional[Mapping[str, Any]] = None, **extra) -> dict:
    """The field at the first spatial element (``transverse(initial_profile=...)``): "gaussian", "superGaussian" (``order``), "flatTop"
    (super-Gaussian of order 10), "tophat" (hard edge), "hermiteGauss" (``mode_x``, ``mode_y``), "laguerreGauss" (``mode_p``, ``mode_l``),
    "besselGauss" (``cone_angle_rad``, Gaussian envelope of ``radius_m``), "airy" (scale ``radius_m``, ``truncation``) or "measured" (``file``: an
    NPZ with x_m, y_m or r_m, fluence or amplitude, optionally phase_rad; ``background``: a number or "auto"). ``geometry`` "round" (default) or "square" (separable in x and y; cartesian
    grids). ``radius_m`` (or ``radius_x_m`` / ``radius_y_m``) is the 1/e^2 radius, or the edge radius / half-width of a tophat; it replaces
    the element's beam size, so no ``beam`` is needed. ``ripple`` / ``noise`` (cartesian grids) seed small-scale self-focusing:
    :func:`profile_ripple`, :func:`profile_noise`."""
    return _clean({"kind": kind, "order": order, "radiusM": radius_m, "geometry": geometry, "radiusXM": radius_x_m, "radiusYM": radius_y_m,
                   "radiusOfCurvatureM": radius_of_curvature_m, "modeX": mode_x, "modeY": mode_y, "modeP": mode_p, "modeL": mode_l,
                   "coneAngleRad": cone_angle_rad, "truncation": truncation, "file": file, "background": background,
                   "ripple": ripple, "noise": noise, **extra})


def profile_ripple(period_m: float, relative_amplitude: float, angle_deg: float = 0.0, phase_rad: float = 0.0) -> dict:
    """A sinusoidal amplitude ripple a cos(2 pi (x cos t + y sin t) / period + phase) on the field: the Bespalov-Talanov seed. The ripple of
    the fluence is 2 a; with a plane wave of intensity I it grows as cosh(g z), g^2 = q (2 Delta - q), q = K^2 / 2k, Delta = k0 n2 I."""
    return {"periodM": period_m, "relativeAmplitude": relative_amplitude, "angleDeg": angle_deg, "phaseRad": phase_rad}


def profile_noise(relative_amplitude_rms: float = 0.0, correlation_length_m: float = 50e-6, phase_rms_rad: float = 0.0, seed: int = 1,
                  components: Optional[int] = None) -> dict:
    """Seeded smooth random amplitude (relative rms) and phase noise with the given correlation length: the seed of small-scale
    self-focusing. The element reports ``fluenceContrastIn`` / ``Out`` and ``smallScaleGain``."""
    return _clean({"relativeAmplitudeRms": relative_amplitude_rms, "phaseRmsRad": phase_rms_rad or None, "correlationLengthM": correlation_length_m,
                   "seed": seed, "components": components})


def aux_beam(role: str, pulse: Optional[Mapping[str, Any]] = None, *, from_results: Optional[str] = None, energy: Optional[float] = None,
             radius_m: Optional[float] = None, profile: str = "gaussian", order: Optional[int] = None,
             delay_s: Any = "auto", radius_of_curvature_m: Optional[float] = None) -> dict:
    """Auxiliary beam for a crystal: a pulse spec (see :func:`pulse`) or ``from_results`` (a results folder)."""
    beam = _clean({"kind": profile, "radiusM": radius_m, "order": order, "radiusOfCurvatureM": radius_of_curvature_m})
    d = {"role": role, "beam": beam, "delayS": delay_s}
    if from_results is not None:
        d["from"] = from_results
        if energy is not None:
            d["energyJ"] = energy
    elif pulse is not None:
        d["pulse"] = pulse
    else:
        raise ValueError("aux_beam needs a pulse or from_results")
    return d


def crystal(id: str, crystal: Any, length_m: float, process: str = "shg", type: Optional[str] = None, *, cut: Any = "auto",
            noncollinear: Any = None, qpm: Any = None, aux: Optional[list] = None, output: Optional[str] = None,
            main_role: Optional[str] = None, record: Optional[list] = None, beam: Optional[Mapping[str, Any]] = None,
            transverse: Optional[Mapping[str, Any]] = None, deff_pm_per_v: Optional[float] = None, seed_noise: Optional[bool] = None,
            temperature_c: Optional[float] = None, overlap: Any = None, chi3: Optional[bool] = None, **extra) -> "T.CrystalElement":
    """A chi(2) crystal element (docs/reference/crystals.md).

    ``cut`` = "auto" (phase matched at the nominal wavelengths) or {"thetaDeg": .., "phiDeg": ..}; ``noncollinear`` in
    degrees or "magic"; ``qpm`` = True / a period in m / {"periodM": .., "model": "averaged"|"domains", "phaseDeg": ..};
    ``type`` defaults to "I" (birefringent) or "0" (QPM); ``overlap`` = "gaussian" (default) | "planeWave" |
    {"model": "planeWave", "radiusM": ..} | {"areaM2": ..} (1-D coupling model); ``chi3=False`` switches SPM/XPM off."""
    inter = _clean({"process": process, "type": type, "noncollinearDeg": noncollinear,
                    "qpm": qpm if (qpm is None or isinstance(qpm, (bool, dict))) else {"periodM": qpm}})
    return _clean({"id": id, "type": "crystal", "crystal": crystal, "lengthM": length_m, "cut": cut, "interaction": inter,
                   "aux": aux, "output": output, "mainRole": main_role, "record": record, "beam": beam,
                   "transverse": transverse, "deffPmPerV": deff_pm_per_v, "seedNoise": seed_noise,
                   "temperatureC": temperature_c, "overlap": overlap, "chi3": chi3, **extra})


# gain media (docs/reference/gain.md)
def pump(kind: str = "cw", wavelength_m: float = 940e-9, radius_m: float = 0.5e-3, power_w: Optional[float] = None,
         energy_j: Optional[float] = None, profile: str = "tophat", order: Optional[int] = None, passes: int = 1,
         delay_s: Optional[float] = None) -> dict:
    """A pump for gain / amplifier elements: kind "cw" (power_w) or "pulsed" (energy_j, delay_s before the seed);
    profile "tophat" | "gaussian" | "superGaussian" (order); passes 2 = reflected back through the crystal."""
    return _clean({"kind": kind, "wavelengthM": wavelength_m, "radiusM": radius_m, "powerW": power_w, "energyJ": energy_j,
                   "profile": profile, "order": order, "passes": passes, "delayS": delay_s})


def gain(id: str, medium: Any, length_m: float, pump: Optional[Mapping[str, Any]] = None, inversion: Optional[Mapping[str, Any]] = None,
         beam: Optional[Mapping[str, Any]] = None, mode_radius_m: Optional[float] = None, transverse: Optional[Mapping[str, Any]] = None,
         thermal_lens: Any = None, slices: Optional[int] = None, steady_state: Any = None, **extra) -> "T.GainElement":
    """A pumped gain medium crossed once (docs/reference/gain.md). ``medium`` = "Yb:YAG" | "Ti:sapphire" | "Nd:YAG" or a
    gain-medium object ({"base": "Yb:YAG", "dopingDensityM3": ...} or a full definition); ``inversion`` =
    {"fraction": b} or {"smallSignalGain": G} instead of / on top of a pump; ``transverse`` for spatial gain."""
    return _clean({"id": id, "type": "gain", "medium": medium, "lengthM": length_m, "pump": pump, "inversion": inversion,
                   "beam": beam, "modeRadiusM": mode_radius_m, "transverse": transverse, "thermalLens": thermal_lens,
                   "slices": slices, "steadyState": steady_state, **extra})


def thermal_lens(*, rod_radius_m: Optional[float] = None, heat_fraction: Optional[float] = None, apply: Optional[bool] = None,
                 track: Optional[bool] = None, focal_length_m: Optional[float] = None) -> dict:
    """The thermal lens of a gain element (``gain(thermal_lens=...)``; docs/reference/gain.md#thermal-lens): heat = ``heat_fraction`` (default
    the quantum defect) x absorbed pump, conducted to a rod of radius ``rod_radius_m``. ``track`` (default True) also puts it on the tracked
    Gaussian beam (thin lenses per slice, fitted over the beam); ``focal_length_m`` is a measured focal length used instead of the heat model."""
    return _clean({"rodRadiusM": rod_radius_m, "heatFraction": heat_fraction, "apply": apply, "track": track, "focalLengthM": focal_length_m})


def multipass_amplifier(id: str, medium: Any, length_m: float, passes: int, pump: Optional[Mapping[str, Any]] = None,
                        between_passes: Optional[Mapping[str, Any]] = None, mode_radius_m: Optional[float] = None,
                        beam: Optional[Mapping[str, Any]] = None, inversion: Optional[Mapping[str, Any]] = None, **extra) -> dict:
    """A multipass amplifier (1-D): ``between_passes`` = {"loss": 0.02, "gddFs2": .., "materials": [{"name", "lengthM"}]}."""
    return _clean({"id": id, "type": "amplifier", "geometry": "multipass", "passes": passes, "medium": medium,
                   "lengthM": length_m, "pump": pump, "betweenPasses": between_passes, "modeRadiusM": mode_radius_m,
                   "beam": beam, "inversion": inversion, **extra})


def regenerative_amplifier(id: str, medium: Any, length_m: float, round_trips: Any = "auto", pump: Optional[Mapping[str, Any]] = None,
                           cavity: Optional[Mapping[str, Any]] = None, mode_radius_m: Optional[float] = None, beam: Optional[Mapping[str, Any]] = None,
                           inversion: Optional[Mapping[str, Any]] = None, passes_per_round_trip: Optional[int] = None, **extra) -> dict:
    """A regenerative amplifier (1-D): ``round_trips`` = N or "auto" (eject at the energy maximum);
    ``cavity`` = {"lossPerRoundTrip": .., "gddFs2": .., "todFs3": .., "materials": [...]}."""
    return _clean({"id": id, "type": "amplifier", "geometry": "regenerative", "roundTrips": round_trips, "medium": medium,
                   "lengthM": length_m, "pump": pump, "cavity": cavity, "modeRadiusM": mode_radius_m, "beam": beam,
                   "inversion": inversion, "passesPerRoundTrip": passes_per_round_trip, **extra})


# lasers (docs/reference/oscillators.md; ultrafast.laser.Oscillator builds whole oscillators)
def saturable_absorber(id: str, kind: str = "slow", modulation_depth: float = 0.02, nonsaturable_loss: float = 0.0,
                       recovery_time_s: Optional[float] = None, saturation_fluence_j_per_m2: Optional[float] = None,
                       spot_radius_m: Optional[float] = None, saturation_energy_j: Optional[float] = None,
                       saturation_power_w: Optional[float] = None, kappa_per_w: Optional[float] = None) -> "T.SaturableAbsorberElement":
    """A saturable absorber element: kind "slow" (SESAM), "fast" or "kerrLens"."""
    return _clean({"id": id, "type": "saturableAbsorber", "kind": kind, "modulationDepth": modulation_depth,
                   "nonsaturableLoss": nonsaturable_loss, "recoveryTimeS": recovery_time_s,
                   "saturationFluenceJPerM2": saturation_fluence_j_per_m2, "spotRadiusM": spot_radius_m,
                   "saturationEnergyJ": saturation_energy_j, "saturationPowerW": saturation_power_w, "kappaPerW": kappa_per_w})


def soft_aperture(id: str, radius_m: float, order: int = 1) -> dict:
    """A soft (Gaussian, or super-Gaussian of ``order``) aperture acting on a carried transverse profile."""
    return {"id": id, "type": "aperture", "softRadiusM": radius_m, "order": order}


# beams
def gaussian_beam(w0_m: float, z_waist_m: float = 0.0, m2: float = 1.0, wavelength_m: Optional[float] = None) -> dict:
    return _clean({"kind": "gaussian", "w0M": w0_m, "zWaistM": z_waist_m, "m2": m2, "wavelengthM": wavelength_m})


def constant_beam(radius_m: Optional[float] = None, area_m2: Optional[float] = None) -> dict:
    return _clean({"kind": "constant", "radiusM": radius_m, "areaM2": area_m2})


def sampled_beam(z_m: Sequence[float], radius_m: Sequence[float]) -> dict:
    return {"kind": "sampled", "zM": list(map(float, z_m)), "radiusM": list(map(float, radius_m))}


# materials and coatings
def material(index: Any = None, n2: Optional[float] = None, base: Optional[str] = None, raman: Any = None,
             attenuation: Any = None, gas: Optional[Mapping[str, Any]] = None, index_reference: Optional[str] = None, **extra) -> dict:
    return _clean({"base": base, "index": index, "indexReference": index_reference, "n2": n2, "raman": raman,
                   "attenuation": attenuation, "gas": gas, **extra})


def sellmeier(B: Sequence[float], C: Sequence[float]) -> dict:
    return {"kind": "sellmeier", "B": list(B), "C": list(C)}


def gas(species: str, pressure_bar: float = 1.0, temperature_k: float = 293.15) -> dict:
    return {"species": species, "pressureBar": pressure_bar, "temperatureK": temperature_k}


def coating_gdd(R: float, gdd_s2: float, tod_s3: float = 0.0) -> dict:
    return {"kind": "gdd", "R": R, "gddS2": gdd_s2, "todS3": tod_s3}


def coating_quarter_wave(design: str, design_wavelength_m: float, H: str = "Ta2O5", L: str = "SiO2_film") -> dict:
    return {"kind": "quarterWave", "design": design, "designWavelengthM": design_wavelength_m, "H": H, "L": L}


# ------------------------------------------------------------------ run spec
def pulse(center_wavelength_m: float, duration_fwhm_s: Optional[float] = None, energy: Optional[float] = None,
          shape: str = "gaussian", rep_rate_hz: Optional[float] = None, average_power_w: Optional[float] = None,
          peak_power_w: Optional[float] = None, gdd_s2: float = 0.0, tod_s3: float = 0.0, polarization="x", **extra) -> "T.Pulse":
    d = _clean({"shape": shape, "centerWavelengthM": center_wavelength_m, "durationFwhmS": duration_fwhm_s,
                "energyJ": energy, "repRateHz": rep_rate_hz, "averagePowerW": average_power_w, "peakPowerW": peak_power_w,
                "polarization": polarization, **extra})
    if gdd_s2 or tod_s3:
        d["chirp"] = {"gddS2": gdd_s2, "todS3": tod_s3}
    return d


def measured_spectrum(wavelengths_m: Sequence[float], intensity: Sequence[float], energy: Optional[float] = None,
                      phase_rad: Optional[Sequence[float]] = None, *, background: Any = None, threshold: Optional[float] = None,
                      resolution_m: Optional[float] = None, deconvolve: Optional[bool] = None,
                      extrapolate: Optional[str] = None, center_wavelength_m: Optional[float] = None, **extra) -> "T.Pulse":
    """An input pulse from a measured spectrum (intensity per unit wavelength, optional phase), docs/reference/measured-data.md.

    The rows may be in any order: they are sorted, repeated wavelengths are merged and non-finite rows dropped (each
    reported in the run notes). ``background``: a number to subtract, ``"auto"`` (median of the darker edge -- robust
    to a spectrum cut off on one side by the spectrometer), ``"min"`` or ``"none"`` (default; a floor above 0.3 % of
    the peak is then reported, because a flat floor shortens the transform limit). ``threshold``: after subtraction,
    values below this fraction of the peak are set to zero. ``resolution_m`` is the recording spectrometer's
    resolution (FWHM); ``deconvolve=True`` removes it (Richardson-Lucy); ``extrapolate`` "zero" (no light outside the
    recorded range, default) or "gaussian" (Gaussian tails fitted to the data)."""
    sp = _clean({"wavelengthsM": list(wavelengths_m), "intensity": list(intensity), "phaseRad": list(phase_rad) if phase_rad is not None else None,
                 "background": background, "threshold": threshold,
                 "resolutionM": resolution_m, "deconvolve": deconvolve, "extrapolate": extrapolate})
    return _clean({"shape": "spectrum", "energyJ": energy, "centerWavelengthM": center_wavelength_m, "spectrum": sp, **extra})


def measured_spectrum_csv(csv: str, energy: Optional[float] = None, *, wavelength_unit: Optional[str] = None,
                          background: Any = None, threshold: Optional[float] = None, resolution_m: Optional[float] = None,
                          deconvolve: Optional[bool] = None, extrapolate: Optional[str] = None,
                          center_wavelength_m: Optional[float] = None, **extra) -> "T.Pulse":
    """An input pulse from a spectrum file (CSV: wavelength, intensity, optional phase in rad; ``wavelength_unit`` "nm"
    (default), "um" or "m"). Relative paths are resolved against the run file's folder. Same cleaning and options as
    :func:`measured_spectrum`."""
    sp = _clean({"csv": str(csv), "wavelengthUnit": wavelength_unit, "background": background, "threshold": threshold,
                 "resolutionM": resolution_m, "deconvolve": deconvolve, "extrapolate": extrapolate})
    return _clean({"shape": "spectrum", "energyJ": energy, "centerWavelengthM": center_wavelength_m, "spectrum": sp, **extra})


def measured(file: Optional[str] = None, *, values: Optional[Mapping[str, Any]] = None, sigma: Optional[float] = None,
             normalize: Optional[str] = None, background: Any = None, columns: Optional[Mapping[str, str]] = None,
             range_x: Optional[Sequence[float]] = None, x_unit: Optional[str] = None, y_unit: Optional[str] = None,
             value_sigma: Optional[float] = None, weight: Optional[float] = None,
             resolution_m: Optional[float] = None, resolution_s: Optional[float] = None, match_resolution: Optional[bool] = None,
             scale: Optional[str] = None, sigma_db: Optional[float] = None, floor_db: Optional[float] = None,
             simulated: Optional[Mapping[str, str]] = None, **extra) -> "T.Measured":
    """Recorded data for an instrument (its ``measured`` key; docs/reference/measured-data.md): a CSV / NPZ / JSON
    ``file`` and/or scalar ``values`` ({name: value | [value, sigma]}, e.g. ``{"energyJ": [1.2e-6, 2e-8]}``).

    The simulated reading is compared on the recording's axis and only where the simulated instrument has readings
    (points outside are left out and counted in the notes). ``resolution_m`` / ``resolution_s``: the recording
    instrument's resolution (FWHM); if it is coarser than the simulated instrument's, the simulated curve is blurred
    to match (``match_resolution=False`` to only report it). ``normalize`` "peak" (default) | "maximum" | "area" |
    "scale" | "none"; ``sigma`` the relative uncertainty per point; ``background`` a number, "auto" (median of the
    darker edge) or "min"; ``range_x`` [min, max] in SI to compare part of the axis. ``scale="log"`` compares in dB
    (``sigma_db``, ``floor_db``): use it for contrast and cross-correlator data. Column names carry units
    (``wavelength_nm``, ``delay_fs``); ``columns`` picks them ({"x", "y", "sigma"} or {"rows", "cols", "trace"}).
    Use as ``instrument("spectrometer", measured=measured("osa.csv", background="auto"))``."""
    return _clean({"file": file, "values": dict(values) if values is not None else None, "sigma": sigma, "normalize": normalize,
                   "background": background, "columns": dict(columns) if columns is not None else None,
                   "rangeX": list(range_x) if range_x is not None else None, "xUnit": x_unit, "yUnit": y_unit,
                   "valueSigma": value_sigma, "weight": weight, "resolutionM": resolution_m, "resolutionS": resolution_s,
                   "matchResolution": match_resolution, "scale": scale, "sigmaDb": sigma_db, "floorDb": floor_db,
                   "simulated": dict(simulated) if simulated is not None else None, **extra})


def instrument(kind: str, at: str = "end", name: Optional[str] = None, **params) -> dict:
    """An instrument (docs/reference/instruments.md). Keyword arguments are its parameters in the engine's names
    (``resolutionM=...``, ``axis=axis(...)``, ``detector={...}``, ``measured=measured(...)`` to compare with a
    recording)."""
    return _clean({"kind": kind, "at": at, "name": name, **params})


def axis(values: Optional[Sequence[float]] = None, *, start: Optional[float] = None, stop: Optional[float] = None,
         step: Optional[float] = None, points: Optional[int] = None, bin_width: Any = None, resolution: Optional[float] = None) -> dict:
    """Where a curve instrument takes its readings (docs/reference/instruments.md#axis-where-readings-are-taken):
    explicit ``values`` (e.g. your spectrometer's pixel wavelengths) or ``start``/``stop`` with ``step`` or ``points``,
    each reading integrating over ``bin_width`` (a number or "pixel") and blurred by a Gaussian ``resolution`` (FWHM).
    SI units of the axis (m for wavelengths, s for delays). Use as ``instrument("spectrometer", axis=axis(...))``."""
    return _clean({"values": list(values) if values is not None else None, "from": start, "to": stop, "step": step,
                   "points": points, "binWidth": bin_width, "resolution": resolution})


def override(target: str, prop: str, model: Any = None, value: Any = None, mode: str = "replace") -> dict:
    d = {"target": target, "property": prop, "mode": mode}
    if model is not None:
        d["model"] = model
    else:
        d["value"] = value
    return d


def physics(*, beam_tracking: Any = None, spatio_temporal: Any = None, transverse: Optional[Mapping[str, Any]] = None, **extra: Any) -> "T.Physics":
    """Physics switches of a run (``run_spec(physics=...)``). ``beam_tracking``: "auto" | "on" | "off" (Gaussian q tracking from the
    ``input_beam``); ``spatio_temporal``: "auto" | "on" | "track" | "off" -- angular / spatial dispersion and pulse-front tilt carried with the
    tracked beam, their group-delay dispersion applied to the pulse ("track": reported only); ``transverse``: the default spatial model.
    Other keys (``nonlinear``, ``raman``, ...) go through ``extra``."""
    return _clean({"beamTracking": beam_tracking, "spatioTemporal": spatio_temporal, "transverse": transverse, **extra})  # type: ignore[return-value]


def run_spec(pulse: Mapping[str, Any], name: str = "run", grid: Optional[Mapping[str, Any]] = None, physics: Optional[Mapping[str, Any]] = None,
             stepper: Optional[Mapping[str, Any]] = None, probes: Any = "all", instruments: Optional[list] = None,
             overrides: Optional[list] = None, outputs: Optional[Mapping[str, Any]] = None, input_beam: Optional[Mapping[str, Any]] = None,
             compute: Optional[Mapping[str, Any]] = None, detectors: Optional[Mapping[str, Any]] = None, **extra: Any) -> "T.RunSpec":
    """A run spec. ``compute`` selects the compute back-end (:func:`compute`; GPU: ``compute(backend="auto")``).
    ``extra`` adds top-level keys provided by plugins, e.g. ``fit=`` (fitting plugin: ``from ultrafast_fitting import fit``)."""
    return _clean({"format": "ultrafast-run", "version": 2, "name": name, "pulse": pulse, "grid": grid, "physics": physics,
                   "stepper": stepper, "probes": probes, "instruments": instruments, "overrides": overrides,
                   "outputs": outputs, "inputBeam": input_beam, "compute": compute, "detectors": detectors, **extra})


def compute(backend: Optional[str] = None, *, threads: Optional[int] = None, fft: Optional[str] = None, device: Optional[int] = None,
            gpu_min_points: Optional[int] = None) -> "T.Compute":
    """Where the run computes (docs/reference/speed.md).

    ``backend``: "cpu" (default), "auto" (each nonlinear element goes to the fastest device that supports it and is
    large enough -- a GPU when the gpu plugin finds one -- else the CPU), "cuda" or "opencl" (gpu plugin; elements
    the device cannot run stay on the CPU with a note). ``threads``: CPU worker threads (0 = all cores). ``fft``: CPU
    FFT provider "auto" (Accelerate on macOS, FFTW when installed, else managed), "managed", "fftw", "accelerate".
    ``device``: GPU index from :func:`ultrafast.devices`. ``gpu_min_points``: smallest problem (complex samples per
    state) "auto" sends to a GPU (default 8192 for 1-D, 32768 for spatial). Results agree with the CPU to rounding."""
    return _clean({"backend": backend, "threads": threads, "fft": fft, "device": device, "gpuMinPoints": gpu_min_points})  # type: ignore[return-value]


def sampling(plane: str = "near", aperture: str = "pinhole", radius_m: Optional[float] = None,
             width_m: Optional[float] = None, focal_length_m: Optional[float] = None, center_x_m: Optional[float] = None,
             center_y_m: Optional[float] = None, na: Optional[float] = None, propagate_m: Optional[float] = None) -> dict:
    """Where an instrument looks (docs/reference/instruments.md): ``plane`` "near" | "far" (focal plane of a lens of
    ``focal_length_m``), ``aperture`` "integrated" | "pinhole" | "iris" | "fibre" | "slit" | "point".
    Use as ``instrument("spectrometer", sampling=sampling("far", "fibre", radius_m=25e-6, focal_length_m=0.1))``."""
    ap = _clean({"kind": aperture, "radiusM": radius_m, "widthM": width_m, "centerXM": center_x_m, "centerYM": center_y_m, "na": na})
    return _clean({"plane": plane, "focalLengthM": focal_length_m, "propagateM": propagate_m, "aperture": ap})


def save(obj: Mapping[str, Any], path: str) -> str:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2)
    return path
