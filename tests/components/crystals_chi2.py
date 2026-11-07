"""χ(2) mixing alone: SFG, DFG, OPA/OPCPA (gain, idler), QPM, non-collinear, BBO angles (type I and II) and walk-off.

What it does: runs the `crystal` element for the processes `sfg`, `dfg`, `opa` and compares with closed forms that do not use
the engine. Most checks use a dispersion-free "ideal" uniaxial crystal (n = 1.7 for every wavelength, d = 2 pm/V, type 0, phase
matched at any colour) with plane-wave coupling, where the coupled-wave equations have exact solutions; the real-crystal checks
(BBO angles and walk-off, MgO:LN QPM period) use Sellmeier equations written out here (Tamosauskas 2018, Gayer 2008).

Sections (one function each):
  opa_gain          cosh² small-signal gain, idler energy sinh² (ωi/ωs), photon conservation; pumps at 515 nm, 1030 nm, 2 µm
  sfg_mixing        low conversion E_sum = (ω3 Γ O L)² ∫Pa Pb dt; equal photon fluxes at high conversion: N_sum/N0 = <tanh²(g B0 L)>
  dfg_mixing        idler energy = OPA idler; Manley-Rowe (pump photons lost = idler photons made)
  noncollinear      mismatched parametric gain G = 1 + (Γ/g)² sinh²(gL) with the vector mismatch of the crossed geometry
  bbo_angles        BBO type-I OPA / SFG and type-II SHG phase-matching angles, indices, group indices, walk-off, GVM
  qpm               MgO:LN period 2π/Δk, idler energy with d → 2d33/π, sinc² of the detuned period

Conventions (docs/reference/crystals.md): amplitudes in √W, Γ = deff √(2/(ε0 c³ n1 n2 n3)), O = √(2/(π w²)) for the plane-wave
overlap at the on-axis intensity of a Gaussian of radius w, dA3/dz = i ω3 Γ O A1 A2 e^{iΔk z}.

Run: python tests/components/crystals_chi2.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
"""
from __future__ import annotations

import math
import sys

from _common import C, Family, b, label_wl, np, overlay, uf
from crystals import bbo_tamosauskas

EPS0 = 8.8541878128e-12
HBAR = 1.054571817e-34
GP = 2 * math.sqrt(math.log(2) / math.pi)          # peak power = GP E / tau
N_ID, NO_ID, D_ID = 1.7, 1.8, 2.0                  # ideal crystal: n_e, n_o, d [pm/V]
W_BEAM = 100e-6


def ideal():
    return {"name": "ideal", "no": {"kind": "constant", "value": NO_ID}, "ne": {"kind": "constant", "value": N_ID},
            "deffPmPerV": D_ID, "n2": 0.0}


def gamma_c(deff_pm, n1, n2, n3):
    return deff_pm * 1e-12 * math.sqrt(2 / (EPS0 * C ** 3 * n1 * n2 * n3))


OVL = math.sqrt(2 / (math.pi * W_BEAM ** 2))        # plane-wave overlap factor
W_OF = lambda lam: 2 * math.pi * C / lam            # noqa: E731


def peak_power(e, tau):
    return GP * e / tau


def pulse_average(f, p0, tau, n=20001):
    """∫ P f(P) dt / ∫ P dt over a Gaussian pulse of peak power p0."""
    t = np.linspace(-3 * tau, 3 * tau, n)
    p = p0 * np.exp(-4 * math.log(2) * t ** 2 / tau ** 2)
    return float((p * f(p)).sum() / p.sum())


def values(res, i=0):
    return res.summary["elements"][i]["values"]


def run_el(el, lam, tau, energy):
    return uf.run(b.system("x", [el]), b.run_spec(b.pulse(lam, tau, energy=energy), physics={"selfSteepening": False}))


def ideal_crystal(process, length, aux, output, interaction_extra=None, **kw):
    return b.crystal("x", ideal(), length, process=process, type="0", cut={"thetaDeg": 90, "phiDeg": 0}, aux=aux, output=output,
                     overlap={"model": "planeWave", "radiusM": W_BEAM}, chi3=False, **kw)


