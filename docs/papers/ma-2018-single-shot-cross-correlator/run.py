#!/usr/bin/env python3
"""Ma et al. 2018, "Single-shot cross-correlator for pulse-contrast characterization of high peak-power lasers" (HPLSE 6, e61).

    python run.py --tier fast             # geometry (phase-matching calculator) + BK7-plate ghost scan + instrument demo, a few minutes
    python run.py --tier full --figures   # adds the dense spike profiles, committed figures

What is reproduced (the paper is a review; most headline numbers are detector specifications and are NOT checks):

1. Time-to-space geometry (eqs. (1), (2), pp. 3-4). The engine has no single-shot (crossing-beam + camera / fibre array) cross-correlator
   instrument, so the window T/W = (n_T sin a + n_S sin b)/c and the resolution dtau = L/c (n_T cos a - n_S cos b) are evaluated
   here from the engine's own non-collinear phase-matching calculator (indices, internal angles a, b of the two inputs to the
   sum wave): BBO type-I SFG 800 + 400 nm and 1053 + 526.5 nm, and PPLN first-order QPM (period 6 um, 800 nm + 3.3 um).
2. Fig. 4c (p. 5): replicas of the sampling pulse from a 1.1 mm BK7 plate. A Branched engine system: beam splitter -> PT arm
   (delay) and sampling arm (SHG in BBO, then the BK7 plate with ghosts) -> SFG in BBO (collinear 1-D model); the PT delay is
   scanned with `study` (energy of the sum wave). Spacing 2 n_g d / c and levels R^(2k) are checked against the engine.
3. The engine's third-order `crossCorrelator` instrument with a 1 ps delay resolution and a 105 dB floor (the 3e10 dynamic range,
   p. 6): pre-pulses of 1e-6, 1e-9 and 1e-11 are read from the trace (the last is below the floor).
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402
from ultrafast import crystal as cr  # noqa: E402

p = Paper(__file__)
C0 = 299792458.0
N = {}

# ---------------------------------------------------------------------------------------------- 1. geometry
def waves(res):
    w = {x["name"]: x for x in res["best"]["waves"]}
    return w

def bbo_geometry(lam_t, nc_deg, length_m):
    r = cr.phase_match("BBO", "sfg", "I", a=lam_t, b=lam_t / 2, noncollinear=nc_deg, length=length_m)
    if not r.get("phaseMatched") or "best" not in r:
        return None
    w = waves(r)
    al, be = abs(w["a"]["angleToSignalDeg"]), abs(w["b"]["angleToSignalDeg"])
    nT, nS = w["a"]["n"], w["b"]["n"]
    win = (nT * math.sin(math.radians(al)) + nS * math.sin(math.radians(be))) / C0            # eq. (1), s per m of beam width
    dtau = length_m / C0 * abs(nT * math.cos(math.radians(al)) - nS * math.cos(math.radians(be)))  # eq. (2)
    return dict(alphaDeg=al, betaDeg=be, windowPsPerCm=win * 1e-2 * 1e12, dtauPs=dtau * 1e12, thetaDeg=r["best"]["thetaDeg"], nT=nT, nS=nS)

ang_scan = {}
for lam in (800e-9, 1053e-9):
    xs, ys, ds = [], [], []
    for nc in range(8, 44, 2):
        g = bbo_geometry(lam, nc, 1.5e-3)
        if g is None:
            break
        xs.append(nc); ys.append(g["windowPsPerCm"]); ds.append(g["dtauPs"])
    ang_scan[lam] = (xs, ys, ds)
g800 = bbo_geometry(800e-9, ang_scan[800e-9][0][-1], 1.5e-3)
g1053 = bbo_geometry(1053e-9, ang_scan[1053e-9][0][-1], 1.5e-3)
N["bbo800_maxAngleDeg"] = ang_scan[800e-9][0][-1]
N["bbo1053_maxAngleDeg"] = ang_scan[1053e-9][0][-1]
N["bbo800_windowPsPerCm"] = g800["windowPsPerCm"]
N["bbo1053_windowPsPerCm"] = g1053["windowPsPerCm"]
N["bbo1053_alphaDeg"], N["bbo1053_betaDeg"] = g1053["alphaDeg"], g1053["betaDeg"]
for L in (1e-3, 1.5e-3, 2e-3):
    g = bbo_geometry(1053e-9, ang_scan[1053e-9][0][-1], L)
    N["dtau1053_L%.1fmm_ps" % (L * 1e3)] = g["dtauPs"]
N["dtau_reference_ps"] = 1.0
# window needing 50 ps (800 nm) and 70 ps (1053 nm): implied beam width (the paper does not give it)
N["impliedWidth800_cm"] = 50.0 / g800["windowPsPerCm"]
N["impliedWidth1053_cm"] = 70.0 / g1053["windowPsPerCm"]

def ppln(period_m, lam_t=800e-9, lam_s=3.3e-6, length=1e-3):
    """Non-collinear angle (inside, between the inputs) where the engine's QPM phase mismatch changes sign; window per cm from eq. (1)."""
    def dk(nc):
        r = cr.phase_match("LiNbO3", "sfg", "0", a=lam_t, b=lam_s, noncollinear=nc, qpm=period_m, length=length)
        return r["best"]["deltaKPerM"], r
    lo, hi = 1.0, 89.0
    flo, _ = dk(lo); fhi, _ = dk(hi)
    if flo * fhi > 0:
        return None
    for _ in range(18):
        mid = 0.5 * (lo + hi)
        fm, _ = dk(mid)
        if fm * flo > 0:
            lo, flo = mid, fm
        else:
            hi = mid
    nc = 0.5 * (lo + hi)
    _, r = dk(nc)
    w = waves(r)
    al, be = abs(w["a"]["angleToSignalDeg"]), abs(w["b"]["angleToSignalDeg"])
    win = (w["a"]["n"] * math.sin(math.radians(al)) + w["b"]["n"] * math.sin(math.radians(be))) / C0 * 1e-2 * 1e12
    dtau = length / C0 * abs(w["a"]["n"] * math.cos(math.radians(al)) - w["b"]["n"] * math.cos(math.radians(be))) * 1e12
    return dict(ncDeg=nc, alphaDeg=al, betaDeg=be, windowPsPerCm=win, dtauPs=dtau)

