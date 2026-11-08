"""Miscellaneous optics alone, each against an analytic or published answer: lens (tracked-beam ABCD, focus, thick-lens GDD),
curved and off-axis mirrors (f_t = R cos(a)/2, f_s = R/(2 cos(a))) with a lens in front, prism (Brewster-cut transmission,
angular dispersion by Snell's law, prism-pair GDD against Fork / Martinez), single grating (diffraction angle through the
magnification, Littrow, efficiency, angular dispersion, pulse-front tilt, a pair of single gratings against Treacy),
ndFilter (10^-OD, OD(lambda), ghost), interface and coatings (Fresnel, Brewster, total internal reflection, quarter-wave AR,
Bragg mirror against the transfer-matrix method), delay, rotator, faradayRotator (dispersion, non-reciprocal double pass),
thinFilmPolarizer (extinction, Malus, energy conservation), saturableAbsorber (slow, fast, kerrLens against the ODE on the
engine's own input power), filter (Gaussian / super-Gaussian / table), mask (zone plate), wavefront (Zernike rms, Strehl,
defocus as a lens), plate and bulk ghosts (delay and level), apertures (rectangular, decentred, astigmatic beam).

Carriers 515 nm, 1030 nm, 2 um where the element has a wavelength dependence; pulses of 1 ps (narrow band: the carrier
value) and 10 fs / 100 fs (spectrum-weighted references); Sequential = Branched for a chain of these elements.

Conventions that matter (docs/concepts.md, docs/component-tests.md): vacuum gaps and `medium="Vacuum"` lenses and mirrors
where reduced and real q coincide; the engine snaps the carrier to the frequency grid (summary grid.omega0), reports GDD / TOD
at the requested carrier (grid.reportOmega0); references that depend on the wavelength use the requested carrier unless the
check says otherwise; pulse-averaged quantities are averaged over the engine's own input spectrum.

Run: python tests/components/optics_misc.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
OM_ONLY=name1,name2 runs only those functions (development).
Main pieces: one guarded function per element family (lens_abcd, thick_lens, mirror_abcd, prism_*, grating_*, nd_filter, ...);
each adds checks to the Family and draws one figure. Known engine bugs carry `known=` and a repro under tests/components/repro/.
"""
from __future__ import annotations

import cmath
import math
import os
import sys

from _common import (C, CARRIERS_M, FS2, Family, b, fwhm, label_dur, label_wl, np, overlay, poly_phase, uf)
import _analytic as A

VAC = "Vacuum"
W0 = 1e-3


# ------------------------------------------------------------------------------------------------------------- helpers
def run(elements, lam=1030e-9, tau=100e-15, beam=None, pulse=None, energy=1e-9, branched=False, grid=None, nonlinear=False, **sysk):
    s = b.system("component", b.Branched(elements) if branched else elements, **sysk)
    spec = b.run_spec(pulse or b.pulse(lam, tau, energy=energy), grid=grid, physics={"nonlinear": nonlinear})
    if beam:
        spec["inputBeam"] = beam
    return uf.run(s, spec)


def gbeam(w0=W0, z=0.0, m2=None):
    d = {"kind": "gaussian", "w0M": w0, "zWaistM": z}
    if m2:
        d["m2"] = m2
    return d


def el(res, i):
    return res.summary["elements"][i]


def val(res, i, key):
    return res.summary["elements"][i]["values"][key]


def omega0(res):
    return res.summary["grid"]["omega0"]


def lam_snapped(res):
    return 2 * math.pi * C / omega0(res)


def pol_pulse(lam, angle_deg, tau=100e-15, energy=1e-9):
    p = b.pulse(lam, tau, energy=energy)
    p["polarization"] = {"angleDeg": angle_deg}
    return p


def stokes_sums(res):
    """(Ex, Ey, Re X) of the output field: energy in x, in y, and the x-y cross term, summed over the spectrum."""
    ax = np.asarray(res["output_ax_re"]) + 1j * np.asarray(res["output_ax_im"])
    ay = np.asarray(res["output_ay_re"]) + 1j * np.asarray(res["output_ay_im"])
    return float((abs(ax) ** 2).sum()), float((abs(ay) ** 2).sum()), float((ax * np.conj(ay)).real.sum())


def in_spectrum(res):
    return np.asarray(res["input_omega_rad_s"]), np.asarray(res["input_spectrum"])


def wavg(res, f):
    """Spectrum-weighted average of f(lambda_m) over the engine's input spectrum."""
    w, s = in_spectrum(res)
    m = s > 1e-9 * s.max()
    lam = 2 * math.pi * C / w[m]
    return float((s[m] * f(lam)).sum() / s[m].sum())


def n_rel(glass, lam):
    """Index of a catalogue glass relative to air (what the catalogue and Snell's law in air see)."""
    return float(A.GLASSES[glass](lam) / A.ciddor_air(lam))


def reflectance(n1, n2, th, pol):
    rs, rp = A.fresnel_rs_rp(n1, n2, th)
    return rp ** 2 if pol == "p" else rs ** 2


def q_after(q, M):
    (a, b_), (c, d) = M
    return (a * q + b_) / (c * q + d)


def w_of_q(q, lam, m2=1.0):
    return math.sqrt(-lam * m2 / (math.pi * (1 / q).imag))


def gapM(z):
    return ((1.0, z), (0.0, 1.0))


def lensM(f):
    return ((1.0, 0.0), (-1.0 / f, 1.0))


def waist_from_parabola(z, w):
    """(z_waist, w_waist) from the exact parabola w^2(z) = a z^2 + b z + c of a Gaussian beam."""
    a2, b2, c2 = np.polyfit(z, np.asarray(w) ** 2, 2)
    return -b2 / (2 * a2), math.sqrt(max(c2 - b2 * b2 / (4 * a2), 0.0))


def tmm(lam, th0, n0, layers, ns, pol):
    """Reflectance and transmittance of a lossless stack [(n, d)] between index n0 (angle th0) and ns (characteristic-matrix method,
    Macleod). Returns (R, T, r) with r the amplitude reflection coefficient (e^{+i w t} convention)."""
    s0 = n0 * math.sin(th0)

    def eta(n):
        cs = cmath.sqrt(1 - (s0 / n) ** 2)
        return n * cs if pol == "s" else n / cs

    M = np.eye(2, dtype=complex)
    for n, d in layers:
        cs = cmath.sqrt(1 - (s0 / n) ** 2)
        dl = 2 * math.pi * n * d * cs / lam
        e = eta(n)
        M = M @ np.array([[cmath.cos(dl), 1j * cmath.sin(dl) / e], [1j * e * cmath.sin(dl), cmath.cos(dl)]])
    Bv, Cv = M @ np.array([1.0, eta(ns)])
    e0 = eta(n0)
    r = (e0 * Bv - Cv) / (e0 * Bv + Cv)
    R = abs(r) ** 2
    return R, 1 - R, r


def qw_stack(design, lam_d, nH, nL):
    """Layers [(n, d)] of a quarter-wave design string such as '(HL)^8 H' at normal incidence."""
    out = []
    toks = design.replace("(", " ( ").replace(")", " ) ").split()
    i = 0

    def sym(s, k):
        n = nH if s == "H" else nL
        return [(n, lam_d / (4 * n))] * k

    while i < len(toks):
        t = toks[i]
        if t == "(":
            j = toks.index(")", i)
            inner = "".join(toks[i + 1:j])
            reps = int(toks[j + 1].lstrip("^")) if j + 1 < len(toks) and toks[j + 1].startswith("^") else 1
            i = j + 2 if reps != 1 or (j + 1 < len(toks) and toks[j + 1].startswith("^")) else j + 1
            for _ in range(reps):
                for ch in inner:
                    out += sym(ch, 1)
        else:
            out += sym(t, 1)
            i += 1
    return out


CONST_MATS = {k: b.material(index={"kind": "expression", "expr": str(v)}) for k, v in (("HI", 2.3), ("LO", 1.38), ("SUB", 1.5), ("ARL", math.sqrt(1.5)), ("SUB2", 2.25))}


# =================================================================================================================== lens
def lens_abcd(fam: Family):
    """Tracked Gaussian beam through thin lenses: w(z) and the focus against the q-parameter ABCD law."""
    f1 = 0.2
    with fam.figure("lens-abcd", "Thin lens f = 0.2 m on a collimated tracked Gaussian beam (w = 1 mm): w(z) after the lens, three carriers",
                    "q' = q / (1 - q/f), then q + z; w = sqrt(-lam / (pi Im(1/q))); waist from the exact parabola w^2(z)") as f:
        for lam in CARRIERS_M:
            zs = f1 + np.linspace(-0.05, 0.05, 11)
            els, prev = [b.lens("L", f1, medium=VAC)], 0.0
            for i, z in enumerate(zs):
                els.append(b.gap(f"g{i}", z - prev, medium=VAC))
                prev = z
            r = run(els, lam, beam=gbeam(), tau=1e-12)
            eng = np.array([val(r, 1 + i, "trackedBeamRadiusXM") for i in range(len(zs))])
            q = q_after(1j * math.pi * W0 ** 2 / lam, lensM(f1))
            ana = np.array([w_of_q(q_after(q, gapM(z)), lam) for z in zs])
            zw, w0f = waist_from_parabola(zs, eng)
            zr = math.pi * W0 ** 2 / lam
            zw_a, w0_a = f1 / (1 + (f1 / zr) ** 2), W0 / math.sqrt(1 + (zr / f1) ** 2)
            fam.check(f"lens f=0.2 w(z) max rel. error {label_wl(lam)}", float(np.max(np.abs(eng / ana - 1))), 0.0, abs=2e-6,
                      formula="q' = q / (1 - q/f)")
            fam.check(f"lens f=0.2 waist radius {label_wl(lam)}", w0f, w0_a, rel=2e-5, unit="m", formula="w0' = w / sqrt(1 + (zR/f)^2)")
            fam.check(f"lens f=0.2 waist position {label_wl(lam)}", zw, zw_a, abs=2e-5, unit="m", formula="z' = f / (1 + (f/zR)^2)")
            overlay(f, (zs - f1) * 1e3, eng * 1e6, ana * 1e6, label=label_wl(lam), xlabel="z after the focal plane (mm)", ylabel="w (um)",
                    marker="o", analytic_label="ABCD" if lam == CARRIERS_M[0] else None)

    # converging input (waist 0.3 m downstream of a gap), two lenses, M2 = 1.5: general q with curvature
    lam = 1030e-9
    zs = np.linspace(0.05, 0.6, 12)
    with fam.figure("lens-telescope", "Gap, lens f = 0.1 m, 0.4 m of vacuum, lens f = 0.3 m, then w(z): M2 = 1.5, input waist 0.3 m downstream of the first lens",
                    "q-parameter law with M2 (lam_eff = M2 lam); waist w0 = 0.5 mm at z = 0.3 m") as f:
        m2 = 1.5
        els = [b.gap("g0", 0.1, medium=VAC), b.lens("L1", 0.1, medium=VAC), b.gap("g1", 0.4, medium=VAC), b.lens("L2", 0.3, medium=VAC)]
        prev = 0.0
        for i, z in enumerate(zs):
            els.append(b.gap(f"h{i}", z - prev, medium=VAC))
            prev = z
        r = run(els, lam, beam=gbeam(0.5e-3, 0.3, m2), tau=1e-12)
        eng = np.array([val(r, 4 + i, "trackedBeamRadiusXM") for i in range(len(zs))])
        q = 1j * math.pi * (0.5e-3) ** 2 / (m2 * lam) + (0.1 - 0.3)
        for M in (lensM(0.1), gapM(0.4), lensM(0.3)):
            q = q_after(q, M)
        ana = np.array([w_of_q(q_after(q, gapM(z)), lam, m2) for z in zs])
        fam.check("two lenses, M2 = 1.5, converging input: w(z) max rel. error", float(np.max(np.abs(eng / ana - 1))), 0.0, abs=2e-6)
        # the beam in front of the first lens: R and w from the gap alone
        eng0 = val(r, 0, "trackedBeamRadiusXM")
        ana0 = w_of_q(1j * math.pi * (0.5e-3) ** 2 / (m2 * lam) - 0.2, lam, m2)
        fam.check("M2 = 1.5 beam radius after 0.1 m (waist 0.3 m ahead)", eng0, ana0, rel=2e-6, unit="m")
        overlay(f, zs * 1e3, eng * 1e6, ana * 1e6, label="1030 nm", xlabel="z after the second lens (mm)", ylabel="w (um)", marker="o",
                analytic_label="ABCD")

    # a lens and a gap in air: the documented rule (a length L in a medium of index n adds L/n to the reduced q)
    lam = 1030e-9
    nair = float(A.ciddor_air(lam))
    zs = np.linspace(0.1, 0.3, 5)
    els, prev = [b.lens("L", 0.2)], 0.0
    for i, z in enumerate(zs):
        els.append(b.gap(f"g{i}", z - prev))
        prev = z
    r = run(els, lam, beam=gbeam(), tau=1e-12)
    eng = np.array([val(r, 1 + i, "trackedBeamRadiusXM") for i in range(len(zs))])
    q = q_after(1j * math.pi * W0 ** 2 * nair / lam, lensM(0.2))   # real q in air (zR = pi n w^2 / lam); the focal length is a real distance in air
    ana = np.array([w_of_q(q_after(q, gapM(z)), lam / nair) for z in zs])
    fam.check("lens + air gap: w(z) max rel. error (lam/n, real distances)", float(np.max(np.abs(eng / ana - 1))), 0.0, abs=2e-5,
              note="a thin lens in air: 1/q' = 1/q - 1/f with the focal length in air")