def aux(role, lam, tau, energy, delay=0.0):
    return b.aux_beam(role, b.pulse(lam, tau, energy=energy), radius_m=W_BEAM, delay_s=delay)


# ============================================================================================================== OPA

def opa_gain(fam: Family):
    L = 5e-3
    with fam.figure("opa-gain-energy", "OPA, ideal crystal, pump 1030 nm / signal 1550 nm / 1 ps: signal gain against pump energy",
                    "G = ⟨cosh²(Γ O √(ωs ωi P(t)) L)⟩_P,  idler: (ωi/ωs) ⟨sinh²(...)⟩_P") as f:
        for lp in (515e-9, 1030e-9, 2e-6):
            ls = 1.5 * lp
            wp, ws = W_OF(lp), W_OF(ls)
            wi = wp - ws
            li = 2 * math.pi * C / wi
            gam = gamma_c(D_ID, N_ID, N_ID, N_ID) * OVL
            # the analytic gain uses the carrier frequencies: it needs the bandwidth small against the idler frequency (≤ 3 %)
            for tau in (1e-12, 300e-15 if lp < 1.5e-6 else 600e-15):
                if lp == 1030e-9 and tau == 1e-12:
                    targets = [0.5, 1.0, 1.5, 2.0, 2.5]
                else:
                    targets = [2.0]
                eng, ana, ep_list = [], [], []
                for x in targets:
                    p0 = (x / (gam * L)) ** 2 / (ws * wi)             # peak pump power giving exponent x at the peak
                    ep = p0 * tau / GP
                    es = 1e-15
                    el = ideal_crystal("opa", L, [aux("pump", lp, tau, ep)], "signal", record=["idler", "pump"])
                    r = run_el(el, ls, tau, es)
                    v = values(r)
                    gain = pulse_average(lambda p: np.cosh(gam * np.sqrt(ws * wi * p) * L) ** 2, p0, tau)
                    idl = pulse_average(lambda p: wi / ws * np.sinh(gam * np.sqrt(ws * wi * p) * L) ** 2, p0, tau)
                    tag = f"pump {label_wl(lp)} {tau * 1e12:g} ps, g0L={x:g}" if tau == 1e-12 else f"pump {label_wl(lp)} {tau * 1e15:g} fs, g0L={x:g}"
                    # 1e-3: the carriers are snapped to grid bins (omega_s omega_i moves at 1e-4, amplified by the gain)
                    fam.check(f"OPA signal gain {tag}", v["energy_signal_J"] / es, gain, rel=1e-3, formula="⟨cosh²(Γ O √(ωs ωi P) L)⟩")
                    fam.check(f"OPA idler energy {tag}", v["energy_idler_J"] / es, idl, rel=1e-3, formula="(ωi/ωs) ⟨sinh²(...)⟩")
                    fam.check(f"OPA Manley-Rowe residual {tag}", abs(v["manleyRoweResidual"]), 0.0, abs=1e-8, formula="engine's spectrally resolved photon bookkeeping")
                    # photons: signal gain = idler made, pump lost
                    ns = (v["energy_signal_J"] - es) / (HBAR * ws)
                    ni = v["energy_idler_J"] / (HBAR * wi)
                    npump = (ep - v["energy_pump_J"]) / (HBAR * wp)
                    fam.check(f"OPA photons signal gained = idler made {tag}", ns, ni, rel=2e-3, formula="Manley-Rowe: ΔN_s = N_i")
                    fam.check(f"OPA photons pump lost = idler made {tag}", npump, ni, rel=2e-3, formula="Manley-Rowe: -ΔN_p = N_i")
                    if tau == 1e-12 and lp == 1030e-9:
                        eng.append(v["energy_signal_J"] / es)
                        ana.append(gain)
                        ep_list.append(ep * 1e6)
                if len(eng) > 1:
                    overlay(f, ep_list, eng, ana, label="1030 → 1550 + 3070 nm", xlabel="pump energy (µJ)", ylabel="signal gain", marker="o", residual="rel",
                            analytic_label="⟨cosh²⟩")
                    if f.axes is not None:
                        f.axes[0].set_yscale("log")


