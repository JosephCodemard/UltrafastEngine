"""Dependence of a fitted number on the operating condition, evaluated in Python.

The mirror of ``Dependence`` in the fitting plugin (``plugins/fitting/src/Dependence.cs``): the form stored in
``fitted-conditions.json`` (``dependences[]``) is evaluated here, so the twin can predict conditions that were never run.
A test checks that this class and the engine agree.
"""
from __future__ import annotations

import math
from typing import Any, Dict, List, Sequence

import numpy as np


def _chebyshev(k: int, xi: float) -> float:
    if k == 0:
        return 1.0
    a, b = 1.0, xi
    for _ in range(2, k + 1):
        a, b = b, 2 * xi * b - a
    return b


class DependenceModel:
    """One stored dependence: ``values(theta)`` gives ``{path: value}``; ``coefficients`` is the flat parameter vector."""

    def __init__(self, spec: Dict[str, Any]):
        self.spec = spec
        self.name: str = spec["name"]
        self.kind: str = spec["kind"]
        self.variable: str = spec["variable"]
        self.paths: List[str] = list(spec["paths"])
        self.lo, self.hi = (float(v) for v in spec["range"])
        if self.kind == "network":
            self.hidden = int(spec["hidden"])
            self.output = spec.get("output", "none")
            self.monotone = spec.get("monotone", "none")
            self.out_lo, self.out_hi = (float(v) for v in spec.get("outputRange", (0.0, 1.0)))
            self.coefficients = np.asarray(spec["weights"], dtype=float)
        else:
            self.reference = float(spec.get("reference", 1.0))
            self.order = len(spec["coefficients"][0])
            self.coefficients = np.asarray(spec["coefficients"], dtype=float).reshape(-1)

    @property
    def size(self) -> int:
        return int(self.coefficients.size)

    def with_coefficients(self, vec: Sequence[float]) -> "DependenceModel":
        new = DependenceModel(self.spec)
        new.coefficients = np.asarray(vec, dtype=float).copy()
        return new

    def _xi(self, theta: float) -> float:
        return 2 * (theta - self.lo) / (self.hi - self.lo) - 1 if self.hi > self.lo else 0.0

    def _value(self, pi: int, theta: float) -> float:
        c = self.coefficients
        if self.kind == "network":
            h = self.hidden
            xi = self._xi(theta)
            base = 2 * h + pi * (h + 1)
            raw = c[base + h]
            mono = self.monotone != "none"
            sign = -1.0 if self.monotone == "decreasing" else 1.0
            for j in range(h):
                a, v = c[j], c[base + j]
                if mono:
                    a, v = a * a, sign * v * v
                raw += v * math.tanh(a * xi + c[h + j])
            if self.output == "bounded":
                return self.out_lo + (self.out_hi - self.out_lo) / (1 + math.exp(-raw))
            if self.output == "positive":
                return raw if raw > 30 else math.log1p(math.exp(raw))
            return raw
        u = theta / self.reference
        row = c[pi * self.order:(pi + 1) * self.order]
        k = self.kind
        if k == "power":
            return float(row[0] * u ** row[1])
        if k == "constant":
            return float(row[0])
        if k == "proportional":
            return float(row[0] * u)
        if k == "linear":
            return float(row[0] + row[1] * u)
        if k == "polynomial":
            return float(sum(row[i] * u ** i for i in range(self.order)))
        if k == "chebyshev":
            xi = self._xi(theta)
            return float(sum(row[i] * _chebyshev(i, xi) for i in range(self.order)))
        raise ValueError(f"unknown dependence kind {k}")

    def values(self, theta: float) -> Dict[str, float]:
        return {p: self._value(i, theta) for i, p in enumerate(self.paths)}