pp1 = ppln(6e-6)
pp3 = ppln(6e-6 / 3)          # a third-order QPM of the 6 um grating acts like a 2 um first-order grating
N["ppln_m1_ok"] = pp1 is not None
if pp1:
    N["ppln_m1_windowPsPerCm"] = pp1["windowPsPerCm"]; N["ppln_m1_ncDeg"] = pp1["ncDeg"]
    N["ppln_m1_alphaDeg"] = pp1["alphaDeg"]; N["ppln_m1_betaDeg"] = pp1["betaDeg"]
if pp3:
    N["ppln_m3_windowPsPerCm"] = pp3["windowPsPerCm"]; N["ppln_m3_ncDeg"] = pp3["ncDeg"]
N["ppln_m3_ok"] = pp3 is not None
N["ppln_ref_45_psPerCm"] = 45.0

# crystal-reflection artefact (p. 7): dt = 2 n_S L / c; the +-15 ps spikes of Fig. 6a imply L (L is not given for that figure)
nS_bbo = g800["nS"]
N["artefact15ps_impliedLength_mm"] = 15e-12 * C0 / (2 * nS_bbo) * 1e3

# ---------------------------------------------------------------------------------------------- 2. BK7 plate replicas (Fig. 4c)
W = 200e-6
beam = {"kind": "gaussian", "w0M": W}
fed = {"role": "b", "fromPort": True, "beam": {"kind": "gaussian", "radiusM": W}}
D_PLATE = 1.1e-3

