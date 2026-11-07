"""χ(2) crystals alone: BBO type-I SHG of 515 nm, 1030 nm and 2 µm pulses.

Checks: the phase-matching angle against Eimerl's BBO Sellmeier (independent of the engine's index data); the
low-conversion efficiency against the plane-wave formula
    η = 8π² d_eff² L² I / (ε0 c n_ω² n_2ω λ_ω²) · sinc²(Δk L / 2),  pulse-averaged η_E = η(I_peak) / √2 (Gaussian in time);
η ∝ L²; η(Δk) / η(0) = sinc²(Δk L / 2) against the Δk the engine reports while the crystal is detuned; energy conservation
at high conversion (fundamental + SH = input); and each output port's carrier in a Branched system (out = SH at λ/2,
fundamental at λ). Pulses of 5 ps (group-velocity mismatch is negligible over 1 mm, so the plane-wave law holds).

Run: python tests/components/crystals.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
"""
from __future__ import annotations

import math
import sys

from _common import C, CARRIERS_M, Family, b, label_wl, np, overlay, uf

EPS0 = 8.8541878128e-12
AREA = 1e-6
TAU = 5e-12


def bbo_eimerl(lam_m):
    """BBO, Eimerl et al., J. Appl. Phys. 62, 1968 (1987): (n_o, n_e), λ in µm."""
    l2 = (lam_m * 1e6) ** 2
    no = math.sqrt(2.7405 + 0.0184 / (l2 - 0.0179) - 0.0155 * l2)
    ne = math.sqrt(2.3730 + 0.0128 / (l2 - 0.0156) - 0.0044 * l2)
    return no, ne


def bbo_tamosauskas(lam_m):
    """BBO, Tamošauskas et al., Opt. Mater. Express 8, 1410 (2018), 0.188-5.2 µm: (n_o, n_e)."""
    l2 = (lam_m * 1e6) ** 2
    no = math.sqrt(1 + 0.90291 * l2 / (l2 - 0.003926) + 0.83155 * l2 / (l2 - 0.018786) + 0.76536 * l2 / (l2 - 60.01))
    ne = math.sqrt(1 + 1.151075 * l2 / (l2 - 0.007142) + 0.21803 * l2 / (l2 - 0.02259) + 0.656 * l2 / (l2 - 263))
    return no, ne


def theta_pm_type1(lam, model=bbo_eimerl):
    """o + o -> e: n_e(θ, λ/2) = n_o(λ)."""
    no1, _ = model(lam)
    no2, ne2 = model(lam / 2)
    s2 = (no1 ** -2 - no2 ** -2) / (ne2 ** -2 - no2 ** -2)
    return math.degrees(math.asin(math.sqrt(s2)))


def run(lam, L, energy, detune=None, branched=False, tau=TAU):
    cut = "auto" if detune is None else {"detuneDeg": float(detune)}
    el = b.crystal("c", "BBO", L, cut=cut, beam=b.constant_beam(area_m2=AREA), overlap="planeWave", chi3=False)
    if branched:
        els = b.Branched([b.link(el, {"out": "pout", "fundamental": "pf"}), b.link(b.probe("pout"), "end"), b.link(b.probe("pf"), "virtual")])
    else:
        els = [el]
    p = b.pulse(lam, tau, energy=energy)
    return uf.run(b.system("shg", els), b.run_spec(p))


def values(res, i=0):
    return res.summary["elements"][i]["values"]


def peak_power(energy, tau):
    return energy / tau * 2 * math.sqrt(math.log(2) / math.pi)


