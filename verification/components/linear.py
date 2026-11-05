"""Linear components alone, against analytic answers: bulk and gap (group delay, GDD, TOD and the full spectral phase
from Sellmeier x Ciddor air), plates (Fresnel transmission, tilted-plate phase), mirrors with GDD coatings, chirped
mirrors, the Taylor dispersion element, the leakMirror's two ports (Sequential and Branched), and the grating pair
against Treacy. Carriers 515 nm, 1030 nm and 2 um; pulses of 1 ps and 10 fs.

Run: python verification/components/linear.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
Main pieces: one function per component (bulk_taylor, bulk_full_phase, bandwidth, plates, mirrors, leak_mirror,
grating_pair); each adds checks to the Family and draws one figure.
"""
from __future__ import annotations

import math
import sys

from _common import (C, CARRIERS_M, FS2, Family, b, centroid, label_dur, label_wl, np, overlay, poly_phase, rms_width,
                     uf)
import _analytic as A

GLASSES = ["FusedSilica", "N-BK7", "CaF2", "Sapphire"]
FS3 = 1e-45


def run(elements, lam, tau=100e-15, beam=None, branched=False, energy=1e-9):
    s = b.system("component", b.Branched(elements) if branched else elements)
    spec = b.run_spec(b.pulse(lam, tau, energy=energy), physics={"nonlinear": False})
    if beam:
        spec["inputBeam"] = beam
    return uf.run(s, spec)


def field_phase(res):
    """(ω, S(ω), φ_out − φ_in) on the output grid."""
    w = np.asarray(res["output_omega_rad_s"])
    return w, np.asarray(res["output_spectrum"]), np.unwrap(np.asarray(res["output_phase_x_rad"]) - np.asarray(res["input_phase_x_rad"]))


def bulk_taylor(fam: Family):
    L = 0.01
    with fam.figure("bulk-gdd", "Bulk, 10 mm: GDD against Sellmeier × Ciddor air",
                    "GDD = d²/dω² [ω n(ω) L / c],  n = n_Sellmeier(λ) · n_air,Ciddor(λ)") as f:
        lams = np.array([515e-9, 700e-9, 1030e-9, 1300e-9, 1600e-9, 2e-6])
        for g in GLASSES:
            eng, ana = [], []
            for lam in lams:
                r = run([b.bulk("m", g, L)], lam)
                t, w0 = r.summary["totals"], r.summary["grid"]["reportOmega0"]
                gd = A.phase_derivatives(A.GLASSES[g], L, r.summary["grid"]["omega0"])[0]   # arrival time: the pulse sits at the grid centre
                _, gdd, tod = A.phase_derivatives(A.GLASSES[g], L, w0)                        # GDD, TOD: at the requested carrier
                eng.append(t["systemGddS2"] / FS2 / 10)
                ana.append(gdd / FS2 / 10)
                if lam in CARRIERS_M:
                    tag = f"{g} {label_wl(lam)}"
                    fam.check(f"bulk GD {tag}", t["arrivalTimeS"], gd, rel=1e-9, unit="s", formula="τ = dφ/dω")
                    fam.check(f"bulk GDD {tag}", t["systemGddS2"], gdd, rel=1e-6, unit="s²", formula="d²φ/dω²")
                    fam.check(f"bulk TOD {tag}", t["systemTodS3"], tod, rel=2e-5, unit="s³", formula="d³φ/dω³")
            overlay(f, lams * 1e9, eng, ana, label=g, xlabel="carrier wavelength (nm)", ylabel="GDD (fs² per mm)",
                    residual="rel", marker="o", style="-", analytic_label="analytic" if g == GLASSES[0] else None)
        # air gap: 1 m
        for lam in CARRIERS_M:
            r = run([b.gap("air", 1.0)], lam)
            t, w0 = r.summary["totals"], r.summary["grid"]["reportOmega0"]
            gd = A.phase_derivatives(A.ciddor_air, 1.0, r.summary["grid"]["omega0"])[0]
            _, gdd, tod = A.phase_derivatives(A.ciddor_air, 1.0, w0)
            fam.check(f"air gap 1 m GD {label_wl(lam)}", t["arrivalTimeS"], gd, rel=1e-9, unit="s")
            fam.check(f"air gap 1 m GDD {label_wl(lam)}", t["systemGddS2"], gdd, rel=1e-4, unit="s²")