def sscc_system():
    """Beam splitter -> PT arm (delay) and sampling arm (SHG, then the BK7 plate with ghosts) -> SFG crystal (collinear 1-D model)."""
    return b.system("sscc-fig4c", b.Branched([
        b.link(b.beam_splitter("bs"), {"c": "ptarm", "d": "sarm"}),
        b.link(b.delay("ptarm", 0.0), "x"),
        b.link(b.delay("sarm", 0.0), "shg"),
        b.link(b.crystal("shg", "BBO", 100e-6, "shg", "I", output="sh", beam=beam, chi3=False), {"out": "gp", "fundamental": "virtual"}),
        b.link(b.plate("gp", "N-BK7", D_PLATE, ghosts={"orders": 3, "overlap": 1.0}), "x:b"),
        b.link(b.crystal("x", "BBO", 100e-6, "sfg", "I", aux=[fed], output="sum", record=["a", "b"], beam=beam, chi3=False),
               {"out": "end", "a": "virtual", "b": "virtual"}),
    ]))

SYS = None

def energies(tau, delays):
    spec = b.run_spec(b.pulse(800e-9, tau, energy=1e-6), grid={"windowS": 200e-12, "satelliteLevel": 1e-11})
    rows = uf.study(SYS, spec, parameters=[{"path": "element:ptarm/delayS", "values": [float(x) for x in delays], "nominal": 0.0}],
                    outputs=["energy:end"])["rows"]
    return np.array([r["energy:end"] for r in rows])

def peak(tau, centre, half, step):
    d = np.arange(centre - half, centre + half + step / 2, step)
    e = energies(tau, d)
    return d[e.argmax()], e.max(), d, e

def spikes(tau, step, with_profiles):
    d_coarse = np.arange(5.8e-12, 7.4e-12, 0.1e-12)
    e = energies(tau, d_coarse)
    c0 = d_coarse[e.argmax()]
    d0, e0, d, ee = peak(tau, c0, 0.25e-12, step)
    out = {"d0": d0, "levels": [], "positions": [], "profiles": []}
    for k in (1, 2, 3):
        dk, ek, dd, eee = peak(tau, d0 + k * DELTA, 0.3e-12, step)
        out["levels"].append(ek / e0); out["positions"].append(dk - d0)
    if with_profiles:
        for k in (0, 1, 2, 3):
            dd = np.arange(d0 + k * DELTA - 1.0e-12, d0 + k * DELTA + 1.0e-12 + 1e-15, 40e-15)
            out["profiles"].append((dd - d0, energies(tau, dd) / e0))
    return out

# the plate's group delay at 400 nm is the engine's own: the ghost spacing 2 n_g d / c
tab = {t["wavelengthM"]: t for t in uf.materials.show("N-BK7")["table"]}
NG400, N400, N800 = tab[4e-07]["groupIndex"], tab[4e-07]["n"], tab[8e-07]["n"]
DELTA = 2 * NG400 * D_PLATE / C0
N["plate_spacing_expected_ps"] = DELTA * 1e12
R400 = ((N400 - 1) / (N400 + 1)) ** 2
N["plate_R400"] = R400
SYS = uf.System.load(sscc_system())

s200 = spikes(200e-15, 50e-15, False)
N["spacing1_ps"] = s200["positions"][0] * 1e12
N["spacing2_ps"] = s200["positions"][1] * 1e12 / 2
N["spacing3_ps"] = s200["positions"][2] * 1e12 / 3
for k in (1, 2, 3):
    N["level%d_200fs" % k] = s200["levels"][k - 1]
    N["level%d_over_R2k_200fs" % k] = s200["levels"][k - 1] / R400 ** (2 * k)
    N["level%d_analytic" % k] = R400 ** (2 * k)
s50 = spikes(50e-15, 25e-15, not p.fast)
for k in (1, 2, 3):
    N["level%d_50fs" % k] = s50["levels"][k - 1]
    N["level%d_50fs_over_R2k" % k] = s50["levels"][k - 1] / R400 ** (2 * k)
