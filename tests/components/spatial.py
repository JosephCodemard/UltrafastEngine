"""Spatial components alone, each against an analytic or published answer (vacuum gaps, so no chromatic index effects).

Covered: free-space Gaussian beam w(z) and M2 (radial and cartesian grids, 515 nm / 1030 nm / 2 um); a round iris
(Gaussian through a hard circle) against the Fresnel integral over the whole profile; a square flat-top through a square
aperture against the separable Fresnel integral; a thin lens (waist w0 = lam f / (pi w), its position, from a fit of
w^2(z), which is an exact parabola); Hermite- and Laguerre-Gauss second-moment radii w sqrt(2m+1), w sqrt(2p+|l|+1), M2,
and their growth with z; an axicon (Bessel core 2.405 / (k sin a)); a thin Kerr lens (n2 given explicitly) against the
Fresnel integral of the Kerr-phase-modulated Gaussian; Young's double slit (fringe period lam z / d and the Fresnel sum);
resonators (lens guide stability and Kogelnik-Li mode, two-mirror cavity, Fox-Li radius); and a Haus-model soliton
oscillator against the soliton area theorem.

Sources are cw (one spectral bin, so the beam is monochromatic and w(z) is exact) or 1 ps pulses (negligible bandwidth);
a 10 fs pulse would need the intensity-weighted average over frequency, which these checks avoid on purpose.
References: _analytic_spatial.py (independent Fresnel quadratures, ABCD, Kogelnik & Li 1966, Siegman).

Run: python tests/components/spatial.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
"""
from __future__ import annotations

import math
import sys

from _common import CARRIERS_M, Family, b, label_wl, np, overlay, uf
import _analytic_spatial as A

LAM = 1030e-9


def src(model, grid, profile, gid="src"):
    return b.gap(gid, 0.0, medium="Vacuum", transverse=b.transverse(model, **grid, initial_profile=profile))


def gap(gid, z, model, grid):
    return b.gap(gid, z, medium="Vacuum", transverse=b.transverse(model, **grid))


def bp(name, at):
    return b.instrument("beamProfile", at=at, name=name)


def chain_radii(model, grid, profile, zs, source, w_key="radiusXM", elements_between=()):
    """Run src + gaps to each z (instrument after each), return the second-moment radii and m2 at each z."""
    els = [src(model, grid, profile)] + list(elements_between)
    inst, prev = [], 0.0
    for i, z in enumerate(zs):
        els.append(gap(f"g{i}", z - prev, model, grid))
        inst.append(bp(f"p{i}", f"g{i}"))
        prev = z
    res = uf.run(b.system("chain", els), b.run_spec(source, instruments=inst))
    return [res.instrument(f"p{i}") for i in range(len(zs))]


# --------------------------------------------------------------------------------------------------------- Gaussian w(z)
def gaussian_wz(fam: Family):
    w0 = 0.4e-3
    with fam.figure("gaussian-wz", "Free-space Gaussian beam, w0 = 0.4 mm: w(z) / w0 against z / zR, three carriers, radial and cartesian",
                    "w(z) = w0 sqrt(1 + (z / zR)^2), zR = pi w0^2 / lam;  M2 = 1") as f:
        for model in ("radial", "cartesian"):
            for lam in CARRIERS_M:
                zr = math.pi * w0 ** 2 / lam
                zs = np.array([0.25, 0.5, 1.0, 1.5, 2.0]) * zr
                wmax = A.gauss_w(w0, zs[-1], lam)
                if model == "radial":
                    grid = dict(points=160, radius_m=4.5 * float(wmax), spectral_samples=1)
                else:
                    grid = dict(nx=96, ny=1, half_width_m=4.5 * float(wmax), spectral_samples=1)
                out = chain_radii(model, grid, b.initial_profile("gaussian", radius_m=w0), zs, b.cw(lam, 1.0))
                eng = np.array([o["radiusXM"] for o in out])
                want = A.gauss_w(w0, zs, lam)
                for z, e, a_, o in zip(zs, eng, want, out):
                    fam.check(f"w(z) {model} {label_wl(lam)} z={z / zr:.2f} zR", e, a_, rel=1e-5, unit="m", formula="w0 sqrt(1+(z/zR)^2)")
                fam.check(f"M2 {model} {label_wl(lam)} z=2 zR", out[-1]["m2"], 1.0, abs=2e-3, formula="M2 = 1")
                overlay(f, zs / zr, eng / w0, want / w0, label=f"{model} {label_wl(lam)}", xlabel="z / zR", ylabel="w / w0",
                        marker="o" if model == "radial" else "s", analytic_label="analytic" if (model, lam) == ("radial", CARRIERS_M[0]) else None)