def shg(fam: Family):
    with fam.figure("shg-length", "BBO type-I SHG, 5 ps, I_peak fixed: conversion efficiency against crystal length",
                    "η_E = ⟨tanh²√(κ L² I(t))⟩_P, κ = 8π² d² / (ε0 c n³ λ²) (≈ κ L² I_peak / √2 at low conversion)") as f:
        for lam in CARRIERS_M:
            energy = 2e-6 if lam > 600e-9 else 1e-6
            r0 = run(lam, 1e-3, energy)
            v = values(r0)
            fam.check(f"phase-matching angle {label_wl(lam)} (Tamošauskas 2018)", v["thetaDeg"], theta_pm_type1(lam, bbo_tamosauskas),
                      abs=0.01, unit="deg", formula="n_e(θ, λ/2) = n_o(λ)")
            if lam < 1.5e-6:   # Eimerl's fit ends near 1.06 µm (0.8° off at 2 µm)
                fam.check(f"phase-matching angle {label_wl(lam)} (Eimerl 1987)", v["thetaDeg"], theta_pm_type1(lam), abs=0.3, unit="deg",
                          formula="independent Sellmeier, 0.3° allowed")
            no1, _ = bbo_eimerl(lam)
            d = abs(v["deffPmPerV"]) * 1e-12
            I = peak_power(energy, TAU) / AREA
            Ls = np.array([0.1e-3, 0.2e-3, 0.4e-3, 0.7e-3, 1e-3])
            eng, ana = [], []
            for L in Ls:
                r = run(lam, float(L), energy)
                eta = values(r)["conversionEfficiency"]
                # Armstrong et al. (1962) per time slice, η(t) = tanh²(√η_low(t)), averaged over the Gaussian power
                k = 8 * math.pi ** 2 * d * d * L * L / (EPS0 * C * no1 ** 3 * lam ** 2)
                t = np.linspace(-3, 3, 4001)
                Pt = np.exp(-4 * math.log(2) * t ** 2)
                want = float((Pt * np.tanh(np.sqrt(k * I * Pt)) ** 2).sum() / Pt.sum())
                # 0.5 %: n_ω = n_2ω = n_o(λ) from Eimerl (agrees to 0.15 %), and d_eff as the engine reports it
                fam.check(f"η SHG {label_wl(lam)} L={L * 1e3:.1f} mm", eta, want, rel=5e-3, formula="Armstrong tanh², pulse average")
                eng.append(eta * 100)
                ana.append(want * 100)
            # ratio of 1 mm to 0.1 mm: the L² law with depletion; 515 nm -> 257 nm: BBO's group-velocity mismatch against 5 ps
            # takes a further 0.3 %
            fam.check(f"η(1 mm)/η(0.1 mm) {label_wl(lam)}", eng[-1] / eng[0], ana[-1] / ana[0], rel=5e-3 if lam < 600e-9 else 1e-3,
                      formula="≈ 100 (L²), less the depletion")
            overlay(f, Ls * 1e3, eng, ana, label=label_wl(lam), xlabel="crystal length (mm)", ylabel="conversion efficiency (%)",
                    marker="o", residual="rel", analytic_label="analytic" if lam == CARRIERS_M[0] else None)

    with fam.figure("shg-sinc", "BBO type-I SHG, 1 mm, 1030 nm: efficiency against the detuning",
                    "η(Δk) / η(0) = sinc²(Δk L / 2), Δk as the engine reports it for each detuning") as f:
        lam, L = 1030e-9, 1e-3
        e0 = values(run(lam, L, 2e-6))["conversionEfficiency"]
        dets = np.linspace(-0.3, 0.3, 13)
        xs, eng, ana = [], [], []
        for dt in dets:
            v = values(run(lam, L, 2e-6, detune=float(dt)))
            x = v["deltaK0PerM"] * L / 2
            want = (math.sin(x) / x) ** 2 if abs(x) > 1e-12 else 1.0
            fam.check(f"sinc² detune {dt:+.2f} deg", v["conversionEfficiency"] / e0, want, abs=1e-2)
            xs.append(v["deltaK0PerM"] * L)
            eng.append(v["conversionEfficiency"] / e0)
            ana.append(want)
        order = np.argsort(xs)
        overlay(f, np.array(xs)[order], np.array(eng)[order], np.array(ana)[order], label="η / η(Δk=0)", xlabel="Δk L (rad)",
                ylabel="relative efficiency", marker="o", analytic_label="sinc²(ΔkL/2)")


def conservation_and_ports(fam: Family):
    for lam in CARRIERS_M:
        r = run(lam, 2e-3, 1e-4)
        v = values(r)
        e_in = r.summary["elements"][0]["energyInJ"]
        fam.check(f"energy conserved at η = {v['conversionEfficiency']:.2f}, {label_wl(lam)}",
                  v["energy_fundamental_J"] + v["energy_sh_J"], e_in, rel=1e-6, unit="J", formula="E_ω + E_2ω = E_in (lossless, χ(3) off)")
        rb = run(lam, 1e-3, 1e-6, branched=True)
        els = {e["id"]: e for e in rb.summary["elements"]}
        fam.check(f"port out carrier {label_wl(lam)}", els["pout"]["pulse"]["centroidWavelengthM"], lam / 2, rel=2e-3, unit="m",
                  formula="out = SH at λ/2")
        fam.check(f"port fundamental carrier {label_wl(lam)}", els["pf"]["pulse"]["centroidWavelengthM"], lam, rel=2e-3, unit="m")
        fam.check(f"Branched η = Sequential η {label_wl(lam)}", els["pout"]["energyInJ"],
                  values(run(lam, 1e-3, 1e-6))["energy_sh_J"], rel=1e-9, unit="J")


def main() -> int:
    fam = Family("crystals", "χ(2) crystals: BBO type-I SHG at 515 nm, 1030 nm and 2 µm")
    for fn in (shg, conservation_and_ports):
        fam.guard(fn.__name__, lambda fn=fn: fn(fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