# what the paper's Fig. 4c shows (read off the 7x magnified page, +-0.15 decade)
PAPER_T = [-11.8, -23.1, -33.8]
PAPER_LV = [6e-4, 2.7e-6, 8e-10]
N["paper_spacing_mean_ps"] = float(np.mean([PAPER_T[0], PAPER_T[1] / 2, PAPER_T[2] / 3])) * -1
for k in (1, 2, 3):
    N["level%d_decades_vs_paper" % k] = math.log10(s200["levels"][k - 1] / PAPER_LV[k - 1])
N["level3_vs_text_1e-9_decades"] = math.log10(s200["levels"][2] / 1e-9)

# ---------------------------------------------------------------------------------------------- 3. instrument at 105 dB, 1 ps
ped = [{"kind": "prePulse", "delayS": d, "relativeIntensity": e} for d, e in zip((-10e-12, -23e-12, -34e-12), (1e-6, 1e-9, 1e-11))]
pu = b.pulse(800e-9, 50e-15, energy=1e-6)
pu["pedestal"] = ped
def xc_run(res_s, probe_delays=None):
    kw = {"resolutionS": res_s} if res_s else {}
    inst = b.instrument("crossCorrelator", name="xc", rangeS=[-45e-12, 10e-12], stepS=0.5e-12, fineStepS=50e-15, fineRangeS=50e-12,
                        dynamicRangeDb=105, noise=False, probeDelaysS=[-10e-12, -23e-12, -34e-12], **kw)
    r = uf.run(uf.System.load(b.system("probe", [{"id": "x", "type": "probe"}])), b.run_spec(pu, grid={"windowS": 120e-12}, instruments=[inst]))
    d, s = np.asarray(r["xc_delay_s"]), np.asarray(r["xc_signal"])
    s = s / s.max()
    v = [i for i in r.summary["instruments"] if i["name"] == "xc"][0]["values"]
    def at(t):
        return 10 * math.log10(s[np.abs(d - t) < 0.4e-12].max())
    return d, s, at, v

dx, sx, at, v0 = xc_run(0)
N["instr_floor_db"] = -105.0
N["instr_pre1e-6_db"] = at(-10e-12)
N["instr_pre1e-9_db"] = at(-23e-12)
N["instr_pre1e-11_db"] = at(-34e-12)
N["instr_reported_contrastAt_m10ps_db"] = v0["contrastAt_m10ps_db"]
N["instr_trace"] = {"x": dx * 1e12, "y": 10 * np.log10(np.maximum(sx, 1e-30))}
# the same with the SSCC's 1 ps resolution (resolutionS): see repro_1.py
dr_, sr_, atr, v1 = xc_run(1e-12)
N["res1ps_pre1e-6_trace_db"] = atr(-10e-12)
N["res1ps_pre1e-6_reported_db"] = v1["contrastAt_m10ps_db"]
N["res1ps_floor_trace_db"] = atr(-20e-12)
N["res1ps_trace"] = {"x": dr_ * 1e12, "y": 10 * np.log10(np.maximum(sr_, 1e-30))}

# ---------------------------------------------------------------------------------------------- figures
fig, axs = p.figure(ncols=2, width_in=10.5, height_in=4.0)
ax = axs[0]
for lam, col, lab in ((800e-9, BLUE, "BBO 800 + 400 nm, L = 1.5 mm"), (1053e-9, ORANGE, "BBO 1053 + 526.5 nm, L = 1.5 mm")):
    x, y, dd = ang_scan[lam]
    ax.plot(x, dd, color=col, label=lab, marker="o", ms=3)
ax.axhline(1.0, color=REF, ls="--", lw=1.2)
ax.annotate("paper: about 1 ps (p. 2)", (9, 1.03), fontsize=8, color=REF)
ax.axvline(40, color=REF2, ls=":", lw=1.2); ax.annotate("paper: about 40 deg (p. 2)", (30.5, 0.1), fontsize=8, color=REF2)
ax.set(xlabel="angle between the two input beams inside the crystal (deg)", ylabel="temporal resolution, eq. (2) (ps)", ylim=(0, 1.3))
ax.legend(fontsize=8)
ax2 = ax.twinx()
for lam, col in ((800e-9, BLUE), (1053e-9, ORANGE)):
    x, y, dd = ang_scan[lam]
    ax2.plot(x, y, color=col, ls="--", lw=1.2)
