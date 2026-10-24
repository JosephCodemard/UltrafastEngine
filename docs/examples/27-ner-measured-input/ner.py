"""Nonlinear ellipse rotation through the plates of example 27, against the analytic result.

Runs the same pulse (measured spectrum, ellipticity 0.3) with linear polarisation (for the B-integral of linear
light), and elliptically with the fused-silica Raman response anisotropic (the default, 25 %), isotropic, and with
Raman switched off. The polarimeter's pulse-averaged azimuth is compared with

    theta(t) = B_lin(t) sin(2 chi) [(1 - fR)/3 + fR b/2],   B_lin(t) = B_lin I(t) / I_peak

and the polarimeter's azimuth is that of the intensity-weighted Stokes vector, 0.5 atan2(<I sin 2theta>, <I cos 2theta>),
for the Gaussian pulse (about theta_peak / sqrt(2) for small angles)

(fR = 0.18, b = anisotropicFraction; with Raman off, (1 - fR)/3 becomes 1/3).

    python docs/examples/27-ner-measured-input/ner.py
"""
import copy
import json
import math
import re
import sys
from pathlib import Path

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here.parents[2] / "python" / "src"))
import ultrafast as uf  # noqa: E402


def jsonc(p):
    return json.loads(re.sub(r"//[^\n]*", "", Path(p).read_text()))


system, spec = jsonc(here / "plates.ufs"), jsonc(here / "run.json")
spec["pulse"]["spectrum"]["csv"] = str(here / "data" / "spectrum_digitised.csv")
spec["instruments"] = [{"kind": "polarimeter", "name": "pol", "at": "end"}]
chi = math.atan(spec["pulse"]["polarization"]["ellipticity"])
FR = 0.18


def run(polarization, raman=None, raman_on=True):
    sysd, sp = copy.deepcopy(system), copy.deepcopy(spec)
    if raman is not None:
        sysd["materials"] = {"FS": {"base": "FusedSilica", "raman": raman}}
        for e in sysd["elements"]:
            e["material"] = "FS"
    sp["pulse"]["polarization"] = polarization
    sp["physics"]["raman"] = raman_on
    sp["probes"] = "end"
    return uf.run(sysd, sp)


def averaged(theta_peak, n=4001):
    """Azimuth of the pulse-integrated Stokes vector when the rotation follows a Gaussian intensity profile."""
    xs = [-4 + 8 * k / (n - 1) for k in range(n)]
    w = [math.exp(-x * x) for x in xs]
    s2 = sum(wi * math.sin(2 * theta_peak * wi) for wi in w)
    s1 = sum(wi * math.cos(2 * theta_peak * wi) for wi in w)
    return 0.5 * math.atan2(s2, s1)


b_lin = run("x").summary["totals"]["bIntegralRad"]
print(f"B-integral of linear light: {b_lin:.3f} rad; ellipse angle chi = {math.degrees(chi):.2f} deg\n")
print(f"{'Raman response':34s} {'azimuth (deg)':>14s} {'analytic':>10s}")
cases = [("anisotropic, b = 0.25 (default)", None, True, (1 - FR) / 3 + FR * 0.25 / 2),
         ("isotropic, b = 0", {"kind": "silica", "anisotropicFraction": 0.0}, True, (1 - FR) / 3),
         ("off (electronic Kerr only)", None, False, 1 / 3)]
for name, raman, on, coef in cases:
    res = run({"angleDeg": 0, "ellipticity": math.tan(chi)}, raman, on)
    az = res.instrument("pol")["azimuthDeg"]
    b = b_lin if on else run("x", None, False).summary["totals"]["bIntegralRad"]
    theory = math.degrees(averaged(b * math.sin(2 * chi) * coef))
    print(f"{name:34s} {abs(az):14.2f} {theory:10.2f}")
