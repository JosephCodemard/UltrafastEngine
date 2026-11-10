"""Temporal pulse shaping with the ao plugin: double pulse, pulse train, flat top, pixel-gap side pulses, a gain-narrowed spectrum
restored by an AOPDF, and closed-loop compensation of an unknown chirp (python temporal_shaping.py)."""
import numpy as np

import ultrafast as uf
from ultrafast import build as b
from ultrafast.algorithms import sensorless

uf.plugins.load_python_plugins()
from ultrafast_ao import SpectralShaper                               # noqa: E402

LAM, TP, C = 800e-9, 30e-15, 299792458.0
WINDOW = 4e-12


def go(elements, pulse=None, window=WINDOW):
    pulse = pulse or b.pulse(LAM, TP, energy=1e-6)
    r = uf.run(b.system("shape", [b.gap("g", 0.0, medium="Vacuum")] + elements), b.run_spec(pulse, grid={"windowS": window}))
    return {"t": r["output_time_s"], "p": r["output_power_w"], "lam": r["output_wavelength_m"], "s": r["output_spectrum"], "sum": r.summary["output"]}


def peaks(t, p, rel=0.05):
    """Positions (s) and heights of the local maxima above ``rel`` of the highest peak."""
    i = np.flatnonzero((p[1:-1] > p[:-2]) & (p[1:-1] >= p[2:]) & (p[1:-1] > rel * p.max())) + 1
    return t[i], p[i]


def aopdf(**kw):
    return {"id": "aopdf", "type": "aopdf", **kw}


def comb_shaper(n_pulses, spacing_s, pixels=1024, span_nm=120.0):
    """A shaper whose mask is the transfer function sum_k exp(i omega k tau) / n of an n-pulse train, sampled on ``pixels`` pixels over ``span_nm``."""
    D = 1e6                                                           # 1 mm of mask per nm of spectrum
    pitch = span_nm * 1e-9 * D / pixels
    lam = LAM + (np.arange(pixels) - (pixels - 1) / 2) * pitch / D
    w = 2 * np.pi * C / lam - 2 * np.pi * C / LAM
    k = np.arange(n_pulses) - (n_pulses - 1) / 2
    T = np.exp(1j * np.outer(w, k * spacing_s)).sum(axis=1) / n_pulses
    return SpectralShaper("comb", pixels=pixels, pixel_pitch_m=pitch, dispersion_m_per_m=D, spot_radius_m=0.5 * pitch, phase_rad=np.angle(T).tolist(),
                          amplitude=np.abs(T).tolist(), center_wavelength_m=LAM)


def closed_loop(chirp_gdd=900.0, chirp_tod=4000.0, sweeps=8):
    """The source carries an unknown GDD and TOD (a dispersion element); modal search on the AOPDF's GDD and TOD with the output peak power as the only metric."""
    chirp = {"id": "chirp", "type": "dispersion", "gddS2": chirp_gdd * 1e-30, "todS3": chirp_tod * 1e-45}
    unit = np.array([300.0, 3000.0])                                  # one unit of the search variables: 300 fs^2 and 3000 fs^3
    shaped = lambda u: go([chirp, aopdf(gddFs2=float(u[0] * unit[0]), todFs3=float(u[1] * unit[1]))], window=2e-12)
    ref = float(go([aopdf()], window=2e-12)["sum"]["peakPowerW"])
    x = np.zeros(2)
    out = [{"x": x * unit, "run": shaped(x)}]
    for _ in range(sweeps):
        x = sensorless.modal_search(lambda u: shaped(u)["sum"]["peakPowerW"], x, bias=0.5, sweeps=1).x
        out.append({"x": x * unit, "run": shaped(x)})
    return {"steps": out, "ref_peak": ref, "chirp": (chirp_gdd, chirp_tod)}


def run():
    out = {}
    out["reference"] = go([])
    out["double"] = go([aopdf(replicaAmplitude=1.0, replicaDelayS=150e-15)])
    out["train"] = go([comb_shaper(3, 120e-15)])
    out["flat"] = go([aopdf(gddFs2=6000.0)], window=4e-12)
    pix = 0.5e-3                                                      # 0.5 mm of mask = 0.5 nm of spectrum: 12 pixels across the 150 fs pulse
    out["gaps"] = {"fill": 0.8, **go([SpectralShaper("px", pixels=60, pixel_pitch_m=pix, dispersion_m_per_m=1e6, spot_radius_m=0.25 * pix, fill_factor=0.8, gap_transmission=0.0,
                                                     center_wavelength_m=LAM)], pulse=b.pulse(LAM, 150e-15, energy=1e-6), window=40e-12)}
    out["pix"] = pix
    # gain narrowing: a Gaussian amplitude filter (stands in for the gain), then an amplitude table that undoes it
    wl = np.linspace(700e-9, 900e-9, 201)
    narrow = np.exp(-((wl - LAM) / 22e-9) ** 2)
    restore = np.minimum(1.0 / narrow, 4.0)
    restore = restore / restore.max()
    broad = b.pulse(LAM, 15e-15, energy=1e-6)
    out["narrowed"] = go([aopdf(id="gain", amplitudeTable=narrow.tolist(), tableWavelengthsM=wl.tolist())], pulse=broad)
    out["restored"] = go([aopdf(id="gain", amplitudeTable=narrow.tolist(), tableWavelengthsM=wl.tolist()), aopdf(id="fix", amplitudeTable=restore.tolist(), tableWavelengthsM=wl.tolist())], pulse=broad)
    out["broad"] = go([], pulse=broad)
    out["loop"] = closed_loop()
    return out