def thick_lens(fam: Family):
    """The thickness of a lens with a material is bulk glass on the axis: group delay, GDD, TOD of the centre thickness."""
    d = 5e-3
    with fam.figure("thick-lens-gdd", "Lens with a glass and a 5 mm centre thickness: GDD against Sellmeier x Ciddor air",
                    "GDD = d^2/dw^2 [w n(w) d / c] (the centre thickness is bulk glass on the axis)") as f:
        lams = np.array([515e-9, 700e-9, 1030e-9, 1300e-9, 1600e-9, 2e-6])
        for g in ("FusedSilica", "N-BK7", "CaF2"):
            eng, ana = [], []
            for lam in lams:
                r = run([b.lens("L", 0.3, g, d)], lam, tau=1e-12)
                t = r.summary["totals"]
                w0 = r.summary["grid"]["reportOmega0"]
                gd0 = A.phase_derivatives(A.GLASSES[g], d, omega0(r))[0]
                _, gdd, tod = A.phase_derivatives(A.GLASSES[g], d, w0)
                eng.append(t["systemGddS2"] / FS2 / 5)
                ana.append(gdd / FS2 / 5)
                if lam in CARRIERS_M:
                    fam.check(f"thick lens GD {g} {label_wl(lam)}", t["arrivalTimeS"], gd0, rel=1e-9, unit="s")
                    fam.check(f"thick lens GDD {g} {label_wl(lam)}", t["systemGddS2"], gdd, rel=1e-6, unit="s^2")
                    fam.check(f"thick lens TOD {g} {label_wl(lam)}", t["systemTodS3"], tod, rel=2e-5, unit="s^3")
            overlay(f, lams * 1e9, eng, ana, label=g, xlabel="carrier wavelength (nm)", ylabel="GDD (fs^2 per mm)", residual="rel", marker="o",
                    analytic_label="analytic" if g == "FusedSilica" else None)
    fam.note("lens thickness: the beam tracker ignores thicknessM (only the dispersion is applied); a thick-lens ABCD is not modelled and not claimed")


def mirror_abcd(fam: Family):
    """Curved and off-axis mirrors, with a lens in front: tangential and sagittal focal lengths on the tracked beam."""
    lam0 = 1030e-9
    R = 1.0
    with fam.figure("mirror-offaxis", "Spherical mirror R = 1 m at 0, 20, 40 deg on a collimated beam: w_x (tangential) and w_y (sagittal) near their foci",
                    "f_t = R cos(a) / 2 (x), f_s = R / (2 cos(a)) (y); q' = q / (1 - q/f); waist from the parabola of w^2(z)") as f:
        for lam in CARRIERS_M:
            for aoi in (0, 20, 40):
                ft, fs = R * math.cos(math.radians(aoi)) / 2, R / (2 * math.cos(math.radians(aoi)))
                res = {}
                for ax, fl in (("X", ft), ("Y", fs)):
                    res[ax] = fl
                zs = 0.5 * (ft + fs) + np.linspace(-0.06, 0.06, 13)
                els, prev = [{"id": "m", "type": "mirror", "radiusOfCurvatureM": R, "aoiDeg": aoi, "before": VAC}], 0.0
                for i, z in enumerate(zs):
                    els.append(b.gap(f"g{i}", z - prev, medium=VAC))
                    prev = z
                r = run(els, lam, beam=gbeam(), tau=1e-12)
                zr = math.pi * W0 ** 2 / lam
                for ax, fl in (("X", ft), ("Y", fs)):
                    eng = np.array([val(r, 1 + i, f"trackedBeamRadius{ax}M") for i in range(len(zs))])
                    q = q_after(1j * zr, lensM(fl))
                    ana = np.array([w_of_q(q_after(q, gapM(z)), lam) for z in zs])
                    zw, w0f = waist_from_parabola(zs, eng)
                    fam.check(f"mirror R=1 m {aoi} deg w_{ax.lower()}(z) max rel. error {label_wl(lam)}", float(np.max(np.abs(eng / ana - 1))), 0.0, abs=2e-6)
                    fam.check(f"mirror R=1 m {aoi} deg waist {ax.lower()} {label_wl(lam)}", w0f, W0 / math.sqrt(1 + (zr / fl) ** 2), rel=2e-5, unit="m")
                    if lam == 1030e-9 and aoi == 40:
                        overlay(f, (zs - 0.5 * (ft + fs)) * 1e3, eng * 1e6, ana * 1e6, label=f"40 deg, {ax.lower()} ({'tangential' if ax == 'X' else 'sagittal'})",
                                xlabel="z after the mirror, minus the mean focal length (mm)", ylabel="w (um)", marker="o",
                                analytic_label="ABCD" if ax == "X" else None)
        # focalLengthM is the same mirror as radiusOfCurvatureM = 2 f
        a = run([{"id": "m", "type": "mirror", "focalLengthM": 0.5, "aoiDeg": 0, "before": VAC}, b.gap("g", 0.5, medium=VAC)], 1030e-9, beam=gbeam(), tau=1e-12)
        c_ = run([{"id": "m", "type": "mirror", "radiusOfCurvatureM": 1.0, "aoiDeg": 0, "before": VAC}, b.gap("g", 0.5, medium=VAC)], 1030e-9, beam=gbeam(), tau=1e-12)
        fam.check("mirror focalLengthM = R/2 same w", val(a, 1, "trackedBeamRadiusXM"), val(c_, 1, "trackedBeamRadiusXM"), rel=1e-12, unit="m")

    # lens, gap, off-axis mirror, gap: astigmatic relay
    lam = 1030e-9
    f_l, d1, Rm, aoi = 0.4, 0.3, 0.8, 30
    ft, fs = Rm * math.cos(math.radians(aoi)) / 2, Rm / (2 * math.cos(math.radians(aoi)))
    zs = np.linspace(0.02, 0.5, 13)
    with fam.figure("lens-mirror-relay", "Lens f = 0.4 m, 0.3 m, mirror R = 0.8 m at 30 deg: astigmatic w_x(z), w_y(z) after the mirror",
                    "ABCD chain per axis: lens f, gap, mirror f_t / f_s, gap") as f:
        els, prev = [b.lens("L", f_l, medium=VAC), b.gap("d", d1, medium=VAC), {"id": "m", "type": "mirror", "radiusOfCurvatureM": Rm, "aoiDeg": aoi, "before": VAC}], 0.0
        for i, z in enumerate(zs):
            els.append(b.gap(f"g{i}", z - prev, medium=VAC))
            prev = z
        r = run(els, lam, beam=gbeam(0.6e-3), tau=1e-12)
        for ax, fm in (("X", ft), ("Y", fs)):
            q = 1j * math.pi * (0.6e-3) ** 2 / lam
            for M in (lensM(f_l), gapM(d1), lensM(fm)):
                q = q_after(q, M)
            eng = np.array([val(r, 3 + i, f"trackedBeamRadius{ax}M") for i in range(len(zs))])
            ana = np.array([w_of_q(q_after(q, gapM(z)), lam) for z in zs])
            fam.check(f"lens + off-axis mirror w_{ax.lower()}(z) max rel. error", float(np.max(np.abs(eng / ana - 1))), 0.0, abs=3e-6)
            overlay(f, zs * 1e3, eng * 1e6, ana * 1e6, label=f"w_{ax.lower()}", xlabel="z after the mirror (mm)", ylabel="w (um)", marker="o",
                    analytic_label="ABCD" if ax == "X" else None)


# ================================================================================================================== prism
def snell_prism_exit(glass, lam, apex, aoi):
    """Signed exit angle of a prism (air, relative to the normal of the exit face) for vacuum-like surroundings with the glass'
    absolute index. aoi, apex in rad."""
    n = float(A.GLASSES[glass](lam))
    t1 = math.asin(math.sin(aoi) / n)
    return math.asin(n * math.sin(t1 - apex))


def prism_disp(glass, lam, apex, aoi, h=1e-9):
    return (snell_prism_exit(glass, lam + h, apex, aoi) - snell_prism_exit(glass, lam - h, apex, aoi)) / (2 * h)


def prism_brewster(glass, lam):
    n = float(A.GLASSES[glass](lam))
    thb = math.atan(n)
    return thb, math.pi - 2 * thb


