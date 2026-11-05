"""Splitters and masks alone: beamSplitter, polarizingSplitter, dichroic and leakMirror energy to each port, the Stokes
relations in a Mach-Zehnder, Sequential = Branched (field arrays), a beam of another colour fed into port b (each output
keeps both carriers), and the aperture's clipped power against the Gaussian formula.

Run: python verification/components/splitters_masks.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
"""
from __future__ import annotations

import math
import sys

from _common import C, CARRIERS_M, Family, b, label_wl, np, overlay, uf


def run(elements, lam=1030e-9, tau=100e-15, branched=False, pulse=None, energy=1e-9):
    s = b.system("component", b.Branched(elements) if branched else elements)
    p = pulse or b.pulse(lam, tau, energy=energy)
    return uf.run(s, b.run_spec(p, physics={"nonlinear": False}))


def port_energy(res, port_key):
    for v in res.summary["graph"]["virtualOutputEnergies"]:
        if v["port"] == port_key:
            return v["energyJ"]
    return 0.0


def beam_splitter(fam: Family):
    with fam.figure("beamsplitter", "beamSplitter: energy to port c (transmitted) and d (reflected), light into a",
                    "E_c = (1 − R) E_in,  E_d = R E_in, at 515 nm, 1030 nm and 2 µm") as f:
        Rs = [0.0, 0.1, 0.3, 0.5, 0.9, 1.0]
        for lam in CARRIERS_M:
            ec = []
            for R in Rs:
                els = [b.link(b.beam_splitter("bs", R), {"c": "end", "d": "virtual"})]
                r = run(els, lam, branched=True)
                e_c = r.summary["graph"]["energyOutJ"]
                e_d = port_energy(r, "bs:d")
                fam.check(f"BS R={R} {label_wl(lam)} port c", e_c, (1 - R) * 1e-9, abs=1e-21, unit="J")
                fam.check(f"BS R={R} {label_wl(lam)} port d", e_d, R * 1e-9, abs=1e-21, unit="J")
                ec.append(e_c / 1e-9)
            overlay(f, Rs, ec, [1 - R for R in Rs], label=f"port c, {label_wl(lam)}", xlabel="reflectivity R",
                    ylabel="E_c / E_in", marker="o", analytic_label="1 − R" if lam == CARRIERS_M[0] else None)


def sequential_equals_branched(fam: Family):
    for lam in CARRIERS_M:
        for R, port in ((0.3, "c"), (0.3, "d")):
            seq = run([b.bulk("g", "FusedSilica", 2e-3), b.beam_splitter("bs", R, output=port), b.gap("a", 0.05)], lam)
            other = "d" if port == "c" else "c"
            br = run([b.link(b.bulk("g", "FusedSilica", 2e-3), "bs"), b.link(b.beam_splitter("bs", R), {port: "a", other: "virtual"}),
                      b.link(b.gap("a", 0.05), "end")], lam, branched=True)
            fs = seq["output_ax_re"] + 1j * seq["output_ax_im"]
            fb = br["output_ax_re"] + 1j * br["output_ax_im"]
            d = float(np.max(np.abs(fs - fb)) / np.max(np.abs(fs)))
            fam.check(f"Sequential = Branched field, BS port {port} {label_wl(lam)}", d, 0.0, abs=1e-12, formula="max|A_seq − A_br| / max|A|")
            fam.check(f"Sequential = Branched GDD, BS port {port} {label_wl(lam)}", seq.summary["totals"]["systemGddS2"],
                      br.summary["totals"]["systemGddS2"], rel=1e-12)