# ============================================================================================================== SFG

def sfg_mixing(fam: Family):
    L = 2e-3
    with fam.figure("sfg-low", "SFG, ideal crystal, 1 ps: sum-frequency energy at low conversion against (ω3 Γ O L)² ∫Pa Pb dt",
                    "E_sum = (ω3 Γ O L)² Pa0 Pb0 τ √(π/(8 ln2)) for two equal Gaussians; both inputs weak (g B0 L = 0.01 ... 0.04)") as f:
        for la, lb in ((1030e-9, 1550e-9), (515e-9, 1030e-9), (2e-6, 1030e-9)):
            l3 = 1 / (1 / la + 1 / lb)
            wa, wb, w3 = W_OF(la), W_OF(lb), W_OF(l3)
            gam = gamma_c(D_ID, N_ID, N_ID, N_ID)
            g = gam * OVL * math.sqrt(wa * wb * w3)
            for tau in (1e-12, 300e-15):
                xs = [0.01, 0.02, 0.04]
                eng, ana = [], []
                for x in xs:
                    pa0 = wa * (x / (g * L)) ** 2                    # g B0 L = x for both inputs (equal photon fluxes)
                    pb0 = pa0 * wb / wa
                    ea, eb = pa0 * tau / GP, pb0 * tau / GP
                    el = ideal_crystal("sfg", L, [aux("b", lb, tau, eb)], "sum")
                    r = run_el(el, la, tau, ea)
                    v = values(r)
                    want = (w3 * gam * OVL * L) ** 2 * pa0 * pb0 * tau * math.sqrt(math.pi / (8 * math.log(2)))
                    eng.append(v["energy_sum_J"])
                    ana.append(want)
                    fam.check(f"SFG energy {label_wl(la)} + {label_wl(lb)} {tau * 1e12:g} ps, g B0 L={x:g}", v["energy_sum_J"], want, rel=2e-3, unit="J",
                              formula="(ω3 Γ O L)² ∫Pa Pb dt (undepleted)")
                if la == 1030e-9 and tau == 1e-12:
                    overlay(f, xs, np.array(eng) * 1e15, np.array(ana) * 1e15, label=f"{label_wl(la)} + 1550 nm → {l3 * 1e9:.0f} nm",
                            xlabel="g B0 L of each input", ylabel="E_sum (fJ)", marker="o", residual="rel", analytic_label="analytic (∝ x⁴)")
    with fam.figure("sfg-depleted", "SFG, ideal crystal, equal photon fluxes: photon conversion against ⟨tanh²(g B0 L)⟩, 1 ps",
                    "B = A/√ω: dB3/dz = i Γ O √(ω1 ω2 ω3) B1 B2, |B1| = |B2| = B0 ⇒ B3 = B0 tanh(g B0 z); N_sum/N0 = ⟨tanh²(g √(P_a/ω_a) L)⟩_P") as f:
        for la, lb in ((1030e-9, 1550e-9), (515e-9, 1030e-9), (2e-6, 1030e-9)):
            l3 = 1 / (1 / la + 1 / lb)
            wa, wb, w3 = W_OF(la), W_OF(lb), W_OF(l3)
            g = gamma_c(D_ID, N_ID, N_ID, N_ID) * OVL * math.sqrt(wa * wb * w3)
            tau = 1e-12
            xs = [0.5, 1.0, 1.5, 2.5]
            eng, ana = [], []
            for x in xs:
                # B0² at the peak = Pa0 / wa; choose g B0 L = x
                pa0 = wa * (x / (g * L)) ** 2
                pb0 = pa0 * wb / wa
                ea, eb = pa0 * tau / GP, pb0 * tau / GP
                el = ideal_crystal("sfg", L, [aux("b", lb, tau, eb)], "sum")
                r = run_el(el, la, tau, ea)
                v = values(r)
                n0 = ea / (HBAR * wa)
                nsum = v["energy_sum_J"] / (HBAR * w3)
                want = pulse_average(lambda p: np.tanh(g * np.sqrt(p / wa) * L) ** 2, pa0, tau)
                fam.check(f"SFG photon conversion {label_wl(la)}+{label_wl(lb)} g B0 L={x:g}", nsum / n0, want, rel=2e-3, formula="⟨tanh²⟩ (exact, with depletion)")
                na = (ea - v["energy_a_J"]) / (HBAR * wa)
                nb = (eb - v["energy_b_J"]) / (HBAR * wb)
                fam.check(f"SFG photons a lost = sum made {label_wl(la)}+{label_wl(lb)} g B0 L={x:g}", na, nsum, rel=2e-3, formula="Manley-Rowe: ΔN_a = N_sum")
                fam.check(f"SFG photons b lost = sum made {label_wl(la)}+{label_wl(lb)} g B0 L={x:g}", nb, nsum, rel=2e-3, formula="Manley-Rowe: ΔN_b = N_sum")
                eng.append(nsum / n0)
                ana.append(want)
            overlay(f, xs, eng, ana, label=f"{label_wl(la)} + {label_wl(lb)}", xlabel="g B0 L at the pulse peak", ylabel="photon conversion", marker="o",
                    residual="rel", analytic_label="⟨tanh²⟩" if la == 1030e-9 else None)