def prism_single(fam: Family):
    with fam.figure("prism-dispersion", "Brewster-cut fused-silica prism at minimum deviation: angular dispersion against Snell's law",
                    "theta_out(lam) = asin[n sin(asin(sin(a_in) / n) - apex)]; dtheta/dlam by central differences of Snell's law with the Sellmeier index") as f:
        lams = np.array([515e-9, 700e-9, 1030e-9, 1300e-9, 1600e-9, 2e-6])
        eng, ana = [], []
        for lam in lams:
            thb, apex = prism_brewster("FusedSilica", lam)
            r = run([b.prism("P", "FusedSilica", math.degrees(apex), math.degrees(thb), 0.0, medium=VAC)], lam, beam=gbeam(), tau=1e-12)
            D = val(r, 0, "trackedAngularDispersionXRadPerM")
            Da = prism_disp("FusedSilica", lam, apex, thb)
            eng.append(D)
            ana.append(Da)
            if lam in CARRIERS_M:
                fam.check(f"prism dtheta/dlam {label_wl(lam)}", D, Da, rel=1e-5, unit="rad/m", formula="Snell's law, Sellmeier n(lam)")
                # transmission: p (x) at Brewster on both faces, s (y) = (1 - Rs)^2 per face
                for pol, ang in (("p", 0), ("s", 90)):
                    rr = run([b.prism("P", "FusedSilica", math.degrees(apex), math.degrees(thb), 0.0, medium=VAC)], lam, pulse=pol_pulse(lam, ang, 1e-12), beam=gbeam())
                    n = float(A.GLASSES["FusedSilica"](lam))
                    t1 = math.asin(math.sin(thb) / n)
                    t2 = apex - t1
                    T = (1 - reflectance(1.0, n, thb, pol)) * (1 - reflectance(n, 1.0, t2, pol))
                    fam.check(f"prism T_{pol} Brewster-cut {label_wl(lam)}", rr.summary["totals"]["energyTransmission"], T, abs=3e-6,
                              formula="(1 - R(in)) (1 - R(out)) from Fresnel; p = 1 at Brewster")
                # exit angle: symmetric, so the beam leaves at -theta_B (minimum deviation)
        overlay(f, lams * 1e9, np.array(eng), np.array(ana), label="engine", xlabel="carrier wavelength (nm)", ylabel="angular dispersion (rad/m)", residual="rel",
                marker="o", analytic_label="Snell")


def prism_pair(fam: Family):
    with fam.figure("prism-pair-gdd", "Brewster-cut fused-silica prism pair, apex separation L: GDD against Fork / Martinez, -lam^3 L (dtheta/dlam)^2 / (2 pi c^2)",
                    "GDD = lam^3 / (2 pi c^2) d^2P/dlam^2, P = L cos(beta): -lam^3 L D^2 / (2 pi c^2) at beta = 0, plus the vacuum-free glass path (0)") as f:
        Ls = np.array([0.1, 0.2, 0.4, 0.6])
        for lam in CARRIERS_M:
            thb, apex = prism_brewster("FusedSilica", lam)
            th_out = snell_prism_exit("FusedSilica", lam, apex, thb)
            D = prism_disp("FusedSilica", lam, apex, thb)
            eng, ana = [], []
            for L in Ls:
                els = [b.prism("P1", "FusedSilica", math.degrees(apex), math.degrees(thb), 0.0, medium=VAC), b.gap("g", float(L), medium=VAC),
                       b.prism("P2", "FusedSilica", math.degrees(apex), math.degrees(th_out), 0.0, medium=VAC)]
                r = run(els, lam, beam=gbeam(), tau=1e-12)
                gdd = r.summary["totals"]["systemGddS2"]
                w0 = r.summary["grid"]["reportOmega0"]
                want = -(2 * math.pi * C / w0) ** 3 * L * D * D / (2 * math.pi * C ** 2)
                eng.append(gdd / FS2)
                ana.append(want / FS2)
                if L in (0.1, 0.4):
                    fam.check(f"prism pair GDD L={L} m {label_wl(lam)}", gdd, want, rel=1e-4, unit="s^2", formula="Fork / Martinez, beta = 0")
            fam.check(f"prism pair leaves no angular dispersion {label_wl(lam)}", abs(val(r, 2, "trackedAngularDispersionXRadPerM")) / abs(D), 0.0, abs=2e-3,
                      formula="second prism entered at the first's exit angle")
            overlay(f, Ls, np.array(eng), np.array(ana), label=label_wl(lam), xlabel="apex separation L (m)", ylabel="GDD (fs^2)", residual="rel", marker="o",
                    analytic_label="Fork" if lam == CARRIERS_M[0] else None)


# ================================================================================================================ grating
def grating_single(fam: Family):
    lpmm = 1200.0
    th_in = 30.0
    with fam.figure("grating-dispersion", "Single grating 1200 l/mm, order -1, AOI 30 deg, tracked beam: angular dispersion and anamorphic magnification",
                    "sin(t_out) = sin(t_in) + m lam / period;  dt/dlam = m / (period cos t_out);  w_out / w_in = cos t_out / cos t_in;  tan(gamma) = lam dt/dlam (Hebling)") as f:
        lams = np.linspace(700e-9, 1100e-9, 9)
        xs, eng_d, ana_d = [], [], []
        for lam in sorted(set(list(lams) + [1030e-9])):
            so = math.sin(math.radians(th_in)) - lam * lpmm * 1e3
            if abs(so) >= 1:
                continue
            co = math.sqrt(1 - so * so)
            r = run([b.grating("G", lpmm, th_in, order=-1)], lam, beam=gbeam(), tau=1e-12)
            D = val(r, 0, "trackedAngularDispersionXRadPerM")
            Da = -lpmm * 1e3 / co
            xs.append(lam * 1e9)
            eng_d.append(D)
            ana_d.append(Da)
            if abs(lam - 1030e-9) < 1e-12:
                fam.check("grating magnification w_out/w_in 1030 nm", val(r, 0, "trackedBeamRadiusXM") / W0, co / math.cos(math.radians(th_in)), rel=1e-6,
                          formula="cos t_out / cos t_in (fixes the diffraction angle)")
                fam.check("grating angular dispersion 1030 nm", D, Da, rel=1e-5, unit="rad/m")
                fam.check("grating pulse-front tilt tan(gamma) = lam dt/dlam 1030 nm", math.tan(math.radians(val(r, 0, "trackedPulseFrontTiltXDeg"))), lam * abs(Da),
                          rel=1e-3, formula="Hebling 1996")
        overlay(f, xs, np.array(eng_d), np.array(ana_d), label="1200 l/mm, 30 deg", xlabel="carrier wavelength (nm)", ylabel="angular dispersion (rad/m)", residual="rel",
                marker="o", analytic_label="m / (period cos t_out)")

    # the three carriers, each with a groove density that has a propagating order; same checks
    for lam, lp in ((515e-9, 1800.0), (1030e-9, 1500.0), (2e-6, 600.0)):
        so = math.sin(math.radians(40)) - lam * lp * 1e3
        co = math.sqrt(1 - so * so)
        r = run([b.grating("G", lp, 40, order=-1)], lam, beam=gbeam(), tau=1e-12)
        fam.check(f"grating magnification {lp:g} l/mm {label_wl(lam)}", val(r, 0, "trackedBeamRadiusXM") / W0, co / math.cos(math.radians(40)), rel=1e-6)
        fam.check(f"grating angular dispersion {lp:g} l/mm {label_wl(lam)}", val(r, 0, "trackedAngularDispersionXRadPerM"), -lp * 1e3 / co, rel=1e-5, unit="rad/m")

    # Littrow: t_out = -t_in, magnification 1, dispersion 2 tan(t)/lam
    for lam, lp in ((515e-9, 1800.0), (1030e-9, 1500.0), (2e-6, 600.0)):
        thl = math.asin(lam * lp * 1e3 / 2)
        r = run([b.grating("G", lp, math.degrees(thl), order=-1)], lam, beam=gbeam(), tau=1e-12)
        fam.check(f"grating Littrow magnification {label_wl(lam)}", val(r, 0, "trackedBeamRadiusXM") / W0, 1.0, abs=2e-6, formula="cos t_out = cos t_in")
        fam.check(f"grating Littrow dispersion {label_wl(lam)}", val(r, 0, "trackedAngularDispersionXRadPerM"), -2 * math.tan(thl) / lam, rel=1e-5, unit="rad/m",
                  formula="2 tan(t_L) / lam")

    # efficiency, polarisation resolved
    for pol, ang in (("p", 0), ("s", 90), ("45", 45)):
        r = run([{"id": "G", "type": "grating", "linesPerMm": 1200, "aoiDeg": 30, "order": -1, "efficiencyS": 0.6, "efficiencyP": 0.9}], pulse=pol_pulse(1030e-9, ang, 1e-12), beam=gbeam())
        want = {"p": 0.9, "s": 0.6, "45": 0.75}[pol]
        fam.check(f"grating efficiency pol {pol}", r.summary["totals"]["energyTransmission"], want, abs=1e-9, formula="eta_p cos^2 + eta_s sin^2")


def grating_pair_single(fam: Family):
    """Two single gratings (the second undoes the first) against the single-pass Treacy GDD and no leftover angular dispersion."""
    cases = {515e-9: 1800.0, 1030e-9: 1500.0, 2e-6: 600.0}
    aoi = 50.0
    G = 0.02
    with fam.figure("grating-single-pair", "Two single gratings (AOI 50 deg, perpendicular separation 20 mm): GDD against Treacy (one pass), no angular dispersion left",
                    "g1: aoi -50 deg, order +1; g2: aoi = exit angle, order -1; slant gap G / cos(t_d); GDD = -m^2 lam^3 G / (2 pi c^2 d^2 cos^3(t_d))") as f:
        xs, eng, ana = [], [], []
        for lam, lp in cases.items():
            sd = lam * lp * 1e3 - math.sin(math.radians(aoi))
            td = math.asin(sd)
            gap = G / math.cos(td)
            r = run([b.grating("g1", lp, -aoi, order=1), b.gap("s", gap, medium=VAC), b.grating("g2", lp, math.degrees(td), order=-1)], lam, beam=gbeam(), tau=1e-12)
            w0 = r.summary["grid"]["reportOmega0"]
            gdd, tod = A.treacy(2 * math.pi * C / w0, lp * 1e3, math.radians(aoi), G, -1, 1)
            t = r.summary["totals"]
            fam.check(f"two gratings GDD {label_wl(lam)}", t["systemGddS2"], gdd, rel=1e-5, unit="s^2", formula="Treacy, 1 pass")
            fam.check(f"two gratings TOD {label_wl(lam)}", t["systemTodS3"], tod, rel=5e-3, unit="s^3", formula="d(GDD)/dw")
            fam.check(f"two gratings leave no angular dispersion {label_wl(lam)}",
                      abs(val(r, 2, "trackedAngularDispersionXRadPerM")) / abs(val(r, 0, "trackedAngularDispersionXRadPerM")), 0.0, abs=2e-3)
            fam.check(f"two gratings restore the beam radius {label_wl(lam)}", val(r, 2, "trackedBeamRadiusXM") / W0, 1.0, abs=2e-4, formula="the magnifications cancel")
            xs.append(lam * 1e9)
            eng.append(t["systemGddS2"] / gdd)
            ana.append(1.0)
        overlay(f, xs, eng, ana, label="engine / Treacy", xlabel="carrier wavelength (nm)", ylabel="GDD / Treacy", marker="o", style="", analytic_label="Treacy")


