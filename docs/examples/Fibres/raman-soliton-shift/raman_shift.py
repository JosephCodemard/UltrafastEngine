"""Soliton self-frequency shift: Raman scattering pushes a fundamental soliton to the red at a rate proportional to T0^-4 (Gordon 1986)
(python raman_shift.py)."""
import numpy as np

import ultrafast as uf
from ultrafast import build as b

C = 299792458.0
LAM, B2, GAMMA = 1550e-9, -20e-27, 1.3e-3
T0S = (40e-15, 50e-15, 65e-15)                      # sech widths of the three fundamental solitons
LENGTH = 6.0
TAU1, TAU2, FR = 12.2e-15, 32e-15, 0.18              # the engine's "silica" Raman model (Blow and Wood)
GRID = {"auto": False, "n": 8192, "windowS": 20e-12}


def raman_first_moment():
    """T_R = f_R * integral t h_R(t) dt of the Blow-Wood response h_R = (tau1^2 + tau2^2) / (tau1 tau2^2) exp(-t / tau2) sin(t / tau1), integrated numerically."""
    t = np.linspace(0, 2e-12, 400001)
    h = (TAU1 ** 2 + TAU2 ** 2) / (TAU1 * TAU2 ** 2) * np.exp(-t / TAU2) * np.sin(t / TAU1)
    norm = np.trapezoid(h, t)
    return FR * np.trapezoid(t * h, t) / norm


def exact_rate(t0):
    """Shift rate of a fixed-width sech soliton with the model's exact Raman response (no first-moment approximation).

    The nonlinear phase is phi_z = gamma V with V = (1 - f_R) P + f_R (h_R * P), so the mean frequency changes by
    d<Omega>/dz = -gamma f_R (1 / E) integral (h_R * P)(t) P'(t) dt (the instantaneous Kerr part integrates to zero). For a delta-prime response it
    gives Gordon's -8 T_R |beta2| / (15 T0^4) exactly. Returns rad/s per m (negative: red shift)."""
    dt = 0.25e-15
    t = np.arange(-4e-12, 4e-12, dt)
    ts = np.arange(0, 2e-12, dt)
    h = (TAU1 ** 2 + TAU2 ** 2) / (TAU1 * TAU2 ** 2) * np.exp(-ts / TAU2) * np.sin(ts / TAU1)
    p = abs(B2) / (GAMMA * t0 ** 2) / np.cosh(t / t0) ** 2
    f = np.convolve(p, h * dt)[:len(p)]
    return -GAMMA * FR * abs(np.trapezoid(f * np.gradient(p, dt), t)) / np.trapezoid(p, t)


def centroid_frequency(r):
    w, s = r["output_omega_rad_s"], r["output_spectrum"]
    return float(np.sum(w * s) / np.sum(s))


def propagate(t0, length, raman=True):
    fib = {"id": "fibre", "type": "fiber", "lengthM": float(length), "betas": [B2], "gamma": GAMMA, "raman": "silica" if raman else "none", "referenceWavelengthM": LAM}
    p0 = abs(B2) / (GAMMA * t0 ** 2)
    pulse = b.pulse(LAM, 1.763 * t0, peak_power_w=p0, shape="sech2")
    return uf.run(b.system("raman", [fib]), b.run_spec(pulse, physics={"selfSteepening": False, "raman": raman}, grid=GRID))


def run(points=24):
    out = {}
    z = np.linspace(0, LENGTH, points)
    for t0 in T0S:
        runs = [propagate(t0, max(zi, 1e-6)) for zi in z]
        w0 = centroid_frequency(runs[0])
        out[t0] = {"z": z, "shift": np.array([centroid_frequency(r) - w0 for r in runs]), "lam": runs[0]["output_wavelength_m"], "spectra": np.array([r["output_spectrum"] for r in runs]),
                   "t": runs[0]["output_time_s"], "power": np.array([r["output_power_w"] for r in runs]), "w": runs[0]["output_omega_rad_s"]}
    return out


def main():
    r = run()
    tr = raman_first_moment()
    print("fundamental solitons N = 1, beta2 = %.0f ps^2/km, gamma = %.1f /(W km), %.0f m of silica fibre, Raman response of the engine (T_R = %.2f fs from the model)" % (B2 * 1e27, GAMMA * 1e3, LENGTH, tr * 1e15))
    rates, exacts = [], []
    for t0 in T0S:
        # the rate is measured over the last two thirds of the fibre (the soliton has settled); Gordon's formula gives the constant rate at fixed T0
        d = r[t0]
        sel = d["z"] > LENGTH / 3
        rate = np.polyfit(d["z"][sel], d["shift"][sel], 1)[0]                   # rad/s per m (negative: red shift)
        gordon = -8 * tr * abs(B2) / (15 * t0 ** 4)
        rates.append(rate)
        exact = exact_rate(t0)
        exacts.append(exact)
        print("T0 = %2.0f fs: shift after %.0f m %.1f THz (%.0f nm), rate %.3f THz/m; Gordon -8 T_R |beta2| / (15 T0^4) = %.3f THz/m (ratio %.2f); exact Raman gain %.3f THz/m (ratio %.3f)"
              % (t0 * 1e15, LENGTH, d["shift"][-1] / (2 * np.pi) * 1e-12, -d["shift"][-1] / (2 * np.pi) * LAM ** 2 / C * 1e9, rate / (2 * np.pi) * 1e-12, gordon / (2 * np.pi) * 1e-12, rate / gordon,
                 exact / (2 * np.pi) * 1e-12, rate / exact))
    slope = np.polyfit(np.log(T0S), np.log(-np.array(rates)), 1)[0]
    print("log-log slope of the rate against T0: %.2f (Gordon: -4)" % slope)
    assert abs(slope + 4) < 0.5
    assert all(abs(rr / ex - 1) < 0.05 for rr, ex in zip(rates, exacts))       # the engine against the exact-gain rate of a fixed-width soliton
    assert all(0.4 < rr / (-8 * tr * abs(B2) / (15 * t ** 4)) < 2.5 for rr, t in zip(rates, T0S))
    print("checks passed")


if __name__ == "__main__":
    main()
