# mypy: disable-error-code="return-value"
"""Helpers to write system files and run specs from Python (plain dicts, so they can be inspected,
edited, saved with :func:`save` and versioned). Every helper simply returns the JSON the engine reads;
see docs/system-file.md and docs/run-spec.md for all fields.

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
           description: Optional[str] = None) -> "T.System":
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
    d["elements"] = list(elements)
    if overrides:
        d["overrides"] = list(overrides)
    return d


def bulk(id: str, material: str, length_m: float, beam: Optional[Mapping[str, Any]] = None, nonlinear: Optional[bool] = None, **extra) -> "T.BulkElement":
    return _clean({"id": id, "type": "bulk", "material": material, "lengthM": length_m, "beam": beam, "nonlinear": nonlinear, **extra})


def gap(id: str, length_m: float, medium: str = "Air", **extra) -> "T.GapElement":
    return _clean({"id": id, "type": "gap", "material": medium, "lengthM": length_m, **extra})


def interface(id: str, before: str = "Air", after: str = "Air", aoi_deg: float = 0.0, coating=None, mode: str = "refract", **extra) -> "T.InterfaceElement":
    return _clean({"id": id, "type": "interface", "mode": mode, "before": before, "after": after, "aoiDeg": aoi_deg, "coating": coating, **extra})


def mirror(id: str, coating=None, aoi_deg: float = 0.0, medium: str = "Air", **extra) -> "T.MirrorElement":
    return _clean({"id": id, "type": "mirror", "before": medium, "aoiDeg": aoi_deg, "coating": coating, **extra})


def dispersion(id: str, gdd_s2: float = 0.0, tod_s3: float = 0.0, fod_s4: float = 0.0, **extra) -> "T.DispersionElement":
    return _clean({"id": id, "type": "dispersion", "gddS2": gdd_s2, "todS3": tod_s3, "fodS4": fod_s4, **extra})


def grating_pair(id: str, lines_per_mm: float, aoi_deg: float, separation_m: float, order: int = -1, passes: int = 2,
                 efficiency: float = 1.0) -> "T.GratingPairElement":
    return {"id": id, "type": "gratingPair", "linesPerMm": lines_per_mm, "aoiDeg": aoi_deg, "separationM": separation_m,
            "order": order, "passes": passes, "efficiency": efficiency}


def chirped_mirror(id: str, gdd_s2: float, bounces: int = 1, reflectance: float = 0.999, tod_s3: float = 0.0, **extra) -> "T.ChirpedMirrorElement":
    """Dispersive mirror (GDD/TOD per bounce). ``extra``: e.g. ``rippleGddFs2=30, ripplePeriodNm=25, complementaryPairs=True``."""
    return {"id": id, "type": "chirpedMirror", "gddS2": gdd_s2, "todS3": tod_s3, "bounces": bounces, "reflectance": reflectance, **extra}


def aperture(id: str, radius_m: float, beam_radius_m: float) -> "T.ApertureElement":
    return {"id": id, "type": "aperture", "radiusM": radius_m, "beamRadiusM": beam_radius_m}


def waveplate(id: str, plate: str = "half", axis_deg: float = 0.0, design_wavelength_m: Optional[float] = None, *,
              material: Optional[str] = None, order: Optional[int] = None, thickness_m: Optional[float] = None,
              construction: Optional[str] = None, plate_thickness_m: Optional[float] = None) -> "T.WaveplateElement":
    """Waveplate: dispersionless design model, or with ``material`` (Quartz, MgF2, ...) a thick plate with full dispersion
    (multi-order ``order`` / ``thickness_m``, or ``construction="compound"`` zero-order)."""
    return _clean({"id": id, "type": "waveplate", "plate": plate, "axisDeg": axis_deg, "designWavelengthM": design_wavelength_m,
                   "material": material, "order": order, "thicknessM": thickness_m, "construction": construction, "plateThicknessM": plate_thickness_m})


def plate(id: str, material: str, thickness_m: float, aoi_deg: float = 0.0, ghosts: Any = False, coating: Any = None,
          wedge_deg: float = 0.0, beam: Optional[Mapping[str, Any]] = None, **extra) -> "T.PlateElement":
    """Plane-parallel plate / window: surfaces, bulk (dispersion, nonlinearity with a beam), optional ghost replicas."""
    return _clean({"id": id, "type": "plate", "material": material, "thicknessM": thickness_m, "aoiDeg": aoi_deg or None,
                   "ghosts": ghosts or None, "coating": coating, "wedgeDeg": wedge_deg or None, "beam": beam, **extra})


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
               transport: Optional[str] = None, initial_profile: Optional[Mapping[str, Any]] = None, **extra) -> dict:
    """Transverse (spatial) model for an element: "radial" (Hankel) or "cartesian" (x-y FFT)."""
    return _clean({"model": model, "points": points, "radiusM": radius_m, "nx": nx, "ny": ny, "halfWidthM": half_width_m,
                   "transport": transport, "initialProfile": initial_profile, **extra})


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
    """A chi(2) crystal element (docs/crystals.md).

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