def mach_zehnder(fam: Family):
    """Equal arms: c = (t² − r²) a, d = 2 r t a (Stokes r' = −r). R = 0.5 sends everything to d."""
    xs, eng, ana = [], [], []
    for R in (0.1, 0.3, 0.5):
        els = [b.link(b.beam_splitter("bs1", R), {"c": "armA", "d": "armB"}),
               b.link(b.gap("armA", 0.1), "bs2:1"), b.link(b.gap("armB", 0.1), "bs2:2"),
               b.link(b.beam_splitter("bs2", R), {"c": "end", "d": "virtual"})]
        r = run(els, branched=True)
        t2, r2 = 1 - R, R
        fam.check(f"Mach-Zehnder R={R} port c", r.summary["graph"]["energyOutJ"], (t2 - r2) ** 2 * 1e-9, abs=1e-15, unit="J",
                  formula="(t² − r²)² E")
        fam.check(f"Mach-Zehnder R={R} port d", port_energy(r, "bs2:d"), 4 * t2 * r2 * 1e-9, abs=1e-15, unit="J", formula="4 r² t² E")


def polarizing_splitter(fam: Family):
    tp, rs = 0.95, 0.9
    with fam.figure("pbs", "polarizingSplitter (T_p = 0.95, R_s = 0.9): port energies against the input polarisation angle",
                    "E_c = [T_p cos²α + (1 − R_s) sin²α] E,  E_d = [(1 − T_p) cos²α + R_s sin²α] E") as f:
        angles = np.linspace(0, 90, 7)
        ec, ea = [], []
        for a in angles:
            p = b.pulse(1030e-9, 100e-15, energy=1e-9)
            p["polarization"] = {"angleDeg": float(a)}
            r = run([b.link(b.polarizing_splitter("pbs", tp, rs), {"c": "end", "d": "virtual"})], branched=True, pulse=p)
            c2, s2 = math.cos(math.radians(a)) ** 2, math.sin(math.radians(a)) ** 2
            want_c = (tp * c2 + (1 - rs) * s2) * 1e-9
            want_d = ((1 - tp) * c2 + rs * s2) * 1e-9
            fam.check(f"PBS {a:g} deg port c", r.summary["graph"]["energyOutJ"], want_c, abs=1e-20, unit="J")
            fam.check(f"PBS {a:g} deg port d", port_energy(r, "pbs:d"), want_d, abs=1e-20, unit="J")
            ec.append(r.summary["graph"]["energyOutJ"] / 1e-9)
            ea.append(want_c / 1e-9)
        overlay(f, angles, ec, ea, label="port c", xlabel="input polarisation angle from x (deg)", ylabel="E_c / E_in", marker="o",
                analytic_label="analytic")


def dichroic(fam: Family):
    edge, width = 1030e-9, 20e-9
    with fam.figure("dichroic", "dichroic (edge 1030 nm, 10-90 % width 20 nm, reflectLong): spectrum in each port, 10 fs input",
                    "R(λ) = 1 / (1 + exp(−(λ − λ_e) ln81 / Δλ)); port d = R S, port c = (1 − R) S") as f:
        els = [b.link(b.dichroic("dc", edge, edge_width_m=width), {"c": "pc", "d": "pd"}),
               b.link(b.probe("pc"), "end"), b.link(b.probe("pd"), "virtual")]
        r = run(els, tau=10e-15, branched=True)
        lam = np.asarray(r["input_wavelength_m"])
        S = np.asarray(r["input_spectrum"])
        ok = np.isfinite(lam) & (lam > 0)
        lam, S = np.where(ok, lam, 1.0), np.where(ok, S, 0.0)
        u = np.clip((lam - edge) / (width / math.log(81)), -700, 700)
        R = 1 / (1 + np.exp(-u))
        e_in = 1e-9
        want_d = e_in * float((S * R).sum() / S.sum())
        fam.check("dichroic port d energy", port_energy(r, "pd:out"), want_d, rel=1e-6, unit="J", formula="∫ R S dω / ∫ S dω")
        fam.check("dichroic port c energy", r.summary["graph"]["energyOutJ"], e_in - want_d, rel=1e-6, unit="J")
        so = np.asarray(r["output_spectrum"])
        m = S > 1e-3 * S.max()
        overlay(f, lam[m] * 1e9, so[m] / S.max(), ((1 - R) * S)[m] / S.max(), label="port c", xlabel="wavelength (nm)",
                ylabel="spectral density / input peak", analytic_label="(1 − R) S")