# --------------------------------------------------------------------------------------------------------- round iris
def round_iris(fam: Family):
    w, a, E = 2e-3, 1e-3, 1e-3
    grid = dict(points=768, radius_m=8e-3, absorbing_boundary=True, spectral_samples=2)
    with fam.figure("round-iris", "Gaussian (w = 2 mm) through a hard round iris (a = 1 mm), fluence at Fresnel number N = a^2/(lam z) = 3, 2, 1",
                    "u(r,z) = (k/iz) e^{ikr^2/2z} int_0^a u0 e^{ik rho^2/2z} J0(k rho r/z) rho drho;  T = 1 - exp(-2a^2/w^2)") as f:
        rho = (np.arange(3000) + 0.5) * a / 3000
        u0 = np.sqrt(2 * E / (math.pi * w * w)) * np.exp(-rho ** 2 / w ** 2)
        for N in (3, 2, 1):
            z = a * a / (LAM * N)
            sys_ = b.system("iris", [src("radial", grid, b.initial_profile("gaussian", radius_m=w)), b.aperture("iris", radius_m=a),
                                     gap("screen", z, "radial", grid)])
            res = uf.run(sys_, b.run_spec(b.pulse(LAM, 1e-12, energy=E), instruments=[bp("scr", "screen")]))
            v = res.instrument("scr")
            r_e, F_e = np.array(v["radius_m"]), np.array(v["fluence_j_m2"])
            m = r_e < 2.5e-3
            F_a = np.abs(A.fresnel_radial(u0.astype(complex), rho, r_e[m], z, LAM)) ** 2
            fam.check(f"iris profile max|dF|/Fpeak N={N}", float(np.max(np.abs(F_e[m] - F_a)) / F_a.max()), 0.0, abs=0.01,
                      formula="whole fluence profile within 1 % of the peak (documented grid accuracy)")
            fam.check(f"iris on-axis fluence N={N}", F_e[0], float(np.interp(r_e[0], r_e[m], F_a)), abs=0.01 * F_a.max(), unit="J/m2",
                      formula="4 F sin^2(pi N / 2) for a plane wave; tolerance 1 % of the profile peak (N = 2 is a near-null)")
            t = next(x for x in res.elements() if x["id"] == "iris")["values"]["transmission"]
            fam.check(f"iris transmission N={N}", t, 1 - math.exp(-2 * a * a / (w * w)), rel=3e-3, formula="1 - exp(-2 a^2 / w^2)")
            overlay(f, r_e[m] * 1e3, F_e[m], F_a, label=f"N={N}", xlabel="r (mm)", ylabel="fluence (J/m2)",
                    marker=None, analytic_label="Fresnel integral" if N == 3 else None)


# --------------------------------------------------------------------------------------------------------- square aperture
def square_aperture(fam: Family):
    h, a, E = 1.5e-3, 1.0e-3, 1e-3
    N = 2
    z = a * a / (LAM * N)
    grid = dict(nx=384, ny=384, half_width_m=6e-3, absorbing_boundary=True, spectral_samples=1)
    with fam.figure("square-aperture", "Square flat-top (half-width 1.5 mm) through a square aperture (a = 1 mm), cut along y = 0 at N = 2",
                    "separable Fresnel integral: F(x,y) = F_in |u_x(x)|^2 |u_y(y)|^2, u(x) = sqrt(k/2 pi i z) int_{-a}^{a} e^{ik(x-x')^2/2z} dx'") as f:
        sys_ = b.system("sq", [src("cartesian", grid, b.initial_profile("tophat", geometry="square", radius_m=h)),
                               b.aperture("ap", half_width_x_m=a, half_width_y_m=a), gap("screen", z, "cartesian", grid)])
        res = uf.run(sys_, b.run_spec(b.cw(LAM, 1.0), instruments=[bp("scr", "screen")]))
        v = res.instrument("scr")
        x, Fx = np.array(v["x_m"]), np.array(v["fluence_x_j_m2"])
        Fin = 1e-9 / (2 * h) ** 2          # cw 1 W for the default 1 ns window
        x0 = (np.arange(3000) + 0.5) * 2 * a / 3000 - a
        m = np.abs(x) < 2.5e-3
        ux = A.fresnel_1d(np.ones(3000, complex), x0, x[m], z, LAM)
        uy0 = A.fresnel_1d(np.ones(3000, complex), x0, np.array([0.0]), z, LAM)[0]
        Fa = Fin * np.abs(ux) ** 2 * abs(uy0) ** 2
        fam.check("square aperture cut max|dF|/Fpeak", float(np.max(np.abs(Fx[m] - Fa)) / Fa.max()), 0.0, abs=0.02,
                  formula="documented accuracy of the area-fraction edge cells: 2 % of the peak")
        t = next(e for e in res.elements() if e["id"] == "ap")["values"]["transmission"]
        # the engine's transmission converges to the geometric value from below by ~ dx / 3a times the edge fluence (docs/reference/transverse.md):
        # 1.6 % at 21 samples across a, so ~1 % at the 32 used here
        fam.check("square aperture transmission", t, (a / h) ** 2, rel=1e-2, formula="(a / h)^2, grid-limited from below (dx / 3a)")
        overlay(f, x[m] * 1e3, Fx[m], Fa, label="y = 0 cut", xlabel="x (mm)", ylabel="fluence (J/m2)", analytic_label="Fresnel integral")