def bulk_full_phase(fam: Family):
    """The applied φ(ω) over a 10 fs pulse's whole band (not only its Taylor terms)."""
    L = 1e-3
    with fam.figure("bulk-phase", "Fused silica, 1 mm, 10 fs pulses: applied spectral phase φ(ω) minus its linear part",
                    "φ(ω) = ω (n(ω) − 1) L / c, linear part removed by a weighted fit; both curves treated alike") as f:
        for lam in CARRIERS_M:
            r = run([b.bulk("m", "FusedSilica", L)], lam, tau=10e-15)
            w, s, ph = field_phase(r)
            m = s > 1e-4 * s.max()
            ana = np.zeros_like(w)
            ana[m] = A.phi_of_omega(lambda x: A.GLASSES["FusedSilica"](x) - 1.0, L, w[m])

            def detrend(y):
                p = np.polyfit(w[m] - w[m].mean(), y[m], 1, w=np.sqrt(s[m]))
                return y[m] - np.polyval(p, w[m] - w[m].mean())
            e, a = detrend(ph), detrend(ana)
            fam.check(f"full phase max |Δφ| {label_wl(lam)} 10 fs", float(np.max(np.abs(e - a))), 0.0, abs=2e-3, unit="rad",
                      formula="over S > 1e-4 S_max")
            overlay(f, 2 * math.pi * C / w[m] * 1e9, e, a, label=label_wl(lam), xlabel="wavelength (nm)",
                    ylabel="φ − linear fit (rad)", analytic_label="analytic" if lam == CARRIERS_M[0] else None)


def bandwidth(fam: Family):
    """No silent narrowing: a linear element keeps |A(ω)|² (energy and rms width) at 1 ps and 10 fs."""
    for lam in CARRIERS_M:
        for tau in (1e-12, 10e-15):
            r = run([b.bulk("m", "CaF2", 2e-3), b.gap("g", 0.1)], lam, tau=tau)
            wi, si = r["input_omega_rad_s"], r["input_spectrum"]
            wo, so = r["output_omega_rad_s"], r["output_spectrum"]
            fam.check(f"rms spectral width out/in {label_wl(lam)} {label_dur(tau)}", rms_width(wo, so) / rms_width(wi, si), 1.0,
                      rel=1e-9, formula="linear: |A(ω)|² unchanged")
            fam.check(f"energy out/in {label_wl(lam)} {label_dur(tau)}", r.summary["totals"]["energyTransmission"], 1.0, rel=1e-12)


