"""Builders for the fitting plugin (plain dicts, like :mod:`ultrafast.build`): transform elements and their operators,
fit objects, the "fit" and "backward" run-spec blocks, and "measured" data for instruments.

>>> from ultrafast import build as b
>>> from ultrafast_fitting import transform, spectral_phase, fit_value, fit, measured
>>> shaper = transform("residual", [spectral_phase(gdd_fs2=fit_value(0, -2000, 2000), tod_fs3=fit_value(0, -2e4, 2e4))])
>>> spec = b.run_spec(b.pulse(800e-9, 25e-15, energy=1e-6),
...                   instruments=[b.instrument("dscan", gddRangeS2=[-500e-30, 500e-30], measured=measured("dscan.npz"))],
...                   fit=fit())
"""
from __future__ import annotations

from typing import Any, Dict, Mapping, Optional, Sequence, Union

Number = Optional[Union[float, int, Dict[str, Any]]]   # a number or a fit object (fit_value); None = not given


def _clean(d):
    return {k: v for k, v in d.items() if v is not None}


# ------------------------------------------------------------------ fit objects
def fit_value(value: float, min: Optional[float] = None, max: Optional[float] = None, *, fit: bool = True,
              prior: Any = None, scale: Optional[float] = None, step: Optional[float] = None,
              name: Optional[str] = None, unit: Optional[str] = None) -> Dict[str, Any]:
    """A number to fit, in place of a plain number in a transform operator: its start ``value``, bounds, an optional
    Gaussian ``prior`` (sigma, or {"mean", "sigma"}). A plain run uses ``value``. ``fit=False`` keeps it fixed."""
    return _clean({"value": value, "fit": fit, "min": min, "max": max, "prior": prior, "scale": scale, "step": step, "name": name, "unit": unit})


