"""Polarisation, component by component: waveplates (Jones calculus, design retardance ∝ ω), polariser (Malus with the
extinction ratio), and the vector Kerr effect of an isotropic instantaneous χ(3) in a dispersionless plate: B-integral
of linear light against k0 n2 I L, circular / linear = 2/3, elliptical 1 − sin²2χ / 3, nonlinear ellipse rotation at the
pulse peak B sin2χ / 3 (Maker, Terhune & Savage 1964), circular at 1.5x energy has the linear spectrum.
Carriers 515 nm, 1030 nm, 2 µm.

Run: python verification/components/polarisation.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
"""
from __future__ import annotations

import math
import sys

from _common import C, CARRIERS_M, Family, b, label_wl, np, overlay, uf

N2, AREA, L = 3e-20, 1e-8, 5e-3


def pol(angle_deg=0.0, ellipticity=0.0):
    return {"angleDeg": float(angle_deg), "ellipticity": float(ellipticity)}


def linear_run(elements, lam, polarization, tau=100e-15):
    p = b.pulse(lam, tau, energy=1e-9)
    p["polarization"] = polarization
    return uf.run(b.system("pol", elements), b.run_spec(p, physics={"nonlinear": False}))


def kerr_run(lam, polarization, b_lin, tau=1e-12, energy_factor=1.0):
    """A dispersionless Kerr plate (constant index 1.45, n2 = 3e-20 m²/W, area 1e-8 m²), no Raman, no self-steepening;
    the peak power is chosen so that linear light gets B = b_lin."""
    p_peak = b_lin * AREA * lam / (2 * math.pi * N2 * L) * energy_factor
    p = b.pulse(lam, tau, peak_power_w=p_peak)
    p["polarization"] = polarization
    mats = {"kerr": {"index": 1.45, "n2": N2}}
    s = b.system("kerr", [b.bulk("plate", "kerr", L, beam=b.constant_beam(area_m2=AREA))], materials=mats)
    return uf.run(s, b.run_spec(p, physics={"selfSteepening": False, "raman": False}, grid=b.grid(auto=False, n=2048, window_s=12 * tau)))


def temporal(res, prefix):
    ax = np.asarray(res[prefix + "_ax_re"]) + 1j * np.asarray(res[prefix + "_ax_im"])
    ay = np.asarray(res[prefix + "_ay_re"]) + 1j * np.asarray(res[prefix + "_ay_im"])
    f = lambda a: np.fft.fftshift(np.fft.ifft(np.fft.ifftshift(a)))
    return f(ax), f(ay)


def azimuth(ex, ey):
    return 0.5 * math.atan2(2 * (ex * np.conj(ey)).real, abs(ex) ** 2 - abs(ey) ** 2)


def waveplates(fam: Family):
    """Design model: Γ(ω) = Γ_d ω / ω_d. Pulse-averaged Stokes over the input spectrum S(ω):
    half-wave at 22.5° on x: s1 = <cos²(Γ/2)>, s2 = <sin²(Γ/2)>;  quarter-wave at 45° on x: s3 = ±<sin Γ>."""
    def avg(r, f):
        w, s = np.asarray(r["input_omega_rad_s"]), np.asarray(r["input_spectrum"])
        m = w > 0
        return float((s[m] * f(w[m])).sum() / s[m].sum())
    for lam in CARRIERS_M:
        wd = 2 * math.pi * C / lam
        r = linear_run([b.waveplate("hwp", "half", 22.5)], lam, pol(0))
        want = avg(r, lambda w: np.sin(math.pi * w / wd / 2) ** 2)
        fam.check(f"half-wave at 22.5 deg, {label_wl(lam)}: s2", r.summary["output"]["stokes"][1], want, abs=1e-9, formula="<sin²(Γ/2)>")
        r = linear_run([b.waveplate("qwp", "quarter", 45)], lam, pol(0))
        want = avg(r, lambda w: np.sin(math.pi / 2 * w / wd))
        fam.check(f"quarter-wave at 45 deg, {label_wl(lam)}: |s3|", abs(r.summary["output"]["stokes"][2]), want, abs=1e-9, formula="<sin Γ>")
    # a 1030 nm half-wave plate is a full-wave plate at 515 nm
    r = linear_run([b.waveplate("hwp", "half", 22.5, design_wavelength_m=1030e-9)], 515e-9, pol(0))
    wd = 2 * math.pi * C / 1030e-9
    want = avg(r, lambda w: np.cos(math.pi * w / wd / 2) ** 2)
    fam.check("1030 nm half-wave plate at 515 nm: s1 (full wave)", r.summary["output"]["stokes"][0], want, abs=1e-9,
              formula="<cos²(Γ/2)>, Γ = π ω / ω_d ≈ 2π")