# --------------------------------------------------------------------------------------------------------- lens focus
def lens_focus(fam: Family):
    w_in, fl = 1.0e-3, 0.5
    with fam.figure("lens-focus", "Collimated Gaussian (w = 1 mm) through a thin lens f = 0.5 m: w(z)^2 near the focus, three carriers",
                    "ABCD on q: q' = q/(1 - q/f), then free space; w0' = lam f / (pi w) to O((f/zR)^2); w^2(z) is an exact parabola") as f:
        for lam in CARRIERS_M:
            zs_after = fl + np.linspace(-0.04, 0.04, 9)
            zr_in = math.pi * w_in ** 2 / lam
            q = 1j * zr_in
            q = q / (1 - q / fl)
            w_focus_est = lam * fl / (math.pi * w_in)
            grid = dict(points=512, radius_m=4e-3, spectral_samples=1)
            els = [src("radial", grid, b.initial_profile("gaussian", radius_m=w_in)), b.lens("lens", fl, medium="Vacuum")]
            inst, prev = [], 0.0
            for i, z in enumerate(zs_after):
                els.append(gap(f"g{i}", z - prev, "radial", grid))
                inst.append(bp(f"p{i}", f"g{i}"))
                prev = z
            res = uf.run(b.system("lens", els), b.run_spec(b.cw(lam, 1.0), instruments=inst))
            eng = np.array([res.instrument(f"p{i}")["radiusXM"] for i in range(len(zs_after))])
            want = np.array([A.w_of_q(A.q_after(q, ((1, z), (0, 1))), lam) for z in zs_after])
            fit = np.polyfit(zs_after, eng ** 2, 2)
            zw = -fit[1] / (2 * fit[0])
            w0f = math.sqrt(max(fit[2] - fit[1] ** 2 / (4 * fit[0]), 0.0))
            fita = np.polyfit(zs_after, want ** 2, 2)
            zwa = -fita[1] / (2 * fita[0])
            fam.check(f"focus waist w0' {label_wl(lam)}", w0f, math.sqrt(fita[2] - fita[1] ** 2 / (4 * fita[0])), rel=5e-3, unit="m",
                      formula="ABCD (compare lam f / (pi w) = %.4g m)" % w_focus_est)
            fam.check(f"focus position {label_wl(lam)}", zw, zwa, abs=0.5e-3, unit="m", formula="waist of 1/q' = 1/q - 1/f")
            fam.check(f"focus waist vs lam f/(pi w) / sqrt(1+(f/zR)^2) {label_wl(lam)}", w0f, w_focus_est / math.sqrt(1 + (fl / zr_in) ** 2),
                      rel=1e-3, unit="m", formula="w0' = lam f / (pi w) / sqrt(1 + (f / zR)^2), zR = pi w^2 / lam of the incoming beam")
            overlay(f, (zs_after - fl) * 1e3, eng * 1e6, want * 1e6, label=label_wl(lam), xlabel="z after the focal plane (mm)",
                    ylabel="w (um)", marker="o", analytic_label="ABCD" if lam == CARRIERS_M[0] else None)


