#!/usr/bin/env python3
"""Escoto et al. (CLEO 2024, SF2O.8): polarisation-based contrast enhancement during spectral broadening in a MPC.

    python run.py --tier fast            # 2-degree waveplate grid, ~1 min (CI)
    python run.py --tier full --figures  # 1-degree grid, B sweep, finite polariser extinction; committed figures

Set-up (abstract): a Kerr medium between two quarter-wave plates and a polariser; both waveplate angles are scanned
over [-50, 50] deg. Two metrics: the transmission through the polariser (normalised to its peak) and the contrast
improvement at +2 ps (peak over intensity at +2 ps, output relative to input).

ASSUMED CELL (labelled hypothesis, the abstract does not give one): the multi-pass cell of Seidel et al., Laser Photon.
Rev. 16, 2100268 (2022): fused-silica plates in air, dispersion compensated per round trip. It is modelled as ONE
dispersion-free-in-effect fused-silica Kerr element (1 mm, constant beam: plane-wave NER) whose energy is set so that
the B-integral of LINEAR light is B = 10 rad (their Fig. 1b value); 900 fs Gaussian input at 1030 nm; no Raman
switching (the engine's silica Raman is on, as in glass).

Angle convention (assumed, see README): the input is linear (x); the polariser is crossed (axis y); QWP1 axis theta1.
The paper plots QWP2 so that the low-transmission ellipse lies along theta1 = theta2; in the engine's lab frame that is
theta2_engine = -theta2_paper. Here theta1, theta2 are the PAPER angles; QWP2 is applied as a Jones matrix on the
engine's output field of [QWP1, Kerr], checked against the engine's own waveplate + polarizer elements.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402

LAM = 1030e-9
TAU = 900e-15
B_LIN = 10.0
C0 = 299792458.0
PAPER_B = 10.0
GRID = {"n": 4096, "windowS": 12e-12, "auto": False}


def system(th1_deg, th2_engine_deg=None):
    el = [{"id": "qwp1", "type": "waveplate", "plate": "quarter", "axisDeg": th1_deg, "designWavelengthM": LAM},
          {"id": "kerr", "type": "bulk", "material": "FusedSilica", "lengthM": 1e-3, "beam": {"kind": "constant", "radiusM": 1e-3}}]
    if th2_engine_deg is not None:
        el += [{"id": "qwp2", "type": "waveplate", "plate": "quarter", "axisDeg": th2_engine_deg, "designWavelengthM": LAM},
               {"id": "pol", "type": "polarizer", "axisDeg": 90, "extinctionRatio": 1e8}]
    return {"format": "ultrafast-system", "version": 2, "name": "ner-mpc", "elements": el}


def spec(energy_j, raman=True):
    return {"format": "ultrafast-run", "version": 2, "name": "ner-mpc",
            "pulse": {"shape": "gaussian", "centerWavelengthM": LAM, "durationFwhmS": TAU, "energyJ": energy_j, "polarization": "x"},
            "grid": GRID, "physics": {"selfSteepening": False, "raman": raman}}


def field(r, which="output"):
    """Complex time-domain envelopes (x, y): the result holds the spectral envelopes on the (ascending) omega grid."""
    def to_time(a):
        return np.fft.fftshift(np.fft.fft(np.fft.ifftshift(a)))
    return (to_time(r[which + "_ax_re"] + 1j * r[which + "_ax_im"]),
            to_time(r[which + "_ay_re"] + 1j * r[which + "_ay_im"]))


def after_qwp2_pol(th2_paper_deg, ax, ay, extinction=0.0):
    """Field behind QWP2 (paper angle) and the crossed polariser (axis y); extinction = leaked intensity fraction."""
    th = math.radians(-th2_paper_deg)
    c, s = math.cos(th), math.sin(th)
    u, v = c * ax + s * ay, -s * ax + c * ay
    v = 1j * v                                   # fast axis along the plate's x (validated against the engine)
    bx, by = c * u - s * v, s * u + c * v
    return by + math.sqrt(extinction) * bx      # crossed polariser passes y; the leak is in amplitude


def contrast_at(t, ex, delay=2e-12):
    """log10 of (peak / intensity at peak + delay) for a field with intensity |ex|^2 (peak interpolated)."""
    p = np.abs(ex) ** 2
    i0 = int(np.argmax(p))
    j = i0 + int(round(delay / (t[1] - t[0])))
    return p[i0] / p[j] if p[j] > 0 else np.inf


def compress(t, e, w0):
    """Peak intensity relative to the input peak after the GDD that maximises it, and that GDD (s^2)."""
    n = len(t)
    dt = t[1] - t[0]
    om = 2 * np.pi * np.fft.fftfreq(n, dt)
    a = np.fft.ifft(e)
    best = (0, 0)
    for g in np.linspace(-2.5e-26, 2.5e-26, 201):
        pk = np.max(np.abs(np.fft.fft(a * np.exp(0.5j * g * om ** 2))) ** 2)
        if pk > best[0]:
            best = (pk, g)
    lo, hi = best[1] - 2.5e-27, best[1] + 2.5e-27
    for g in np.linspace(lo, hi, 101):
        pk = np.max(np.abs(np.fft.fft(a * np.exp(0.5j * g * om ** 2))) ** 2)
        if pk > best[0]:
            best = (pk, g)
    return np.fft.fft(a * np.exp(0.5j * best[1] * om ** 2)), best[1]


def spectrum(t, e):
    """Wavelength (nm) and power spectrum, peak 1, of an envelope e(t) (convention e^{-i w t})."""
    n = len(t)
    om = 2 * np.pi * np.fft.fftshift(np.fft.fftfreq(n, t[1] - t[0]))
    s = np.abs(np.fft.fftshift(np.fft.ifft(e))) ** 2
    lam = 2 * np.pi * C0 / (2 * np.pi * C0 / LAM + om)
    return lam * 1e9, s / s.max()


def width(t, p):
    i = int(np.argmax(p))
    h = p[i] / 2
    lo = i
    while lo > 0 and p[lo] > h:
        lo -= 1
    hi = i
    while hi < len(p) - 1 and p[hi] > h:
        hi += 1
    return t[hi] - t[lo]


def main():
    p = Paper(__file__)
    step = 2.0 if p.fast else 1.0
    angles = np.arange(-50, 50 + 1e-9, step)
    n = len(angles)

    # calibrate: linear light, energy for B = 10 rad (B is proportional to the energy for one element)
    r = uf.run(system(0.0), spec(1e-3))
    energy = 1e-3 * B_LIN / r.summary["totals"]["bIntegralRad"]

    # one engine run per QWP1 angle; QWP2 and the polariser are applied on the output field
    fields = []
    t = None
    e_in = None
    for th1 in angles:
        r = uf.run(system(float(th1)), spec(energy))
        t = r["output_time_s"]
        ax, ay = field(r)
        fields.append((ax, ay))
        if e_in is None:
            e_in = field(r, "input")
    pin = np.abs(e_in[0]) ** 2 + np.abs(e_in[1]) ** 2
    ein = pin.sum()
    cin = math.exp(4 * math.log(2) * (2e-12 / TAU) ** 2)   # analytic Gaussian: peak / I(+2 ps)

    eff = np.zeros((n, n))      # [i1, i2]
    ci = np.full((n, n), np.nan)
    for i1 in range(n):
        ax, ay = fields[i1]
        for i2 in range(n):
            e = after_qwp2_pol(angles[i2], ax, ay)
            pe = np.abs(e) ** 2
            eff[i1, i2] = pe.sum() / ein
            if eff[i1, i2] > 1e-3 * 0.0 + 1e-12:
                ci[i1, i2] = math.log10(contrast_at(t, e) / cin)
    effn = eff / eff.max()
    k = int(np.argmax(effn))
    i1m, i2m = np.unravel_index(k, effn.shape)
    sym = float(np.max(np.abs(effn - effn[::-1, ::-1])))

    # engine's own QWP2 + polariser elements at a few points: validates the Jones matrices used above
    chk = []
    for th1, th2 in [(10, 10), (30, -20), (-35, 30), (40, 40)]:
        r = uf.run(system(th1, -th2), spec(energy))
        ax, ay = field(r)
        a0, b0 = fields[int(np.argmin(np.abs(angles - th1)))]
        e = after_qwp2_pol(th2, a0, b0)
        chk.append(abs((np.abs(ax) ** 2 + np.abs(ay) ** 2).sum() - (np.abs(e) ** 2).sum()) / ein)
    jones_vs_engine = float(max(chk))

    # diagonal theta1 = theta2 (paper convention)
    diag = np.array([effn[i, i] for i in range(n)])
    cid = np.array([ci[i, i] for i in range(n)])
    pos = angles > 0
    th = angles[pos]
    dg = diag[pos]

    def lo_hi(a, b, f=np.argmax):
        m = (th >= a) & (th <= b)
        j = np.where(m)[0][f(dg[m])]
        return float(th[j]), float(dg[j])

    p1, e_p1 = lo_hi(2, 20)
    mn, e_mn = lo_hi(15, 32, np.argmin)
    p2, e_p2 = lo_hi(28, 42)
    z_m = (th >= 38)
    zero = float(th[z_m][np.argmin(dg[z_m])])
    e_at3 = float(np.interp(3, th, dg))
    e_at40 = float(np.interp(40, th, dg))
    cid_pos = cid[pos]

    # contrast improvement: largest value within 2 deg of the diagonal (outside the origin) and far from it
    on_v, on_a, off_v = [], [], []
    for i in range(n):
        for j in range(n):
            v = ci[i, j]
            if not np.isfinite(v) or max(abs(angles[i]), abs(angles[j])) < 3:
                continue
            if abs(angles[i] - angles[j]) <= 2:
                on_v.append(v)
                on_a.append(abs(angles[i]))
            elif abs(angles[i] - angles[j]) >= 10:
                off_v.append(v)
    ci_on = float(max(on_v)) if on_v else float("nan")
    ci_on_angle = float(on_a[int(np.argmax(on_v))]) if on_v else float("nan")
    ci_off = float(max(off_v)) if off_v else float("nan")

    # P1 / P2 spectra and compressed pulses
    sel = {}
    for name, a in (("P1", p1), ("P2", p2)):
        i = int(np.argmin(np.abs(angles - a)))
        e = after_qwp2_pol(angles[i], *fields[i])
        lam, sp = spectrum(t, e)
        ec, g = compress(t, e, 0)
        pc = np.abs(ec) ** 2
        sel[name] = {"angle": float(angles[i]), "lam": lam, "sp": sp, "t": t, "e": e, "ec": ec, "gdd": g,
                     "peakRel": float(pc.max() / np.abs(e).max() ** 2), "pc": pc}
    for name in sel:
        s = sel[name]
        pk = s["pc"].max()
        s["cleanFrac"] = float(s["pc"][np.abs(s["t"] - s["t"][int(np.argmax(s["pc"]))]) <= 1.5 * width(s["t"], s["pc"])].sum() / s["pc"].sum())
        # modulation depth of the spectrum between the 10 % points of its envelope
        m = s["sp"] > 0.02
        sm = s["sp"][m]
        idx = np.where((sm[1:-1] < sm[:-2]) & (sm[1:-1] < sm[2:]))[0] + 1
        s["modulation"] = float(np.max(1 - sm[idx] / np.maximum(np.maximum.accumulate(sm)[idx], 1e-12))) if len(idx) else 0.0
        s["bw"] = float(np.ptp(s["lam"][m]))

    numbers = {
        "bLinear": B_LIN, "energyForB10J": energy, "jonesVsEngineMaxErr": jones_vs_engine, "symmetryMaxDev": sym,
        "efficiencyAtOrigin": float(effn[n // 2, n // 2]),
        "mapPeakTheta1": float(angles[i1m]), "mapPeakTheta2": float(angles[i2m]),
        "mapPeakAbsTheta1": abs(float(angles[i1m])), "mapPeakAbsTheta2": abs(float(angles[i2m])),
        "mapPeakOppositeSigns": float(np.sign(angles[i1m]) * np.sign(angles[i2m]) < 0),
        "diagP1Angle": p1, "diagP1Eff": e_p1, "diagMinAngle": mn, "diagMinEff": e_mn, "diagP2Angle": p2, "diagP2Eff": e_p2,
        "diagEffAt3": e_at3, "diagEffAt40": e_at40, "diagZeroAngle": zero,
        "diagonalCurve": {"x": th, "y": dg},
        "ciDiagMax": ci_on, "ciDiagMaxAbsAngle": ci_on_angle, "ciOffDiagMax": ci_off,
        "ciAtP1": float(np.interp(p1, th, cid_pos)), "ciAtP2": float(np.interp(p2, th, cid_pos)),
        "ciAt45": float(np.interp(45, th, cid_pos)),
        "p1Modulation": sel["P1"]["modulation"], "p2Modulation": sel["P2"]["modulation"],
        "p1BandwidthNm": sel["P1"]["bw"], "p2BandwidthNm": sel["P2"]["bw"],
        "p1CompressedPeakRel": sel["P1"]["peakRel"], "p2CompressedPeakRel": sel["P2"]["peakRel"],
        "p1CleanFraction": sel["P1"]["cleanFrac"], "p2CleanFraction": sel["P2"]["cleanFrac"],
        "inputContrastAt2ps": cin,
    }

    extra = {}
    if not p.fast:
        # B sweep of the diagonal (cell strength is the main unknown) and finite polariser extinction
        sweep = {}
        for bl in (6.0, 15.0, 25.0):
            en = energy * bl / B_LIN
            ds = []
            for a in angles[pos]:
                r = uf.run(system(float(a)), spec(en))
                ds.append((np.abs(after_qwp2_pol(a, *field(r))) ** 2).sum() / ein)
            ds = np.array(ds)
            sweep[bl] = ds / max(ds.max(), 1e-30)
        extra["sweep"] = sweep
        cie = []
        for i in np.where(pos)[0]:
            e = after_qwp2_pol(angles[i], *fields[i], extinction=1e-5)
            cie.append(math.log10(contrast_at(t, e) / cin))
        extra["ci_er"] = np.array(cie)
        numbers["ciAtP2ExtinctionOneE5"] = float(np.interp(p2, th, extra["ci_er"]))

    p.write_numbers(numbers)
    plots(p, angles, effn, ci, diag, cid, th, dg, cid_pos, sel, (i1m, i2m), numbers, extra)


def plots(p, angles, effn, ci, diag, cid, th, dg, cid_pos, sel, imax, nb, extra):
    import matplotlib.pyplot as plt
    ext = [angles[0], angles[-1], angles[0], angles[-1]]
    # Fig A: efficiency map (axes as the paper: x = theta2, y = theta1)
    fig, ax = p.figure(2, 1, 9.2, 4.2)
    im = ax[0].imshow(effn, origin="lower", extent=ext, cmap="viridis", aspect="equal")
    ax[0].set_xlabel("QWP2 angle theta2 (deg)")
    ax[0].set_ylabel("QWP1 angle theta1 (deg)")
    ax[0].set_title("MPC efficiency, normalised to its peak")
    fig.colorbar(im, ax=ax[0], fraction=0.046)
    for sgn in (1, -1):
        ax[0].plot(-30 * sgn, 35 * sgn, "o", mfc="none", mec="white", ms=11, mew=1.6)
    ax[0].plot([-50, 50], [-50, 50], "--", color="white", lw=0.8)
    ax[0].text(-48, 44, "o: read off the paper's maxima\n(-30,35) and (30,-35)", color="white", fontsize=7.5)
    c = np.where(np.isfinite(ci), ci, np.nan)
    im2 = ax[1].imshow(c, origin="lower", extent=ext, cmap="magma", aspect="equal", vmin=max(0, np.nanmin(c)), vmax=np.nanmax(c))
    ax[1].set_xlabel("QWP2 angle theta2 (deg)")
    ax[1].set_ylabel("QWP1 angle theta1 (deg)")
    ax[1].set_title("contrast improvement at +2 ps (log10)")
    fig.colorbar(im2, ax=ax[1], fraction=0.046)
    ax[1].plot([-50, 50], [-50, 50], "--", color="white", lw=0.8)
    p.save(fig, "maps")

    # Fig B: the diagonal
    fig, ax = p.figure(2, 1, 9.6, 3.8)
    ax[0].plot(th, dg, color=BLUE, label="engine, B = 10 rad (assumed)")
    pts = [(3, 0.55), (10, 1.0), (26, 0.47), (35, 0.62), (40, 0.4), (44, 0.0)]
    ax[0].plot(*zip(*pts), "o", color=REF, label="read off the paper's Fig. 2a")
    for b, d in extra.get("sweep", {}).items():
        ax[0].plot(th, d, lw=1.2, ls=":", label="engine, B = %g rad (own peak = 1)" % b)
    ax[0].set_xlabel("theta1 = theta2 (deg)")
    ax[0].set_ylabel("efficiency (normalised)")
    ax[0].set_title("along the diagonal")
    ax[0].legend(fontsize=7.5)
    ax[1].plot(th, cid_pos, color=ORANGE, label="ideal polariser")
    if "ci_er" in extra:
        ax[1].plot(th, extra["ci_er"], color=AQUA, label="polariser leak 1e-5")
    ax[1].axhspan(8, 9, color=REF2, alpha=0.25, label="paper, near 45 deg: ~8-9 (read off Fig. 1c)")
    ax[1].set_xlabel("theta1 = theta2 (deg)")
    ax[1].set_ylabel("contrast improvement (log10)")
    ax[1].legend(fontsize=7.5)
    p.save(fig, "diagonal")

    # Fig C: spectra and compressed pulses of P1 and P2
    fig, ax = p.figure(2, 1, 9.6, 3.8)
    for (name, s), col in zip(sel.items(), (BLUE, ORANGE)):
        ax[0].plot(s["lam"], s["sp"], color=col, label="%s (%g deg)" % (name, s["angle"]))
        pc = s["pc"] / sel["P1"]["pc"].max()
        tt = (s["t"] - s["t"][int(np.argmax(s["pc"]))]) * 1e15
        ax[1].plot(tt, pc, color=col, label="%s, GDD %.0f fs2" % (name, s["gdd"] * 1e30))
    ax[0].set_xlim(1000, 1060)
    ax[0].set_xlabel("wavelength (nm)")
    ax[0].set_ylabel("spectrum (peak = 1)")
    ax[0].legend()
    ax[1].set_xlim(-1500, 1500)
    ax[1].set_xlabel("time (fs)")
    ax[1].set_ylabel("compressed power (P1 peak = 1)")
    ax[1].legend()
    p.save(fig, "p1-p2")


if __name__ == "__main__":
    main()