ax2.set_ylabel("window per cm of beam width, eq. (1) (ps/cm, dashed)"); ax2.spines["right"].set_visible(True); ax2.grid(False)
ax = axs[1]
labels = ["BBO 800", "BBO 1053", "PPLN m=1", "PPLN m=3"]
vals = [g800["windowPsPerCm"], g1053["windowPsPerCm"], pp1["windowPsPerCm"] if pp1 else 0, pp3["windowPsPerCm"] if pp3 else 0]
ax.bar(labels, vals, color=[BLUE, ORANGE, AQUA, AQUA])
ax.plot([2], [45], "D", color=REF, label="paper: 45 ps/cm (PPLN, 6 um, m = 1; p. 4)")
ax.plot([3], [70], "D", color=REF2, label="paper: about 70 ps/cm (m = 3, 5; p. 4)")
ax.set(ylabel="window per cm of beam width (ps/cm)"); ax.legend(fontsize=8)
p.save(fig, "geometry")

fig, ax = p.figure(width_in=7.2, height_in=4.4)
if s50["profiles"]:
    for k, (dd, ee) in enumerate(s50["profiles"]):
        ax.semilogy(-dd * 1e12, ee, color=BLUE, lw=1.4, label="engine, 50 fs pulse" if k == 0 else None)
tt = [0] + [-x * 1e12 for x in s200["positions"]]
ax.semilogy(tt, [1] + s200["levels"], "o", color=ORANGE, label="engine peaks, 200 fs pulse (no dispersion)")
ax.semilogy([0] + [-x * 1e12 for x in s50["positions"]], [1] + s50["levels"], "s", color=BLUE, mfc="none", label="engine peaks, 50 fs pulse")
ax.semilogy(tt, [1] + [R400 ** (2 * k) for k in (1, 2, 3)], "_", ms=18, mew=2, color=REF2, label="R^(2k), Fresnel (BK7, 400 nm)")
ax.semilogy([0] + PAPER_T, [1] + PAPER_LV, "D", color=REF, label="paper Fig. 4c, read off (about +-0.15 decade)")
ax.axhline(10 ** -10.5, color=REF2, ls=":"); ax.annotate("3e10 dynamic range (p. 6)", (-40, 10 ** -10.3), fontsize=8, color=REF2)
ax.set(xlabel="time (ps)", ylabel="sum-frequency signal / main peak", ylim=(1e-11, 3), xlim=(-40, 5)); ax.legend(fontsize=8, loc="center left")
p.save(fig, "bk7-replicas")

fig, ax = p.figure(width_in=7.2, height_in=4.2)
ax.plot(dx * 1e12, 10 * np.log10(np.maximum(sx, 1e-30)), color=BLUE, lw=1.4, label="engine crossCorrelator, 105 dB floor, no resolution smoothing")
ax.plot(dr_ * 1e12, 10 * np.log10(np.maximum(sr_, 1e-30)), color=ORANGE, lw=1.2, label="same with resolutionS = 1 ps (floor moves up)")
ax.plot([-10, -23, -34], [-60, -90, -110], "D", color=REF, label="injected pre-pulses: 1e-6, 1e-9, 1e-11")
ax.axhline(-105, color=REF2, ls=":"); ax.annotate("3e10 range (paper, p. 6)", (-44, -103.5), fontsize=8, color=REF2)
ax.set(xlabel="delay (ps)", ylabel="signal / peak (dB)", ylim=(-115, 3), xlim=(-45, 10)); ax.legend(fontsize=8, loc="center left")
p.save(fig, "instrument-dynamic-range")

p.write_numbers(N)