# ============================================================================================================== DFG

def dfg_mixing(fam: Family):
    L = 5e-3
    for lp in (515e-9, 1030e-9, 2e-6):
        ls = 1.5 * lp
        wp, ws = W_OF(lp), W_OF(ls)
        wi = wp - ws
        gam = gamma_c(D_ID, N_ID, N_ID, N_ID) * OVL
        tau = 1e-12
        x = 1.2
        p0 = (x / (gam * L)) ** 2 / (ws * wi)
        ep = p0 * tau / GP
        es = 1e-12
        el = ideal_crystal("dfg", L, [aux("signal", ls, tau, es)], "idler", mainRole="pump")
        r = run_el(el, lp, tau, ep)
        v = values(r)
        idl = pulse_average(lambda p: wi / ws * np.sinh(gam * np.sqrt(ws * wi * p) * L) ** 2, p0, tau) * es
        fam.check(f"DFG idler energy pump {label_wl(lp)}", v["energy_idler_J"], idl, rel=1e-3, unit="J", formula="(ωi/ωs) E_s ⟨sinh²(Γ O √(ωs ωi P) L)⟩")
        ni = v["energy_idler_J"] / (HBAR * wi)
        npump = (ep - v["energy_pump_J"]) / (HBAR * wp)
        fam.check(f"DFG pump photons lost = idler photons {label_wl(lp)}", npump, ni, rel=2e-3, formula="Manley-Rowe")
        fam.check(f"DFG Manley-Rowe residual {label_wl(lp)}", abs(v["manleyRoweResidual"]), 0.0, abs=1e-8)
        # the same crystal as an OPA (signal main) makes the same idler
        el2 = ideal_crystal("opa", L, [aux("pump", lp, tau, ep)], "signal", record=["idler"])
        r2 = run_el(el2, ls, tau, es)
        fam.check(f"DFG idler = OPA idler {label_wl(lp)}", v["energy_idler_J"], values(r2)["energy_idler_J"], rel=2e-3, unit="J",
                  formula="the two process names describe the same χ(2) interaction")


# ============================================================================================================== non-collinear