# =============================================================================================================== ndFilter
def nd_filter(fam: Family):
    with fam.figure("nd-od", "Absorptive neutral-density filter on fused silica (3 mm): power transmission against 10^-OD (1 - R)^2, three carriers",
                    "T = 10^-OD (1 - R)^2 with R = ((n - 1) / (n + 1))^2 of the two bare surfaces") as f:
        ods = [0.0, 0.3, 1.0, 2.0, 3.0, 4.0]
        for lam in CARRIERS_M:
            eng, ana = [], []
            n = n_rel("FusedSilica", lam)
            Rf = ((n - 1) / (n + 1)) ** 2
            for od in ods:
                r = run([b.nd_filter("N", od, material="FusedSilica", thickness_m=3e-3)], lam, tau=1e-12)
                eng.append(r.summary["totals"]["energyTransmission"])
                ana.append(10 ** (-od) * (1 - Rf) ** 2)
                fam.check(f"ndFilter OD {od:g} {label_wl(lam)}", eng[-1], ana[-1], rel=1e-6, formula="10^-OD (1 - R)^2")
            overlay(f, ods, eng, ana, label=label_wl(lam), xlabel="optical density", ylabel="power transmission", residual="rel", marker="o",
                    analytic_label="10^-OD (1-R)^2" if lam == CARRIERS_M[0] else None)
        # reflective: the coating transmits 10^-OD, the substrate adds its two surfaces
        r = run([b.nd_filter("N", 1.0, kind="reflective")], 1030e-9, tau=1e-12)
        n = n_rel("FusedSilica", 1030e-9)
        fam.check("ndFilter reflective OD 1", r.summary["totals"]["energyTransmission"], 0.1 * (1 - ((n - 1) / (n + 1)) ** 2) ** 2, rel=1e-4)

    # OD(lambda): the absorbance is interpolated; 10 fs spectrum through a sloped table
    with fam.figure("nd-od-lambda", "ndFilter with OD(lambda) table (0.3 at 950 nm to 1.3 at 1110 nm), 10 fs pulse: output / input spectrum",
                    "T(lam) = 10^-OD(lam) (1 - R(lam))^2, OD interpolated linearly in angular frequency between the table points") as f:
        wl = [0.95e-6, 1.11e-6]
        odv = [0.3, 1.3]
        r = run([b.nd_filter("N", odv, wavelengths_m=wl, material="FusedSilica", thickness_m=2e-3)], 1030e-9, tau=10e-15)
        w = np.asarray(r["output_omega_rad_s"])
        si, so = np.asarray(r["input_spectrum"]), np.asarray(r["output_spectrum"])
        m = si > 1e-3 * si.max()
        lam = 2 * math.pi * C / w[m]
        wt = [2 * math.pi * C / x for x in wl]
        od_w = np.interp(w[m], sorted(wt), [odv[1], odv[0]])   # wt[1] < wt[0]: ascending omega -> OD of 1110 first
        ana = 10 ** (-od_w) * np.array([(1 - ((n_rel("FusedSilica", x) - 1) / (n_rel("FusedSilica", x) + 1)) ** 2) ** 2 for x in lam])
        eng = so[m] / si[m]
        fam.check("ndFilter OD(lambda) max rel. error of T over the band", float(np.max(np.abs(eng / ana - 1))), 0.0, abs=1e-3,
                  formula="absorbance interpolated (not the transmission)")
        overlay(f, lam * 1e9, eng, ana, label="10 fs, 1030 nm", xlabel="wavelength (nm)", ylabel="power transmission", residual="rel", analytic_label="10^-OD(lam)(1-R)^2")


def ghost_window(res, delay_guess):
    """Energy of the main pulse and of the replica around +delay (windows split half way), and the replica's centroid delay."""
    t = np.asarray(res["output_time_s"])
    P = np.asarray(res["output_power_w"])
    dt = t[1] - t[0]
    tm = t[int(np.argmax(P))]
    mw = np.abs(t - tm) < delay_guess / 2
    gw = np.abs(t - (tm + delay_guess)) < delay_guess / 2
    em, eg = P[mw].sum() * dt, P[gw].sum() * dt
    tg = float((t[gw] * P[gw]).sum() / P[gw].sum())
    tm_c = float((t[mw] * P[mw]).sum() / P[mw].sum())
    return em, eg, tg - tm_c


def ghosts(fam: Family):
    d = 5e-3
    with fam.figure("plate-ghost", "Fused-silica plate, 5 mm, ghosts on (normal incidence): replica delay 2 n_g d / c and energy R^2, three carriers, 1 ps and 100 fs",
                    "replica = twice the glass path: delay = group delay of 2d of glass; energy ratio = R_front R_back = R^2, R = ((n-1)/(n+1))^2") as f:
        xs, eng, ana = [], [], []
        for lam in CARRIERS_M:
            for tau in (1e-12, 100e-15):
                r = run([b.plate("p", "FusedSilica", d, ghosts=True)], lam, tau=tau)
                gd2 = A.phase_derivatives(A.GLASSES["FusedSilica"], 2 * d, omega0(r))[0] - 2 * d / C   # extra glass delay of the round trip (the vacuum part is the same path in the main beam? no: add it)
                gd2 = A.phase_derivatives(A.GLASSES["FusedSilica"], 2 * d, omega0(r))[0]
                em, eg, dly = ghost_window(r, gd2)
                n = n_rel("FusedSilica", lam)
                R2 = (((n - 1) / (n + 1)) ** 2) ** 2
                fam.check(f"plate ghost delay {label_wl(lam)} {label_dur(tau)}", dly, gd2, rel=2e-5, unit="s", formula="2 n_g d / c")
                fam.check(f"plate ghost energy ratio {label_wl(lam)} {label_dur(tau)}", eg / em, R2, rel=2e-4, formula="R^2 (second order R^4 neglected)")
                if tau == 1e-12:
                    xs.append(lam * 1e9)
                    eng.append(eg / em)
                    ana.append(R2)
        overlay(f, xs, np.array(eng) * 1e3, np.array(ana) * 1e3, label="ghost / main", xlabel="carrier wavelength (nm)", ylabel="energy ratio (1e-3)", residual="rel",
                marker="o", style="", analytic_label="R^2")

    # oblique incidence: s and p replicas, exact plane-wave group delay of the round trip
    lam = 1030e-9
    aoi = 40.0
    for pol, ang in (("p", 0), ("s", 90)):
        r = run([b.plate("p", "FusedSilica", d, aoi_deg=aoi, ghosts=True)], pulse=pol_pulse(lam, ang, 1e-12))
        n = n_rel("FusedSilica", lam)
        th = math.radians(aoi)
        t2 = math.asin(math.sin(th) / n)
        Rf = reflectance(1.0, n, th, pol)
        Rb = reflectance(n, 1.0, t2, pol)
        # replica phase psi(w) = 2 w d sqrt(n^2 - n_o^2 sin^2) / c  (absolute indices)
        w0 = omega0(r)
        no = lambda w: float(A.ciddor_air(2 * math.pi * C / w))
        ng = lambda w: float(A.GLASSES["FusedSilica"](2 * math.pi * C / w))
        psi = lambda w: 2 * w * d * math.sqrt(ng(w) ** 2 - (no(w) * math.sin(th)) ** 2) / C
        h = 1e-4 * w0
        dly = (psi(w0 + h) - psi(w0 - h)) / (2 * h)
        em, eg, dm = ghost_window(r, dly)
        fam.check(f"plate ghost {pol} 40 deg delay", dm, dly, rel=3e-4, unit="s", formula="d psi / d w, psi = 2 w d sqrt(n^2 - n_o^2 sin^2 a) / c")
        fam.check(f"plate ghost {pol} 40 deg energy ratio", eg / em, Rf * Rb, rel=5e-4, formula="R_front R_back (s, p Fresnel)")

    # bulk with ghosts and AR-coated end faces; ndFilter ghost with the absorption of two extra passes
    r = run([b.bulk("g", "CaF2", 5e-3, ghosts={"frontReflectance": 0.002, "backReflectance": 0.002})], 1030e-9, tau=1e-12)
    gd2 = A.phase_derivatives(A.GLASSES["CaF2"], 10e-3, omega0(r))[0]
    em, eg, dly = ghost_window(r, gd2)
    fam.check("bulk CaF2 ghost delay", dly, gd2, rel=2e-5, unit="s", formula="2 n_g L / c")
    fam.check("bulk CaF2 ghost level (R = 0.002 each face)", eg / em, 0.002 * 0.002, rel=1e-4, formula="R_front R_back")
    od = 0.5
    r = run([b.nd_filter("N", od, material="FusedSilica", thickness_m=3e-3, ghosts=True)], 1030e-9, tau=1e-12)
    gd2 = A.phase_derivatives(A.GLASSES["FusedSilica"], 6e-3, omega0(r))[0]
    em, eg, dly = ghost_window(r, gd2)
    n = n_rel("FusedSilica", 1030e-9)
    R = ((n - 1) / (n + 1)) ** 2
    fam.check("ndFilter ghost delay", dly, gd2, rel=2e-5, unit="s")
    fam.check("ndFilter ghost level R^2 10^(-2 OD)", eg / em, R * R * 10 ** (-2 * od), rel=2e-4, formula="two more passes through the absorber")


# =============================================================================================== interface and coatings
def fresnel_interface(fam: Family):
    n = 1.5
    angles = np.array([0, 15, 30, 45, 56.31, 65, 75, 85])
    with fam.figure("interface-fresnel", "Bare interface vacuum -> n = 1.5 (reflect mode): power reflectance of p (x) and s (y) against Fresnel",
                    "R_s = ((cos a - n cos t) / (cos a + n cos t))^2, R_p = ((n cos a - cos t) / (n cos a + cos t))^2") as f:
        for pol, ang in (("p", 0), ("s", 90)):
            eng, ana = [], []
            for a in angles:
                r = run([b.mirror("m", aoi_deg=float(a), medium=VAC, after="SUB")], pulse=pol_pulse(1030e-9, ang, 1e-12), materials=CONST_MATS)
                eng.append(r.summary["totals"]["energyTransmission"])
                ana.append(reflectance(1.0, n, math.radians(a), pol))
                fam.check(f"Fresnel R_{pol} {a:g} deg", eng[-1], ana[-1], abs=1e-9)
            overlay(f, angles, eng, ana, label=f"{pol}", xlabel="angle of incidence (deg)", ylabel="power reflectance", marker="o", analytic_label="Fresnel" if pol == "p" else None)
    # Brewster and total internal reflection, glass n(lambda) with the air index
    for lam in CARRIERS_M:
        nr = n_rel("FusedSilica", lam)
        thb = math.atan(nr)
        r = run([b.interface("i", "Air", "FusedSilica", aoi_deg=math.degrees(thb))], pulse=pol_pulse(lam, 0, 1e-12))
        fam.check(f"Brewster angle {math.degrees(thb):.2f} deg: T_p = 1 {label_wl(lam)}", r.summary["totals"]["energyTransmission"], 1.0, abs=2e-6, formula="tan a_B = n")
        rr = run([b.interface("i", "FusedSilica", "Air", aoi_deg=math.degrees(math.atan(1 / nr)))], pulse=pol_pulse(lam, 0, 1e-12))
        fam.check(f"internal Brewster angle: T_p = 1 {label_wl(lam)}", rr.summary["totals"]["energyTransmission"], 1.0, abs=2e-6, formula="tan a = 1/n")
        thc = math.asin(1 / nr)
        for fac, want in ((0.98, None), (1.02, 0.0)):
            rt = run([b.interface("i", "FusedSilica", "Air", aoi_deg=math.degrees(thc) * fac)], pulse=pol_pulse(lam, 90, 1e-12))
            if want is not None:
                fam.check(f"total internal reflection beyond {math.degrees(thc):.2f} deg: T_s = 0 {label_wl(lam)}", rt.summary["totals"]["energyTransmission"], 0.0, abs=1e-9)
            else:
                th = thc * fac
                sin_t = nr * math.sin(th)
                tt = math.asin(sin_t)
                fam.check(f"just below the critical angle T_s {label_wl(lam)}", rt.summary["totals"]["energyTransmission"], 1 - reflectance(nr, 1.0, th, "s") if False else 1 - A.fresnel_rs_rp(nr, 1.0, th)[0] ** 2, abs=5e-5, note='abs 5e-5: dR/dlam is steep at 0.98 of the critical angle and a 1 ps pulse has a band')