# gain media (docs/gain.md)
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
    """A pumped gain medium crossed once (docs/gain.md). ``medium`` = "Yb:YAG" | "Ti:sapphire" | "Nd:YAG" or a
    gain-medium object ({"base": "Yb:YAG", "dopingDensityM3": ...} or a full definition); ``inversion`` =
    {"fraction": b} or {"smallSignalGain": G} instead of / on top of a pump; ``transverse`` for spatial gain."""
    return _clean({"id": id, "type": "gain", "medium": medium, "lengthM": length_m, "pump": pump, "inversion": inversion,
                   "beam": beam, "modeRadiusM": mode_radius_m, "transverse": transverse, "thermalLens": thermal_lens,
                   "slices": slices, "steadyState": steady_state, **extra})


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


# lasers (docs/oscillators.md; ultrafast.laser.Oscillator builds whole oscillators)
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


def instrument(kind: str, at: str = "end", name: Optional[str] = None, **params) -> dict:
    return _clean({"kind": kind, "at": at, "name": name, **params})


def override(target: str, prop: str, model: Any = None, value: Any = None, mode: str = "replace") -> dict:
    d = {"target": target, "property": prop, "mode": mode}
    if model is not None:
        d["model"] = model
    else:
        d["value"] = value
    return d


def run_spec(pulse: Mapping[str, Any], name: str = "run", grid: Optional[Mapping[str, Any]] = None, physics: Optional[Mapping[str, Any]] = None,
             stepper: Optional[Mapping[str, Any]] = None, probes: Any = "all", instruments: Optional[list] = None,
             overrides: Optional[list] = None, outputs: Optional[Mapping[str, Any]] = None, input_beam: Optional[Mapping[str, Any]] = None,
             **extra: Any) -> "T.RunSpec":
    """A run spec. ``extra`` adds top-level keys provided by plugins, e.g. ``fit=`` (fitting plugin:
    ``from ultrafast_fitting import fit``)."""
    return _clean({"format": "ultrafast-run", "version": 2, "name": name, "pulse": pulse, "grid": grid, "physics": physics,
                   "stepper": stepper, "probes": probes, "instruments": instruments, "overrides": overrides,
                   "outputs": outputs, "inputBeam": input_beam, **extra})


def sampling(plane: str = "near", aperture: str = "pinhole", radius_m: Optional[float] = None,
             width_m: Optional[float] = None, focal_length_m: Optional[float] = None, center_x_m: Optional[float] = None,
             center_y_m: Optional[float] = None, na: Optional[float] = None, propagate_m: Optional[float] = None) -> dict:
    """Where an instrument looks (docs/measurements.md): ``plane`` "near" | "far" (focal plane of a lens of
    ``focal_length_m``), ``aperture`` "integrated" | "pinhole" | "iris" | "fibre" | "slit" | "point".
    Use as ``instrument("spectrometer", sampling=sampling("far", "fibre", radius_m=25e-6, focal_length_m=0.1))``."""
    ap = _clean({"kind": aperture, "radiusM": radius_m, "widthM": width_m, "centerXM": center_x_m, "centerYM": center_y_m, "na": na})
    return _clean({"plane": plane, "focalLengthM": focal_length_m, "propagateM": propagate_m, "aperture": ap})


def save(obj: Mapping[str, Any], path: str) -> str:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2)
    return path