def noncollinear(fam: Family):
    L, tau, ep, es = 5e-3, 1e-12, 1e-6, 1e-15
    lp, ls = 1030e-9, 1550e-9
    wp, ws = W_OF(lp), W_OF(ls)
    wi = wp - ws
    gam = gamma_c(D_ID, N_ID, N_ID, N_ID) * OVL

    def ne(theta):
        return 1 / math.sqrt((math.cos(theta) / NO_ID) ** 2 + (math.sin(theta) / N_ID) ** 2)

    with fam.figure("nopa-gain-angle", "Non-collinear OPA, ideal crystal: signal gain against the pump-signal angle",
                    "G = 1 + (Γ/g)² sinh²(gL), g² = Γ² - (Δk/2)²,  Δk = k_s + √(k_i² - (k_p sin α)²) - k_p cos α (e index of each direction)") as f:
        alphas = [0.0, 0.2, 0.4, 0.6]
        eng, ana, dks = [], [], []
        for adeg in alphas:
            inter = {"noncollinearDeg": adeg} if adeg else {}
            el = b.crystal("x", ideal(), L, process="opa", type="0", cut={"thetaDeg": 90, "phiDeg": 0}, noncollinear=adeg if adeg else None,
                           aux=[aux("pump", lp, tau, ep)], output="signal", overlap={"model": "planeWave", "radiusM": W_BEAM}, chi3=False)
            r = run_el(el, ls, tau, es)
            v = values(r)
            a = math.radians(adeg)
            th = math.pi / 2
            kp, ks = ne(th + a) * wp / C, ne(th) * ws / C
            beta, ki = 0.0, 0.0
            for _ in range(30):
                ki = ne(th + beta) * wi / C
                beta = math.asin(kp * math.sin(a) / ki)
            dk = ks + math.sqrt(ki ** 2 - (kp * math.sin(a)) ** 2) - kp * math.cos(a)

            def G(p, dk=dk):
                G2 = gam ** 2 * ws * wi * p
                g2 = G2 - dk ** 2 / 4
                pos = np.sqrt(np.maximum(g2, 1e-30))
                neg = np.sqrt(np.maximum(-g2, 1e-30))
                return np.where(g2 > 0, 1 + G2 / np.maximum(g2, 1e-30) * np.sinh(pos * L) ** 2, 1 + G2 / np.maximum(-g2, 1e-30) * np.sin(neg * L) ** 2)

            want = pulse_average(G, peak_power(ep, tau), tau)
            fam.check(f"NOPA mismatch Δk α={adeg:g}°", v["deltaK0PerM"], dk, rel=2e-3, abs=0.05, unit="1/m", formula="vector mismatch of the crossed geometry")
            fam.check(f"NOPA signal gain α={adeg:g}°", v["energy_signal_J"] / es, want, rel=0.02, formula="mismatched parametric gain, pulse-averaged (2 %: carriers snapped to bins)")
            eng.append(v["energy_signal_J"] / es)
            ana.append(want)
        overlay(f, alphas, eng, ana, label="1030 → 1550 + 3070 nm, 1 ps", xlabel="pump-signal angle inside the crystal (deg)", ylabel="signal gain", marker="o",
                residual="rel", analytic_label="mismatched gain")


# ============================================================================================================== BBO angles

def bbo_n(lam, theta_rad=None, which="e"):
    no, ne0 = bbo_tamosauskas(lam)
    if which == "o":
        return no
    return 1 / math.sqrt((math.cos(theta_rad) / no) ** 2 + (math.sin(theta_rad) / ne0) ** 2)


def solve_theta(fun, lo=1.0, hi=89.0):
    flo = fun(math.radians(lo))
    for _ in range(80):
        mid = 0.5 * (lo + hi)
        fm = fun(math.radians(mid))
        if np.sign(fm) == np.sign(flo):
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def walkoff(lam, theta):
    no, ne0 = bbo_tamosauskas(lam)
    nth = bbo_n(lam, theta)
    return abs(math.atan(nth ** 2 / 2 * (1 / ne0 ** 2 - 1 / no ** 2) * math.sin(2 * theta)))