# --------------------------------------------------------------------------------------------------------- HG / LG modes
def structured_modes(fam: Family):
    w, lam = 0.5e-3, LAM
    zr = math.pi * w * w / lam
    grid = dict(nx=192, ny=192, half_width_m=6e-3, spectral_samples=1, absorbing_boundary=True)
    cases = [("HG(1,0)", b.initial_profile("hermiteGauss", radius_m=w, mode_x=1, mode_y=0), 3, 1),
             ("HG(2,0)", b.initial_profile("hermiteGauss", radius_m=w, mode_x=2, mode_y=0), 5, 1),
             ("HG(0,3)", b.initial_profile("hermiteGauss", radius_m=w, mode_x=0, mode_y=3), 1, 7),
             ("HG(2,1)", b.initial_profile("hermiteGauss", radius_m=w, mode_x=2, mode_y=1), 5, 3),
             ("LG(p=1,l=0)", b.initial_profile("laguerreGauss", radius_m=w, mode_p=1, mode_l=0), 3, 3),
             ("LG(p=0,l=2)", b.initial_profile("laguerreGauss", radius_m=w, mode_p=0, mode_l=2), 3, 3),
             ("LG(p=1,l=2)", b.initial_profile("laguerreGauss", radius_m=w, mode_p=1, mode_l=2), 5, 5)]
    with fam.figure("hg-lg-sizes", "Hermite- and Laguerre-Gauss modes, w = 0.5 mm, 1030 nm: second-moment radius at z = 0 and z = zR",
                    "HG(m,n): W_x = w sqrt(2m+1), W_y = w sqrt(2n+1), M2 = 2m+1;  LG(p,l): W = w sqrt(2p+|l|+1) = M2 w;  then W(z) = W(0) sqrt(1 + (z/zR)^2)") as f:
        names, eng, want = [], [], []
        for i, (name, prof, mx, my) in enumerate(cases):
            out = chain_radii("cartesian", grid, prof, [1e-9, zr], b.cw(lam, 1.0))
            for o, zfac, tag in ((out[0], 1.0, "z=0"), (out[1], math.sqrt(2.0), "z=zR")):
                fam.check(f"{name} Wx {tag}", o["radiusXM"], w * math.sqrt(mx) * zfac, rel=0.01, unit="m", formula="w sqrt(2m+1) sqrt(1+(z/zR)^2)")
                fam.check(f"{name} Wy {tag}", o["radiusYM"], w * math.sqrt(my) * zfac, rel=0.01, unit="m")
                if tag == "z=0":
                    fam.check(f"{name} M2x", o["m2"], mx, rel=0.01, formula="2m+1 (HG) / 2p+|l|+1 (LG)")
                    fam.check(f"{name} M2y", o["m2Y"], my, rel=0.01)
                    names.append(name)
                    eng.append(o["radiusXM"] / w)
                    want.append(math.sqrt(mx))
                else:
                    fam.check(f"{name} M2x invariant at z=zR", o["m2"], mx, rel=0.01, formula="M2 does not change in free space")
        overlay(f, np.arange(len(names)), eng, want, label="Wx(z=0) / w", xlabel="mode (" + ", ".join(f"{i}:{n}" for i, n in enumerate(names)) + ")",
                ylabel="W_x / w", marker="o", style="", analytic_label="sqrt(2m+1)")


# --------------------------------------------------------------------------------------------------------- axicon
def axicon(fam: Family):
    w = 1e-3
    with fam.figure("axicon", "Axicon on a Gaussian beam (w = 1 mm, cone angle 0.5 deg), half way along the Bessel zone: core against the Fresnel integral",
                    "mask exp(-i k r sin a) on exp(-r^2/w^2), Fresnel integral at z = w / (2 tan a); core zeros near 2.405 / (k sin a), 5.520 / (k sin a)") as f:
        ang = math.radians(0.5)
        for lam, npts in ((1030e-9, 640), (2e-6, 512), (515e-9, 900)):
            k = 2 * math.pi / lam
            zmax = w / math.tan(ang)
            zz = 0.5 * zmax
            grid = dict(points=npts, radius_m=4e-3, spectral_samples=1, absorbing_boundary=True)
            sys_ = b.system("ax", [src("radial", grid, b.initial_profile("gaussian", radius_m=w)), b.mask("ax", "axicon", cone_angle_rad=ang),
                                   gap("scr", zz, "radial", grid)])
            res = uf.run(sys_, b.run_spec(b.cw(lam, 1.0), instruments=[bp("scr", "scr")]))
            v = res.instrument("scr")
            r, F = np.array(v["radius_m"]), np.array(v["fluence_j_m2"])
            ks = k * math.sin(ang)
            rmax = 14 / ks
            sel = r < rmax
            # reference: Fresnel integral of the masked Gaussian (amplitude from the engine's input energy 1 W x 1 ns)
            rho = (np.arange(60000) + 0.5) * (4.0 * w) / 60000
            u0 = np.sqrt(2e-9 / (math.pi * w * w)) * np.exp(-rho ** 2 / w ** 2) * np.exp(-1j * ks * rho)
            rf = np.linspace(0, rmax, 700)
            Fa = np.abs(A.fresnel_radial(u0, rho, rf, zz, lam)) ** 2
            Fa_at = np.interp(r[sel], rf, Fa)
            fam.check(f"axicon profile max|dF|/Fpeak {label_wl(lam)}", float(np.max(np.abs(F[sel] - Fa_at)) / Fa.max()), 0.0, abs=1e-3,
                      formula="Fresnel integral of the axicon-masked Gaussian, r < 14 / (k sin a)")
            # zeros of the reference (fine grid): the Bessel core, J0 zeros / (k sin a) up to the finite-aperture shift
            mins = [i for i in range(1, len(rf) - 1) if Fa[i] < Fa[i - 1] and Fa[i] <= Fa[i + 1]][:2]
            for j, (i, ref) in enumerate(zip(mins, (2.404826, 5.520078))):
                c = np.polyfit(rf[i - 3:i + 4], Fa[i - 3:i + 4], 2)
                zer = -c[1] / (2 * c[0])
                fam.check(f"axicon zero #{j + 1} (Fresnel integral) {label_wl(lam)}", zer, ref / ks, rel=0.02, unit="m",
                          formula=f"{ref:.4f} / (k sin a); 2 % = finite Gaussian envelope and cone-angle curvature (documented)")
            # the engine's first zero from its own samples: parabola through the three lowest-curvature points around the minimum
            i0 = int(np.argmin(F[:np.searchsorted(r, 3.2 / ks)]))
            cc = np.polyfit(r[i0 - 1:i0 + 2], F[i0 - 1:i0 + 2], 2)
            zer_e = -cc[1] / (2 * cc[0])
            zer_ref = rf[mins[0]] if mins else float("nan")
            c = np.polyfit(rf[mins[0] - 3:mins[0] + 4], Fa[mins[0] - 3:mins[0] + 4], 2)
            fam.check(f"axicon first zero, engine vs Fresnel integral {label_wl(lam)}", zer_e, -c[1] / (2 * c[0]), rel=0.03, unit="m",
                      formula="3 % = parabola through 3 radial samples (spacing R / N)")
            overlay(f, ks * r[sel], F[sel] / Fa.max(), Fa_at / Fa.max(), label=label_wl(lam), xlabel="k sin(a) r", ylabel="F / F_peak(reference)",
                    analytic_label="Fresnel integral" if lam == 1030e-9 else None)