def plates(fam: Family):
    nfs = A.GLASSES["FusedSilica"]
    d = 5e-3
    angles = np.array([0, 20, 40, 50, 55.4, 60, 70])
    with fam.figure("plate-fresnel", "Uncoated fused-silica plate: power transmission of p (x) and s (y) against Fresnel",
                    "T = (1 − |r|²)², r_s, r_p from n_glass / n_air (two faces, no multiple reflections)") as f:
        for pol, key in (("p", "x"), ("s", "y")):
            eng, ana = [], []
            for aoi in angles:
                s = b.system("plate", [b.plate("p", "FusedSilica", d, aoi_deg=float(aoi))])
                pulse = b.pulse(1030e-9, 100e-15, energy=1e-9)
                if key == "y":
                    pulse["polarization"] = {"angleDeg": 90}
                r = uf.run(s, b.run_spec(pulse, physics={"nonlinear": False}))
                w0 = r.summary["grid"]["omega0"]
                lam = 2 * math.pi * C / w0
                rs, rp = A.fresnel_rs_rp(1.0, float(nfs(lam) / A.ciddor_air(lam)), math.radians(aoi))
                R = rp ** 2 if key == "x" else rs ** 2
                eng.append(r.summary["totals"]["energyTransmission"])
                ana.append((1 - R) ** 2)
            for aoi, e_, a_ in zip(angles, eng, ana):
                fam.check(f"plate T_{pol} {aoi:g} deg", e_, a_, abs=2e-5)
            overlay(f, angles, eng, ana, label=f"{pol} ({key})", xlabel="angle of incidence (deg)", ylabel="power transmission",
                    marker="o", analytic_label="Fresnel" if key == "x" else None)

    with fam.figure("plate-tilt-gdd", "Tilted fused-silica plate, 5 mm, 1030 nm: GDD against the plane-wave phase",
                    "φ(ω) = (ω/c) [d √(n² − n_o² sin²θ) + n_o sinθ d tanθ_t0] (k_z d plus the air path to the plane normal to the beam); with and without an inputBeam") as f:
        lam = 1030e-9
        for beam, tag in ((None, "untracked"), ({"kind": "gaussian", "w0M": 1e-3}, "tracked")):
            eng, ana = [], []
            for aoi in angles:
                r = run([b.plate("p", "FusedSilica", d, aoi_deg=float(aoi))], lam, beam=beam)
                w0 = r.summary["grid"]["reportOmega0"]
                _, gdd, _ = A.derivatives(A.tilted_plate_phase(nfs, d, math.radians(aoi), A.ciddor_air, w0), w0)
                eng.append(r.summary["totals"]["systemGddS2"] / FS2)
                ana.append(gdd / FS2)
            for aoi, e_, a_ in zip(angles, eng, ana):
                fam.check(f"plate GDD {tag} {aoi:g} deg", e_ * FS2, a_ * FS2, rel=1e-5, unit="s²")
            overlay(f, angles, eng, ana, label=tag, xlabel="angle of incidence (deg)", ylabel="GDD (fs²)", marker="o",
                    analytic_label="plane-wave k_z" if tag == "untracked" else None)


def mirrors(fam: Family):
    lam = 1030e-9
    w_c = 2 * math.pi * C / lam
    cases = [
        ("mirror, GDD coating", [b.mirror("m", coating=b.coating_gdd(0.99, -100e-30, 50e-45))], -100e-30, 50e-45, 0.99),
        ("chirped mirror x4", [b.chirped_mirror("cm", -50e-30, bounces=4, reflectance=0.99, tod_s3=-30e-45)], -200e-30, -120e-45, 0.99 ** 4),
        ("dispersion element", [b.dispersion("d", 1000e-30, 2000e-45)], 1000e-30, 2000e-45, 1.0),
    ]
    for name, els, gdd, tod, T in cases:
        for tau in (1e-12, 30e-15):
            r = run(els, lam, tau=tau)
            w0 = r.summary["grid"]["omega0"]
            w, s, ph = field_phase(r)
            c = poly_phase(w, ph, s, w0, 4)
            # the field is read at the grid centre ω0 (snapped to the frequency step); the summary reports at the requested carrier
            gdd_at = gdd + tod * (w0 - w_c)
            fam.check(f"{name} GDD (field, {label_dur(tau)})", c[2], gdd_at, rel=1e-4, abs=1e-33, unit="s²", formula="GDD + TOD (ω0 − ω_c)")
            fam.check(f"{name} TOD (field, {label_dur(tau)})", c[3], tod, rel=1e-3, abs=1e-46, unit="s³")
            fam.check(f"{name} GDD (summary, {label_dur(tau)})", r.summary["totals"]["systemGddS2"], gdd, rel=1e-9, abs=1e-36, unit="s²", formula="reported at the requested carrier")
            fam.check(f"{name} transmission ({label_dur(tau)})", r.summary["totals"]["energyTransmission"], T, rel=1e-12)