def bbo_angles(fam: Family):
    # type-I OPA: o(signal) + o(idler) <- e(pump): n_e(θ, λp)/λp = n_o(λs)/λs + n_o(λi)/λi
    with fam.figure("bbo-opa-angle", "BBO type-I OPA (ooe): phase-matching angle for a 515 nm pump against the signal wavelength",
                    "n_e(θ, λp)/λp = n_o(λs)/λs + n_o(λi)/λi  (Tamošauskas 2018 Sellmeier)") as f:
        lp = 515e-9
        sig = np.array([650e-9, 700e-9, 800e-9, 900e-9, 1000e-9])
        eng, ana = [], []
        for ls in sig:
            li = 1 / (1 / lp - 1 / ls)
            r = uf.crystal.phase_match("BBO", "opa", pump=lp, signal=float(ls), type="I")
            th_engine = r["best"]["thetaDeg"]
            rhs = bbo_n(ls, which="o") / ls + bbo_n(li, which="o") / li
            th = solve_theta(lambda t: bbo_n(lp, t) / lp - rhs)
            fam.check(f"BBO type-I OPA angle 515 → {ls * 1e9:.0f} + {li * 1e9:.0f} nm", th_engine, th, abs=0.01, unit="deg", formula="n_e(θ,λp)/λp = Σ n_o/λ")
            eng.append(th_engine)
            ana.append(th)
        overlay(f, sig * 1e9, eng, ana, label="engine (calculator)", xlabel="signal wavelength (nm)", ylabel="θ (deg)", marker="o", analytic_label="Sellmeier")
    # in-simulation angle of the element (cut auto) for the OPA, SFG and SHG type II
    for lam_p, lam_s in ((515e-9, 800e-9),):
        el = b.crystal("x", "BBO", 1e-3, process="opa", type="I", cut="auto", aux=[aux("pump", lam_p, 1e-12, 1e-6)], output="signal",
                       overlap={"model": "planeWave", "radiusM": W_BEAM}, chi3=False)
        r = run_el(el, lam_s, 1e-12, 1e-12)
        li = 1 / (1 / lam_p - 1 / lam_s)
        rhs = bbo_n(lam_s, which="o") / lam_s + bbo_n(li, which="o") / li
        fam.check("OPA element: cut = auto angle", values(r)["thetaDeg"], solve_theta(lambda t: bbo_n(lam_p, t) / lam_p - rhs), abs=0.02, unit="deg")
        fam.check("OPA element: carrier mismatch ≈ 0 at the auto cut", abs(values(r)["deltaK0PerM"]) * 1e-3, 0.0, abs=0.02, unit="rad/mm")
    # type-I SFG o + o -> e of two colours
    for la, lb in ((800e-9, 1300e-9), (1030e-9, 1030e-9), (1030e-9, 1550e-9)):
        l3 = 1 / (1 / la + 1 / lb)
        r = uf.crystal.phase_match("BBO", "sfg", a=la, b=lb, type="I")
        rhs = bbo_n(la, which="o") / la + bbo_n(lb, which="o") / lb
        th = solve_theta(lambda t: bbo_n(l3, t) / l3 - rhs)
        fam.check(f"BBO type-I SFG angle {label_wl(la)} + {label_wl(lb)}", r["best"]["thetaDeg"], th, abs=0.01, unit="deg")
    # type-II SHG: o(ω) + e(θ, ω) -> e(θ, 2ω):  2 n_e(θ, 2ω) = n_o(ω) + n_e(θ, ω)
    for lam in (800e-9, 1030e-9, 1550e-9):
        r = uf.crystal.phase_match("BBO", "shg", fundamental=lam, type="II")
        th = solve_theta(lambda t: 2 * bbo_n(lam / 2, t) - bbo_n(lam, which="o") - bbo_n(lam, t), lo=5.0, hi=85.0)
        fam.check(f"BBO type-II SHG angle {label_wl(lam)}", r["best"]["thetaDeg"], th, abs=0.02, unit="deg", formula="2 n_e(θ,λ/2) = n_o(λ) + n_e(θ,λ)")
    # walk-off, indices, group indices and GVM of type-I SHG
    with fam.figure("bbo-walkoff", "BBO type-I SHG: walk-off angle of the second harmonic against the fundamental wavelength",
                    "tan ρ = (n_e(θ)²/2)(1/n_e² - 1/n_o²) sin 2θ at the phase-matching angle (Tamošauskas 2018)") as f:
        lams = np.array([700e-9, 800e-9, 1030e-9, 1300e-9, 1550e-9, 2000e-9])
        eng, ana = [], []
        for lam in lams:
            r = uf.crystal.phase_match("BBO", "shg", fundamental=float(lam), type="I")
            th = math.radians(r["best"]["thetaDeg"])
            wv = {w["name"]: w for w in r["best"]["waves"]}
            rho = wv["sh"]["walkOffMrad"] * 1e-3
            want = walkoff(lam / 2, th)
            fam.check(f"BBO walk-off of the SH {label_wl(lam)}", rho, want, rel=3e-3, unit="rad", formula="tan ρ = (n_e(θ)²/2)(1/n_e² - 1/n_o²) sin2θ")
            fam.check(f"BBO index of the fundamental (o) {label_wl(lam)}", wv["fundamental"]["n"], bbo_n(lam, which="o"), abs=2e-4)
            fam.check(f"BBO index of the SH (e at θ) {label_wl(lam)}", wv["sh"]["n"], bbo_n(lam / 2, th), abs=2e-4)
            h = lam * 1e-4

            def ng(l, theta=None, which="e"):
                nf = lambda x: bbo_n(x, theta, which)             # noqa: E731
                return nf(l) - l * (nf(l + h) - nf(l - h)) / (2 * h)

            gvm_want = (ng(lam / 2, th) - ng(lam, which="o")) / C * 1e15 * 1e-3      # fs/mm
            gvm = r["best"]["gvm"]["sh-fundamental fs/mm"]
            fam.check(f"BBO SH-fundamental GVM {label_wl(lam)}", gvm, gvm_want, rel=2e-3, unit="fs/mm", formula="(n_g,2ω - n_g,ω)/c")
            eng.append(rho * 1e3)
            ana.append(want * 1e3)
        overlay(f, lams * 1e9, eng, ana, label="engine (calculator)", xlabel="fundamental wavelength (nm)", ylabel="walk-off (mrad)", marker="o", residual="rel",
                analytic_label="analytic")