# --------------------------------------------------------------------------------------------------------- Kerr lens
def kerr_lens(fam: Family):
    w, lam, L, n2 = 0.5e-3, LAM, 1e-3, 1e-19
    k = 2 * math.pi / lam
    z = 1.0
    with fam.figure("kerr-lens", "Thin Kerr plate (n2 = 1e-19 m2/W, 1 mm), cw Gaussian w = 0.5 mm: fluence 1 m behind it for B = 0 ... 1.5 rad",
                    "phase B exp(-2r^2/w^2), B = k n2 I0 L, I0 = 2P / (pi w^2); then the Fresnel integral of the modulated Gaussian") as f:
        rho = (np.arange(6000) + 0.5) * (5 * w) / 6000
        for B_target in (0.5, 1.0, 1.5):
            I0 = B_target / (k * n2 * L)
            P = I0 * math.pi * w * w / 2
            grid = dict(points=256, radius_m=5e-3, spectral_samples=1, absorbing_boundary=True)
            mats = {"Kerr": b.material(index=1.0, n2=n2)}
            sys_ = b.system("kerr", [src("radial", grid, b.initial_profile("gaussian", radius_m=w)),
                                     b.bulk("plate", "Kerr", L, transverse=b.transverse("radial", **grid)), gap("scr", z, "radial", grid)], materials=mats)
            res = uf.run(sys_, b.run_spec(b.cw(lam, P), instruments=[bp("scr", "scr")]))
            v = res.instrument("scr")
            r_e, F_e = np.array(v["radius_m"]), np.array(v["fluence_j_m2"])
            plate = next(x for x in res.elements() if x["id"] == "plate")
            u0 = np.sqrt(I0 * 1e-9) * np.exp(-rho ** 2 / w ** 2) * np.exp(1j * B_target * np.exp(-2 * rho ** 2 / w ** 2))
            m = r_e < 3e-3
            F_a = np.abs(A.fresnel_radial(u0, rho, r_e[m], z, lam)) ** 2
            fam.check(f"Kerr plate B={B_target} fluence profile max|dF|/Fpeak", float(np.max(np.abs(F_e[m] - F_a)) / F_a.max()), 0.0, abs=0.01,
                      formula="Fresnel integral of the Kerr-modulated Gaussian")
            fam.check(f"Kerr plate B={B_target} on-axis fluence", F_e[0], float(np.interp(r_e[0], r_e[m], F_a)), rel=0.01, unit="J/m2")
            fam.check(f"Kerr plate B={B_target} B integral reported", plate["bIntegralRad"], B_target, rel=0.02, unit="rad",
                      formula="B = k n2 I0 L (peak)")
            overlay(f, r_e[m] * 1e3, F_e[m], F_a, label=f"B = {B_target}", xlabel="r (mm)", ylabel="fluence (J/m2)",
                    analytic_label="Fresnel integral" if B_target == 0.5 else None)