def leak_mirror(fam: Family):
    T, gR, gT = 0.05, -200.0, 300.0
    with fam.figure("leak-mirror", "leakMirror: GDD of the reflected (out) and leaked (leak) ports, Sequential and Branched",
                    "out: R = 1 − T, GDD = gddFs2;  leak: T, GDD = leakGddFs2") as f:
        xs, eng, ana = [], [], []
        for i, (topo, port) in enumerate([("Sequential", "out"), ("Sequential", "leak"), ("Branched", "out"), ("Branched", "leak")]):
            el = b.leak_mirror("lm", T, gdd_fs2=gR, leak_gdd_fs2=gT, output=port if topo == "Sequential" else None)
            if topo == "Branched":
                other = "leak" if port == "out" else "out"
                els = [b.link(el, {port: "end", other: "virtual"})]
            else:
                els = [el]
            r = run(els, 1030e-9, tau=30e-15, branched=topo == "Branched")
            w0 = r.summary["grid"]["omega0"]
            w, s, ph = field_phase(r)
            c = poly_phase(w, ph, s, w0, 3)
            want = (gR if port == "out" else gT) * FS2
            fam.check(f"leakMirror {topo} {port} GDD (field)", c[2], want, rel=1e-6, unit="s²")
            fam.check(f"leakMirror {topo} {port} GDD (summary)", r.summary["totals"]["systemGddS2"], want, rel=1e-6, unit="s²")
            fam.check(f"leakMirror {topo} {port} energy", r.summary["output"]["energyJ"], 1e-9 * ((1 - T) if port == "out" else T), rel=1e-12, unit="J")
            xs.append(i)
            eng.append(r.summary["totals"]["systemGddS2"] / FS2)
            ana.append(want / FS2)
        overlay(f, xs, eng, ana, label="reported GDD", xlabel="case: 0 Seq out, 1 Seq leak, 2 Br out, 3 Br leak",
                ylabel="GDD (fs²)", marker="o", style="", analytic_label="gddFs2 / leakGddFs2")


def grating_pair(fam: Family):
    # groove densities with a propagating first order at each carrier, AOI 50 deg, G = 0.1 m
    cases = {515e-9: 1800.0, 1030e-9: 1500.0, 2e-6: 600.0}
    with fam.figure("grating-pair", "Grating pair (G = 0.1 m, AOI 50°, order −1): GDD and TOD against Treacy",
                    "GDD = −passes m² λ³ G / (2π c² d² cos³θ_d), sin θ_d = |m| λ/d − sin θ_i; TOD = dGDD/dω") as f:
        for passes in (1, 2):
            xs, eng, ana = [], [], []
            for lam, lpm in cases.items():
                r = run([b.grating_pair("gp", lpm, 50, 0.1, passes=passes)], lam)
                w0 = r.summary["grid"]["reportOmega0"]
                gdd, tod = A.treacy(2 * math.pi * C / w0, lpm * 1e3, math.radians(50), 0.1, -1, passes)
                fam.check(f"gratingPair GDD {label_wl(lam)} {passes} pass", r.summary["totals"]["systemGddS2"], gdd, rel=1e-5, unit="s²")
                fam.check(f"gratingPair TOD {label_wl(lam)} {passes} pass", r.summary["totals"]["systemTodS3"], tod, rel=1e-4, unit="s³")
                xs.append(lam * 1e9)
                eng.append(r.summary["totals"]["systemGddS2"] / gdd)
                ana.append(1.0)
            overlay(f, xs, eng, ana, label=f"{passes} pass(es)", xlabel="carrier wavelength (nm)", ylabel="GDD / Treacy",
                    marker="o", style="", analytic_label="Treacy" if passes == 1 else None)
    # off case: no propagating order must be an error, not an empty output
    try:
        run([b.grating_pair("gp", 1500, 50, 0.1)], 2e-6)
        fam.check("gratingPair without a propagating order raises", 0, 1, abs=0.5)
    except uf.UltrafastError:
        fam.check("gratingPair without a propagating order raises", 1, 1, abs=0.5)


def main() -> int:
    fam = Family("linear", "Linear components: bulk, gap, plate, mirrors, dispersion, leakMirror, grating pair")
    for fn in (bulk_taylor, bulk_full_phase, bandwidth, plates, mirrors, leak_mirror, grating_pair):
        fam.guard(fn.__name__, lambda fn=fn: fn(fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
