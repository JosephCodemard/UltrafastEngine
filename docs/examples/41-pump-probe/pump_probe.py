"""A pump-probe experiment as a Branched system, checked against the exact cross-phase result.

A strong pump and a weak probe leave one pulse on a 98:2 splitter, travel in two arms (a QWP at +45 and -45 degrees turn them into opposite
circular polarisations, the probe arm is delayed by tau), recombine on a 50:50 splitter and cross a Kerr plate. A QWP at +45 degrees and a
polarizer then pass only the probe. For opposite circular polarisations an isotropic chi(3) only adds a phase, so the probe's spectral
centroid shift is exact: it is minus the probe-intensity-weighted mean of d(phase)/dt,
    phase(t) = (4/3) k0 n2 L I_pump(t),   shift = -<d phase / dt>_probe.
The pulses are 1 ps long on purpose: the waveplates' retardance is proportional to omega, so a QWP pair does not extinguish the pump's spectral
wings (1e-4 of the pump leaks into the probe channel at 100 fs, 1e-6 at 1 ps) and that leak shifts the probe's measured centroid.
"""
import math
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

import ultrafast as uf
from ultrafast import build as b

BLUE, ORANGE = "#2a78d6", "#eb6834"
C, LAM, N2, L = 299792458.0, 1030e-9, 2.6e-20, 2e-3       # n2 of the engine's FusedSilica (m^2/W)
FWHM, E_IN, AREA, SPLIT = 1e-12, 5e-6, 4e-10, 0.98        # pump and probe duration, energy into the splitter, beam area
TP = FWHM / (2 * math.sqrt(math.log(2)))                  # 1/e half-width of the intensity


def system(tau_s):
    """The pump arm carries the pump, the probe arm the probe; the arm that arrives early gets no extra delay."""
    return b.system("pump-probe", b.Branched([
        b.link(b.beam_splitter("bs1", reflectivity=SPLIT), {"c": "probeQwp", "d": "pumpQwp"}),
        b.link(b.waveplate("pumpQwp", "quarter", axis_deg=45), "pumpDelay"),
        b.link(b.delay("pumpDelay", delay_s=max(0.0, -tau_s)), "bs2:1"),
        b.link(b.waveplate("probeQwp", "quarter", axis_deg=-45), "probeDelay"),
        b.link(b.delay("probeDelay", delay_s=max(0.0, tau_s)), "bs2:2"),
        b.link(b.beam_splitter("bs2", reflectivity=0.5), {"c": "plate", "d": "virtual"}),
        b.link(b.bulk("plate", "FusedSilica", L, beam=b.constant_beam(area_m2=AREA)), "analyserQwp"),
        b.link(b.waveplate("analyserQwp", "quarter", axis_deg=45), "analyser"),
        b.link(b.polarizer("analyser", axis_deg=0), "end"),
    ]))


def run(tau_s, kerr=True):
    spec = b.run_spec(b.pulse(LAM, FWHM, energy=E_IN), grid=b.grid(n=8192, window_s=40e-12),
                      physics={"raman": False, "selfSteepening": False, "nonlinear": kerr})
    res = uf.run(system(tau_s), spec)
    w, s = res["output_omega_rad_s"], res["output_spectrum"]
    return res, float((w * s).sum() / s.sum())


def reference(tau_s, i_pump_w_m2):
    """Exact centroid shift (rad/s) of a Gaussian probe in the cross-phase of a Gaussian pump."""
    t = np.linspace(-12 * TP, 12 * TP, 20001)
    probe = np.exp(-((t - tau_s) / TP) ** 2)
    phase = 4.0 / 3.0 * (2 * math.pi * C / LAM / C) * N2 * L * i_pump_w_m2 * np.exp(-(t / TP) ** 2)
    return float(-(probe * np.gradient(phase, t)).sum() / probe.sum())


if __name__ == "__main__":
    # pump intensity at the plate: the pump arm carries SPLIT of the energy, bs2 sends half on, a Gaussian pulse peaks at 0.9394 E / tau
    pump_energy = E_IN * SPLIT * 0.5
    i_pump = 0.9394 * pump_energy / FWHM / AREA
    taus = np.linspace(-2e-12, 2e-12, 33)
    ref_res, centroid0 = run(0.0, kerr=False)
    print("probe energy at the output %.3f nJ  (expected %.3f nJ; the pump is blocked)" % (
        ref_res.summary["output"]["energyJ"] * 1e9, E_IN * (1 - SPLIT) * 0.5 * 1e9))
    shift, packets = [], []
    for tau in taus:
        res, c = run(tau)
        shift.append(c - run(tau, kerr=False)[1])
        packets.append(res.summary["graph"]["packetsAtEnd"])
    shift = np.array(shift)
    model = np.array([reference(t, i_pump) for t in taus])
    err = np.abs(shift - model).max() / np.abs(model).max()
    print("peak cross-phase B = %.2f rad; largest centroid shift %.3e rad/s (analytic %.3e)" % (
        4 / 3 * 2 * math.pi / LAM * N2 * L * i_pump, np.abs(shift).max(), np.abs(model).max()))
    print("largest deviation from the analytic shift  %.2e of the peak shift" % err)
    print("packets at the end: %s" % sorted(set(packets)))

    fig, ax = plt.subplots(figsize=(6.2, 3.8))
    ax.plot(taus * 1e15, model / 1e12, color=ORANGE, lw=2, label="analytic")
    ax.plot(taus * 1e15, shift / 1e12, "o", color=BLUE, ms=4.5, label="UltrafastEngine")
    ax.set_xlabel("probe delay  tau (fs)"); ax.set_ylabel("probe centroid shift (rad / ps)")
    ax.axhline(0, color="0.7", lw=0.8); ax.legend(frameon=False); fig.tight_layout()
    fig.savefig("pump_probe.png", dpi=130)
    sys.exit(0 if err < 0.025 and packets and set(packets) == {1} else 1)