def coatings(fam: Family):
    nsub, nAR = 1.5, math.sqrt(1.5)
    lam_d = 1030e-9
    ar = {"AR": {"kind": "stack", "designWavelengthM": lam_d, "layers": [{"material": "ARL", "qwot": 1}]}}
    n_scan = np.linspace(450e-9, 2400e-9, 33)
    with fam.figure("coating-ar", "Single quarter-wave AR layer (n = sqrt(1.5), design 1030 nm) on n = 1.5: transmission of a 1 ps pulse against the transfer-matrix method",
                    "T = 1 - |r|^2 with the characteristic matrix of the layer; T = 1 at the design wavelength, bare Fresnel at half of it (absentee layer)") as f:
        eng, ana = [], []
        for lam in n_scan:
            r = run([b.interface("i", VAC, "SUB", coating="AR")], lam, tau=1e-12, materials=CONST_MATS, coatings=ar)
            T = wavg(r, lambda l: np.array([tmm(x, 0.0, 1.0, [(nAR, lam_d / (4 * nAR))], nsub, "s")[1] for x in l]))
            eng.append(r.summary["totals"]["energyTransmission"])
            ana.append(T)
        eng, ana = np.array(eng), np.array(ana)
        fam.check("AR layer: max |T_engine - T_TMM| over 450-2400 nm", float(np.max(np.abs(eng - ana))), 0.0, abs=1e-6)
        overlay(f, n_scan * 1e9, eng, ana, label="engine", xlabel="carrier wavelength (nm)", ylabel="power transmission", marker="o", analytic_label="TMM")
    for lam in CARRIERS_M + (lam_d,):
        r = run([b.interface("i", VAC, "SUB", coating="AR")], lam, tau=1e-12, materials=CONST_MATS, coatings=ar)
        Tw = wavg(r, lambda l: np.array([tmm(x, 0.0, 1.0, [(nAR, lam_d / (4 * nAR))], nsub, "s")[1] for x in l]))
        fam.check(f"AR layer T at {label_wl(lam)}", r.summary["totals"]["energyTransmission"], Tw, abs=1e-6, formula="TMM, averaged over the pulse spectrum")
    r = run([b.interface("i", VAC, "SUB", coating="AR")], lam_d, tau=1e-12, materials=CONST_MATS, coatings=ar)
    fam.check("AR layer at its design wavelength: R = 0", r.summary["totals"]["energyTransmission"], 1.0, abs=2e-6, formula="n_layer^2 = n_sub: r = 0")
    # absentee layer at 515 nm: a lambda/2 layer gives bare Fresnel exactly
    r = run([b.interface("i", VAC, "SUB", coating="AR")], 515.0e-9 * lam_d / 1030e-9, tau=1e-12, materials=CONST_MATS, coatings=ar)
    fam.check("lambda/2 layer is absent (515 nm): T = 1 - ((n-1)/(n+1))^2", r.summary["totals"]["energyTransmission"], 1 - ((nsub - 1) / (nsub + 1)) ** 2, abs=5e-6)

    # Bragg mirror (HL)^8 H on n = 1.5
    nH, nL = 2.3, 1.38
    N = 8
    hr = {"HR": b.coating_quarter_wave(f"(HL)^{N} H", lam_d, H="HI", L="LO")}
    layers = qw_stack(f"(HL)^{N} H", lam_d, nH, nL)
    assert len(layers) == 2 * N + 1
    scan = np.linspace(600e-9, 1900e-9, 41)
    with fam.figure("coating-bragg", "Bragg mirror (HL)^8 H, n_H = 2.3, n_L = 1.38 on n = 1.5, design 1030 nm: reflectance of a 1 ps pulse against the transfer-matrix method",
                    "R = |r|^2 of the characteristic matrix; peak R = ((1 - Y) / (1 + Y))^2, Y = n_H^(2N+2) / (n_L^(2N) n_s); half-wave layers are absent at 515 nm") as f:
        eng, ana = [], []
        for lam in scan:
            r = run([b.mirror("m", coating="HR", medium=VAC, after="SUB")], lam, tau=1e-12, materials=CONST_MATS, coatings=hr)
            Rw = wavg(r, lambda l: np.array([tmm(x, 0.0, 1.0, layers, nsub, "s")[0] for x in l]))
            eng.append(r.summary["totals"]["energyTransmission"])
            ana.append(Rw)
        eng, ana = np.array(eng), np.array(ana)
        fam.check("Bragg mirror: max |R_engine - R_TMM| over 600-1900 nm", float(np.max(np.abs(eng - ana))), 0.0, abs=2e-5)
        overlay(f, scan * 1e9, eng, ana, label="engine", xlabel="carrier wavelength (nm)", ylabel="power reflectance", marker="o", analytic_label="TMM")
    Y = nH ** (2 * N + 2) / (nL ** (2 * N) * nsub)
    r = run([b.mirror("m", coating="HR", medium=VAC, after="SUB")], lam_d, tau=1e-12, materials=CONST_MATS, coatings=hr)
    fam.check("Bragg mirror peak reflectance", r.summary["totals"]["energyTransmission"], ((1 - Y) / (1 + Y)) ** 2, abs=3e-7, formula="((1 - Y)/(1 + Y))^2")
    r = run([b.mirror("m", coating="HR", medium=VAC, after="SUB")], 515e-9, tau=1e-12, materials=CONST_MATS, coatings=hr)
    fam.check("Bragg mirror at lambda_d / 2 (absentee layers): bare Fresnel, TMM over the band", r.summary["totals"]["energyTransmission"],
              wavg(r, lambda l: np.array([tmm(x, 0.0, 1.0, layers, nsub, "s")[0] for x in l])), abs=2e-6)
    fam.check("Bragg mirror at lambda_d / 2 (absentee layers): bare Fresnel within the band's detuning", r.summary["totals"]["energyTransmission"], ((nsub - 1) / (nsub + 1)) ** 2, abs=5e-5,
              note="17 layers: a 1e-3 detuning of the half-wave condition already moves R by 1e-3 relative")
    # group delay of the reflection at the stop-band centre and near its edge, against d(phase)/dw of the TMM
    for lam in (1030e-9, 1000e-9, 1070e-9):
        r = run([b.mirror("m", coating="HR", medium=VAC, after="SUB")], lam, tau=1e-12, materials=CONST_MATS, coatings=hr)
        w0 = r.summary["grid"]["reportOmega0"]
        h = 2e-4 * w0
        ph = lambda w: cmath.phase(tmm(2 * math.pi * C / w, 0.0, 1.0, layers, nsub, "s")[2])
        dph = ph(w0 + h) - ph(w0 - h)
        dph = (dph + math.pi) % (2 * math.pi) - math.pi
        tau_a = -dph / (2 * h)
        fam.check(f"Bragg mirror reflection group delay {label_wl(lam)}", el(r, 0)["own"]["groupDelayS"], tau_a, rel=3e-4, abs=1e-19, unit="s",
                  formula="-d arg(r) / dw (e^{+iwt} convention of the characteristic matrix)")


# ====================================================================================================== delay and rotators
def delay_element(fam: Family):
    for lam in CARRIERS_M:
        for tau in (1e-12, 10e-15):
            r = run([b.delay("d", delay_s=2.5e-12)], lam, tau=tau)
            t = r.summary["totals"]
            fam.check(f"delay 2.5 ps group delay {label_wl(lam)} {label_dur(tau)}", t["arrivalTimeS"], 2.5e-12, abs=1e-18, unit="s")
            fam.check(f"delay GDD {label_wl(lam)} {label_dur(tau)}", t["systemGddS2"], 0.0, abs=1e-36, unit="s^2")
            fam.check(f"delay energy {label_wl(lam)} {label_dur(tau)}", t["energyTransmission"], 1.0, abs=1e-12)
    r = run([b.delay("d", length_m=0.3)], 1030e-9, tau=1e-12)
    fam.check("delay lengthM = 0.3 m (vacuum path)", r.summary["totals"]["arrivalTimeS"], 0.3 / C, rel=1e-9, unit="s", formula="L / c")


def rotator(fam: Family):
    with fam.figure("rotator", "Polarisation rotator: input linear at 20 deg, rotation angle scan; fraction of the energy in x and y against Jones rotation",
                    "E_x = cos^2(psi + theta), E_y = sin^2(psi + theta); circular light is unchanged") as f:
        thetas = np.linspace(0, 180, 13)
        for lam in CARRIERS_M:
            ex, ex_a = [], []
            for th in thetas:
                r = run([{"id": "r", "type": "rotator", "angleDeg": float(th)}], pulse=pol_pulse(lam, 20.0, 1e-12))
                Ex, Ey, X = stokes_sums(r)
                tot = Ex + Ey
                a = math.radians(20 + th)
                ex.append(Ex / tot)
                ex_a.append(math.cos(a) ** 2)
                fam.check(f"rotator {th:g} deg E_x/E {label_wl(lam)}", Ex / tot, math.cos(a) ** 2, abs=2e-9)
                fam.check(f"rotator {th:g} deg cross term {label_wl(lam)}", X / tot, math.cos(a) * math.sin(a), abs=2e-9)
            overlay(f, thetas, ex, ex_a, label=label_wl(lam), xlabel="rotation (deg)", ylabel="E_x / E", marker="o", analytic_label="cos^2(20 deg + theta)" if lam == CARRIERS_M[0] else None)
    p = b.pulse(1030e-9, 1e-12, energy=1e-9)
    p["polarization"] = {"circular": "right"} if False else {"angleDeg": 0}
    # circular input via two waveplates is covered in polarisation.py; here: energy conserved for any rotation
    r = run([{"id": "r", "type": "rotator", "angleDeg": 37.0}], 1030e-9)
    fam.check("rotator energy", r.summary["totals"]["energyTransmission"], 1.0, abs=1e-12)