def _op(kind: str, params: Dict[str, Any], fit: Optional[Sequence[str]] = None, bounds: Optional[Mapping[str, Sequence[float]]] = None,
        priors: Optional[Mapping[str, Any]] = None, extra: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    d = {"kind": kind, **_clean(params)}
    if fit:
        d["fit"] = list(fit)
    if bounds:
        d["bounds"] = {k: list(v) for k, v in bounds.items()}
    if priors:
        d["priors"] = dict(priors)
    if extra:
        d.update(extra)
    return d


# ------------------------------------------------------------------ transform element and operators
def transform(id: str, operators: Sequence[Mapping[str, Any]], regularization: Optional[float] = None) -> Dict[str, Any]:
    """A transform element (docs/plugins/fitting.md#transform-elements): physical changes to the beam, applied in order.
    Put it where the change happens (after the stretcher, before the compressor, ...)."""
    return _clean({"id": id, "type": "transform", "operators": [dict(o) for o in operators], "regularization": regularization})


def spectral_phase(gdd_fs2: Number = None, tod_fs3: Number = None, fod_fs4: Number = None, fifth_fs5: Number = None, *,
                   knots_wavelengths_m: Optional[Sequence[float]] = None, knots_phase_rad: Optional[Sequence[Number]] = None,
                   center_wavelength_m: Number = None, smoothness: Optional[float] = None, fit: Optional[Sequence[str]] = None,
                   bounds=None, priors=None, **extra) -> Dict[str, Any]:
    """Spectral phase: Taylor terms about the centre wavelength and/or spline knots of phase vs wavelength.
    ``fit=["knots.phaseRad"]`` fits every knot; ``smoothness`` penalises their curvature."""
    knots = None
    if knots_wavelengths_m is not None:
        knots = {"wavelengthsM": list(knots_wavelengths_m), "phaseRad": list(knots_phase_rad if knots_phase_rad is not None else [0.0] * len(knots_wavelengths_m))}
    return _op("spectralPhase", {"gddFs2": gdd_fs2, "todFs3": tod_fs3, "fodFs4": fod_fs4, "fifthFs5": fifth_fs5, "knots": knots,
                                 "centerWavelengthM": center_wavelength_m, "smoothness": smoothness}, fit, bounds, priors, extra)


def spectral_amplitude(shape: Optional[str] = None, *, fwhm_m: Number = None, center_wavelength_m: Number = None, peak: Number = None,
                       order: Number = None, tilt_db_per_nm: Number = None, curvature_db_per_nm2: Number = None,
                       knots_wavelengths_m: Optional[Sequence[float]] = None, knots_transmission: Optional[Sequence[Number]] = None,
                       model: Optional[Mapping[str, Any]] = None, passive: Optional[bool] = None, causal: Optional[bool] = None,
                       smoothness: Optional[float] = None, fit: Optional[Sequence[str]] = None, bounds=None, priors=None, **extra) -> Dict[str, Any]:
    """Spectral amplitude (power transmission): "gaussian" / "superGaussian" band, "tilt" (dB/nm), "spline" knots, or a
    filter "model". ``causal=True`` adds the minimum phase a physical filter with this amplitude must have."""
    knots = None
    if knots_wavelengths_m is not None:
        knots = {"wavelengthsM": list(knots_wavelengths_m), "transmission": list(knots_transmission if knots_transmission is not None else [1.0] * len(knots_wavelengths_m))}
    return _op("spectralAmplitude", {"shape": shape, "fwhmM": fwhm_m, "centerWavelengthM": center_wavelength_m, "peak": peak, "order": order,
                                     "tiltDbPerNm": tilt_db_per_nm, "curvatureDbPerNm2": curvature_db_per_nm2, "knots": knots, "model": model,
                                     "passive": passive, "causal": causal, "smoothness": smoothness}, fit, bounds, priors, extra)


def energy(transmission: Number = None, *, gain_db: Number = None, fit: Optional[Sequence[str]] = None, bounds=None, priors=None) -> Dict[str, Any]:
    """A flat power transmission (or gain in dB)."""
    return _op("energy", {"transmission": transmission, "gainDb": gain_db}, fit, bounds, priors)


def delay(delay_s: Number = None, *, fit: Optional[Sequence[str]] = None, bounds=None, priors=None) -> Dict[str, Any]:
    """A pure group delay (relative to other beams; the engine's frame follows the pulse)."""
    return _op("delay", {"delayS": delay_s}, fit, bounds, priors)


def jones(retardance_rad: Number = None, axis_deg: Number = None, rotation_deg: Number = None, diattenuation: Number = None,
          diattenuation_axis_deg: Number = None, *, fit: Optional[Sequence[str]] = None, bounds=None, priors=None) -> Dict[str, Any]:
    """Polarisation errors: retarder (retardance, fast axis), diattenuator (weak-axis power loss), then a rotation."""
    return _op("jones", {"retardanceRad": retardance_rad, "axisDeg": axis_deg, "rotationDeg": rotation_deg, "diattenuation": diattenuation,
                         "diattenuationAxisDeg": diattenuation_axis_deg}, fit, bounds, priors)


def replica(delay_s: Number, relative_intensity: Number = None, phase_rad: Number = None, gdd_fs2: Number = None, *,
            relative_intensity_db: Number = None, fit: Optional[Sequence[str]] = None, bounds=None, priors=None) -> Dict[str, Any]:
    """A coherent pre- (negative delay) or post-pulse: a double reflection, an etalon. Give the level linearly or in dB
    (``relative_intensity_db``, better for fitting)."""
    return _op("replica", {"delayS": delay_s, "relativeIntensity": relative_intensity, "relativeIntensityDb": relative_intensity_db,
                           "phaseRad": phase_rad, "gddFs2": gdd_fs2}, fit, bounds, priors)


def phase_noise(rms_rad: Number = None, amplitude_rms: Number = None, correlation_nm: Number = None, seed: Optional[int] = None, *,
                fit: Optional[Sequence[str]] = None, bounds=None, priors=None) -> Dict[str, Any]:
    """Correlated spectral phase / amplitude noise: the coherent pedestal of stretchers and shapers."""
    return _op("phaseNoise", {"rmsRad": rms_rad, "amplitudeRms": amplitude_rms, "correlationNm": correlation_nm, "seed": seed}, fit, bounds, priors)


def pedestal(type: str = "ase", *, relative_intensity: Number = None, relative_intensity_db: Number = None, duration_s: Number = None,
             energy_fraction: Number = None, level: Number = None, level_db: Number = None, rise_s: Number = None, fall_s: Number = None,
             seed: Optional[int] = None, fit: Optional[Sequence[str]] = None, bounds=None, priors=None) -> Dict[str, Any]:
    """An incoherent background (ASE plateau or exponential wings): contrast degradation. Levels linearly or in dB."""
    return _op("pedestal", {"type": type, "relativeIntensity": relative_intensity, "relativeIntensityDb": relative_intensity_db,
                            "durationS": duration_s, "energyFraction": energy_fraction, "level": level, "levelDb": level_db,
                            "riseS": rise_s, "fallS": fall_s, "seed": seed}, fit, bounds, priors)


def wavefront(zernike: Optional[Mapping[Union[int, str], Number]] = None, *, radius_m: Number = None, wavelength_m: Number = None,
              tilt_x_rad: Number = None, tilt_y_rad: Number = None, fit: Optional[Sequence[str]] = None, bounds=None, priors=None) -> Dict[str, Any]:
    """Wavefront error: Zernike terms {Noll index: waves rms} and pointing, applied to the spatial profile (spatial
    models) or reported (1-D beams)."""
    z = {str(k): v for k, v in zernike.items()} if zernike else None
    return _op("wavefront", {"zernike": z, "radiusM": radius_m, "wavelengthM": wavelength_m, "tiltXRad": tilt_x_rad, "tiltYRad": tilt_y_rad}, fit, bounds, priors)


# ------------------------------------------------------------------ run-spec blocks
def measured(file: Optional[str] = None, *, values: Optional[dict] = None, sigma: Optional[float] = None,
             normalize: Optional[str] = None, background: Any = None, columns: Optional[dict] = None,
             range_x: Optional[Sequence[float]] = None, value_sigma: Optional[float] = None, weight: Optional[float] = None,
             resolution_m: Optional[float] = None, resolution_s: Optional[float] = None, scale: Optional[str] = None,
             sigma_db: Optional[float] = None, floor_db: Optional[float] = None, **extra) -> dict:
    """Recorded data for an instrument (docs/plugins/fitting.md): a CSV / NPZ / JSON ``file`` and/or scalar ``values``
    ({name: value | [value, sigma]}). ``resolution_m`` / ``resolution_s``: the recording instrument's resolution, checked
    against the simulated instrument. ``scale="log"`` compares in dB (``sigma_db``, ``floor_db``): use it for contrast and
    cross-correlator data. Relative file names are resolved against the run file's folder."""
    return _clean({"file": file, "values": values, "sigma": sigma, "normalize": normalize, "background": background,
                   "columns": columns, "rangeX": list(range_x) if range_x is not None else None, "valueSigma": value_sigma,
                   "weight": weight, "resolutionM": resolution_m, "resolutionS": resolution_s, "scale": scale,
                   "sigmaDb": sigma_db, "floorDb": floor_db, **extra})


def fit_parameter(path: str, min: Optional[float] = None, max: Optional[float] = None, *, name: Optional[str] = None,
                  initial: Optional[float] = None, unit: Optional[str] = None, scale: Optional[float] = None,
                  step: Optional[float] = None, prior: Any = None) -> dict:
    """A number to fit by path: "pulse/energyJ", "element:comp/gddFs2", "instrument:spec/resolutionM",
    "material:FusedSilica/n2" (docs/plugins/fitting.md#fitting). Transform numbers are better marked with fit_value."""
    return _clean({"path": path, "min": min, "max": max, "name": name, "initial": initial, "unit": unit, "scale": scale, "step": step, "prior": prior})


def fit(parameters: Optional[Sequence[dict]] = None, estimator: Optional[str] = None, max_iterations: Optional[int] = None,
        tolerance: Optional[float] = None, freeze_grid: Optional[bool] = None, parallel: Optional[int] = None, **options: Any) -> dict:
    """The "fit" block: fit every transform number marked with fit_value (and ``parameters`` by path) to every instrument
    with measured data. ``estimator`` "levenbergMarquardt" (default), "cmaes" or "mcmc"; ``options`` are the estimator's
    (population, sigma0, walkers, steps, burnIn, seed, ...). ``uf.run`` returns the best run with ``summary["fit"]``."""
    return _clean({"parameters": list(parameters) if parameters else None, "estimator": estimator, "maxIterations": max_iterations,
                   "tolerance": tolerance, "freezeGrid": freeze_grid, "parallel": parallel, **options})


def backward(start: Optional[str] = None, stop: Optional[str] = None, regularization: Optional[float] = None,
             active: Optional[str] = None, reference_pulse: Optional[Mapping[str, Any]] = None) -> dict:
    """The "backward" block: the run's pulse is the field measured at ``start`` (an element id or "end"); propagate it
    back to ``stop`` (an element id or "input"). ``active`` "error" | "linearized" (needs ``reference_pulse``) | "identity"."""
    return _clean({"from": start, "to": stop, "regularization": regularization, "active": active, "referencePulse": reference_pulse})