# --------------------------------------------------------------------------------------------------------- Young
def young(fam: Family):
    w, slit, sep, z = 3e-3, 0.2e-3, 1e-3, 1.0
    with fam.figure("young", "Young's double slit (slit 0.2 mm, separation 1 mm, Gaussian w = 3 mm), screen at 1 m: pattern at 515 nm and 1030 nm",
                    "fringe period lam z / d (exact: the cross-term phase k x d / z is linear in x); pattern = |Fresnel sum over both slits|^2") as f:
        for lam in (515e-9, 1030e-9):
            k = 2 * math.pi / lam
            grid = dict(nx=4096, ny=1, half_width_m=16e-3, spectral_samples=1, absorbing_boundary=True)
            sys_ = b.system("slits", [src("cartesian", grid, b.initial_profile("gaussian", radius_m=w)),
                                      b.mask("s", "slits", count=2, width_m=slit, separation_m=sep), gap("screen", z, "cartesian", grid)])
            p = uf.run(sys_, b.run_spec(b.cw(lam, 1.0), instruments=[bp("p", "screen")])).instrument("p")
            x, Fx = np.array(p["x_m"]), np.array(p["fluence_x_j_m2"])
            keep = np.abs(x) < 3e-3
            src_x = np.linspace(-1.5e-3, 1.5e-3, 6001)
            field = np.exp(-src_x ** 2 / w ** 2) * ((np.abs(src_x - sep / 2) < slit / 2) | (np.abs(src_x + sep / 2) < slit / 2))
            ex = np.abs(A.fresnel_1d(field.astype(complex), src_x, x[keep], z, lam)) ** 2
            tv = 0.5 * float(np.abs(Fx[keep] / Fx[keep].sum() - ex / ex.sum()).sum())
            fam.check(f"Young pattern total-variation distance {label_wl(lam)}", tv, 0.0, abs=0.005, formula="0.5 sum |F/sum F - ref/sum ref|")
            # fringe period from the maxima of the central lobe (parabola through 7 samples; a flat top gives two equal neighbours, merged)
            def period(xs_, ys_):
                pk = [i for i in range(3, len(xs_) - 3) if ys_[i] > ys_[i - 1] and ys_[i] >= ys_[i + 1] and ys_[i] > 0.2 * ys_.max()]
                pos = []
                for i in pk:
                    sl = slice(i - 3, i + 4)
                    c = np.polyfit(xs_[sl], ys_[sl], 2)
                    xp = -c[1] / (2 * c[0])
                    if pos and abs(xp - pos[-1]) < 0.25 * lam * z / sep:
                        pos[-1] = 0.5 * (pos[-1] + xp)
                    else:
                        pos.append(xp)
                return float(np.mean(np.diff(pos)))
            xs, ys = x[keep], Fx[keep]
            xf = np.linspace(-3e-3, 3e-3, 6001)
            ef = np.abs(A.fresnel_1d(field.astype(complex), src_x, xf, z, lam)) ** 2
            fam.check(f"Young fringe period, engine vs Fresnel sum {label_wl(lam)}", period(xs, ys), period(xf, ef), rel=3e-3, unit="m",
                      formula="same maxima fit on both patterns")
            # the maxima sit at lam z / d only up to the slope of the single-slit and Gaussian envelope (a few %)
            fam.check(f"Young fringe period vs lam z / d {label_wl(lam)}", period(xf, ef), lam * z / sep, rel=0.02, unit="m",
                      formula="lam z / d; 2 % covers the single-slit envelope slope, which pulls the maxima in by ~1.3 % (analytic estimate)")
            overlay(f, xs * 1e3, ys / ys.max(), ex / ex.max(), label=label_wl(lam), xlabel="x (mm)", ylabel="fluence (normalised)",
                    analytic_label="Fresnel sum" if lam == 515e-9 else None)