# ============================================================================================================== QPM

def mgo_ln_ne(lam_m, t_c=24.5):
    """MgO:LiNbO3 (5 %) extraordinary index, Gayer et al., Appl. Phys. B 91, 343 (2008)."""
    l = lam_m * 1e6
    f = (t_c - 24.5) * (t_c + 570.82)
    a1, a2, a3, a4, a5, a6 = 5.756, 0.0983, 0.2020, 189.32, 12.52, 1.32e-2
    b1, b2, b3, b4 = 2.860e-6, 4.700e-8, 6.113e-8, 1.516e-4
    n2 = a1 + b1 * f + (a2 + b2 * f) / (l ** 2 - (a3 + b3 * f) ** 2) + (a4 + b4 * f) / (l ** 2 - a5 ** 2) - a6 * l ** 2
    return math.sqrt(n2)


def qpm(fam: Family):
    lp, ls = 1064e-9, 1550e-9
    li = 1 / (1 / lp - 1 / ls)
    np_, ns_, ni_ = mgo_ln_ne(lp), mgo_ln_ne(ls), mgo_ln_ne(li)
    dk0 = 2 * math.pi * (np_ / lp - ns_ / ls - ni_ / li)
    period = 2 * math.pi / dk0
    r = uf.crystal.phase_match("MgO:LiNbO3", "dfg", pump=lp, signal=ls, qpm=True, temperature_c=24.5)
    fam.check("MgO:LN QPM period 1064 → 1550 + 3393 nm (24.5 °C)", r["best"].get("qpmPeriodM", float("nan")), period, rel=2e-3, unit="m",
              formula="Λ = 2π/Δk, Δk = 2π (n_p/λp - n_s/λs - n_i/λi), Gayer 2008 e-ray Sellmeier")
    L, tau = 10e-3, 100e-12      # long pulses: the group-velocity mismatch of LN (~0.3 ps/mm) would otherwise average the sinc²
    gam = gamma_c(2.0 / math.pi * 25.0, np_, ns_, ni_) * OVL
    ws, wi, wp = W_OF(ls), W_OF(li), W_OF(lp)
    x = 1.0
    p0 = (x / (gam * L)) ** 2 / (ws * wi)
    ep = p0 * tau / GP
    es = 1e-12

    def el_at(per):
        return b.crystal("x", "MgO:LiNbO3", L, process="dfg", type="0", cut="auto", qpm={"periodM": float(per), "model": "averaged"}, temperature_c=24.5,
                         aux=[aux("signal", ls, tau, es)], output="idler", mainRole="pump", overlap={"model": "planeWave", "radiusM": W_BEAM}, chi3=False)

    r0 = run_el(el_at(period), lp, tau, ep)
    v0 = values(r0)
    idl = pulse_average(lambda p: wi / ws * np.sinh(gam * np.sqrt(ws * wi * p) * L) ** 2, p0, tau) * es
    fam.check("QPM DFG idler energy at the solved period (d_eff = 2 d33/π)", v0["energy_idler_J"], idl, rel=0.02, unit="J",
              formula="(ωi/ωs) E_s ⟨sinh²(Γ O √(ωs ωi P) L)⟩, d33 = 25 pm/V (2 % for the independent Sellmeier / d tensor)")
    fam.check("QPM deff reported = 2 d33/π", abs(v0["deffPmPerV"]), 2 / math.pi * 25.0, rel=1e-3, unit="pm/V")
    fam.check("QPM: |mismatch before the grating| = K_G", abs(v0["deltaK0PerM"]), dk0, rel=1e-3, unit="1/m", formula="|Δk0| = 2π |n_p/λp - n_s/λs - n_i/λi| (engine sign: k_s + k_i - k_p)")
    with fam.figure("qpm-sinc", "QPM DFG in MgO:LN, 10 mm: idler energy against the grating period",
                    "η(Λ)/η(Λ_pm) = sinc²((Δk0 - 2π/Λ) L / 2) / sinc²(0) (small signal)") as f:
        # small signal: weak pump so that sinh² ~ argument²
        ep_w = 1e-8
        pers = period * (1 + np.array([-3, -2, -1, -0.5, 0, 0.5, 1, 2, 3]) * 1e-4)
        vals_ = []
        for per in pers:
            rr = run_el(el_at(per), lp, tau, ep_w)
            vals_.append(values(rr)["energy_idler_J"])
        vals_ = np.array(vals_)
        dk = dk0 - 2 * math.pi / pers
        x = dk * L / 2
        ana = np.where(np.abs(x) > 1e-12, (np.sin(x) / np.where(x == 0, 1, x)) ** 2, 1.0)
        ref = vals_[4] / ana[4]
        for per, e_, a_ in zip(pers, vals_, ana):
            fam.check(f"QPM sinc² Λ/Λ0 - 1 = {(per / period - 1) * 1e4:+.1f}e-4", e_ / ref, float(a_), abs=3e-3, formula="sinc²(ΔK L/2)")
        overlay(f, (pers / period - 1) * 1e4, vals_ / ref, ana, label="idler energy", xlabel="(Λ/Λ_pm - 1) × 10⁴", ylabel="η / η(Λ_pm)", marker="o",
                analytic_label="sinc²")


FUNCS = ["opa_gain", "sfg_mixing", "dfg_mixing", "noncollinear", "bbo_angles", "qpm"]


def main() -> int:
    fam = Family("crystals_chi2", "χ(2) mixing: SFG, DFG, OPA/OPCPA, QPM, non-collinear, BBO angles and walk-off")
    for name in FUNCS:
        fam.guard(name, lambda name=name: globals()[name](fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
