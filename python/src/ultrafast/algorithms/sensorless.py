"""Sensorless optimisation and transmission-matrix measurement (NumPy only; the metric is any function you pass).

* :func:`spgd`            stochastic parallel gradient descent (Vorontsov and Sivokon, JOSA A 15, 2745 (1998)): all channels are perturbed at once by
                          random +-``amplitude`` and the metric difference of the two perturbations drives the update. The standard tool to phase-lock
                          a laser array or correct a wavefront from one number.
* :func:`hill_climb`      one coordinate at a time, step halved when neither direction helps.
* :func:`modal_search`    for each mode in turn, measure the metric at three biases and move to the vertex of the parabola (Booth, Opt. Express 14, 2006
                          style): n modes need 2 n + 1 evaluations per sweep for a metric that is quadratic in the mode amplitudes.
* :func:`transmission_matrix`  phase-stepping measurement of the input-modes -> output-mode map relative to a reference mode (Popoff et al., PRL 104,
                          100601 (2010)); :func:`focus_phases` turns it into the phases that focus on one output (enhancement (pi/4)(N - 1) + 1 for a
                          random medium, Vellekoop and Mosk, Opt. Lett. 32, 2309 (2007)).

Every optimiser returns an :class:`OptimiserResult` with the best parameters, the metric history (``history[k]`` = metric of the iterate k) and the
number of metric evaluations, so the cost of a method is a number.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Callable, List, Optional

import numpy as np


@dataclass
class OptimiserResult:
    x: np.ndarray
    history: List[float] = field(default_factory=list)
    evaluations: int = 0
    iterates: List[np.ndarray] = field(default_factory=list)


def spgd(metric: Callable[[np.ndarray], float], x0: np.ndarray, *, iterations: int = 200, gain: float = 1.0, amplitude: float = 0.1, seed: int = 0,
         maximise: bool = True, keep_iterates: bool = False) -> OptimiserResult:
    """x <- x + gain (J(x + d) - J(x - d)) d for a maximised metric J, d_i = +-``amplitude`` (Bernoulli, independent per channel). Two metric evaluations
    per iteration (plus one to log the history). ``gain`` is in units of 1 / metric: the expected step along the gradient is 2 gain amplitude^2 grad J."""
    rng = np.random.default_rng(seed)
    x = np.array(x0, float)
    sign = 1.0 if maximise else -1.0
    res = OptimiserResult(x.copy())
    for _ in range(iterations):
        d = amplitude * rng.choice([-1.0, 1.0], size=x.shape)
        jp, jm = metric(x + d), metric(x - d)
        res.evaluations += 2
        x = x + sign * gain * (jp - jm) * d
        res.history.append(float(metric(x))); res.evaluations += 1
        if keep_iterates:
            res.iterates.append(x.copy())
    res.x = x
    return res


def hill_climb(metric: Callable[[np.ndarray], float], x0: np.ndarray, *, step: float = 0.2, sweeps: int = 20, shrink: float = 0.5, maximise: bool = True) -> OptimiserResult:
    """Coordinate hill climbing: try +-step on each coordinate, keep improvements, halve (``shrink``) the step after a sweep without progress."""
    x = np.array(x0, float); s = 1.0 if maximise else -1.0
    best = s * metric(x); res = OptimiserResult(x.copy(), [s * best], 1)
    for _ in range(sweeps):
        improved = False
        for i in range(x.size):
            for d in (step, -step):
                t = x.copy(); t[i] += d; v = s * metric(t); res.evaluations += 1
                if v > best:
                    x, best, improved = t, v, True
                    break
        res.history.append(s * best)
        if not improved:
            step *= shrink
    res.x = x
    return res


def modal_search(metric: Callable[[np.ndarray], float], x0: np.ndarray, *, bias: float = 0.3, sweeps: int = 2, maximise: bool = True) -> OptimiserResult:
    """Modal search: for each coordinate measure J at x - bias, x, x + bias, fit a parabola and move to its vertex (limited to +-2 bias). Exact in one
    sweep for a metric that is quadratic and separable in the modes. 2 evaluations per mode per sweep (the centre is reused)."""
    x = np.array(x0, float); s = 1.0 if maximise else -1.0
    j0 = s * metric(x); res = OptimiserResult(x.copy(), [s * j0], 1)
    for _ in range(sweeps):
        for i in range(x.size):
            tp = x.copy(); tp[i] += bias; tm = x.copy(); tm[i] -= bias
            jp, jm = s * metric(tp), s * metric(tm); res.evaluations += 2
            curv = jp + jm - 2 * j0
            if curv < -1e-30:
                step = 0.5 * bias * (jm - jp) / curv
                x[i] += float(np.clip(step, -2 * bias, 2 * bias))
                j0 = s * metric(x); res.evaluations += 1
        res.history.append(s * j0)
    res.x = x
    return res


def transmission_matrix(intensity: Callable[[np.ndarray], np.ndarray], n_inputs: int) -> np.ndarray:
    """Phase-stepping measurement of the transmission matrix from intensities only (a phase-only input device, no reference arm).

    ``intensity(phases)`` returns the output intensities (a vector over output modes m) for the input phases. Input 0 is the reference. Stepping inputs
    0 and k together and taking the *mixed second difference* removes everything that is linear in either step, leaving the cross term exactly:
    ``D(a, b) = I(a, b) - I(a, 0) - I(0, b) + I(0, 0) = 2 Re[w (e^{i a} - 1)(e^{-i b} - 1)]`` with ``w_mk = t_m0 conj(t_mk)`` (a = phase of input 0, b of input k).
    D(pi, pi) = 8 Re w and D(pi, pi/2) = 4 Re w - 4 Im w give w for every output, however dark. Returns ``H[m][k] = conj(w_mk) / |t_m0|`` for k >= 1 and
    ``H[:, 0] = 1``: the transmission matrix in the gauge t_m0 real and positive, with an unknown positive factor |t_m0| per output row. Phases are exact, which is
    all a phase-only focusing needs; amplitudes are relative within the row. Cost: 5 (n_inputs - 1) + 3 intensity measurements."""
    n = n_inputs
    def ph(a, k=None, b=0.0):
        p = np.zeros(n); p[0] = a
        if k is not None:
            p[k] = b
        return p
    I00 = np.asarray(intensity(np.zeros(n)), float)
    Ia0 = np.asarray(intensity(ph(np.pi)), float)                     # input 0 at pi
    H = np.ones((I00.size, n), complex)
    for k in range(1, n):
        I0b_pi = np.asarray(intensity(ph(0.0, k, np.pi)), float)
        I0b_hp = np.asarray(intensity(ph(0.0, k, np.pi / 2)), float)
        Iab_pi = np.asarray(intensity(ph(np.pi, k, np.pi)), float)
        Iab_hp = np.asarray(intensity(ph(np.pi, k, np.pi / 2)), float)
        d1 = Iab_pi - Ia0 - I0b_pi + I00
        d2 = Iab_hp - Ia0 - I0b_hp + I00
        re = d1 / 8
        im = re - d2 / 4
        H[:, k] = np.conj(re + 1j * im)
    return H


def focus_phases(H: np.ndarray, output: int) -> np.ndarray:
    """Input phases that focus on output mode ``output``: minus the phase of the measured t_mk (all inputs then add in phase)."""
    return -np.angle(H[output])
