"""Nonlinear pulse compression: self-phase modulation broadens the spectrum in a normal-dispersion fibre, a compressor removes the chirp
(python spm_compression.py)."""
import numpy as np

import ultrafast as uf
from ultrafast import build as b
from ultrafast.algorithms import sensorless

LAM, TAU, ENERGY = 1030e-9, 300e-15, 4.0e-9
B2, GAMMA, LENGTH = 23e-27, 1.0e-3, 0.8             # 23 ps^2/km (normal), gamma = 1 /(W km), 0.8 m
GRID = {"auto": False, "n": 4096, "windowS": 10e-12}
PHYS = {"selfSteepening": False, "raman": False}


def fibre(length, b2=B2):
    return {"id": "fibre", "type": "fiber", "lengthM": float(length), "betas": [b2], "gamma": GAMMA, "raman": "none", "referenceWavelengthM": LAM}


def go(elements):
    return uf.run(b.system("spm", elements), b.run_spec(b.pulse(LAM, TAU, energy=ENERGY), physics=PHYS, grid=GRID))


def rms_width(x, w):
    p = w / w.sum()
    m = (x * p).sum()
    return float(np.sqrt(((x - m) ** 2 * p).sum()))


def run(points=30):
    z = np.linspace(0, LENGTH, points)
    runs = [go([fibre(max(zi, 1e-6))]) for zi in z]
    out = {"z": z, "t": runs[0]["output_time_s"], "power": np.array([r["output_power_w"] for r in runs]), "w": runs[0]["output_omega_rad_s"],
           "lam": runs[0]["output_wavelength_m"], "spectrum": np.array([r["output_spectrum"] for r in runs]), "peak": runs[0].summary["output"]["peakPowerW"]}
    out["bw"] = np.array([rms_width(r["output_omega_rad_s"], r["output_spectrum"]) for r in runs])
    out["fwhm"] = np.array([r.summary["output"]["fwhmS"] for r in runs])
    # compressor: a dispersion element, GDD found from the output peak power only
    peak = lambda g: float(go([fibre(LENGTH), {"id": "comp", "type": "dispersion", "gddS2": float(g[0]) * 1e-27}])["output_power_w"].max())
    s = sensorless.modal_search(peak, np.array([-10.0]), bias=3.0, sweeps=8, maximise=True)
    out["gdd_ps2_km"] = float(s.x[0])
    out["compressed"] = go([fibre(LENGTH), {"id": "comp", "type": "dispersion", "gddS2": float(s.x[0]) * 1e-27}])
    out["uncompressed"] = go([fibre(LENGTH)])
    out["input"] = go([])
    nodisp = go([fibre(LENGTH, 0.0)])                                  # the same fibre without dispersion: pure SPM
    out["spm_only_growth"] = rms_width(nodisp["output_omega_rad_s"], nodisp["output_spectrum"]) / out["bw"][0]
    out["nodisp"] = nodisp
    return out


def main():
    r = run()
    p0 = r["peak"]
    phi = GAMMA * p0 * LENGTH
    print("input: %.0f fs, %.1f nJ at %.0f nm (peak power %.1f kW); fibre %.1f m, beta2 = +%.0f ps^2/km, gamma = %.1f /(W km)" % (TAU * 1e15, ENERGY * 1e9, LAM * 1e9, p0 / 1e3, LENGTH, B2 * 1e27, GAMMA * 1e3))
    print("peak nonlinear phase gamma P0 L = %.1f rad (the dispersion length of the pulse is %.2f m, so dispersion acts as well)" % (phi, (TAU / 1.665) ** 2 / B2))
    grow = r["bw"][-1] / r["bw"][0]
    pred = np.sqrt(1 + 4 / (3 * np.sqrt(3)) * phi ** 2)
    print("rms spectral width, same fibre without dispersion (pure SPM): %.2f x the input (formula for a Gaussian pulse sqrt(1 + 4 phi^2 / (3 sqrt 3)) = %.2f x)" % (r["spm_only_growth"], pred))
    print("rms spectral width, with the fibre's dispersion: %.2f x (the pulse stretches while it is modulated, its power falls and so does the phase)" % grow)
    fin, fun, fout = r["input"].summary["output"], r["uncompressed"].summary["output"], r["compressed"].summary["output"]
    print("duration: input %.0f fs, after the fibre %.0f fs, after a compressor of GDD %.1f x 10^-27 s^2 (found from the peak power alone) %.0f fs" % (fin["fwhmS"] * 1e15, fun["fwhmS"] * 1e15, r["gdd_ps2_km"], fout["fwhmS"] * 1e15))
    print("peak power after compression %.1f x the input; transform limit of the broadened spectrum: %.0f fs (temporal Strehl %.2f)" % (fout["peakPowerW"] / fin["peakPowerW"], fout["transformLimitFwhmS"] * 1e15, fout["temporalStrehl"]))
    assert abs(r["spm_only_growth"] / pred - 1) < 0.03 and grow < r["spm_only_growth"]
    assert fout["fwhmS"] < 0.4 * fin["fwhmS"] and fout["peakPowerW"] > 2.0 * fin["peakPowerW"]
    print("checks passed")


if __name__ == "__main__":
    main()