def faraday(fam: Family):
    lam_d = 1030e-9
    lam0 = 258e-9

    def theta(lam, th_d):
        return th_d * (lam_d ** 2 - lam0 ** 2) / (lam ** 2 - lam0 ** 2)

    with fam.figure("faraday-dispersion", "Faraday rotator 45 deg at 1030 nm, Verdet 1/(lam^2 - lam0^2), lam0 = 258 nm: output polarisation at 515 nm, 1030 nm, 2 um (1 ps)",
                    "theta(lam) = theta_d (lam_d^2 - lam0^2) / (lam^2 - lam0^2); E_x, E_y, cross term averaged over the input spectrum") as f:
        xs, eng, ana = [], [], []
        for lam in CARRIERS_M:
            r = run([{"id": "f", "type": "faradayRotator", "rotationDeg": 45.0, "designWavelengthM": lam_d}], pulse=pol_pulse(lam, 0.0, 1e-12))
            Ex, Ey, X = stokes_sums(r)
            tot = Ex + Ey
            wx = wavg(r, lambda l: np.cos(np.radians(theta(l, 45.0))) ** 2)
            wy = wavg(r, lambda l: np.sin(np.radians(theta(l, 45.0))) ** 2)
            wc = wavg(r, lambda l: np.cos(np.radians(theta(l, 45.0))) * np.sin(np.radians(theta(l, 45.0))))
            fam.check(f"Faraday E_x/E {label_wl(lam)}", Ex / tot, wx, abs=1e-6)
            fam.check(f"Faraday E_y/E {label_wl(lam)}", Ey / tot, wy, abs=1e-6)
            fam.check(f"Faraday cross term {label_wl(lam)}", X / tot, wc, abs=1e-6)
            fam.check(f"Faraday rotation angle at the carrier {label_wl(lam)}",
                      math.degrees(0.5 * math.atan2(2 * X, Ex - Ey)) % 180.0, theta(lam, 45.0) % 180.0, abs=0.02, unit="deg")
            xs.append(lam * 1e9)
            eng.append(math.degrees(0.5 * math.atan2(2 * X, Ex - Ey)) % 180.0)
            ana.append(theta(lam, 45.0) % 180.0)
        overlay(f, xs, eng, ana, label="engine", xlabel="carrier wavelength (nm)", ylabel="rotation mod 180 (deg)", marker="o", style="", analytic_label="Verdet dispersion")

    # non-reciprocal double pass: rotator, mirror, rotator (same direction in the transported basis) = 90 deg; a reciprocal half-wave plate pair = 0
    for lam in CARRIERS_M:
        els = [{"id": "f1", "type": "faradayRotator", "rotationDeg": 45.0, "verdetDispersion": "none"}, b.mirror("m"),
               {"id": "f2", "type": "faradayRotator", "rotationDeg": 45.0, "verdetDispersion": "none"}]
        r = run(els, pulse=pol_pulse(lam, 0.0, 1e-12))
        Ex, Ey, X = stokes_sums(r)
        fam.check(f"isolator double pass (Faraday, mirror, Faraday): E_y/E = 1 {label_wl(lam)}", Ey / (Ex + Ey), 1.0, abs=1e-9, formula="2 x 45 deg")
        r = run([b.waveplate("h1", "half", 22.5, design_wavelength_m=lam), b.mirror("m"), b.waveplate("h2", "half", 22.5, design_wavelength_m=lam)], pulse=pol_pulse(lam, 0.0, 1e-12))
        Ex, Ey, X = stokes_sums(r)
        fam.check(f"reciprocal double pass (half-wave plate, mirror, plate): E_x/E = 1 {label_wl(lam)}", Ex / (Ex + Ey), 1.0, abs=3e-5, formula="HWP^2 = 1; retardance is proportional to w, so the 1 ps band leaves (pi dw/w)^2 ~ 1e-5")
    # direction -1 reverses, V B L sets the angle
    r = run([{"id": "f", "type": "faradayRotator", "rotationDeg": 30.0, "direction": -1, "verdetDispersion": "none"}], pulse=pol_pulse(1030e-9, 0.0, 1e-12))
    Ex, Ey, X = stokes_sums(r)
    fam.check("Faraday direction -1: rotation -30 deg", math.degrees(0.5 * math.atan2(2 * X, Ex - Ey)), -30.0, abs=1e-6, unit="deg")
    r = run([{"id": "f", "type": "faradayRotator", "verdetRadPerTM": 40.0, "fieldT": 1.1, "lengthM": 0.018, "verdetDispersion": "none"}], pulse=pol_pulse(1030e-9, 0.0, 1e-12))
    Ex, Ey, X = stokes_sums(r)
    fam.check("Faraday V B L = 40 rad/(T m) x 1.1 T x 0.018 m", math.radians(math.degrees(0.5 * math.atan2(2 * X, Ex - Ey))), 40.0 * 1.1 * 0.018, abs=1e-6, unit="rad")


# ============================================================================================================== polariser
def thin_film_polarizer(fam: Family):
    with fam.figure("tfp-extinction", "Thin-film polariser (stack model, 56 deg): transmitted and reflected port energy against the input polarisation angle",
                    "T_total = T_p cos^2 + T_s sin^2 (Malus); T_t(p)/T_back + R_r(p) = 1 per polarisation (lossless stack)") as f:
        for lam in CARRIERS_M:
            res = {}
            for pol, ang in (("p", 0.0), ("s", 90.0)):
                tp = run([b.thin_film_polarizer("t", output="transmitted", aoi_deg=56, designWavelengthM=lam)], lam, pulse=pol_pulse(lam, ang, 1e-12))
                rf = run([b.thin_film_polarizer("t", output="reflected", aoi_deg=56, designWavelengthM=lam)], lam, pulse=pol_pulse(lam, ang, 1e-12))
                res[pol] = (tp.summary["totals"]["energyTransmission"], rf.summary["totals"]["energyTransmission"])
            nr = n_rel("FusedSilica", lam)
            th = math.radians(56)
            tt = math.asin(math.sin(th) / nr)
            for pol in ("p", "s"):
                Tback = 1 - reflectance(nr, 1.0, tt, pol)
                fam.check(f"TFP {pol}: T_transmitted-port / T_back + R_reflected-port = 1 {label_wl(lam)}", res[pol][0] / Tback + res[pol][1], 1.0, abs=2e-5,
                          formula="lossless coating; the back face is bare Fresnel")
            fam.check(f"TFP extinction T_p / T_s > 100 {label_wl(lam)}", min(res["p"][0] / max(res["s"][0], 1e-12), 100.0), 100.0, abs=1e-9, note="extinction ratio")
            xs, eng, ana = [], [], []
            for a in (0, 15, 30, 45, 60, 75, 90):
                rr = run([b.thin_film_polarizer("t", aoi_deg=56, designWavelengthM=lam)], lam, pulse=pol_pulse(lam, float(a), 1e-12))
                want = res["p"][0] * math.cos(math.radians(a)) ** 2 + res["s"][0] * math.sin(math.radians(a)) ** 2
                fam.check(f"TFP Malus {a} deg {label_wl(lam)}", rr.summary["totals"]["energyTransmission"], want, abs=2e-9)
                xs.append(a)
                eng.append(rr.summary["totals"]["energyTransmission"])
                ana.append(want)
            overlay(f, xs, eng, ana, label=label_wl(lam), xlabel="input polarisation angle from the p axis (deg)", ylabel="transmitted energy fraction", marker="o",
                    analytic_label="T_p cos^2 + T_s sin^2" if lam == CARRIERS_M[0] else None)
    # ideal model: Malus with Tp, Ts, and the axis
    for axis in (0.0, 25.0):
        for psi in (0.0, 25.0, 70.0):
            r = run([{"id": "t", "type": "thinFilmPolarizer", "model": "ideal", "transmissionP": 0.95, "transmissionS": 0.01, "axisDeg": axis}], pulse=pol_pulse(1030e-9, psi, 1e-12))
            c2 = math.cos(math.radians(psi - axis)) ** 2
            fam.check(f"TFP ideal axis {axis:g} input {psi:g} deg", r.summary["totals"]["energyTransmission"], 0.95 * c2 + 0.01 * (1 - c2), abs=1e-9, formula="T_p cos^2 + T_s sin^2")
    # Brewster-angle back face: a bare plate at its Brewster angle (the substrate of the polariser is cut for it)
    for lam in CARRIERS_M:
        nr = n_rel("FusedSilica", lam)
        thb = math.atan(nr)
        r = run([b.plate("p", "FusedSilica", 3e-3, aoi_deg=math.degrees(thb))], lam, pulse=pol_pulse(lam, 0.0, 1e-12))
        fam.check(f"plate at Brewster: T_p = 1 {label_wl(lam)}", r.summary["totals"]["energyTransmission"], 1.0, abs=3e-6)


# ====================================================================================================== saturable absorber
def sa_reference(res, kind, q0, qns, esat=None, tau_a=None, psat=None, kappa=None):
    """Power transmission of the pulse from the documented ODE / law, on the engine's own input power P(t)."""
    t = np.asarray(res["input_time_s"])
    P = np.asarray(res["input_power_w"])
    # refine: 8 sub-steps per sample with linear interpolation of P
    tf = np.linspace(t[0], t[-1], 8 * (len(t) - 1) + 1)
    Pf = np.interp(tf, t, P)
    dt = tf[1] - tf[0]
    if kind == "slow":
        q = np.empty_like(Pf)
        qq = q0
        for i, p in enumerate(Pf):
            a = 1.0 / tau_a + p / esat
            qss = (q0 / tau_a) / a
            qq = qss + (qq - qss) * math.exp(-a * dt)
            q[i] = qq
    elif kind == "fast":
        q = q0 / (1 + Pf / psat)
    else:
        q = np.maximum(0.0, q0 - kappa * Pf)
    return float((Pf * np.exp(-(q + qns))).sum() / Pf.sum())


def saturable_absorber(fam: Family):
    esat = 1e-9
    q0, qns = 0.2, 0.05
    G = b.grid(n=8192, window_s=5e-12)   # 0.6 fs sampling: the loss integral is converged (the default 8 fs grid is 4e-4 off)
    with fam.figure("sa-slow", "Slow saturable absorber (SESAM): pulse transmission against pulse energy / E_sat, recovery 1 ps and 1 ms (100 fs pulse)",
                    "dq/dt = -(q - q0)/tau_A - q P/E_sat, field x exp(-(q + q_ns)/2); T = integral P exp(-(q + q_ns)) dt / E, on the engine's own P(t)") as f:
        S = np.logspace(-2, 2, 9)
        for tau_a in (1e-12, 1e-3):
            eng, ana = [], []
            for s in S:
                r = run([b.saturable_absorber("s", "slow", q0, qns, recovery_time_s=tau_a, saturation_energy_j=esat)], 1030e-9, tau=100e-15, energy=s * esat, grid=G)
                eng.append(r.summary["totals"]["energyTransmission"])
                ana.append(sa_reference(r, "slow", q0, qns, esat=esat, tau_a=tau_a))
                fam.check(f"SESAM tau_A={tau_a:g} s, E/E_sat={s:.3g}", eng[-1], ana[-1], abs=1e-4, formula="ODE on P(t)")
            overlay(f, S, eng, ana, label=f"tau_A = {tau_a:g} s", xlabel="E / E_sat", ylabel="pulse power transmission", marker="o", analytic_label="ODE" if tau_a == 1e-12 else None)
    # short pulse, slow absorber, closed form: T = (1/S) integral_0^S exp(-q0 e^-s) ds (pulse much shorter than tau_A)
    r = run([b.saturable_absorber("s", "slow", 0.02, 0.0, recovery_time_s=1e-3, saturation_energy_j=esat)], 1030e-9, tau=100e-15, energy=1e-9, grid=G)
    ss = np.linspace(0, 1.0, 20001)
    yy = np.exp(-0.02 * np.exp(-ss))
    Tcf = float(((yy[1:] + yy[:-1]) / 2 * np.diff(ss)).sum())
    fam.check("SESAM short-pulse closed form T = (1/S) int exp(-q0 e^-s) ds, S = 1", r.summary["totals"]["energyTransmission"], Tcf, abs=1e-4,
              formula="energy-weighted loss of q0 e^-s; first order gives q0 (1 - e^-S)/S")
    fam.check("SESAM reports saturation parameter E/E_sat", val(r, 0, "saturationParameter"), 1.0, rel=1e-9)
    fam.check("SESAM reports low-signal loss q0 + q_ns", val(r, 0, "lowSignalLoss"), 0.02, rel=1e-9)
    # saturation fluence and spot radius: S = F_peak / F_sat with F_peak = 2 E / (pi w^2)
    r = run([b.saturable_absorber("s", "slow", 0.2, 0.0, saturation_fluence_j_per_m2=2.0, spot_radius_m=1e-4, recovery_time_s=1e-3)], 1030e-9, tau=100e-15, energy=1e-9)
    fam.check("SESAM E/E_sat from F_sat and spot radius", val(r, 0, "saturationParameter"), 2 * 1e-9 / (math.pi * (1e-4) ** 2 * 2.0), rel=1e-9,
              formula="F_peak / F_sat, F_peak = 2 E / (pi w^2)")

    with fam.figure("sa-fast", "Fast saturable absorber and Kerr-lens modulation: transmission against pulse energy (100 fs)",
                    "fast: q = q0 / (1 + P/P_sat);  kerrLens: q = max(0, q0 - kappa P);  T = integral P exp(-(q + q_ns)) dt / E") as f:
        psat = 1e4
        E = np.logspace(-10, -7, 7)
        eng, ana = [], []
        for e in E:
            r = run([b.saturable_absorber("s", "fast", 0.1, 0.02, saturation_power_w=psat)], 1030e-9, tau=100e-15, energy=float(e), grid=G)
            eng.append(r.summary["totals"]["energyTransmission"])
            ana.append(sa_reference(r, "fast", 0.1, 0.02, psat=psat))
            fam.check(f"fast absorber E={e:.2g} J", eng[-1], ana[-1], abs=1e-4, formula="q0 / (1 + P/P_sat)")
        overlay(f, E * 1e9, eng, ana, label="fast", xlabel="pulse energy (nJ)", ylabel="pulse power transmission", marker="o", analytic_label="analytic")
        kappa = 5e-6
        eng, ana = [], []
        for e in E:
            r = run([b.saturable_absorber("s", "kerrLens", 0.1, 0.0, kappa_per_w=kappa)], 1030e-9, tau=100e-15, energy=float(e), grid=G)
            eng.append(r.summary["totals"]["energyTransmission"])
            ana.append(sa_reference(r, "kerr", 0.1, 0.0, kappa=kappa))
            fam.check(f"Kerr-lens absorber E={e:.2g} J", eng[-1], ana[-1], abs=1e-4, formula="max(0, q0 - kappa P)")
        overlay(f, E * 1e9, eng, ana, label="kerrLens", xlabel="pulse energy (nJ)", ylabel="pulse power transmission", marker="s", analytic_label=None)