# --------------------------------------------------------------------------------------------------------- resonators
def resonators(fam: Family):
    d_fix, f_lens = 0.6, 0.5
    with fam.figure("resonator-lens-guide", "Lens-guide resonator (gap d + lens f = 0.5 m, ring): eigenmode radius at the start of the gap against d, 1030 nm",
                    "m = (A+D)/2 = 1 - d/(2f);  w^2 = lam |B| / (pi sqrt(1 - m^2)), B = d  (Kogelnik & Li 1966)") as f:
        ds = np.array([0.2, 0.4, 0.6, 0.8, 1.0, 1.4, 1.8])
        eng, want = [], []
        for d in ds:
            res = uf.run(b.system("cav", [b.resonator("c", [b.gap("g", float(d), medium="Vacuum"), b.lens("l", f_lens, medium="Vacuum")])]),
                         b.run_spec(b.cw(LAM, 1.0)))
            v = res.elements()[0]["values"]
            w_a, m_a = A.ring_mode_from_matrix(1, d, -1 / f_lens, 1 - d / f_lens, LAM)
            fam.check(f"lens guide stability d={d:g} m", v["stabilityX"], m_a, abs=1e-9, formula="(A + D)/2 = 1 - d/(2f)... (A=1, D=1-d/f)")
            fam.check(f"lens guide mode radius d={d:g} m", v["modeRadiusXM"], w_a, rel=1e-6, unit="m", formula="sqrt(lam B / (pi sqrt(1 - m^2)))")
            fam.check(f"lens guide curvature d={d:g} m", v["modeCurvatureXPerM"], (1 - d / f_lens - 1) / (2 * d), rel=1e-6, abs=1e-9, unit="1/m",
                      formula="Re(1/q) = (D - A) / (2B)")
            eng.append(v["modeRadiusXM"])
            want.append(w_a)
        overlay(f, ds, np.array(eng) * 1e3, np.array(want) * 1e3, label="resonator element", xlabel="gap d (m)", ylabel="mode radius (mm)",
                marker="o", analytic_label="Kogelnik-Li")
        for lam in CARRIERS_M:
            res = uf.run(b.system("cav", [b.resonator("c", [b.gap("g", d_fix, medium="Vacuum"), b.lens("l", f_lens, medium="Vacuum")])]),
                         b.run_spec(b.cw(lam, 1.0)))
            fam.check(f"lens guide mode radius {label_wl(lam)}", res.elements()[0]["values"]["modeRadiusXM"],
                      A.ring_mode_from_matrix(1, d_fix, -1 / f_lens, 1 - d_fix / f_lens, lam)[0], rel=1e-6, unit="m", formula="w ~ sqrt(lam)")
    R1, R2, L = 1.5, 2.0, 1.0
    w1a, w2a = A.linear_resonator_mode(R1, R2, L, LAM)
    for name, order, wa, Rm in (("mirror 1", (R1, R2), w1a, R1), ("mirror 2", (R2, R1), w2a, R2)):
        res = uf.run(b.system("cav", [b.resonator("c", [b.lens("m1", order[0] / 2, medium="Vacuum"), b.gap("g", L, medium="Vacuum"),
                                                         b.lens("m2", order[1] / 2, medium="Vacuum")], geometry="linear")]), b.run_spec(b.cw(LAM, 1.0)))
        v = res.elements()[0]["values"]
        fam.check(f"two-mirror cavity spot on {name}", v["modeRadiusXM"], wa, rel=1e-6, unit="m",
                  formula="w1^2 = (lam L / pi) sqrt(g2 / (g1 (1 - g1 g2)))  (Kogelnik & Li 1966)")
        fam.check(f"two-mirror cavity stability", v["stabilityX"], 2 * (1 - L / R1) * (1 - L / R2) - 1, abs=1e-9, formula="2 g1 g2 - 1")
        fam.check(f"two-mirror cavity wavefront matches the mirror, {name}", v["modeCurvatureXPerM"], 1 / Rm, rel=1e-6, unit="1/m",
                  formula="R(mirror) = R of the mode on the mirror")
    # Fox-Li iteration on a radial grid against the ABCD eigenmode (documented agreement: 3 %)
    grid = dict(points=128, radius_m=3.5e-3, spectral_samples=1)
    res = uf.run(b.system("cav", [b.resonator("c", [b.gap("g", d_fix, medium="Vacuum"), b.lens("l", f_lens, medium="Vacuum")],
                                                  transverse=b.transverse("radial", **grid))]), b.run_spec(b.cw(LAM, 1.0)))
    v = res.elements()[0]["values"]
    w_a = A.ring_mode_from_matrix(1, d_fix, -1 / f_lens, 1 - d_fix / f_lens, LAM)[0]
    fam.check("Fox-Li radius vs ABCD eigenmode", v["foxLiRadiusXM"], w_a, rel=0.03, unit="m",
              formula="Fox & Li 1961 iteration vs Kogelnik-Li; 3 % = the documented accuracy (the loss-free iteration breathes about the mode)")
    fam.check("Fox-Li round-trip loss of a loss-free lens guide", v["roundTripLoss"], 0.0, abs=1e-6, formula="no aperture: unitary round trip")