def fed_colour(fam: Family):
    """1 nJ at 1030 nm into a, 2 nJ at 515 nm into b: each output port must carry both colours with the right energies."""
    R = 0.3
    green = b.pulse(515e-9, 100e-15, energy=2e-9)
    with fam.figure("fed-colour", "beamSplitter R = 0.3, 1030 nm into a and a coherent 515 nm beam into b: port c spectrum",
                    "port c: (1 − R) · 1 nJ at 1030 nm + R · 2 nJ at 515 nm (different colours do not interfere)") as f:
        els = [b.link(b.beam_splitter("bs", R), {"c": "end", "d": "virtual"}, inputs={"b": b.virtual_input(green, coherent_with="input")})]
        r = run(els, branched=True)
        lam = np.asarray(r["output_wavelength_m"])
        s = np.asarray(r["output_spectrum"])
        e_c = r.summary["graph"]["energyOutJ"]
        g = float(s[lam < 750e-9].sum() / s.sum())
        fam.check("fed 515 nm: port c energy", e_c, ((1 - R) * 1e-9 + R * 2e-9), rel=1e-9, unit="J")
        fam.check("fed 515 nm: port c share at 515 nm", g * e_c, R * 2e-9, rel=1e-6, unit="J",
                  formula="the fed beam keeps its own carrier (it was built on the 1030 nm grid before the fix)")
        fam.check("fed 515 nm: port c share at 1030 nm", (1 - g) * e_c, (1 - R) * 1e-9, rel=1e-6, unit="J")
        fam.check("fed 515 nm: port d energy", port_energy(r, "bs:d"), (R * 1e-9 + (1 - R) * 2e-9), rel=1e-9, unit="J")
        # the analytic port-c spectrum: the two input spectra scaled by their port factors
        pa = run([b.probe("p")], 1030e-9)
        pg = run([b.probe("p")], pulse=green)
        def density(res, prefix, energy):
            """Energy spectral density (J per rad/s) on the result's ω axis."""
            w_ = np.asarray(res[prefix + "_omega_rad_s"]); s_ = np.asarray(res[prefix + "_spectrum"])
            return w_, s_ / (s_.sum() * abs(w_[1] - w_[0])) * energy
        w, dens = density(r, "output", e_c)
        wa, da = density(pa, "input", 1e-9)
        wg, dg = density(pg, "input", 2e-9)
        want = (1 - R) * np.interp(w, wa, da, left=0, right=0) + R * np.interp(w, wg, dg, left=0, right=0)
        m = (want > 1e-4 * want.max()) | (dens > 1e-4 * dens.max())
        overlay(f, w[m] / 1e15, dens[m] * 1e24, want[m] * 1e24, label="port c", xlabel="angular frequency (rad/fs)",
                ylabel="energy density (nJ per rad/ps)", analytic_label="0.7 S_1030 + 0.3 S_515", marker=".", style="")


def aperture(fam: Family):
    """Hard circular aperture on a tracked Gaussian beam: T = 1 − exp(−2 a² / w²)."""
    w = 1e-3
    xs, eng, ana = [], [], []
    for a in (0.5e-3, 0.8e-3, 1e-3, 1.5e-3):
        s = b.system("ap", [b.aperture("ap", radius_m=a)])
        spec = b.run_spec(b.pulse(1030e-9, 100e-15, energy=1e-9), physics={"nonlinear": False})
        spec["inputBeam"] = {"kind": "gaussian", "w0M": w}
        r = uf.run(s, spec)
        want = 1 - math.exp(-2 * a * a / (w * w))
        fam.check(f"aperture a/w = {a / w:g} transmission", r.summary["totals"]["energyTransmission"], want, rel=1e-6)


def main() -> int:
    fam = Family("splitters_masks", "Splitters and masks: beamSplitter, polarizingSplitter, dichroic, fed colours, aperture")
    for fn in (beam_splitter, sequential_equals_branched, mach_zehnder, polarizing_splitter, dichroic, fed_colour, aperture):
        fam.guard(fn.__name__, lambda fn=fn: fn(fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
