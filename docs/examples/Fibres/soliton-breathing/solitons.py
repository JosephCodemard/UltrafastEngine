"""Fundamental and third-order solitons in a single-mode fibre: the N = 1 soliton keeps its shape, the N = 3 soliton breathes with the period
z0 = (pi / 2) L_D (python solitons.py)."""
import numpy as np

import ultrafast as uf
from ultrafast import build as b

LAM, B2, GAMMA, T0 = 1550e-9, -20e-27, 1.3e-3, 100e-15         # 20 ps^2/km anomalous dispersion, gamma = 1.3 /(W km), sech T0 = 100 fs (FWHM 176 fs)
LD = T0 ** 2 / abs(B2)
Z0 = np.pi / 2 * LD                                              # soliton period
GRID = {"auto": False, "n": 2048, "windowS": 4e-12}


def peak_power(n):
    return n ** 2 * abs(B2) / (GAMMA * T0 ** 2)


def propagate(n, length_m):
    fib = {"id": "fibre", "type": "fiber", "lengthM": float(length_m), "betas": [B2], "gamma": GAMMA, "raman": "none", "referenceWavelengthM": LAM}
    pulse = b.pulse(LAM, 1.763 * T0, peak_power_w=peak_power(n), shape="sech2")
    return uf.run(b.system("soliton", [fib]), b.run_spec(pulse, physics={"selfSteepening": False, "raman": False}, grid=GRID))


def evolution(n, zmax, points=90):
    z = np.linspace(0, zmax, points)
    runs = [propagate(n, max(zi, 1e-6)) for zi in z]
    t = runs[0]["output_time_s"]
    return {"z": z, "t": t, "power": np.array([r["output_power_w"] for r in runs]), "lam": runs[0]["output_wavelength_m"],
            "spectrum": np.array([r["output_spectrum"] for r in runs]), "peak": np.array([r.summary["output"]["peakPowerW"] for r in runs]),
            "fwhm": np.array([r.summary["output"]["fwhmS"] for r in runs])}


def run():
    return {1: evolution(1, 2 * Z0), 3: evolution(3, 2 * Z0)}


def main():
    r = run()
    print("fibre: beta2 = %.0f ps^2/km, gamma = %.1f /(W km), sech pulse T0 = %.0f fs (FWHM %.0f fs) at %.0f nm" % (B2 * 1e27, GAMMA * 1e3, T0 * 1e15, 1.763 * T0 * 1e15, LAM * 1e9))
    print("dispersion length L_D = %.3f m, soliton period z0 = pi/2 L_D = %.3f m" % (LD, Z0))
    for n in (1, 3):
        print("N = %d: input peak power %.2f kW (N^2 |beta2| / (gamma T0^2))" % (n, peak_power(n) / 1e3))
    e1 = r[1]
    print("N = 1: peak power stays within %.2f %% and the FWHM within %.2f %% of the input over two periods" % (100 * np.max(np.abs(e1["peak"] / peak_power(1) - 1)), 100 * np.max(np.abs(e1["fwhm"] / e1["fwhm"][0] - 1))))
    e3 = r[3]
    zi = np.argmax(e3["peak"][:45])
    k = np.argmin(np.abs(e3["z"] - Z0))
    print("N = 3: largest compression in the first period: peak %.1f x the input at z = %.2f m = %.2f z0; the peak is back to %.3f x at z0 = %.3f m (grid step %.3f m)"
          % (e3["peak"][zi] / peak_power(3), e3["z"][zi], e3["z"][zi] / Z0, e3["peak"][k] / peak_power(3), Z0, e3["z"][1]))
    i2 = np.argmin(np.abs(e3["z"] - 2 * Z0))
    print("N = 3: after two periods the peak is %.3f x the input" % (e3["peak"][i2] / peak_power(3)))
    assert np.max(np.abs(e1["peak"] / peak_power(1) - 1)) < 0.01
    assert abs(e3["peak"][k] / peak_power(3) - 1) < 0.03 and abs(e3["peak"][i2] / peak_power(3) - 1) < 0.03
    assert e3["peak"][zi] > 3 * peak_power(3) and e3["peak"][k] < 1.03 * peak_power(3)
    print("checks passed")


if __name__ == "__main__":
    main()