def main():
    r = run()
    ref = r["reference"]["sum"]
    print("input pulse: %.1f fs FWHM, 800 nm" % (ref["fwhmS"] * 1e15))
    t, p = r["double"]["t"], r["double"]["p"]
    pk, ph = peaks(t, p)
    print("double pulse (aopdf replica, 150 fs): peaks at %s fs, height ratio %.3f" % (np.round((pk - pk.mean()) * 1e15, 0).tolist(), ph[1] / ph[0]))
    pk3, ph3 = peaks(r["train"]["t"], r["train"]["p"], 0.2)
    print("3-pulse train (shaper, 120 fs): %d pulses spaced %.0f fs" % (len(pk3), np.mean(np.diff(pk3)) * 1e15))
    fl = r["flat"]["sum"]
    bw = 2 * np.pi * 0.441 / TP                                      # angular FWHM bandwidth of a 30 fs Gaussian pulse
    print("stretched (GDD 6000 fs^2): FWHM %.0f fs (stationary phase: GDD x bandwidth = %.0f fs)" % (fl["fwhmS"] * 1e15, 6000e-30 * bw * 1e15))
    g = r["gaps"]
    tg, pg = g["t"], g["p"]
    main_i = np.argmax(pg)
    # side pulse position: 2 pi / (pixel bandwidth); pixel bandwidth = d omega per pixel = 2 pi c / lam^2 * (pixel / D)
    dw = 2 * np.pi * C / LAM ** 2 * (r["pix"] / 1e6)
    t_side = 2 * np.pi / dw
    near = np.abs(tg - tg[main_i] - t_side) < 0.1 * t_side
    i_side = np.flatnonzero(near)[np.argmax(pg[near])]
    e_ratio = pg[near].sum() / pg[np.abs(tg - tg[main_i]) < 0.1 * t_side].sum()       # energy of the side pulse over the main pulse
    f_ = g["fill"]
    pred = (np.sin(np.pi * f_) / (np.pi * f_)) ** 2 * np.exp(-2 * (np.pi * 0.25) ** 2)   # Weiner: comb of gaps x the resolution of the spot (w0 = pitch / 4)
    print("pixel gaps (fill factor 0.8, pixel %.0f um): side pulse at %.2f ps (2 pi / pixel bandwidth = %.2f ps), energy ratio %.4f (formula %.4f)"
          % (r["pix"] * 1e6, (tg[i_side] - tg[main_i]) * 1e12, t_side * 1e12, e_ratio, pred))
    nb, rb, bb = r["narrowed"]["sum"], r["restored"]["sum"], r["broad"]["sum"]
    print("gain narrowing: FWHM %.1f fs (broad input) -> %.1f fs (narrowed) -> %.1f fs (restored by the AOPDF amplitude table)" % (bb["fwhmS"] * 1e15, nb["fwhmS"] * 1e15, rb["fwhmS"] * 1e15))
    L = r["loop"]
    print("closed loop, unknown chirp GDD %.0f fs^2 and TOD %.0f fs^3: peak power per sweep (fraction of the transform limit): %s"
          % (L["chirp"][0], L["chirp"][1], " ".join("%.2f" % (s["run"]["sum"]["peakPowerW"] / L["ref_peak"]) for s in L["steps"])))
    print("  found GDD %.0f fs^2, TOD %.0f fs^3" % tuple(L["steps"][-1]["x"]))
    assert len(pk) == 2 and abs((pk[1] - pk[0]) * 1e15 - 150) < 3 and abs(ph[1] / ph[0] - 1) < 0.02
    assert len(pk3) == 3 and abs(np.mean(np.diff(pk3)) * 1e15 - 120) < 3
    assert abs((tg[i_side] - tg[main_i]) / t_side - 1) < 0.02 and abs(e_ratio / pred - 1) < 0.05
    assert rb["fwhmS"] < nb["fwhmS"] * 0.75
    assert L["steps"][-1]["run"]["sum"]["peakPowerW"] / L["ref_peak"] > 0.9
    print("checks passed")


if __name__ == "__main__":
    main()
