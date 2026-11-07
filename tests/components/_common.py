"""Shared helpers for the component verification scripts (tests/components/<family>.py).

What it does: finds the engine (repo `python/src` on sys.path; the hosted build through ULTRAFAST_LIBRARY when the
caller has not set it), collects checks, prints the numbers table, draws the analytic-vs-engine figures with a
residual subplot, and writes a JSON record per family that `tests/components/run_all.py` turns into the HTML gallery.

Main pieces:
  Family(name, title)                       a family of checks; one per script
    .check(name, engine, analytic, rel=, abs=, unit=, formula=, note=)  -> bool   one scalar comparison
    .figure(key, title, formula) -> Figure  a context for one figure (overlay + residual)
    .finish() -> int                        prints the table, writes report/<family>.json, returns the exit code
  overlay(fig, x, engine, analytic, xlabel, ylabel, ...)   overlay + residual panel on a Figure
  CARRIERS_M = (515e-9, 1030e-9, 2e-6), DURATIONS_S = (1e-12, 10e-15)
  spectral_phase(res, prefix), rms_width(x, w), centroid(x, w)   readers of a Result

Command line of every family script: `--no-plots` (numbers only), `--out DIR` (default tests/components/report).
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Optional

ROOT = Path(__file__).resolve().parents[2]
_SRC = ROOT / "python" / "src"
if str(_SRC) not in sys.path:
    sys.path.insert(0, str(_SRC))
_HOSTED = _SRC / "ultrafast" / "bin" / "hosted" / "UltrafastEngine.Native.dll"
if "ULTRAFAST_LIBRARY" not in os.environ and _HOSTED.exists():
    os.environ["ULTRAFAST_LIBRARY"] = str(_HOSTED)
os.environ.setdefault("ULTRAFAST_STRICT_OPTIONS", "1")

import numpy as np  # noqa: E402

import ultrafast as uf  # noqa: E402,F401
from ultrafast import build as b  # noqa: E402,F401

C = 299792458.0
CARRIERS_M = (515e-9, 1030e-9, 2e-6)
DURATIONS_S = (1e-12, 10e-15)
FS = 1e-15
FS2 = 1e-30


def label_wl(lam: float) -> str:
    return f"{lam * 1e9:.0f} nm" if lam < 1.5e-6 else f"{lam * 1e6:.0f} µm"


def label_dur(tau: float) -> str:
    return f"{tau * 1e12:.0f} ps" if tau >= 1e-12 else f"{tau * 1e15:.0f} fs"


# ----------------------------------------------------------------------------------------------- readers of a Result

def spectrum(res, prefix: str = "output"):
    """(ω [rad/s], |A(ω)|² summed over polarisations, complex A_x(ω)) of `prefix` (input / output)."""
    w = np.asarray(res[prefix + "_omega_rad_s"])
    s = np.asarray(res[prefix + "_spectrum"])
    return w, s


def centroid(x, wgt) -> float:
    x, wgt = np.asarray(x, float), np.asarray(wgt, float)
    return float((x * wgt).sum() / wgt.sum())


def rms_width(x, wgt) -> float:
    m = centroid(x, wgt)
    x, wgt = np.asarray(x, float), np.asarray(wgt, float)
    return float(math.sqrt(((x - m) ** 2 * wgt).sum() / wgt.sum()))


def fwhm(x, y) -> float:
    """Full width at half maximum by linear interpolation of the outermost crossings."""
    x, y = np.asarray(x, float), np.asarray(y, float)
    h = y.max() / 2
    idx = np.where(y >= h)[0]
    i0, i1 = idx[0], idx[-1]

    def cross(a, b_):
        return x[a] + (h - y[a]) * (x[b_] - x[a]) / (y[b_] - y[a])

    left = cross(i0 - 1, i0) if i0 > 0 else x[i0]
    right = cross(i1, i1 + 1) if i1 + 1 < len(x) else x[i1]
    return float(right - left)


def poly_phase(w, phase, weight, w0, order: int = 4):
    """Fit φ(ω) = Σ c_k (ω-ω0)^k / k! over the points carrying weight > 1e-3 max. Returns [φ0, φ1, φ2, ...]."""
    m = weight > 1e-3 * weight.max()
    dw = (w[m] - w0)
    scale = np.abs(dw).max()
    p = np.polyfit(dw / scale, np.unwrap(phase[m]), order, w=np.sqrt(weight[m]))
    p = p[::-1]
    return [p[k] * math.factorial(k) / scale ** k for k in range(order + 1)]


# ----------------------------------------------------------------------------------------------- checks and report

class _Fig:
    def __init__(self, fam: "Family", key: str, title: str, formula: str):
        self.fam, self.key, self.title, self.formula = fam, key, title, formula
        self.fig = None
        self.axes = None
        self.checks: List[str] = []

    def __enter__(self):
        if self.fam.plots:
            import matplotlib
            matplotlib.use("Agg")
            import matplotlib.pyplot as plt
            self.fig, self.axes = plt.subplots(2, 1, figsize=(7.2, 5.4), sharex=True,
                                               gridspec_kw={"height_ratios": [3, 1.3], "hspace": 0.08})
            self.fig.suptitle(self.title, fontsize=11)
        return self

    def __exit__(self, *exc):
        if self.fig is not None and exc[0] is None:
            import matplotlib.pyplot as plt
            path = self.fam.out / "figures" / f"{self.fam.name}-{self.key}.png"
            path.parent.mkdir(parents=True, exist_ok=True)
            self.fig.savefig(path, dpi=110, bbox_inches="tight")
            plt.close(self.fig)
            self.fam.figures.append({"key": self.key, "title": self.title, "formula": self.formula,
                                     "file": f"figures/{path.name}", "checks": self.checks})
        elif self.fam.plots is False:
            self.fam.figures.append({"key": self.key, "title": self.title, "formula": self.formula,
                                     "file": None, "checks": self.checks})
        return False


def overlay(f: _Fig, x, engine, analytic, *, label: str, xlabel: str, ylabel: str, residual: str = "abs",
            analytic_label: Optional[str] = None, style: str = "-", marker: Optional[str] = None):
    """Draw engine (solid / markers) and analytic (dashed black) on the top panel and engine − analytic (or the
    relative residual when residual='rel') on the bottom panel. No-op in --no-plots mode."""
    if f.axes is None:
        return
    top, bot = f.axes
    x, engine, analytic = np.asarray(x, float), np.asarray(engine, float), np.asarray(analytic, float)
    line, = top.plot(x, engine, style, marker=marker, ms=4, lw=1.6, label=f"engine, {label}")
    top.plot(x, analytic, "--", color="k", lw=1.0, alpha=0.75,
             label=analytic_label if analytic_label else None)
    if residual == "rel":
        r = (engine - analytic) / np.where(np.abs(analytic) > 0, np.abs(analytic), 1.0)
        bot.set_ylabel("relative residual")
    else:
        r = engine - analytic
        unit = ylabel[ylabel.rfind("("):] if ylabel.endswith(")") and "(" in ylabel else ""
        bot.set_ylabel(f"residual {unit}".strip())
    bot.plot(x, r, style, marker=marker, ms=3, lw=1.0, color=line.get_color())
    bot.axhline(0, color="k", lw=0.6)
    bot.set_xlabel(xlabel)
    top.set_ylabel(ylabel)
    top.grid(alpha=0.3)
    bot.grid(alpha=0.3)
    handles, labels = top.get_legend_handles_labels()
    if handles:
        top.legend(fontsize=8, loc="best", frameon=False)


class Family:
    def __init__(self, name: str, title: str):
        p = argparse.ArgumentParser(description=title)
        p.add_argument("--no-plots", action="store_true", help="numbers only (CI mode)")
        p.add_argument("--out", default=str(ROOT / "verification" / "report"), help="report folder")
        a = p.parse_args()
        self.name, self.title = name, title
        self.plots = not a.no_plots
        self.out = Path(a.out)
        self.out.mkdir(parents=True, exist_ok=True)
        self.rows: List[Dict[str, Any]] = []
        self.figures: List[Dict[str, Any]] = []
        self.notes: List[str] = []
        self._fig: Optional[_Fig] = None
        self._t0 = time.time()
        print(f"== {title}")

    def figure(self, key: str, title: str, formula: str) -> _Fig:
        self._fig = _Fig(self, key, title, formula)
        return self._fig

    def check(self, name: str, engine: float, analytic: float, *, rel: float = 0.0, abs: float = 0.0,
              unit: str = "", formula: str = "", note: str = "", known: str = "") -> bool:
        """|engine − analytic| ≤ abs + rel·|analytic| passes. `known`: a reported, not yet fixed engine bug -- a failure
        is shown as KNOWN (with this text) and does not fail the script; a pass is flagged so the marker gets removed."""
        engine, analytic = float(engine), float(analytic)
        err = engine - analytic
        tol = abs + rel * math.fabs(analytic)
        ok = math.isfinite(engine) and math.fabs(err) <= tol
        relerr = err / analytic if analytic != 0 else float("nan")
        status = ("ok" if ok else "FAIL") if not known else ("ok (known bug fixed? remove the marker)" if ok else "KNOWN")
        self.rows.append({"name": name, "engine": engine, "analytic": analytic, "error": err, "relError": relerr,
                          "tolerance": tol, "unit": unit, "ok": ok or bool(known), "status": status, "formula": formula,
                          "note": known or note})
        if self._fig is not None:
            self._fig.checks.append(name)
        return ok

    def note(self, text: str):
        self.notes.append(text)
        print("  note:", text)

    def guard(self, name: str, fn):
        """Run fn(); an exception becomes a failed check (so one broken component does not hide the others)."""
        try:
            fn()
        except Exception as e:  # noqa: BLE001
            import traceback
            traceback.print_exc()
            self.rows.append({"name": name, "engine": float("nan"), "analytic": float("nan"), "error": float("nan"),
                              "relError": float("nan"), "tolerance": 0.0, "unit": "", "ok": False, "status": "FAIL",
                              "formula": "", "note": f"exception: {type(e).__name__}: {e}"})
        finally:
            self._fig = None

    def finish(self) -> int:
        w = max([len(r["name"]) for r in self.rows] + [10])
        print(f"{'check':<{w}}  {'engine':>13}  {'analytic':>13}  {'rel. error':>10}  {'tol':>10}  unit   result")
        for r in self.rows:
            rel = r["relError"]
            print(f"{r['name']:<{w}}  {r['engine']:>13.6g}  {r['analytic']:>13.6g}  "
                  f"{(f'{rel:.2e}' if math.isfinite(rel) else '-'):>10}  {r['tolerance']:>10.3g}  {r['unit']:<6} "
                  f"{r['status']}" + (f"   ({r['note']})" if r['status'] != 'ok' and r['note'] else ""))
        nfail = sum(not r["ok"] for r in self.rows)
        print(f"{self.name}: {len(self.rows) - nfail} passed, {nfail} failed ({time.time() - self._t0:.1f} s)")
        rec = {"family": self.name, "title": self.title, "rows": self.rows, "figures": self.figures,
               "notes": self.notes, "seconds": time.time() - self._t0}
        (self.out / f"{self.name}.json").write_text(json.dumps(rec, indent=1, default=float))
        return 1 if nfail else 0