def polariser(fam: Family):
    er = 1e5
    with fam.figure("malus", "Polariser along x, extinction ratio 1e5: transmission against the input angle",
                    "T(θ) = cos²θ + sin²θ / ER") as f:
        angles = np.linspace(0, 90, 10)
        eng, ana = [], []
        for a in angles:
            r = linear_run([b.polarizer("pol", 0.0, er)], 1030e-9, pol(a))
            want = math.cos(math.radians(a)) ** 2 + math.sin(math.radians(a)) ** 2 / er
            fam.check(f"polariser {a:.0f} deg", r.summary["totals"]["energyTransmission"], want, rel=1e-6, abs=1e-12)
            eng.append(r.summary["totals"]["energyTransmission"])
            ana.append(want)
        overlay(f, angles, eng, ana, label="transmission", xlabel="input polarisation angle (deg)", ylabel="power transmission",
                marker="o", analytic_label="Malus")


def kerr(fam: Family):
    b_lin = 1.0
    with fam.figure("kerr-ellipse", "Isotropic instantaneous Kerr plate: B-integral against the ellipticity angle χ",
                    "B(χ) / B_lin = 1 − sin²(2χ) / 3;  B_lin = k0 n2 P / A L") as f:
        for lam in CARRIERS_M:
            r0 = kerr_run(lam, pol(0), b_lin)
            fam.check(f"B linear {label_wl(lam)}", r0.summary["totals"]["bIntegralRad"], b_lin, rel=2e-3, unit="rad", formula="k0 n2 I L")
            chis = np.linspace(0, math.pi / 4, 6)
            eng, ana = [], []
            for chi in chis:
                r = kerr_run(lam, pol(0, math.tan(chi)), b_lin)
                ratio = r.summary["totals"]["bIntegralRad"] / r0.summary["totals"]["bIntegralRad"]
                want = 1 - math.sin(2 * chi) ** 2 / 3
                fam.check(f"B(χ={math.degrees(chi):.0f}°)/B_lin {label_wl(lam)}", ratio, want, rel=2e-3)
                eng.append(ratio)
                ana.append(want)
            overlay(f, np.degrees(chis), eng, ana, label=label_wl(lam), xlabel="ellipticity angle χ (deg)", ylabel="B / B_linear",
                    marker="o", analytic_label="1 − sin²2χ / 3" if lam == CARRIERS_M[0] else None)

    with fam.figure("ner-rotation", "Nonlinear ellipse rotation at the pulse peak, B_lin = 1 rad, χ from 0 to 30°",
                    "θ = B_lin sin(2χ) / 3 (instantaneous isotropic χ(3))") as f:
        for lam in CARRIERS_M:
            chis = np.radians([5, 10, 15, 20, 25, 30])
            eng, ana = [], []
            for chi in chis:
                r = kerr_run(lam, pol(0, math.tan(chi)), b_lin)
                ix, iy = temporal(r, "input")
                ox, oy = temporal(r, "output")
                k = int(np.argmax(abs(ix) ** 2 + abs(iy) ** 2))
                rot = abs(azimuth(ox[k], oy[k]) - azimuth(ix[k], iy[k]))
                want = b_lin * math.sin(2 * chi) / 3
                fam.check(f"rotation χ={math.degrees(chi):.0f}° {label_wl(lam)}", rot, want, rel=1e-2, unit="rad")
                eng.append(rot * 1e3)
                ana.append(want * 1e3)
            overlay(f, np.degrees(chis), eng, ana, label=label_wl(lam), xlabel="ellipticity angle χ (deg)", ylabel="rotation (mrad)",
                    marker="o", analytic_label="B sin2χ / 3" if lam == CARRIERS_M[0] else None)

    # circular at 1.5x the power has the linear spectrum (Raman off)
    for lam in CARRIERS_M:
        rl = kerr_run(lam, pol(0), 4.0, tau=200e-15)
        rc = kerr_run(lam, "rcp", 4.0, tau=200e-15, energy_factor=1.5)
        sl, sc = np.asarray(rl["output_spectrum"]), np.asarray(rc["output_spectrum"])
        dev = float(np.max(np.abs(sl / sl.max() - sc / sc.max())))
        fam.check(f"circular at 1.5x = linear spectrum {label_wl(lam)}", dev, 0.0, abs=1e-3, formula="max |S_c/S_c,max − S_l/S_l,max|")


def main() -> int:
    fam = Family("polarisation", "Polarisation: waveplates, polariser, vector Kerr (2/3, NER rotation)")
    for fn in (waveplates, polariser, kerr):
        fam.guard(fn.__name__, lambda fn=fn: fn(fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
