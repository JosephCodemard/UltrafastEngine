"""Closed-loop test benches that drive the engine: a deformable mirror with a wavefront sensor and a focal-spot camera, and a tiled-aperture array.

:class:`DmBench` builds the system  source (Gaussian) -> aberration screen -> deformable mirror -> [wavefront sensor] -> lens -> focal plane [camera],
runs it with new commands for every call, and returns the sensor readings. ``calibrate``, ``correct`` and ``loop`` wrap
:mod:`ultrafast.algorithms.control` around it; ``focus_metric`` is the peak fluence (a sensorless metric for :mod:`ultrafast.algorithms.sensorless`).
Each call is one engine run (about 0.1 s for 128 x 128 cells).
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional, Sequence, Tuple

import numpy as np

import ultrafast as uf
from ultrafast import build as b
from ultrafast.algorithms import control, zernike

from .build import DeformableMirror
from .devices import dm_actuators, dm_influence_matrix


class DmBench:
    """A simulated AO bench. ``aberration`` maps Noll index -> waves rms (at ``wavelength_m``) over the disc of radius ``pupil_radius_m`` (default 95 % of the
    grid half-width, so the Zernike polynomials are only evaluated where the grid is; the Gaussian beam underfills it). The deformable mirror is a square ``actuators`` x ``actuators`` array of pitch ``pitch_m`` centred on the beam."""

    def __init__(self, *, wavelength_m: float = 1030e-9, beam_radius_m: float = 1.0e-3, grid_n: int = 128, half_width_m: float = 2.0e-3,
                 actuators: int = 10, pitch_m: float = 0.4e-3, coupling: float = 0.15, aberration: Optional[Dict[int, float]] = None,
                 pupil_radius_m: Optional[float] = None, lenslets: int = 16, focal_length_m: float = 0.5, stroke_m: Optional[float] = None,
                 incidence_angle_rad: float = 0.0):
        self.wavelength_m, self.beam_radius_m, self.n, self.half = wavelength_m, beam_radius_m, grid_n, half_width_m
        self.actuators, self.pitch_m, self.coupling = actuators, pitch_m, coupling
        self.pupil_radius_m = pupil_radius_m or 0.95 * half_width_m
        self.lenslets, self.focal_length_m, self.stroke_m, self.aoi = lenslets, focal_length_m, stroke_m, incidence_angle_rad
        self.n_actuators = actuators * actuators
        self.positions = dm_actuators("square", pitch_m=pitch_m, actuators_x=actuators)
        dx = 2 * half_width_m / grid_n
        self.x = (np.arange(grid_n) - (grid_n - 1) / 2) * dx
        self.aberration = dict(aberration or {})
        self.screen_phase = self._screen(self.aberration)
        self._valid = None
        self._reference_peak = None
        self.runs = 0

    # --------------------------------------------------------------------------------------------- model
    def _screen(self, coefs: Dict[int, float]) -> np.ndarray:
        """Phase screen (rad) from Zernike coefficients (waves rms), on the grid cells."""
        w = np.zeros((self.n, self.n))
        for j, c in coefs.items():
            w += c * zernike.basis([j], self.x, self.x, self.pupil_radius_m)[0]
        return 2 * np.pi * w

    def _grid(self, initial=None):
        return b.transverse("cartesian", nx=self.n, ny=self.n, half_width_m=self.half, spectral_samples=1, absorbing_boundary=True, initial_profile=initial)

    def system(self, commands_m: Sequence[float], *, sensor: bool = True, camera: bool = True) -> dict:
        dm = DeformableMirror("dm", pitch_m=self.pitch_m, actuators_x=self.actuators, actuators_y=self.actuators, commands_m=np.asarray(commands_m, float).tolist(),
                              influence_coupling=self.coupling, stroke_m=self.stroke_m, incidence_angle_rad=self.aoi or None)
        screen = {"id": "screen", "type": "modulator", "cellsX": self.n, "cellsY": self.n, "pitchXM": 2 * self.half / self.n, "phaseRad": self.screen_phase.tolist(),
                  "designWavelengthM": self.wavelength_m, "outsideTransmission": 1.0}
        els = [b.gap("src", 0.0, medium="Vacuum", transverse=self._grid(b.initial_profile("gaussian", radius_m=self.beam_radius_m))), screen, dm]
        els += [b.lens("lens", self.focal_length_m), b.gap("z", self.focal_length_m, medium="Vacuum", transverse=self._grid())]
        return b.system("dm-bench", els)

    def run(self, commands_m: Optional[Sequence[float]] = None, *, sensor: bool = True, camera: bool = True):
        cmd = np.zeros(self.n_actuators) if commands_m is None else np.asarray(commands_m, float)
        inst = []
        if sensor:
            inst.append(b.instrument("wavefrontSensor", at="dm", name="wfs", pixels=self.lenslets, apertureRadiusM=self.pupil_radius_m, threshold=0.0, zernikeTerms=15))
        if camera:
            inst.append(b.instrument("beamProfile", at="z", name="cam"))
        self.runs += 1
        return uf.run(self.system(cmd), b.run_spec(b.cw(self.wavelength_m, 1.0), instruments=inst))

    # --------------------------------------------------------------------------------------------- readings
    def slopes(self, commands_m=None) -> np.ndarray:
        """Zonal measurement vector [slope_x; slope_y] (rad) over the lenslets inside the pupil."""
        d = self.run(commands_m, camera=False).instrument("wfs")
        sx, sy = np.asarray(d["slope_x_rad"]), np.asarray(d["slope_y_rad"])
        if self._valid is None:
            X, Y = np.meshgrid(d["x_m"], d["y_m"], indexing="ij")
            self._valid = (np.hypot(X, Y) <= 1.3 * self.beam_radius_m) & np.isfinite(sx) & np.isfinite(sy)       # lenslets that see the beam
        return np.concatenate([sx[self._valid], sy[self._valid]])

    def wavefront_waves(self, commands_m=None) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
        d = self.run(commands_m, camera=False).instrument("wfs")
        return np.asarray(d["wavefront_waves"]), np.asarray(d["x_m"]), np.asarray(d["y_m"])

    def peak_fluence(self, commands_m=None) -> float:
        """Peak of the focal fluence map (J/m^2), refined between pixels by a quadratic fit through the 3 x 3 neighbourhood of the brightest pixel (the focal
        pixel is 0.8 spot radii for the default grid, so the raw maximum would depend on where the spot falls)."""
        F = np.asarray(self.run(commands_m, sensor=False).instrument("cam")["fluence_map_j_m2"])
        i, j = np.unravel_index(np.argmax(F), F.shape)
        i = min(max(i, 1), F.shape[0] - 2); j = min(max(j, 1), F.shape[1] - 2)
        n = np.log(np.maximum(F[i - 1:i + 2, j - 1:j + 2], 1e-300))              # a Gaussian spot is a parabola in the log: the fit is exact for it
        dx = (n[2, 1] - n[0, 1]) / 2; dy = (n[1, 2] - n[1, 0]) / 2
        dxx = n[2, 1] - 2 * n[1, 1] + n[0, 1]; dyy = n[1, 2] - 2 * n[1, 1] + n[1, 0]
        dxy = (n[2, 2] - n[2, 0] - n[0, 2] + n[0, 0]) / 4
        H = np.array([[dxx, dxy], [dxy, dyy]]); g = np.array([dx, dy])
        try:
            off = -np.linalg.solve(H, g)
        except np.linalg.LinAlgError:
            return float(F[i, j])
        off = np.clip(off, -1, 1)
        return float(np.exp(n[1, 1] + g @ off + 0.5 * off @ H @ off))

    def reference_peak(self) -> float:
        """Peak fluence of the same beam without aberration and with a flat mirror (the diffraction-limited focus)."""
        if self._reference_peak is None:
            saved = self.screen_phase
            self.screen_phase = np.zeros_like(saved)
            self._reference_peak = self.peak_fluence(None)
            self.screen_phase = saved
        return self._reference_peak

    def strehl(self, commands_m=None) -> float:
        """Focal peak fluence over the diffraction-limited peak (the Strehl ratio; same energy, same Gaussian beam)."""
        return self.peak_fluence(commands_m) / self.reference_peak()

    # --------------------------------------------------------------------------------------------- control
    def calibrate(self, poke_m: float = 20e-9) -> np.ndarray:
        """Interaction matrix D: sensor change per metre of actuator command (push-pull poke about the aberrated state, linear device: exact)."""
        return control.interaction_matrix(self.slopes, self.n_actuators, poke_m)

    def correct(self, D: np.ndarray, regularisation: float = 1e-3, method: str = "tikhonov", n_modes: Optional[int] = None) -> Tuple[np.ndarray, np.ndarray]:
        """One-shot correction: measure with the mirror flat, command = -G m. Returns (commands, singular values)."""
        G, s = control.control_matrix(D, method=method, regularisation=regularisation, n_modes=n_modes)
        return control.correct(G, self.slopes(None)), s

    def loop(self, D: np.ndarray, *, gain: float = 0.5, steps: int = 6, regularisation: float = 1e-3) -> Tuple[np.ndarray, List[float]]:
        """Closed-loop integrator; returns the final commands and the rms slope per step."""
        G, _ = control.control_matrix(D, regularisation=regularisation)
        c = np.zeros(self.n_actuators); hist = []
        for _ in range(steps):
            m = self.slopes(c)
            hist.append(float(np.sqrt(np.mean(m ** 2))))
            c = c - gain * (G @ m)
        return c, hist

    def zernike_commands(self, coefficients_waves: Dict[int, float], ridge: float = 1e-6) -> np.ndarray:
        """Commands (m) that best produce the given Zernike wavefront (waves rms) on the mirror: weighted ridge least squares over the pupil, weighted by the beam
        amplitude (the fit matters where the light is); ``ridge`` stabilises the actuators the beam does not see."""
        X, Y = np.meshgrid(self.x, self.x, indexing="ij")
        inside = np.hypot(X, Y) <= self.pupil_radius_m
        wt = np.exp(-(X[inside] ** 2 + Y[inside] ** 2) / self.beam_radius_m ** 2)
        M = dm_influence_matrix(self.positions, X[inside], Y[inside], pitch_m=self.pitch_m, coupling=self.coupling) * (2 * np.cos(self.aoi))
        w = np.zeros(X.shape)
        for j, c in coefficients_waves.items():
            w += c * zernike.basis([j], self.x, self.x, self.pupil_radius_m)[0]
        A = M * wt[:, None]; y = (w * self.wavelength_m)[inside] * wt
        scale = np.linalg.norm(A, 2)
        return np.linalg.solve(A.T @ A + (ridge * scale) ** 2 * np.eye(A.shape[1]), A.T @ y)
