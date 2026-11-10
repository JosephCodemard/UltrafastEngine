"""The fitted twin: predict any condition with credible bands, and what-if comparisons.

A :class:`Twin` is a project plus one fit folder (``fit.json``, ``fitted-system.ufs``, ``fitted-run.json``,
``fitted-conditions.json``). The parameters and their covariance come from ``fit.json`` (Gaussian posterior at the optimum:
sigma and correlation); a *draw* is one parameter vector sampled from it. Credible bands are the spread of the engine's output
over draws -- the same model, different parameters -- so a band is as wide as the data allow, not wider.
"""
from __future__ import annotations

import copy
import json
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence

import numpy as np

from . import paths as P
from .dependence import DependenceModel

DEFAULT_SAMPLES = 12


def _nan(v) -> float:
    return float("nan") if v is None else float(v)


class Twin:
    def __init__(self, project, fit_dir):
        self.project = project
        self.dir = Path(fit_dir)
        self.fit: Dict[str, Any] = json.loads((self.dir / "fit.json").read_text(encoding="utf-8"))
        self.system: Dict[str, Any] = json.loads((self.dir / "fitted-system.ufs").read_text(encoding="utf-8"))
        run = json.loads((self.dir / "fitted-run.json").read_text(encoding="utf-8"))
        run.pop("fit", None)
        for ins in run.get("instruments", []):
            ins.pop("measured", None)
        self.run = run
        fc = self.dir / "fitted-conditions.json"
        self.cond_file: Dict[str, Any] = json.loads(fc.read_text(encoding="utf-8")) if fc.is_file() else {}
        self.deps = [DependenceModel(d) for d in self.cond_file.get("dependences", [])]
        self.params: List[Dict[str, Any]] = self.fit["parameters"]
        self.values = np.array([p["value"] for p in self.params], dtype=float)
        self.sigmas = np.array([_nan(p.get("sigma")) for p in self.params], dtype=float)
        self._layout = self._make_layout()

    # ------------------------------------------------------------------ parameters
    def _make_layout(self):
        """For each parameter: ('plain', index) or ('dep', dep_index, offset). Dependence coefficients are contiguous, in dependence order."""
        out, di, off = [], 0, 0
        for i, p in enumerate(self.params):
            if p.get("origin") in ("dependence", "network"):
                while off >= self.deps[di].size:
                    di, off = di + 1, 0
                out.append(("dep", di, off))
                off += 1
            else:
                out.append(("plain", i, 0))
        return out

    def names(self) -> List[str]:
        return [p["name"] for p in self.params]

    def covariance(self) -> np.ndarray:
        n = len(self.params)
        corr = self.fit.get("correlation")
        s = np.nan_to_num(self.sigmas, nan=0.0)
        if corr is None:
            return np.diag(s ** 2)
        r = np.nan_to_num(np.array(corr, dtype=float), nan=0.0)
        np.fill_diagonal(r, 1.0)
        return r * np.outer(s, s)

    def draw(self, n: int, seed: int = 1) -> np.ndarray:
        """``n`` parameter vectors; row 0 is the optimum, the others are sampled from the Gaussian posterior (clipped to the bounds)."""
        rng = np.random.default_rng(seed)
        c = self.covariance()
        w, v = np.linalg.eigh((c + c.T) / 2)
        a = v * np.sqrt(np.clip(w, 0, None))
        out = [self.values.copy()]
        lo = np.array([_nan(p.get("min")) for p in self.params]); hi = np.array([_nan(p.get("max")) for p in self.params])
        for _ in range(max(0, n - 1)):
            x = self.values + a @ rng.standard_normal(len(self.values))
            x = np.where(np.isnan(lo), x, np.maximum(x, lo)); x = np.where(np.isnan(hi), x, np.minimum(x, hi))
            out.append(x)
        return np.array(out)

    # ------------------------------------------------------------------ one simulation
    def inputs(self, vec: Optional[Sequence[float]] = None, variables: Optional[Dict[str, float]] = None,
               set: Optional[Dict[str, Any]] = None, name: Optional[str] = None):
        """(system, run) for a condition: fitted files + the condition's ``set`` + the parameter vector (dependences evaluated at ``variables``)."""
        vec = self.values if vec is None else np.asarray(vec, dtype=float)
        system, run = copy.deepcopy(self.system), copy.deepcopy(self.run)
        if name is not None:
            try:
                c = self.project.condition(name)
                variables = {**c.get("variables", {}), **(variables or {})}
                set = {**c.get("set", {}), **(set or {})}
            except Exception:
                pass
        P.apply_set(system, run, set or {})
        deps = [d.with_coefficients(d.coefficients) for d in self.deps]
        for i, (kind, a, b) in enumerate(self._layout):
            if kind == "dep":
                deps[a].coefficients[b] = vec[i]
            else:
                p = self.params[i]
                if p.get("condition") in (None, name):
                    P.set_value(system, run, p["path"], float(vec[i]))
        for d in deps:
            if d.variable not in (variables or {}):
                raise KeyError(f"the dependence {d.name} needs the condition variable \"{d.variable}\"")
            for path, val in d.values(float(variables[d.variable])).items():
                P.set_value(system, run, path, val)
        return system, run

    def simulate(self, vec=None, variables=None, set=None, name=None, data: Optional[str] = None):
        import ultrafast as uf
        system, run = self.inputs(vec, variables, set, name)
        if data is not None:
            for ins in run["instruments"]:
                if ins.get("name") == self.project.instrument:
                    ins["measured"] = {"file": str(data), "sigma": self.project.sigma}
        return uf.run(system, run)

    # ------------------------------------------------------------------ prediction with bands
    def _spectrum(self, res, compare: bool):
        from ultrafast import measured
        ins = self.project.instrument
        if compare:
            c = measured.curve(res, ins)
            return c["measured_x"], c["simulated_on_measured"], c["measured_y"], c["measured_sigma"]
        x, y = res[f"{ins}_wavelength_m"], res[f"{ins}_spectrum"]
        return x, y / np.max(y), None, None

    def predict(self, variables: Optional[Dict[str, float]] = None, set: Optional[Dict[str, Any]] = None, name: Optional[str] = None,
                n_samples: int = DEFAULT_SAMPLES, seed: int = 1, data: Optional[str] = None) -> Dict[str, Any]:
        """The instrument reading of a condition: ``x``, ``y`` (peak-normalised), ``y_sigma`` (1-sigma credible band over ``n_samples``
        parameter draws) and the output scalars with sigma. With ``data`` (a recorded file) the curves are on its axis and the
        measurement and its coverage by the band are returned too."""
        draws = self.draw(max(1, n_samples), seed)
        xs, ys, scal = [], [], []
        measured_y = sig = None
        for vec in draws:
            res = self.simulate(vec, variables, set, name, data)
            x, y, my, ms = self._spectrum(res, data is not None)
            xs.append(x); ys.append(y); measured_y, sig = (my, ms) if my is not None else (measured_y, sig)
            out = res.summary.get("output", {})
            scal.append({k: float(out[k]) for k in ("energyJ", "fwhmS", "peakPowerW", "spectralFwhmM") if k in out})
        Y = np.array(ys)
        result = {"name": name, "variables": variables or {}, "x": xs[0], "y": Y[0], "y_sigma": Y.std(axis=0, ddof=1) if len(Y) > 1 else np.zeros_like(Y[0]),
                  "scalars": {k: (float(np.mean([s[k] for s in scal])), float(np.std([s[k] for s in scal], ddof=1)) if len(scal) > 1 else 0.0)
                              for k in scal[0]}, "n_samples": len(draws), "_draws": Y, "_scal": scal}
        if measured_y is not None:
            noise = np.asarray(sig, dtype=float) if sig is not None else np.full_like(measured_y, self.project.sigma)
            z = np.abs(measured_y - Y[0]) / np.sqrt(result["y_sigma"] ** 2 + noise ** 2)
            result.update(measured=measured_y, coverage2sigma=float(np.mean(z < 2)), rmsNormalisedResidual=float(np.sqrt(np.mean(z ** 2))))
        return result

    # ------------------------------------------------------------------ what-if
    def whatif(self, variants: Dict[str, Dict[str, Any]], n_samples: int = DEFAULT_SAMPLES, seed: int = 1) -> Dict[str, Any]:
        """Compare settings without beam time. ``variants`` maps a label to ``{"variables": {...}, "set": {...}}``. The same parameter
        draws are used for every variant, so the difference to the first variant has a proper (paired) uncertainty."""
        out: Dict[str, Any] = {}
        for label, v in variants.items():
            out[label] = self.predict(v.get("variables"), v.get("set"), n_samples=n_samples, seed=seed)
        first = next(iter(out))
        table = {}
        for label, pr in out.items():
            row = {}
            for k, (m, s) in pr["scalars"].items():
                dm = m - out[first]["scalars"][k][0]
                d = np.array([_scalar(pr["_scal"][i], k) - _scalar(out[first]["_scal"][i], k) for i in range(pr["n_samples"])])
                row[k] = {"value": m, "sigma": s, "delta": dm, "deltaSigma": float(d.std(ddof=1)) if d is not None and len(d) > 1 else None}
            table[label] = row
        return {"variants": {k: {kk: vv for kk, vv in v.items() if not kk.startswith("_")} for k, v in out.items()}, "scalars": table, "reference": first}

    # ------------------------------------------------------------------ the big four
    def validate(self, n_samples: int = DEFAULT_SAMPLES, write: bool = True):
        from .validate import validate
        return validate(self, n_samples, write)

    def design(self, candidates, **kw):
        from .design import rank_candidates
        return rank_candidates(self, candidates, **kw)

    def tracker(self, **kw):
        from .track import DriftTracker
        return DriftTracker(self, **kw)


def _scalar(d, k):
    return d.get(k, float("nan"))