# ================================================================================================================ filter
def spectral_filters(fam: Family):
    lam_c = 1030e-9
    fw = 20e-9
    with fam.figure("filter-shapes", "Spectral filters on a 10 fs pulse: Gaussian and super-Gaussian (order 3) band-pass, FWHM 20 nm; output / input spectrum",
                    "T = exp(-ln2 (2 (w - wc) / dw)^(2k)), dw = 2 pi c FWHM / lam^2: half power at lam_c +- FWHM/2, T(lam) measured as S_out / S_in") as f:
        for order, name in ((1, "gaussian"), (3, "superGaussian")):
            model = {"kind": name, "centerWavelengthM": lam_c, "fwhmM": fw}
            if name == "superGaussian":
                model["order"] = order
            r = run([b.filter_element("f", model)], 1030e-9, tau=10e-15, grid=b.grid(n=8192, window_s=20e-12))
            w = np.asarray(r["output_omega_rad_s"])
            si, so = np.asarray(r["input_spectrum"]), np.asarray(r["output_spectrum"])
            m = si > 1e-3 * si.max()
            lam = 2 * math.pi * C / w[m]
            T = so[m] / si[m]
            dw = 2 * math.pi * C * fw / lam_c ** 2
            wc = 2 * math.pi * C / lam_c
            Ta = np.exp(-math.log(2) * (2 * (w[m] - wc) / dw) ** (2 * order))
            fam.check(f"{name} filter T(lambda) max error", float(np.max(np.abs(T - Ta))), 0.0, abs=1e-6, formula="power transmission")
            order_ = np.argsort(lam)
            width = fwhm(lam[order_], T[order_])
            fam.check(f"{name} filter FWHM in wavelength", width, fw, rel=3e-4, unit="m", formula="fwhmM (first-order conversion: (FWHM/2 lam)^2 ~ 1e-4)")
            fam.check(f"{name} filter peak transmission", float(T.max()), 1.0, abs=1e-6)
            overlay(f, lam * 1e9, T, Ta, label=name, xlabel="wavelength (nm)", ylabel="power transmission", analytic_label="analytic" if order == 1 else None)
        # a broad filter: FWHM in wavelength is only first-order in the conversion to frequency
        broad = 200e-9
        r = run([b.filter_element("f", {"kind": "gaussian", "centerWavelengthM": lam_c, "fwhmM": broad})], 1030e-9, tau=5e-15, grid=b.grid(n=8192, window_s=20e-12))
        w = np.asarray(r["output_omega_rad_s"])
        si, so = np.asarray(r["input_spectrum"]), np.asarray(r["output_spectrum"])
        m = si > 1e-4 * si.max()
        lam = 2 * math.pi * C / w[m]
        o = np.argsort(lam)
        width = fwhm(lam[o], (so[m] / si[m])[o])
        fam.check("gaussian filter 200 nm: FWHM in wavelength (exact)", width, broad, rel=1.5e-2, unit="m",
                  note="the docs say FWHM in wavelength: the filter is Gaussian in omega with FWHM_w = 2 pi c FWHM_lam / lam_c^2, so the exact wavelength FWHM is FWHM/(1 - delta^2), delta = FWHM/(2 lam_c) = 0.097: +0.94 %")
    # table: transmission and group delay
    wl = np.linspace(700e-9, 1500e-9, 1601)
    lam_a = lam_c
    t_tab = 0.2 + 0.7 * np.exp(-((wl - 1030e-9) / 40e-9) ** 2)
    r = run([b.filter_element("f", {"kind": "table", "wavelengthsM": list(wl), "transmission": list(t_tab)})], 1030e-9, tau=10e-15, grid=b.grid(n=8192, window_s=20e-12))
    w = np.asarray(r["output_omega_rad_s"])
    si, so = np.asarray(r["input_spectrum"]), np.asarray(r["output_spectrum"])
    m = si > 1e-3 * si.max()
    lam = 2 * math.pi * C / w[m]
    Ta = 0.2 + 0.7 * np.exp(-((lam - 1030e-9) / 40e-9) ** 2)
    fam.check("table filter T(lambda) max error (1600-interval table, linear interpolation)", float(np.max(np.abs(so[m] / si[m] - Ta))), 0.0, abs=2e-4)
    # group delay table: tau(w) = tau0 + beta (w - w0): constant delay + GDD beta
    for lam in CARRIERS_M:
        wc = 2 * math.pi * C / lam
        wlt = np.linspace(lam * 0.8, lam * 1.25, 121)
        wt = 2 * math.pi * C / wlt
        tau0, beta = 3e-12, 400e-30
        gd = tau0 + beta * (wt - wc)
        r = run([b.filter_element("f", {"kind": "table", "wavelengthsM": list(wlt), "transmission": [1.0] * len(wlt), "groupDelayS": list(gd)})], lam, tau=100e-15)
        w0 = omega0(r)
        ww = np.asarray(r["output_omega_rad_s"])
        S = np.asarray(r["output_spectrum"])
        ph = np.unwrap(np.asarray(r["output_phase_x_rad"]) - np.asarray(r["input_phase_x_rad"]))
        c = poly_phase(ww, ph, S, w0, 3)
        fam.check(f"table groupDelayS: GDD (field fit) {label_wl(lam)}", c[2], beta, rel=2e-3, unit="s^2", formula="d(tau)/dw")
        fam.check(f"table groupDelayS: arrival time {label_wl(lam)}", r.summary["totals"]["arrivalTimeS"], tau0 + beta * (w0 - wc), rel=1e-4, unit="s",
                  formula="tau0 + beta (w0 - wc) at the grid centre (the field itself is in the co-moving frame)")


# ================================================================================================================== mask
def zone_plate_mask(fam: Family):
    w = 1e-3
    grid = dict(points=4096, radius_m=3e-3, spectral_samples=1)

    def src(lam):
        return b.gap("src", 0.0, medium=VAC, transverse=b.transverse("radial", **grid, initial_profile=b.initial_profile("gaussian", radius_m=w)))

    with fam.figure("mask-zoneplate", "Binary amplitude zone plate on a Gaussian beam (w = 1 mm): reported transmission against the ring sum of the open zones",
                    "T = sum over open zones [exp(-2 r_in^2 / w^2) - exp(-2 r_out^2 / w^2)], r_n^2 = n lam f, centre zone open; phase zone plate: T = 1") as f:
        fls = [0.2, 0.4, 0.8]
        for lam in CARRIERS_M:
            eng, ana = [], []
            for fl in fls:
                r = uf.run(b.system("zp", [src(lam), b.mask("zp", "zonePlate", focal_length_m=fl, design_wavelength_m=lam),
                                           b.gap("g", 0.0, medium=VAC, transverse=b.transverse("radial", **grid))]), b.run_spec(b.cw(lam, 1.0)))
                T = val(r, 1, "transmission")
                u = lambda n: math.exp(-2 * n * lam * fl / w ** 2)
                Ta = sum(u(n) - u(n + 1) for n in range(0, 400, 2))
                eng.append(T)
                ana.append(Ta)
                fam.check(f"zone plate T f={fl} m {label_wl(lam)}", T, Ta, abs=3e-3, formula="ring sum; 4096 samples across 3 mm, cell-averaged edges")
            overlay(f, fls, eng, ana, label=label_wl(lam), xlabel="focal length (m)", ylabel="transmission", marker="o", analytic_label="ring sum" if lam == CARRIERS_M[0] else None)
        lam = 1030e-9
        r = uf.run(b.system("zp", [src(lam), b.mask("zp", "zonePlate", focal_length_m=0.2, binary_phase=True), b.gap("g", 0.0, medium=VAC, transverse=b.transverse("radial", **grid))]),
                   b.run_spec(b.cw(lam, 1.0)))
        dr = grid["radius_m"] / grid["points"]
        loss = sum(2 / 3 * 4 * math.sqrt(n * lam * 0.2) * dr / w ** 2 * math.exp(-2 * n * lam * 0.2 / w ** 2) for n in range(1, 400))
        fam.check("phase zone plate transmission", val(r, 1, "transmission"), 1.0 - loss, abs=0.3 * loss + 1e-4,
                  formula="pi edges in a cell average to |2f-1|: T = 1 - (2/3) sum_edges 4 r dr / w^2 exp(-2 r^2/w^2) (a sampling loss, falls with the grid step)")