# --------------------------------------------------------------------------------------------------------- soliton oscillator
def soliton_oscillator(fam: Family):
    from ultrafast.laser import Oscillator  # noqa: F401  (the area theorem is also computed here independently)
    # the lumped soliton needs a small nonlinear phase per round trip (the sampled soliton of Kelly sidebands is the other regime):
    # cases are kept at B per round trip below ~0.35 rad; at B = 0.85 rad (GDD -300 fs2, or SPM 2.5e-6 /W) the pulse is 12-24 % longer
    cases = [("GDD -500 fs2, SPM 1.5e-6", -500, 1.5e-6), ("GDD -800 fs2, SPM 1.5e-6", -800, 1.5e-6), ("GDD -500 fs2, SPM 1.0e-6", -500, 1.0e-6),
             ("GDD -500 fs2, SPM 1.2e-6", -500, 1.2e-6), ("GDD -900 fs2, SPM 1.8e-6", -900, 1.8e-6), ("GDD -400 fs2, SPM 1.0e-6", -400, 1.0e-6)]
    with fam.figure("soliton-area-theorem", "Haus-model mode-locked oscillator (sech2 seed, 800 nm, 200 nm gain, Kerr-lens absorber): FWHM against the soliton area theorem",
                    "FWHM = 1.763 T0, T0 = 2 |D| / (delta E_intracavity)  ->  3.526 |D| / (delta E)  (Haus 1975; Kaertner)") as f:
        eng, want = [], []
        for name, gdd, spm in cases:
            osc = {"id": "laser", "type": "oscillator", "model": "haus", "repRateHz": 80e6, "outputCoupler": 0.05,
                   "gain": {"smallSignalGain": 1.5, "saturationEnergyJ": 5e-9, "bandwidthM": 200e-9, "shape": "gaussian"},
                   "lossPerRoundTrip": 0.02, "gddFs2": gdd, "spmRadPerW": spm,
                   "absorber": {"kind": "kerrLens", "modulationDepth": 0.03, "kappaPerW": 3e-8}, "start": "pulse", "roundTrips": {"max": 8000}}
            res = uf.run(b.system("osc", [osc]), b.run_spec(b.pulse(800e-9, 60e-15, energy=1e-9, shape="sech2"),
                                                            grid={"auto": False, "n": 4096, "windowS": 8e-12}))
            v = res.elements()[0]["values"]
            tau_a = 3.526 * abs(gdd) * 1e-30 / (spm * v["intracavityEnergyJ"])
            fam.check(f"oscillator B per round trip below 0.35 rad ({name})", min(v["bIntegralPerRoundTripRad"], 0.35), v["bIntegralPerRoundTripRad"], abs=0,
                      unit="rad", formula="regime of the lumped soliton (precondition of the area theorem)")
            fam.check(f"oscillator converged ({name})", v["converged"], 1, abs=0)
            fam.check(f"oscillator single pulse ({name})", v["pulsesInCavity"], 1, abs=0)
            # LaserTests documents 10 %: the lumped soliton is perturbed by the absorber, the gain filter and the 5 % output coupler; the
            # deviation grows with B per round trip (0.3 % at B = 0.13 rad, 4 % at 0.30 rad), so 6 % at B < 0.35 rad
            fam.check(f"oscillator FWHM vs soliton area theorem ({name})", v["outputFwhmS"], tau_a, rel=0.06, unit="s",
                      formula="3.526 |D| / (delta E_intracavity); the perturbation grows with B per round trip")
            fam.check(f"oscillator time-bandwidth product ({name})", v["timeBandwidthProduct"], 0.315, rel=0.15,
                      formula="sech2: 0.315 (15 %: the same perturbations)")
            fam.check(f"oscillator output = T_oc x intracavity ({name})", v["outputEnergyJ"], 0.05 * v["intracavityEnergyJ"], rel=1e-6, unit="J",
                      formula="output coupler transmission 5 %")
            eng.append(v["outputFwhmS"] * 1e15)
            want.append(tau_a * 1e15)
        overlay(f, np.arange(len(cases)), eng, want, label="steady-state FWHM", xlabel="case (" + "; ".join(f"{i}: {c[0]}" for i, c in enumerate(cases)) + ")",
                ylabel="FWHM (fs)", marker="o", style="", analytic_label="area theorem")


FUNCS = [gaussian_wz, round_iris, square_aperture, lens_focus, structured_modes, axicon, kerr_lens, young, resonators, soliton_oscillator]


def main() -> int:
    fam = Family("spatial", "Spatial components: diffraction, lens, structured beams, axicon, Kerr lens, Young slits, resonators, soliton oscillator")
    for fn in FUNCS:
        fam.guard(fn.__name__, lambda fn=fn: fn(fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
