"""Drift tracking with an ensemble Kalman filter.

State: the tracked parameters (default: all shared ones). Ensemble: draws from the fit's Gaussian posterior. Each new measurement
(a condition and its recorded file) is an observation of the instrument reading; the observation operator is the engine itself
(one run per member). Between measurements the ensemble gets process noise (a random walk: ``drift`` x the fitted sigma per measurement, or ``process_sigma`` in the parameter's own unit -- set it to the drift you expect between measurements: the fitted sigma is usually far smaller) and
an inflation factor, so the filter can follow a parameter that moves instead of collapsing on its old value. A parameter is
reported as drifted when its mean moves more than ``alarm`` fitted sigmas from the fit.
"""
from __future__ import annotations

import json
from typing import Any, Dict, List, Optional, Sequence

import numpy as np


class DriftTracker:
    def __init__(self, twin, parameters: Optional[Sequence[str]] = None, members: int = 16, drift: float = 1.0, inflation: float = 1.02,
                 alarm: float = 3.0, seed: int = 1, process_sigma: Optional[Dict[str, float]] = None):
        self.twin = twin
        names = twin.names()
        if parameters is None:
            parameters = [p["name"] for p in twin.params if p.get("origin") not in ("dependence", "network") and p.get("condition") is None]
        missing = [n for n in parameters if n not in names]
        if missing:
            raise KeyError(f"unknown parameters {missing}; the fit has {names}")
        self.idx = [names.index(n) for n in parameters]
        self.names = list(parameters)
        self.rng = np.random.default_rng(seed)
        self.drift, self.inflation, self.alarm = drift, inflation, alarm
        draws = twin.draw(members + 1, seed)[1:]
        self.members = members
        self.X = draws.copy()                       # members x all parameters; only self.idx columns are updated
        self.sig0 = np.array([twin.sigmas[i] if np.isfinite(twin.sigmas[i]) and twin.sigmas[i] > 0 else 1e-3 * (abs(twin.values[i]) or 1.0) for i in self.idx])
        self.mean0 = twin.values[self.idx].copy()
        self.lo = np.array([_f(twin.params[i].get("min")) for i in self.idx]); self.hi = np.array([_f(twin.params[i].get("max")) for i in self.idx])
        # random-walk step per measurement: ``drift`` x the fitted sigma, or an absolute value in the parameter's unit (``process_sigma``)
        self.process = np.array([float((process_sigma or {}).get(n, drift * s0)) for n, s0 in zip(self.names, self.sig0)])
        self.history: List[Dict[str, Any]] = [self._snapshot("start", None)]

    def _snapshot(self, label, innovation):
        m = self.X[:, self.idx].mean(axis=0); s = self.X[:, self.idx].std(axis=0, ddof=1)
        z = (m - self.mean0) / self.sig0
        return {"label": label, "mean": dict(zip(self.names, m.tolist())), "sigma": dict(zip(self.names, s.tolist())), "zFromFit": dict(zip(self.names, z.tolist())),
                "drifted": [n for n, zz in zip(self.names, z) if abs(zz) > self.alarm], "rmsInnovation": innovation}

    def update(self, data: str, variables: Optional[Dict[str, float]] = None, set: Optional[Dict[str, Any]] = None, label: Optional[str] = None,
               name: Optional[str] = None) -> Dict[str, Any]:
        """Assimilate one recorded file for a condition."""
        tw = self.twin
        N = self.members
        # forecast: random-walk process noise and inflation on the tracked columns
        A = self.X[:, self.idx] - self.X[:, self.idx].mean(axis=0)
        self.X[:, self.idx] = self.X[:, self.idx].mean(axis=0) + self.inflation * A + self.process * self.rng.standard_normal((N, len(self.idx)))
        # observations through the engine
        from ultrafast import measured
        H, y, sigma = [], None, None
        for i in range(N):
            res = tw.simulate(self.X[i], variables, set, name, data)
            c = measured.curve(res, tw.project.instrument)
            H.append(c["simulated_on_measured"]); y = c["measured_y"]
            sigma = np.asarray(c["measured_sigma"], dtype=float) if c["measured_sigma"] is not None else np.full_like(y, tw.project.sigma)
        H = np.array(H)                                     # N x d
        Ay = (H - H.mean(axis=0)).T                         # d x N
        Ax = (self.X[:, self.idx] - self.X[:, self.idx].mean(axis=0)).T   # m x N
        Rinv = 1.0 / sigma ** 2
        # (R + Ay Ay^T/(N-1))^-1 by Woodbury in the N-dimensional ensemble space
        G = (N - 1) * np.eye(N) + Ay.T @ (Rinv[:, None] * Ay)
        def apply_minv(v):                                  # v: d x k
            t = Rinv[:, None] * v
            return t - (Rinv[:, None] * Ay) @ np.linalg.solve(G, Ay.T @ t)
        pert = y[None, :] + sigma[None, :] * self.rng.standard_normal((N, y.size))
        innov = (pert - H).T                                # d x N
        gain_part = Ax @ Ay.T / (N - 1)                     # m x d
        dX = gain_part @ apply_minv(innov)                  # m x N
        self.X[:, self.idx] += dX.T
        self.X[:, self.idx] = np.clip(self.X[:, self.idx], np.where(np.isnan(self.lo), -np.inf, self.lo), np.where(np.isnan(self.hi), np.inf, self.hi))
        rms = float(np.sqrt(np.mean(((y - H.mean(axis=0)) / sigma) ** 2)))
        snap = self._snapshot(label or f"step {len(self.history)}", rms)
        self.history.append(snap)
        return snap

    def write(self, path) -> None:
        with open(path, "w", encoding="utf-8") as fh:
            json.dump({"parameters": self.names, "alarmSigmas": self.alarm, "history": self.history}, fh, indent=2)


def _f(v) -> float:
    return float("nan") if v is None else float(v)