# ============================================================================================================== wavefront
def wavefront_zernike(fam: Family):
    lam_ref = 1030e-9
    R = 2e-3
    w = 1e-3
    m = w * w / (2 * R * R)             # E[rho^2] under the Gaussian intensity
    var = {
        4: lambda a: 12 * a * a * m * m,                                          # sqrt(3)(2 rho^2 - 1)
        11: lambda a: 5 * a * a * (720 * m ** 4 - 288 * m ** 3 + 36 * m * m),     # sqrt(5)(6 rho^4 - 6 rho^2 + 1)
        5: lambda a: 6 * a * a * m * m,                                           # sqrt(6) rho^2 sin 2 theta
        6: lambda a: 6 * a * a * m * m,
    }
    with fam.figure("wavefront-rms", "wavefront element on a tracked Gaussian beam (w = 1 mm, R = 2 mm): reported rms and Strehl against Gaussian-weighted Zernike variances",
                    "sigma = a lam sqrt(Var); Var(Z4) = 12 m^2, Var(Z11) = 5 (720 m^4 - 288 m^3 + 36 m^2), Var(Z5, Z6) = 6 m^2, m = w^2 / 2R^2; Strehl = exp(-(2 pi sigma / lam)^2)") as f:
        coeffs = [0.02, 0.05, 0.1, 0.15, 0.2]
        for z in (4, 11, 5):
            eng, ana = [], []
            for a in coeffs:
                cz = [0.0] * z
                cz[z - 1] = a
                for lam in CARRIERS_M if a == 0.1 else (1030e-9,):
                    r = run([b.wavefront("wf", cz, radius_m=R, referenceWavelengthM=lam_ref)], lam, beam=gbeam(w), tau=1e-12)
                    sig = val(r, 0, "wavefrontRmsM")
                    sa = a * lam_ref * math.sqrt(var[z](1.0))
                    fam.check(f"wavefront Z{z} a={a} rms {label_wl(lam)}", sig, sa, rel=5e-4, unit="m", formula="Gaussian-weighted variance",
                              note="engine integrates numerically over a finite disc: -1e-4 (Z4), -2.5e-4 (Z11) relative")
                    fam.check(f"wavefront Z{z} a={a} Strehl {label_wl(lam)}", val(r, 0, "strehlEstimate"), math.exp(-(2 * math.pi * sig / lam) ** 2), rel=2e-5,
                              formula="Marechal, at the requested carrier")
                    if lam == 1030e-9:
                        eng.append(sig)
                        ana.append(sa)
            overlay(f, coeffs, np.array(eng) * 1e9, np.array(ana) * 1e9, label=f"Z{z}", xlabel="Zernike coefficient (waves rms at 1030 nm over r = 2 mm)", ylabel="wavefront rms (nm)",
                    residual="rel", marker="o", analytic_label="analytic" if z == 4 else None)
        for z, nm in ((1, "piston"), (2, "x tilt"), (3, "y tilt")):
            cz = [0.0] * z
            cz[z - 1] = 0.3
            r = run([b.wavefront("wf", cz, radius_m=R)], 1030e-9, beam=gbeam(w), tau=1e-12)
            fam.check(f"wavefront {nm} alone: rms after piston and tilt removal = 0", val(r, 0, "wavefrontRmsM"), 0.0, abs=1e-12, unit="m")

    # defocus acts as a lens: positive Z4 raises the OPD at the edge = diverging, f = -R^2 / (4 sqrt(3) a lam_ref); same OPD at every carrier
    a, R2, w2 = 0.4, 1.5e-3, 1e-3
    fl = -R2 ** 2 / (4 * math.sqrt(3) * a * lam_ref)
    grid = dict(points=1024, radius_m=6e-3, spectral_samples=1)
    with fam.figure("wavefront-defocus", "Z4 = 0.4 waves (R = 1.5 mm) on a Gaussian beam (w = 1 mm, radial grid): beam radius after 0.5 m and 1 m against the equivalent diverging lens",
                    "OPD W = a lam sqrt(3) (2 rho^2 - 1) is a lens of f = -R^2 / (4 sqrt(3) a lam_ref) (positive edge OPD diverges), whatever the carrier") as f:
        zs = [0.25, 0.5, 0.75, 1.0]
        for lam in CARRIERS_M:
            els = [b.gap("src", 0.0, medium=VAC, transverse=b.transverse("radial", **grid, initial_profile=b.initial_profile("gaussian", radius_m=w2))),
                   b.wavefront("wf", [0, 0, 0, a], radius_m=R2, referenceWavelengthM=lam_ref)]
            inst, prev = [], 0.0
            for i, z in enumerate(zs):
                els.append(b.gap(f"g{i}", z - prev, medium=VAC, transverse=b.transverse("radial", **grid)))
                inst.append(b.instrument("beamProfile", at=f"g{i}", name=f"p{i}"))
                prev = z
            r = uf.run(b.system("wf", els), b.run_spec(b.cw(lam, 1.0), instruments=inst))
            eng = np.array([r.instrument(f"p{i}")["radiusXM"] for i in range(len(zs))])
            q = q_after(1j * math.pi * w2 ** 2 / lam, lensM(fl))
            ana = np.array([w_of_q(q_after(q, gapM(z)), lam) for z in zs])
            fam.check(f"Z4 defocus beam radius vs diverging lens, max rel. error {label_wl(lam)}", float(np.max(np.abs(eng / ana - 1))), 0.0, abs=1e-3,
                      formula="thin lens f = -R^2 / (4 sqrt(3) a lam_ref)")
            overlay(f, zs, eng * 1e3, ana * 1e3, label=label_wl(lam), xlabel="propagation distance (m)", ylabel="w (mm)", marker="o", analytic_label="lens" if lam == CARRIERS_M[0] else None)


# ============================================================================================================== apertures
def apertures(fam: Family):
    from math import erf
    w = 1e-3
    with fam.figure("aperture-rect", "Rectangular hard aperture on a tracked Gaussian beam (w = 1 mm): half widths 0.4 and 0.8 mm, decentred in x",
                    "T = [erf(sqrt2 (a_x - d)/w) + erf(sqrt2 (a_x + d)/w)] / 2 x erf(sqrt2 a_y / w)") as f:
        xs, eng, ana = [], [], []
        for d in (0.0, 0.1e-3, 0.2e-3, 0.4e-3, 0.8e-3):
            ax, ay = 0.4e-3, 0.8e-3
            r = run([b.aperture("a", half_width_x_m=ax, half_width_y_m=ay, decenter_x_m=d if d else None)], 1030e-9, beam=gbeam(w), tau=1e-12)
            T = 0.5 * (erf(math.sqrt(2) * (ax - d) / w) + erf(math.sqrt(2) * (ax + d) / w)) * erf(math.sqrt(2) * ay / w)
            xs.append(d * 1e3)
            eng.append(r.summary["totals"]["energyTransmission"])
            ana.append(T)
            fam.check(f"rectangular aperture decentre {d * 1e3:g} mm", eng[-1], T, abs=1e-6, formula="separable error functions")
        overlay(f, xs, eng, ana, label="engine", xlabel="decentre (mm)", ylabel="transmission", marker="o", analytic_label="analytic")
    # round aperture on an astigmatic beam (w_x = 0.5 mm, w_y = 1.2 mm from a 2-axis beam model): numeric quadrature
    wx, wy = 0.5e-3, 1.2e-3
    for a in (0.4e-3, 0.8e-3, 1.5e-3):
        beam = {"kind": "gaussian", "w0xM": wx, "w0yM": wy, "wavelengthM": 1030e-9}
        s = b.system("ap", [b.aperture("a", radius_m=a)])
        spec = b.run_spec(b.pulse(1030e-9, 1e-12, energy=1e-9), physics={"nonlinear": False})
        spec["inputBeam"] = beam
        r = uf.run(s, spec)
        n = 2000
        x = (np.arange(n) + 0.5) * (2 * a / n) - a
        X, Y = np.meshgrid(x, x)
        inside = X ** 2 + Y ** 2 <= a * a
        I = np.exp(-2 * X ** 2 / wx ** 2 - 2 * Y ** 2 / wy ** 2)
        T = float((I * inside).sum() * (2 * a / n) ** 2 * 2 / (math.pi * wx * wy))
        fam.check(f"round aperture a={a * 1e3:g} mm on w = 0.5 x 1.2 mm", r.summary["totals"]["energyTransmission"], T, rel=2e-3, formula="2D quadrature of the elliptical Gaussian over the disc")
    # grating-made astigmatic beam, then a round aperture
    lp, th = 1200.0, 30.0
    co = math.sqrt(1 - (math.sin(math.radians(th)) - 1030e-9 * lp * 1e3) ** 2)
    wxg = W0 * co / math.cos(math.radians(th))
    a = 0.8e-3
    r = run([b.grating("G", lp, th, order=-1), b.aperture("a", radius_m=a)], 1030e-9, beam=gbeam(W0), tau=1e-12)
    n = 2000
    x = (np.arange(n) + 0.5) * (2 * a / n) - a
    X, Y = np.meshgrid(x, x)
    I = np.exp(-2 * X ** 2 / wxg ** 2 - 2 * Y ** 2 / W0 ** 2)
    T = float((I * (X ** 2 + Y ** 2 <= a * a)).sum() * (2 * a / n) ** 2 * 2 / (math.pi * wxg * W0))
    fam.check("round aperture behind a grating (w_x = 0.78 mm, w_y = 1 mm)", r.summary["totals"]["energyTransmission"], T, rel=2e-3, formula="quadrature with the anamorphic beam")


# ======================================================================================================= topology
def branched_chain(els):
    return [b.link(e, els[i + 1]["id"] if i + 1 < len(els) else "end") for i, e in enumerate(els)]


def sequential_equals_branched(fam: Family):
    for lam in CARRIERS_M:
        chains = {
            "nd + faraday + rotator + filter": [b.nd_filter("nd", 0.5, material="FusedSilica", thickness_m=2e-3),
                                                 {"id": "fr", "type": "faradayRotator", "rotationDeg": 20.0}, {"id": "ro", "type": "rotator", "angleDeg": 10.0},
                                                 b.filter_element("fl", {"kind": "gaussian", "centerWavelengthM": lam, "fwhmM": 0.05 * lam})],
            "plate ghosts + delay + SA": [b.plate("pl", "FusedSilica", 2e-3, aoi_deg=20, ghosts=True), b.delay("dl", delay_s=1e-12),
                                          b.saturable_absorber("sa", "slow", 0.1, 0.01, saturation_energy_j=1e-9)],
        }
        for name, els in chains.items():
            seq = run(els, lam, tau=100e-15)
            br = run(branched_chain(els), lam, tau=100e-15, branched=True)
            fs = np.asarray(seq["output_ax_re"]) + 1j * np.asarray(seq["output_ax_im"])
            fb = np.asarray(br["output_ax_re"]) + 1j * np.asarray(br["output_ax_im"])
            fam.check(f"Sequential = Branched field: {name} {label_wl(lam)}", float(np.max(np.abs(fs - fb)) / np.max(np.abs(fs))), 0.0, abs=1e-12, formula="max|A_seq - A_br| / max|A|")
    # tracked-beam elements: prism pair and lenses
    lam = 1030e-9
    thb, apex = prism_brewster("FusedSilica", lam)
    tho = snell_prism_exit("FusedSilica", lam, apex, thb)
    els = [b.lens("l", 1.0, medium=VAC), b.prism("p1", "FusedSilica", math.degrees(apex), math.degrees(thb), 0.0, medium=VAC), b.gap("g", 0.2, medium=VAC),
           b.prism("p2", "FusedSilica", math.degrees(apex), math.degrees(tho), 0.0, medium=VAC)]
    seq = run(els, lam, beam=gbeam(), tau=100e-15)
    br = run(branched_chain(els), lam, beam=gbeam(), tau=100e-15, branched=True)
    fam.check("Sequential = Branched GDD: lens + prism pair", br.summary["totals"]["systemGddS2"], seq.summary["totals"]["systemGddS2"], rel=1e-12, unit="s^2")


def main() -> int:
    fam = Family("optics_misc", "Miscellaneous optics: lens, mirrors, prism, grating, ndFilter, coatings, delay, rotators, polariser, absorber, filter, mask, wavefront, ghosts, aperture")
    fns = (lens_abcd, thick_lens, mirror_abcd, prism_single, prism_pair, grating_single, grating_pair_single, nd_filter, ghosts, fresnel_interface, coatings, delay_element,
           rotator, faraday, thin_film_polarizer, saturable_absorber, spectral_filters, zone_plate_mask, wavefront_zernike, apertures, sequential_equals_branched)
    only = [s for s in os.environ.get("OM_ONLY", "").split(",") if s]
    for fn in fns:
        if only and fn.__name__ not in only:
            continue
        fam.guard(fn.__name__, lambda fn=fn: fn(fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
