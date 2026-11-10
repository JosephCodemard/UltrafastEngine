"""Experiment design and placement planning: which measurement would teach the twin the most?

For a candidate measurement (a condition: variables and ``set`` edits -- a different energy, another position of the instrument,
a different input pulse) the engine gives the reading y(theta) at the fitted parameters; finite differences in each parameter give the
Jacobian J, and with the stated noise sigma the Fisher information is J^T J / sigma^2. Adding it to the current information C^-1
gives the expected posterior covariance C' = (C^-1 + J^T J / sigma^2)^-1. The score is the expected information gain
0.5 (log det C - log det C') (D-optimal) or, with ``weights``, the reduction of sum w_i var_i (A-optimal). It is a *linearised
expectation at the fitted point*: it says where the twin is blind, not what the data will show.
"""
from __future__ import annotations

from typing import Any, Dict, List, Optional, Sequence

import numpy as np


def _reading(twin, vec, cand) -> np.ndarray:
    res = twin.simulate(vec, cand.get("variables"), cand.get("set"), cand.get("name"))
    ins = twin.project.instrument
    y = np.asarray(res[f"{ins}_spectrum"], dtype=float)
    return y / max(np.max(y), 1e-300)


def jacobian(twin, cand: Dict[str, Any], rel_step: float = 0.05) -> np.ndarray:
    """d(reading)/d(parameter) at the fitted point, one engine run per parameter (step = 5 % of the sigma, at least 1e-4 of the value)."""
    base = twin.values
    y0 = _reading(twin, base, cand)
    cols = []
    for i in range(len(base)):
        s = twin.sigmas[i]
        h = rel_step * (s if np.isfinite(s) and s > 0 else 1e-3 * (abs(base[i]) or 1.0))
        h = max(h, 1e-4 * abs(base[i]), 1e-12)
        v = base.copy(); v[i] += h
        cols.append((_reading(twin, v, cand) - y0) / h)
    return np.array(cols).T


def _logdet(m: np.ndarray) -> float:
    w = np.linalg.eigvalsh((m + m.T) / 2)
    return float(np.sum(np.log(np.clip(w, 1e-300, None))))


def information_gain(C: np.ndarray, J: np.ndarray, sigma: float, weights: Optional[Sequence[float]] = None):
    """(gain, posterior covariance, posterior sigmas). Parameters with no variance (C = 0, e.g. fixed) are left out."""
    keep = np.diag(C) > 0
    Ck = C[np.ix_(keep, keep)]; Jk = J[:, keep]
    prec = np.linalg.pinv(Ck) + Jk.T @ Jk / sigma ** 2
    post = np.linalg.pinv(prec)
    if weights is None:
        gain = 0.5 * (_logdet(Ck) - _logdet(post))
    else:
        w = np.asarray(weights, dtype=float)[keep]
        gain = float(np.sum(w * (np.diag(Ck) - np.diag(post))))
    full = np.zeros_like(C); full[np.ix_(keep, keep)] = post
    return float(gain), full, np.sqrt(np.clip(np.diag(full), 0, None))


def rank_candidates(twin, candidates: List[Dict[str, Any]], picks: int = 1, weights: Optional[Sequence[float]] = None,
                    sigma: Optional[float] = None) -> Dict[str, Any]:
    """Rank candidate measurements by expected information gain; ``picks`` > 1 selects greedily (each pick updates the covariance).
    A candidate is ``{"name", "variables", "set"}``."""
    sigma = twin.project.sigma if sigma is None else sigma
    C = twin.covariance()
    Js = {c["name"]: jacobian(twin, c) for c in candidates}
    first = []
    for c in candidates:
        g, _, ps = information_gain(C, Js[c["name"]], sigma, weights)
        first.append({"name": c["name"], "gain": g, "posteriorSigma": dict(zip(twin.names(), ps.tolist()))})
    ranking = sorted(first, key=lambda r: -r["gain"])
    chosen, cur, left = [], C, {c["name"] for c in candidates}
    for _ in range(max(1, picks)):
        best = None
        for n in left:
            g, post, ps = information_gain(cur, Js[n], sigma, weights)
            if best is None or g > best[0]:
                best = (g, n, post, ps)
        if best is None:
            break
        chosen.append({"name": best[1], "gain": best[0], "posteriorSigma": dict(zip(twin.names(), best[3].tolist()))})
        cur = best[2]; left.discard(best[1])
    return {"currentSigma": dict(zip(twin.names(), np.sqrt(np.clip(np.diag(C), 0, None)).tolist())), "ranking": ranking, "chosen": chosen, "noiseSigma": sigma}


def energy_candidates(twin, variable: str, values: Sequence[float], set_key: Optional[str] = None) -> List[Dict[str, Any]]:
    """Candidates that differ in one condition variable (also written to ``set_key``, e.g. ``pulse/energyJ``, when given)."""
    out = []
    for v in values:
        c: Dict[str, Any] = {"name": f"{variable}={v:.4g}", "variables": {variable: float(v)}}
        if set_key:
            c["set"] = {set_key: float(v)}
        out.append(c)
    return out


def placement_candidates(twin, base: Optional[Dict[str, Any]] = None) -> List[Dict[str, Any]]:
    """Candidates that put the instrument at each element (after it) and at the input / end, for one condition (``base``: variables and set)."""
    ins = twin.project.instrument
    if base is None:   # default: the conditions of the last training measurement
        last = [c for c in twin.project.conditions() if not c.get("heldOut")][-1]
        base = {"variables": last.get("variables", {}), "set": last.get("set", {})}
    places = ["input"] + [e["id"] for e in twin.system.get("elements", []) if "id" in e] + ["end"]
    seen, out = set(), []
    for p in places:
        if p in seen:
            continue
        seen.add(p)
        out.append({"name": f"{ins} at {p}", "variables": base.get("variables", {}), "set": {**base.get("set", {}), f"instrument:{ins}/at": p}})
    return out
