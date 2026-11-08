"""Waveguides, gain media, gases + ionisation and bulk Kerr / Raman alone, each against an analytic or published answer.

What it does: runs the `fiber`, `gain`, gas `bulk` (n(p), n2(p), rotational Raman, PPT ionisation, plasma) and Kerr `bulk`
elements one at a time through the Python API and compares with closed forms written out below (they do not use the
engine). Every check prints its numbers; most are drawn engine-over-analytic with a residual panel.

Sections (one function each):
  fiber_spm          SPM phase = gamma P(t) L at 3 carriers x (1 ps, 10 fs); Gaussian rms broadening sqrt(1 + 4 B^2 / (3 sqrt 3))
  fiber_dispersion   beta2 / beta3 reported and as GDD / TOD; D(lambda) curve -> beta2 = -D lambda^2 / (2 pi c); Gaussian
                     broadening sqrt(1 + (L / L_D)^2)
  fiber_solitons     N = 1 soliton invariance and phase z / (2 L_D); N = 2 return at z0 = (pi / 2) L_D; SSFS (Gordon 1986)
  fiber_loss_gain    dB/km loss, dB/m gain
  fiber_dispersive_wave   resonance sum_k beta_k dw^k / k! = gamma P0 / 2 (Akhmediev & Karlsson 1995) and the spectral peak
  gain               small-signal gain exp(sigma beta N L), Frantz-Nodvik, energy extraction, cw pumping steady state
  gases              n(p) refractivity scaling, n2 scaling with density, rotational Raman (N2 revival, n2 sum), Air
  ionisation         PPT multiphoton order, electron density = rho_n int W dt, Drude blue shift
  bulk_kerr          B = k0 n2 I L, SPM broadening, self-steepening shift of the peak, linear-in-B NER of an elliptic input

Conventions that matter (docs/concepts.md): envelope e^{-i omega t} (SPM phase is +gamma P L), `*_ax_re/im` are spectral
envelopes (time domain = fftshift(fft(ifftshift(a)))), `*_phase_x_rad` is the spectral phase, and the engine's 1-D beam cell
is pi w^2 / 2 (the on-axis fluence of a Gaussian beam).

Run: python tests/components/media.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
"""
from __future__ import annotations

import math
import sys

from _common import C, CARRIERS_M, DURATIONS_S, Family, b, centroid, fwhm, label_dur, label_wl, np, overlay, rms_width, uf

H = 6.62607015e-34
HBAR = H / (2 * math.pi)
KB = 1.380649e-23
EPS0 = 8.8541878128e-12
GAUSS_P0 = 2 * math.sqrt(math.log(2) / math.pi)      # peak power = GAUSS_P0 E / tau_FWHM for a Gaussian
SECH_T0 = 1 / (2 * math.log(1 + math.sqrt(2)))        # T0 = SECH_T0 * FWHM


def tfield(res, prefix):
    """(t, A(t)) with |A|^2 = P(t) [W] from the spectral envelope of `prefix` (convention e^{-i omega t})."""
    a = np.asarray(res[prefix + "_ax_re"]) + 1j * np.asarray(res[prefix + "_ax_im"])
    A = np.fft.fftshift(np.fft.fft(np.fft.ifftshift(a)))
    P = np.asarray(res[prefix + "_power_w"])
    return np.asarray(res[prefix + "_time_s"]), A * math.sqrt(P.sum() / (np.abs(A) ** 2).sum())


def energy_gauss_peak(p0, tau):
    return p0 * tau / GAUSS_P0


def sech_energy(p0, t0):
    return 2 * p0 * t0


def vals(res, i=0):
    return res.summary["elements"][i]["values"]


def element(res, i=0):
    return res.summary["elements"][i]


def run1(el, pulse, **physics):
    return uf.run(b.system("c", [el] if isinstance(el, dict) else el), b.run_spec(pulse, physics=physics or None))


def fwhm_power(res, prefix):
    return fwhm(res[prefix + "_time_s"], res[prefix + "_power_w"])


def unwrap_about_peak(d, ip):
    """Unwrap a phase array so that the value at index ip is its principal value."""
    u = np.unwrap(d)
    return u - u[ip] + d[ip]


# ======================================================================================================= fibre

KNOWN_2UM_10FS = ("2 µm, 10 fs is 1.5 optical cycles: the B = 3 SPM spectrum reaches omega = 0, where an envelope model has no field. The engine "
                  "drops that part, books it as 'spectrum at non-positive frequency' and warns (GridSolverTests."
                  "PulseBroadenedToZeroFrequency_IsReportedAndBooked); the result is outside the model's range, not a numerical error")

def fiber_spm(fam: Family):
    """gamma P(t) L at B = 3 for every carrier and both bandwidths; the Gaussian broadening factor."""
    B0 = 3.0
    res_plot = {}
    for lam in CARRIERS_M:
        for tau in DURATIONS_S:
            L, p0 = 1.0, 1e3
            gamma = B0 / (p0 * L)
            el = b.fiber("f", L, betas=[0.0], gamma=gamma, raman="none")
            r = run1(el, b.pulse(lam, tau, energy=energy_gauss_peak(p0, tau)), selfSteepening=False)
            t, Ai = tfield(r, "input")
            _, Ao = tfield(r, "output")
            P = np.abs(Ai) ** 2
            m = P > 0.05 * P.max()                     # the body of the pulse; the wings carry the FFT noise floor
            ip = int(np.argmax(P))
            d = np.angle(Ao / np.where(np.abs(Ai) > 0, Ai, 1))
            rel = unwrap_about_peak(np.where(m, d, 0.0), ip)
            want = gamma * L * (P - P.max())
            err = float(np.max(np.abs((rel - rel[ip] - want)[m])))
            tag = f"{label_wl(lam)} {label_dur(tau)}"
            kn = KNOWN_2UM_10FS if (lam == 2e-6 and tau == 10e-15) else ""
            # 10 mrad of 3 rad (0.3 %): the auto grid samples a 10 fs pulse with 12 points per FWHM and the SPM spectrum is 2.8x wider
            fam.check(f"SPM phase max |Δφ - γLΔP| {tag}", err, 0.0, abs=1e-2, unit="rad", known=kn,
                      formula="φ(t) - φ(0) = γ L (P(t) - P0), B = 3, pulse body P > 5 % of the peak")
            res_plot[(lam, tau)] = (t, rel - rel[ip], want, m)
            ratio = rms_width(r["output_omega_rad_s"], r["output_spectrum"]) / rms_width(r["input_omega_rad_s"], r["input_spectrum"])
            fam.check(f"SPM rms broadening {tag}", ratio, math.sqrt(1 + 4 * B0 ** 2 / (3 * math.sqrt(3))), rel=3e-3, known=kn,
                      formula="σ/σ0 = sqrt(1 + 4 B² / (3 sqrt3)) for a Gaussian (Agrawal, Nonlinear Fiber Optics)")
            fam.check(f"SPM keeps the power profile {tag}", float(np.max(np.abs(np.abs(Ao) ** 2 - P)) / P.max()), 0.0, abs=2e-3, known=kn,
                      formula="|A(t)|² unchanged by pure SPM (no dispersion)")
    for tau, key in ((1e-12, "fiber-spm-phase"), (10e-15, "fiber-spm-phase-10fs")):
        with fam.figure(key, f"Fibre SPM, B = 3, {label_dur(tau)}: temporal phase against γ L P(t), three carriers",
                        "φ(t) - φ(0) = γ L (P(t) - P0);  engine: arg A_out/A_in from the spectral envelopes") as f:
            for lam in CARRIERS_M:
                t, e, a, m = res_plot[(lam, tau)]
                sc = 1e12 if tau >= 1e-12 else 1e15
                overlay(f, t[m] * sc, e[m], a[m], label=label_wl(lam), xlabel="t (ps)" if tau >= 1e-12 else "t (fs)", ylabel="Δφ (rad)",
                        analytic_label="γ L P(t)" if lam == CARRIERS_M[0] else None)

    with fam.figure("fiber-spm-broadening", "Fibre SPM, 1030 nm Gaussian: rms spectral broadening against B",
                    "σ/σ0 = sqrt(1 + 4 B² / (3 sqrt3))") as f:
        Bs = np.array([0.5, 1.0, 2.0, 3.0, 5.0])
        eng = []
        for B_ in Bs:
            p0 = 1e3
            r = run1(b.fiber("f", 1.0, betas=[0.0], gamma=float(B_ / p0), raman="none"), b.pulse(1030e-9, 1e-12, energy=energy_gauss_peak(p0, 1e-12)), selfSteepening=False)
            ratio = rms_width(r["output_omega_rad_s"], r["output_spectrum"]) / rms_width(r["input_omega_rad_s"], r["input_spectrum"])
            fam.check(f"SPM broadening B={B_:g}", ratio, math.sqrt(1 + 4 * B_ ** 2 / (3 * math.sqrt(3))), rel=3e-3, formula="sqrt(1 + 4B²/(3√3))")
            eng.append(ratio)
        overlay(f, Bs, eng, np.sqrt(1 + 4 * Bs ** 2 / (3 * math.sqrt(3))), label="1030 nm, 1 ps", xlabel="B = γ P0 L (rad)", ylabel="σ/σ0",
                marker="o", residual="rel", analytic_label="sqrt(1 + 4B²/(3√3))")


def beta_curve_D(lam_m):
    """A linear dispersion-parameter curve D(λ) [ps/(nm km)] used for the D -> beta2 check."""
    return 17.0 + 0.05 * (lam_m * 1e9 - 1550.0)


def fiber_dispersion(fam: Family):
    with fam.figure("fiber-gdd", "Fibre: Gaussian broadening against L / L_D (1030 nm, 1 ps), GDD and TOD reported",
                    "GDD = β2 L, TOD = β3 L;  τ(L)/τ0 = sqrt(1 + (L/L_D)²),  L_D = T0²/|β2|,  T0 = τ/(2 sqrt ln2)") as f:
        for lam in CARRIERS_M:
            for tau in DURATIONS_S:
                t0 = tau / (2 * math.sqrt(math.log(2)))
                b2 = -t0 * t0 / 0.5                           # L_D = 0.5 m
                L = 0.5 * 1.5
                el = b.fiber("f", L, betas=[b2, 0.0], gamma=0.0, raman="none", nonlinear=False)
                r = run1(el, b.pulse(lam, tau, energy=1e-12))
                e = element(r)
                tag = f"{label_wl(lam)} {label_dur(tau)}"
                fam.check(f"fibre GDD reported {tag}", e["own"]["gddS2"], b2 * L, rel=1e-4, unit="s²", formula="β2 L")
                fam.check(f"fibre β2 reported {tag}", e["values"]["beta2S2PerM"], b2, rel=1e-4, unit="s²/m")
                fam.check(f"dispersive broadening {tag}", fwhm_power(r, "output") / tau, math.sqrt(1 + 1.5 ** 2), rel=3e-3,
                          formula="sqrt(1 + (L/L_D)²), L = 1.5 L_D")
        lam, tau = 1030e-9, 1e-12
        t0 = tau / (2 * math.sqrt(math.log(2)))
        b2 = -t0 * t0 / 0.5
        zs = np.linspace(0.1, 2.0, 7)
        eng = []
        for z in zs:
            r = run1(b.fiber("f", float(z * 0.5), betas=[b2], gamma=0.0, raman="none", nonlinear=False), b.pulse(lam, tau, energy=1e-12))
            eng.append(fwhm_power(r, "output") / tau)
        overlay(f, zs, eng, np.sqrt(1 + zs ** 2), label="1030 nm, 1 ps", xlabel="L / L_D", ylabel="τ(L)/τ0", marker="o", residual="rel",
                analytic_label="sqrt(1 + (L/L_D)²)")
        for lam in CARRIERS_M:
            b3 = 5e-41
            r = run1(b.fiber("f", 2.0, betas=[0.0, b3], gamma=0.0, raman="none", nonlinear=False), b.pulse(lam, 100e-15, energy=1e-12))
            fam.check(f"fibre TOD reported {label_wl(lam)}", element(r)["own"]["todS3"], b3 * 2.0, rel=1e-3, unit="s³", formula="β3 L")

    with fam.figure("fiber-D-curve", "Fibre dispersion curve D(λ): reported β2 against -D λ² / (2π c)",
                    "β2 = -D λ² / (2π c); D(λ) = 17 + 0.05 (λ - 1550 nm) ps/(nm km), interpolated and integrated twice by the engine") as f:
        wl = np.linspace(400e-9, 3000e-9, 1041)
        D = beta_curve_D(wl)
        eng, ana, lams = [], [], []
        for lam in CARRIERS_M:
            el = b.fiber("f", 0.1, dispersion={"wavelengthsM": [float(x) for x in wl], "dPsNmKm": [float(x) for x in D]}, gamma=0.0, raman="none", nonlinear=False)
            r = run1(el, b.pulse(lam, 100e-15, energy=1e-12))
            lc = element(r)["carrierWavelengthM"]
            want = -beta_curve_D(lc) * 1e-6 * lc ** 2 / (2 * math.pi * C)
            got = element(r)["values"]["beta2S2PerM"]
            fam.check(f"D(λ) → β2 {label_wl(lam)}", got, want, rel=3e-4, unit="s²/m", formula="β2 = -D λ²/(2πc) at the (grid-snapped) carrier")
            lrep = 2 * math.pi * C / r.summary["grid"]["reportOmega0"]
            wantg = -beta_curve_D(lrep) * 1e-6 * lrep ** 2 / (2 * math.pi * C) * 0.1
            fam.check(f"D(λ) → GDD of 0.1 m {label_wl(lam)}", element(r)["own"]["gddS2"], wantg, rel=1e-3, unit="s²",
                      formula="reported GDD is at grid.reportOmega0 (cubic spline of β2(ω) through the table)")
            eng.append(got)
            ana.append(want)
            lams.append(lc * 1e9)
        overlay(f, lams, eng, ana, label="engine at the 3 carriers", xlabel="wavelength (nm)", ylabel="β2 (s²/m)", marker="o", residual="rel",
                analytic_label="-Dλ²/(2πc)")


def fiber_solitons(fam: Family):
    with fam.figure("fiber-soliton-phase", "Fundamental soliton (N = 1): peak phase against z / (2 L_D), 1030 nm, 1 ps",
                    "A(z, t) = sqrt(P0) sech(t/T0) exp(i z/(2 L_D)); P0 = |β2|/(γ T0²)") as f:
        for lam in CARRIERS_M:
            for tau, b2, gamma in ((1e-12, -20e-27, 1e-3), (10e-15, -2e-28, 0.1)):
                t0 = tau * SECH_T0
                p0 = abs(b2) / (gamma * t0 * t0)
                ld = t0 * t0 / abs(b2)
                L = 3.0 * ld
                el = b.fiber("f", L, betas=[b2], gamma=gamma, raman="none")
                r = run1(el, b.pulse(lam, tau, energy=sech_energy(p0, t0), shape="sech2"), selfSteepening=False)
                tag = f"{label_wl(lam)} {label_dur(tau)}"
                t, Ai = tfield(r, "input")
                _, Ao = tfield(r, "output")
                pin = np.abs(Ai) ** 2
                ip = int(np.argmax(pin))
                fam.check(f"soliton peak kept {tag}", np.abs(Ao[ip]) ** 2, pin[ip], rel=3e-3, unit="W", formula="N = 1: P(z, 0) = P0")
                fam.check(f"soliton FWHM kept {tag}", fwhm_power(r, "output") / fwhm_power(r, "input"), 1.0, abs=3e-3, formula="N = 1: τ(z) = τ0")
                ph = float(np.angle(Ao[ip] / Ai[ip]))
                want = (L / (2 * ld)) % (2 * math.pi)
                dd = (ph - want + math.pi) % (2 * math.pi) - math.pi
                fam.check(f"soliton phase z/(2L_D) {tag}", dd, 0.0, abs=5e-3, unit="rad", formula="φ_peak = z/(2 L_D) (mod 2π), z = 3 L_D")
                if lam == 1030e-9 and tau == 1e-12:
                    zs = np.linspace(0.5, 5, 10)
                    eng = []
                    for z in zs:
                        rr = run1(b.fiber("f", float(z * ld), betas=[b2], gamma=gamma, raman="none"), b.pulse(lam, tau, energy=sech_energy(p0, t0), shape="sech2"),
                                  selfSteepening=False)
                        _, a1 = tfield(rr, "input")
                        _, a2 = tfield(rr, "output")
                        eng.append(float(np.angle(a2[ip] / a1[ip])))
                    ana = np.angle(np.exp(1j * zs / 2))
                    overlay(f, zs, eng, ana, label="1030 nm, 1 ps", xlabel="z / L_D", ylabel="peak phase (rad, principal value)", marker="o",
                            analytic_label="z/(2 L_D)")

    with fam.figure("fiber-soliton-n2", "N = 2 soliton at z0 = (π/2) L_D: the pulse returns, 1030 nm 1 ps",
                    "u(z0, τ) = u(0, τ) for N = 2 (Agrawal, Applications of Nonlinear Fiber Optics, 5.2); z0 = (π/2) L_D") as f:
        lam, tau, b2, gamma = 1030e-9, 1e-12, -20e-27, 1e-3
        t0 = tau * SECH_T0
        p0 = 4 * abs(b2) / (gamma * t0 * t0)
        ld = t0 * t0 / abs(b2)
        r = run1(b.fiber("f", math.pi / 2 * ld, betas=[b2], gamma=gamma, raman="none"), b.pulse(lam, tau, energy=sech_energy(p0, t0), shape="sech2"), selfSteepening=False)
        t, Ai = tfield(r, "input")
        _, Ao = tfield(r, "output")
        pi_, po = np.abs(Ai) ** 2, np.abs(Ao) ** 2
        fam.check("N=2 soliton returns: L1 distance of P(t)", float(np.abs(po - pi_).sum() / pi_.sum()), 0.0, abs=0.01, formula="Σ|P_out - P_in| / ΣP_in at z0")
        fam.check("N=2 soliton returns: peak power", po.max(), pi_.max(), rel=0.01, unit="W")
        m = pi_ > 1e-3 * pi_.max()
        overlay(f, t[m] * 1e12, po[m], pi_[m], label="z = z0", xlabel="t (ps)", ylabel="P (W)", analytic_label="input pulse")

    with fam.figure("fiber-ssfs", "Soliton self-frequency shift against Gordon's perturbation integral (Blow-Wood Raman, 1550 nm)",
                    "dΩ/dz = -(γ fR / 2 P0 T0)(1/π) ∫0∞ |P̃(Ω)|² Ω Im H(Ω) dΩ,  |P̃|² = (π P0 T0² Ω / sinh(π Ω T0 / 2))²") as f:
        lam = 1550e-9
        b2 = -15e-6 * lam * lam / (2 * math.pi * C)
        gamma = 1.3e-3
        t1, t2, fR = 12.2e-15, 32e-15, 0.18
        eng, ana, t0s = [], [], []
        for t0 in (200e-15, 300e-15, 450e-15):
            p0 = abs(b2) / (gamma * t0 * t0)
            L = 30.0
            r = run1(b.fiber("f", L, betas=[b2], gamma=gamma, raman="silica"), b.pulse(lam, t0 / SECH_T0, peak_power_w=p0, shape="sech2"), selfSteepening=False)
            rate = (centroid(r["output_omega_rad_s"], r["output_spectrum"]) - centroid(r["input_omega_rad_s"], r["input_spectrum"])) / L
            Om = np.linspace(1e9, 40 / t0, 4000)
            imH = ((t1 ** 2 + t2 ** 2) / (t1 * t2 ** 2) * (1 / t1) / ((1 / t2 - 1j * Om) ** 2 + 1 / t1 ** 2)).imag
            pw = (math.pi * p0 * t0 ** 2 * Om / np.sinh(math.pi * Om * t0 / 2)) ** 2
            gordon = -gamma * fR / (2 * p0 * t0) * 2 * float(np.trapezoid(pw * Om * imH, Om)) / (2 * math.pi)
            fam.check(f"SSFS rate T0={t0 * 1e15:.0f} fs", rate, gordon, rel=0.03, unit="rad/s/m",
                      formula="Gordon 1986 integral with the Blow-Wood response (12.2 fs, 32 fs, fR = 0.18)")
            eng.append(rate)
            ana.append(gordon)
            t0s.append(t0 * 1e15)
        overlay(f, t0s, eng, ana, label="1550 nm, N = 1", xlabel="T0 (fs)", ylabel="dΩ/dz (rad/s/m)", marker="o", residual="rel",
                analytic_label="Gordon integral")


def fiber_loss_gain(fam: Family):
    with fam.figure("fiber-loss-gain", "Fibre: energy transmission against dB loss",
                    "E_out/E_in = 10^(-α_dB L/10) (α in dB/km); gain 10^(g_dB L/10) (g in dB/m) checked in the table") as f:
        Ls = np.array([100.0, 300.0, 1000.0, 3000.0])
        eng, ana = [], []
        for L in Ls:
            r = run1(b.fiber("f", float(L), betas=[0.0], gamma=0.0, loss_db_per_km=3.0, raman="none", nonlinear=False), b.pulse(1030e-9, 1e-12, energy=1e-9))
            t = element(r)["energyOutJ"] / element(r)["energyInJ"]
            want = 10 ** (-0.3 * L / 1000)
            fam.check(f"loss 3 dB/km, L = {L:g} m", t, want, rel=3e-4, formula="10^(-αL/10)")
            eng.append(t)
            ana.append(want)
        overlay(f, Ls, eng, ana, label="3 dB/km", xlabel="L (m)", ylabel="transmission", marker="o", residual="rel", analytic_label="10^(-αL/10)")
    for lam in CARRIERS_M:
        r = run1(b.fiber("f", 2.0, betas=[0.0], gamma=0.0, gain_db_per_m=5.0, raman="none", nonlinear=False), b.pulse(lam, 1e-12, energy=1e-9))
        fam.check(f"gain 5 dB/m over 2 m {label_wl(lam)}", element(r)["energyOutJ"] / element(r)["energyInJ"], 10.0, rel=3e-4, formula="10^(gL/10) = 10")


def fiber_dispersive_wave(fam: Family):
    """An N = 1 soliton over a strong beta3 radiates a dispersive wave at the phase-matched frequency. (An N = 3 fission is
    not a clean reference: the DW is then phase matched to the compressed soliton's power, not to the input's.)"""
    b2, b3, gamma, fw = -10e-27, 1e-40, 0.1, 30e-15
    lam = 1550e-9
    t0 = fw * SECH_T0
    p0 = abs(b2) / (gamma * t0 * t0)
    r = run1(b.fiber("pcf", 0.5, betas=[b2, b3], gamma=gamma, raman="none"), b.pulse(lam, fw, energy=sech_energy(p0, t0), shape="sech2"), selfSteepening=False)
    p0s = float(np.max(r["input_power_w"]))
    v = vals(r)
    lc = element(r)["carrierWavelengthM"]
    wc = 2 * math.pi * C / lc
    dws = np.linspace(1e13, 1e15, 400001)
    fv = b2 * dws ** 2 / 2 + b3 * dws ** 3 / 6 - gamma * p0s / 2
    k = int(np.where(np.sign(fv[:-1]) != np.sign(fv[1:]))[0][0])
    lam_dw = 2 * math.pi * C / (wc + dws[k])
    with fam.figure("fiber-dw", "Fibre N = 1 soliton over β3 > 0: dispersive-wave peak against the phase-matching condition",
                    "β2 Δω²/2 + β3 Δω³/6 = γ P0 / 2 (Akhmediev & Karlsson 1995); spectrum after 0.5 m = 17 L_D") as f:
        fam.check("dispersive-wave wavelength reported", v["dispersiveWaveWavelengthM"], lam_dw, rel=1e-3, unit="m", formula="root of the phase-matching condition")
        om = np.asarray(r["output_omega_rad_s"])
        ok = om > 0
        w = np.where(ok, 2 * math.pi * C / np.where(ok, om, 1.0), np.nan)
        sw = np.asarray(r["output_spectrum"]) * 2 * math.pi * C / np.where(ok, w, 1.0) ** 2
        band = ok & (w > 1100e-9) & (w < 1400e-9)
        pk = float(w[band][int(np.argmax(sw[band]))])
        fam.check("dispersive-wave peak in the simulated spectrum", pk, lam_dw, rel=0.01, unit="m",
                  formula="radiated peak vs the phase-matched wavelength (1 % allowed: the soliton's recoil and the finite resonance width)")
        if f.axes is not None:
            top, bot = f.axes
            top.semilogy(w[ok] * 1e9, np.maximum(sw[ok] / sw[ok].max(), 1e-12), label="engine output spectrum")
            top.axvline(lam_dw * 1e9, color="k", ls="--", label="phase-matched wavelength")
            top.set_xlim(900, 2400)
            top.set_ylim(1e-8, 2)
            top.set_ylabel("S(λ) (rel.)")
            top.legend(frameon=False, fontsize=8)
            top.grid(alpha=0.3)
            bot.plot([pk * 1e9], [(pk - lam_dw) / lam_dw], "o")
            bot.axhline(0, color="k", lw=0.6)
            bot.set_ylabel("(peak - DW)/DW")
            bot.set_xlabel("wavelength (nm)")


# ======================================================================================================= gain

TOY_SIGMA = 2e-24          # m^2
TOY_ND = 1e26              # 1/m^3
TOY_TAU = 1e-3             # s


def toy_medium(lam, fwhm_m=None):
    fwhm_m = fwhm_m if fwhm_m is not None else 0.2 * lam
    return {"name": "toy4", "lifetimeS": TOY_TAU, "dopingDensityM3": TOY_ND, "host": "YAG",
            "emission": {"kind": "gaussians", "bands": [{"wavelengthM": lam, "sigmaM2": TOY_SIGMA, "fwhmM": fwhm_m}]}}


def gain(fam: Family):
    L, w = 5e-3, 0.5e-3
    area = math.pi * w * w / 2                           # the 1-D cell
    with fam.figure("gain-small-signal", "Gain element: weak-pulse energy gain against exp(σ β N L), four-level toy medium, 1 ps",
                    "G = ⟨exp(σ(λ) β N_t L)⟩_S, Gaussian band σ(λ) = σ0 exp(-4 ln2 (λ-λ0)²/Δλ²), Δλ = 0.2 λ0; the 10 fs rows use the input spectrum S") as f:
        for lam in CARRIERS_M:
            for tau in DURATIONS_S:
                fwhm_m = 0.2 * lam
                betas = np.array([0.02, 0.1, 0.2, 0.4])
                eng, ana = [], []
                for beta in betas:
                    r = run1(b.gain("g", toy_medium(lam, fwhm_m), L, inversion={"fraction": float(beta)}, mode_radius_m=w, spm=False), b.pulse(lam, tau, energy=1e-12))
                    om = np.asarray(r["input_omega_rad_s"])
                    ok = om > 0                                   # the auto grid can reach omega <= 0 for a 10 fs pulse at 2 um
                    wl = 2 * math.pi * C / np.where(ok, om, 1.0)
                    s = np.where(ok, np.asarray(r["input_spectrum"]), 0.0)
                    sig = TOY_SIGMA * np.exp(-4 * math.log(2) * (wl - lam) ** 2 / fwhm_m ** 2)
                    want = float((s * np.exp(sig * beta * TOY_ND * L)).sum() / s.sum())
                    got = element(r)["energyOutJ"] / element(r)["energyInJ"]
                    fam.check(f"small-signal gain {label_wl(lam)} {label_dur(tau)} β={beta:g}", got, want, rel=1e-3, formula="⟨exp(σ β N L)⟩ over the input spectrum")
                    eng.append(got)
                    ana.append(want)
                    if tau == 1e-12:
                        fam.check(f"small-signal gain = e^(σβNL) {label_wl(lam)} 1 ps β={beta:g}", got, math.exp(TOY_SIGMA * beta * TOY_ND * L), rel=3e-3,
                                  formula="narrow band: G = exp(σ_e β N_t L)")
                if tau == 1e-12:
                    overlay(f, betas, eng, ana, label=label_wl(lam), xlabel="inversion fraction β", ylabel="energy gain", marker="o", residual="rel",
                            analytic_label="exp(σβNL)" if lam == CARRIERS_M[0] else None)

    with fam.figure("gain-frantz-nodvik", "Gain element: saturated energy gain against Frantz-Nodvik (1030 nm, 10 ps, β = 0.25)",
                    "E_out = E_s ln(1 + G0 (e^{E_in/E_s} - 1)),  E_s = h ν A / σ,  A = π w²/2,  G0 = e^{σ β N L}") as f:
        lam = 1030e-9
        hnu = H * C / lam
        beta = 0.25
        g0 = math.exp(TOY_SIGMA * beta * TOY_ND * L)
        es = hnu * area / TOY_SIGMA
        xs = np.array([0.01, 0.1, 0.5, 1.0, 3.0, 10.0])
        eng, ana = [], []
        for x in xs:
            ein = float(x * es)
            r = run1(b.gain("g", toy_medium(lam, 100e-9), L, inversion={"fraction": beta}, mode_radius_m=w, spm=False, slices=48), b.pulse(lam, 10e-12, energy=ein))
            eo = element(r)["energyOutJ"]
            want = es * math.log(1 + g0 * (math.exp(ein / es) - 1))
            fam.check(f"Frantz-Nodvik E_in/E_s={x:g}", eo / ein, want / ein, rel=1.5e-3, formula="G = E_s ln(1 + G0 (e^{x} - 1)) / E_in")
            eng.append(eo / ein)
            ana.append(want / ein)
            v = vals(r)
            stored = hnu * area * L * TOY_ND * (v["inversionFractionBefore"] - v["inversionFractionAfter"])
            fam.check(f"energy extracted = stored loss E_in/E_s={x:g}", eo - ein, stored, rel=3e-3, unit="J", formula="E_out - E_in = hν A L N_t (β_b - β_a)")
        overlay(f, xs, eng, ana, label="engine", xlabel="E_in / E_s", ylabel="energy gain E_out/E_in", marker="o", residual="rel", analytic_label="Frantz-Nodvik")
        v = vals(run1(b.gain("g", toy_medium(lam, 100e-9), L, inversion={"fraction": beta}, mode_radius_m=w, spm=False), b.pulse(lam, 10e-12, energy=1e-12)))
        fam.check("saturation fluence reported", v["saturationFluenceJPerM2"], hnu / TOY_SIGMA, rel=1e-3, unit="J/m²", formula="F_s = hν/σ_e (four-level)")
        fam.check("small-signal gain reported", v["smallSignalGain"], g0, rel=1e-3)
        r = run1(b.gain("g", toy_medium(lam, 100e-9), L, inversion={"fraction": beta}, mode_radius_m=w, spm=False, slices=48), b.pulse(lam, 10e-12, energy=float(50 * es)))
        stored = hnu * area * L * TOY_ND * beta
        fam.check("full extraction at 50 E_s", (element(r)["energyOutJ"] - element(r)["energyInJ"]) / stored, 1.0, abs=0.01,
                  formula="all the stored energy hν A L N_t β is extracted")

    lam_p, P, wp = 940e-9, 20.0, 0.5e-3
    sap = 1e-24
    med = toy_medium(1030e-9, 100e-9)
    med["absorption"] = {"kind": "gaussians", "bands": [{"wavelengthM": lam_p, "sigmaM2": sap, "fwhmM": 30e-9}]}
    med["dopingDensityM3"] = 1e25
    r = run1(b.gain("g", med, 1e-4, pump=b.pump("cw", lam_p, wp, power_w=P, profile="tophat"), mode_radius_m=0.2e-3, spm=False, slices=4, thermal_lens=False),
             b.pulse(1030e-9, 10e-12, energy=1e-15))
    phi = P / (math.pi * wp * wp) / (H * C / lam_p)
    sep = TOY_SIGMA * math.exp(-4 * math.log(2) * (lam_p - 1030e-9) ** 2 / (100e-9) ** 2)     # the toy emission band's tail at the pump
    beta = sap * phi * TOY_TAU / (1 + (sap + sep) * phi * TOY_TAU)
    fam.check("cw pumping: steady-state inversion (thin, four-level)", vals(r)["inversionFractionBefore"], beta, rel=0.01,
              formula="β = σ_a Φ τ / (1 + (σ_a + σ_e) Φ τ), Φ = P/(π w² hν_p), σ_e = the toy band's tail at 940 nm")


# ======================================================================================================= gases

def gas_run(species, pressure_bar, temperature_k, lam, tau, energy, L, area=2e-8, extra_material=None, element_kw=None, nonlinear=True, **physics):
    mat = {"gas": b.gas(species, pressure_bar, temperature_k)}
    mat.update(extra_material or {})
    el = b.bulk("cell", "m", L, beam=b.constant_beam(area_m2=area), nonlinear=nonlinear, **(element_kw or {}))
    return uf.run(b.system("c", [el], materials={"m": mat}), b.run_spec(b.pulse(lam, tau, energy=energy), physics=physics or None))


def group_index_minus_one(res, L):
    return element(res)["arrivalTimeS"] * C / L - 1.0


def rot_n2_adiabatic(B_cm, dalpha_A3, T, pressure_bar, spin=(2, 1), jmax=70):
    """Orientational n2 of a linear rigid rotor for pulses that are short against collisions but long against the rotation
    (the molecules follow the field adiabatically, populations fixed): n2 = N dalpha^2 <d<cos^2>/dF> / (4 eps0^2 c), with
    F = dalpha I / (2 eps0 c) the alignment energy of cos^2(theta) and d<cos^2>/dF = sum_a rho_a d<cos^2>_a/dF from exact
    diagonalisation of B J(J+1) - F cos^2(theta) in each (M, parity) block. Independent of the engine's linear-response sum."""
    hh, cc = H, C
    Bj = B_cm * 100 * hh * cc
    da = 4 * math.pi * EPS0 * dalpha_A3 * 1e-30
    f_small = 1e-4 * Bj
    num, den = 0.0, 0.0
    for M in range(0, jmax):
        for par in (0, 1):
            Js = np.array([J for J in range(M, jmax) if J % 2 == par], float)
            if len(Js) < 2:
                continue
            weight = spin[par] * (1 if M == 0 else 2)
            diag = (2 * Js * (Js + 1) - 2 * M * M - 1) / ((2 * Js - 1) * (2 * Js + 3))
            off = np.sqrt(((Js[:-1] + 1) ** 2 - M * M) * ((Js[:-1] + 2) ** 2 - M * M)) / ((2 * Js[:-1] + 3) * np.sqrt((2 * Js[:-1] + 1) * (2 * Js[:-1] + 5)))
            Cm = np.diag(diag) + np.diag(off, 1) + np.diag(off, -1)
            E = Bj * Js * (Js + 1)
            rho = weight * np.exp(-E / (KB * T))
            c_of = []
            for sgn in (+1, -1):
                ev, V = np.linalg.eigh(np.diag(E) - sgn * f_small * Cm)
                c_of.append(np.einsum("ij,ik,kj->j", V, Cm, V))
            dc = (c_of[0] - c_of[1]) / (2 * f_small)                # adiabatic states keep their order: level j stays level j
            # high levels are truncated by the basis; the thermal weight there is zero
            num += float((rho * dc).sum())
            den += float(rho.sum() * (2 * 1))  if False else 0.0
    # normalisation: sum over all (2J+1) states of the thermal weights
    Z = sum(spin[J % 2] * (2 * J + 1) * math.exp(-Bj * J * (J + 1) / (KB * T)) for J in range(jmax))
    d_cos2_dF = num / Z
    n_dens = pressure_bar * 1e5 / (KB * T)
    return n_dens * da * da * d_cos2_dF / (4 * EPS0 ** 2 * C)


def gases(fam: Family):
    from _analytic import ciddor_air
    # n(p, T): Air against Ciddor (group index from the arrival time of the pulse), and density scaling of Ar
    with fam.figure("gas-air-index", "Gas bulk, Air: group index (arrival time) against Ciddor 1996, 3 carriers and 4 pressures",
                    "n_g - 1 = c t_arrival / L - 1 = (n - λ dn/dλ) - 1, Ciddor dry air 20 °C, 450 ppm CO2") as f:
        for lam in CARRIERS_M:
            L = 10.0
            ps = np.array([0.5, 1.01325, 2.0, 4.0])
            eng, ana = [], []
            for p in ps:
                r = gas_run("Air", float(p), 293.15, lam, 100e-15, 1e-9, L, nonlinear=False)
                lc = element(r)["carrierWavelengthM"]
                h = lc * 1e-3
                n = lambda x: ciddor_air(x, 20.0, p * 1e5, 450.0)     # noqa: E731
                want = n(lc) - lc * (n(lc + h) - n(lc - h)) / (2 * h) - 1
                got = group_index_minus_one(r, L)
                fam.check(f"Air n_g-1 {label_wl(lam)} p={p:g} bar", got, float(want), rel=2e-4, formula="Ciddor (1996) n(λ, p, T), group index by differentiation")
                eng.append(got)
                ana.append(float(want))
            overlay(f, ps, np.array(eng) * 1e4, np.array(ana) * 1e4, label=label_wl(lam), xlabel="pressure (bar)", ylabel="(n_g - 1) × 10⁴", marker="o",
                    residual="rel", analytic_label="Ciddor" if lam == CARRIERS_M[0] else None)
    # Ar: refractivity proportional to the density p / T
    for lam in CARRIERS_M:
        L = 10.0
        base = group_index_minus_one(gas_run("Ar", 1.0, 293.15, lam, 100e-15, 1e-9, L, nonlinear=False), L)
        for p, T in ((2.0, 293.15), (0.25, 293.15), (1.0, 250.0), (3.0, 330.0)):
            got = group_index_minus_one(gas_run("Ar", p, T, lam, 100e-15, 1e-9, L, nonlinear=False), L)
            rr = (p / 1.0) * (293.15 / T)                           # density ratio
            # the Sellmeier susceptibility n² - 1 scales with the density, n - 1 = χ/2 - χ²/8: ratio = rr (1 - (n0 - 1)(rr - 1)/2)
            want = rr * (1 - base * (rr - 1) / 2)
            fam.check(f"Ar (n_g-1) ∝ p/T, p={p:g} bar T={T:g} K {label_wl(lam)}", got / base, want, rel=1e-5,
                      formula="n² - 1 ∝ density p/(kB T): (n-1) ratio = r (1 - (n0-1)(r-1)/2), r = density ratio")
    # GDD of Ar proportional to the density too
    r1 = gas_run("Ar", 1.0, 293.15, 800e-9, 30e-15, 1e-9, 5.0, nonlinear=False)
    r3 = gas_run("Ar", 3.0, 293.15, 800e-9, 30e-15, 1e-9, 5.0, nonlinear=False)
    fam.check("Ar GDD ∝ pressure (3 bar / 1 bar)", element(r3)["own"]["gddS2"] / element(r1)["own"]["gddS2"], 3.0, rel=1e-3,
              formula="GDD ∝ n - 1 ∝ density; 0.1 %: the (n - 1)² term of n = sqrt(1 + χ) is not linear in the density")

    # Kerr n2: B = k0 n2 I L with a given n2, and the density scaling of the built-in n2
    with fam.figure("gas-n2-density", "Gas bulk, Ar: Kerr phase B = k0 n2 I L against pressure (built-in n2 scaled with the density)",
                    "B ∝ n2 ∝ density ∝ p/T; absolute: n2(Ar, 1 bar, 273 K) = 1.0e-23 m²/W (Wahlstrand et al. 2012: 1.0e-19 cm²/W at 800 nm)") as f:
        ps = np.array([0.5, 1.0, 2.0, 4.0])
        lam, tau, area, L = 1030e-9, 1e-12, 1e-8, 1.0
        e0 = 1e-4
        I0 = e0 / tau * GAUSS_P0 / area
        eng, ana = [], []
        for p in ps:
            r = gas_run("Ar", float(p), 273.15, lam, tau, e0, L, area=area, selfSteepening=False)
            B = element(r)["bIntegralRad"]
            want = 2 * math.pi / lam * 1.0e-23 * p * I0 * L
            fam.check(f"Ar B-integral p={p:g} bar (n2 = 1.0e-23 p m²/W at 273 K)", B, want, rel=0.1, formula="B = k0 n2 I L; published n2 good to ~10 %")
            eng.append(B)
            ana.append(want)
        fam.check("Ar B ∝ p (4 bar / 0.5 bar)", eng[-1] / eng[0], 8.0, rel=5e-4,
                  formula="n2 ∝ density (5e-4: the index n - 1 ~ 1e-3 at 4 bar enters the Kerr coupling)")
        overlay(f, ps, eng, ana, label="Ar 273 K, 1030 nm", xlabel="pressure (bar)", ylabel="B (rad)", marker="o", residual="rel", analytic_label="k0 n2(1 bar) p I L")
    # given n2 -> exact B and exact SPM broadening
    lam, tau, area, L, n2 = 1030e-9, 1e-12, 1e-8, 1.0, 5e-23
    e0 = 1e-4
    I0 = e0 / tau * GAUSS_P0 / area
    r = gas_run("Ar", 1.0, 293.15, lam, tau, e0, L, area=area, extra_material={"n2": n2}, selfSteepening=False)
    B_want = 2 * math.pi / lam * n2 * I0 * L
    fam.check("gas with n2 given: B = k0 n2 I L", element(r)["bIntegralRad"], B_want, rel=2e-3, formula="B = k0 n2 I_peak L")
    ratio = rms_width(r["output_omega_rad_s"], r["output_spectrum"]) / rms_width(r["input_omega_rad_s"], r["input_spectrum"])
    fam.check("gas with n2 given: SPM broadening", ratio, math.sqrt(1 + 4 * B_want ** 2 / (3 * math.sqrt(3))), rel=3e-3,
              formula="σ/σ0 = sqrt(1 + 4B²/(3√3)) with B = k0 n2 I L")

    # rotational Raman (N2): adiabatic orientational n2; revival not directly observable here -> see C# GasTests
    with fam.figure("gas-n2-rotational", "Gas bulk, N2 1 bar 293 K: SPM broadening of a 50 ps pulse with the rotational response against n2_el + n2_rot",
                    "n2_rot from exact diagonalisation of the rigid rotor (adiabatic following; B = 1.98957 cm⁻¹, Δα = 0.70 Å³, 2:1 spin weights); σ/σ0 = sqrt(1 + 4B²/(3√3))") as f:
        lam, tau, area, L = 1030e-9, 50e-12, 1e-8, 1.0
        n2_rot = rot_n2_adiabatic(1.98957, 0.70, 293.15, 1.0)
        n2_el = 8.0e-24 * 273.15 / 293.15                                # the documented electronic value, scaled to 293 K
        Bs, eng, ana = [], [], []
        for e0 in (2e-4, 5e-4, 1e-3):
            I0 = e0 / tau * GAUSS_P0 / area
            r = gas_run("N2", 1.0, 293.15, lam, tau, e0, L, area=area, selfSteepening=False)
            Bt = 2 * math.pi / lam * (n2_el + n2_rot) * I0 * L
            ratio = rms_width(r["output_omega_rad_s"], r["output_spectrum"]) / rms_width(r["input_omega_rad_s"], r["input_spectrum"])
            want = math.sqrt(1 + 4 * Bt ** 2 / (3 * math.sqrt(3)))
            fam.check(f"N2 50 ps SPM broadening, B_total={Bt:.2f}", ratio, want, rel=0.03,
                      formula="n2 = n2_el (8.0e-24 at 273 K, documented) + n2_rot (adiabatic rotor); 3 % allowed for the finite pulse / 60 ps dephasing")
            Bs.append(Bt)
            eng.append(ratio)
            ana.append(want)
        fam.check("N2 reported B-integral = k0 (n2_el + n2_rot) I L", element(r)["bIntegralRad"], Bt, rel=0.02,
                  formula="the element's B includes the adiabatic orientational n2; independent rotor sum")
        overlay(f, Bs, eng, ana, label="N2, 50 ps", xlabel="B_total = k0 (n2_el + n2_rot) I L (rad)", ylabel="σ/σ0", marker="o", residual="rel", analytic_label="Gaussian SPM")
        # no rotation: only n2_el
        r = gas_run("N2", 1.0, 293.15, lam, tau, 1e-3, L, area=area, extra_material={"rotationalRaman": False}, selfSteepening=False)
        I0 = 1e-3 / tau * GAUSS_P0 / area
        fam.check("N2 with the rotational response off: B = k0 n2_el I L", element(r)["bIntegralRad"], 2 * math.pi / lam * n2_el * I0 * L, rel=0.01)
        # circular light sees 1/4 of the orientational response
        fam.check("N2 orientational n2 vs published (Nibbering 1997 / Wahlstrand 2012: 2-3 e-23 m²/W)", n2_rot, 2.7e-23, rel=0.35,
                  formula="order of magnitude only: published orientational n2 of N2 at 1 atm is (2-3.5)e-23 m²/W")


# ======================================================================================================= ionisation

def adk_rate(i_wm2, ip_ev, l=1, m=0, z=1):
    """Cycle-averaged ADK rate [1/s] (Ammosov, Delone & Krainov 1986; atomic units), averaged factor sqrt(3F/(pi kappa^3))."""
    from math import gamma as gam
    au_f = 5.14220674763e11
    ip = ip_ev / 27.211386245988
    F = np.sqrt(2 * np.asarray(i_wm2, float) / (EPS0 * C)) / au_f
    k = math.sqrt(2 * ip)
    ns = z / k
    ls = ns - 1
    c2 = 2 ** (2 * ns) / (ns * gam(ns + ls + 1) * gam(ns - ls))
    g_lm = (2 * l + 1) * math.factorial(l + abs(m)) / (2 ** abs(m) * math.factorial(abs(m)) * math.factorial(l - abs(m)))
    with np.errstate(divide="ignore", over="ignore", invalid="ignore"):
        w = c2 * g_lm * ip * (2 * k ** 3 / F) ** (2 * ns - abs(m) - 1) * np.exp(-2 * k ** 3 / (3 * F))
        w = np.where(F > 0, w * np.sqrt(3 * F / (math.pi * k ** 3)), 0.0)
    return w * 4.134137333518e16


def ion_cell(i0, lam, tau, model, p=1.0, L=1e-3, species="Ar", avalanche=False):
    area = 1e-7
    mat = {"gas": b.gas(species, p, 293.15), "n2": 0.0}
    el = b.bulk("cell", "m", L, beam=b.constant_beam(area_m2=area), ionization={"model": model, "avalanche": avalanche})
    r = uf.run(b.system("c", [el], materials={"m": mat}), b.run_spec(b.pulse(lam, tau, energy=i0 * area * tau / GAUSS_P0), physics={"selfSteepening": False}))
    return r, area


def ionisation(fam: Family):
    rho_n = 1e5 / (KB * 293.15)
    with fam.figure("ion-adk-density", "Ionisation, Ar 1 bar, 2 µm 30 fs: electron density against the ADK rate (tunnel regime)",
                    "ρ = ρ_n (1 - exp(-∫ W_ADK(I(t)) dt)), cycle-averaged ADK (Ip = 15.76 eV, l = 1, m = 0)") as f:
        i0s = np.array([0.4e18, 0.6e18, 0.8e18, 1.0e18, 1.3e18, 1.5e18])
        eng, ana = [], []
        for i0 in i0s:
            r, area = ion_cell(float(i0), 2e-6, 30e-15, "adk")
            P = np.asarray(r["input_power_w"])
            dt = float(r["input_time_s"][1] - r["input_time_s"][0])
            want = rho_n * (1 - math.exp(-adk_rate(P / area, 15.76).sum() * dt))
            got = element(r)["values"]["peakElectronDensityM3"]
            fam.check(f"ADK electron density I={i0 / 1e4:.1e} W/cm²", got, want, rel=0.02, unit="1/m³", formula="ρ_n (1 - exp(-∫W dt))")
            eng.append(got)
            ana.append(want)
        overlay(f, i0s / 1e4, eng, ana, label="Ar, 2 µm, 30 fs", xlabel="peak intensity (W/cm²)", ylabel="peak electron density (1/m³)", marker="o", residual="rel",
                analytic_label="ADK")
    # PPT multiphoton order at 800 nm
    rs = []
    for i0 in (1.0e15, 1.2e15):
        r, _ = ion_cell(i0, 800e-9, 50e-15, "ppt")
        rs.append(element(r)["values"]["peakElectronDensityM3"])
    k_eff = math.log(rs[1] / rs[0]) / math.log(1.2)
    fam.check("PPT multiphoton order, Ar at 800 nm (I ~ 1e11 W/cm², γ ≈ 30)", k_eff, math.ceil(15.76 / (H * C / 800e-9 / 1.602176634e-19)), rel=0.01,
              formula="W ∝ I^K with K = ceil(Ip/ħω) = 11 (ρ ∝ ∫ W dt ∝ I0^K)")
    # PPT tunnel limit: agrees with ADK at gamma << 1 (10 µm)
    r1, area = ion_cell(2.5e17, 10e-6, 100e-15, "ppt")
    P = np.asarray(r1["input_power_w"])
    dt = float(r1["input_time_s"][1] - r1["input_time_s"][0])
    want = rho_n * (1 - math.exp(-adk_rate(P / area, 15.76).sum() * dt))
    fam.check("PPT → ADK at 10 µm (γ ≈ 0.1)", element(r1)["values"]["peakElectronDensityM3"], want, rel=0.25,
              formula="PPT reduces to the cycle-averaged ADK rate for γ << 1 (25 %: Coulomb-correction factors differ)")

    # blue shift
    with fam.figure("ion-blueshift", "Ionisation, Ar 1 bar 1 mm: mean spectral shift against the Drude plasma phase",
                    "Δω = (k0 L / (2 n0 ρ_c)) ⟨dρ/dt⟩_P with ρ_c = ε0 m_e ω0²/e², ρ(t) from the ADK rate; positive = blue") as f:
        ME, QE = 9.1093837015e-31, 1.602176634e-19
        eng, ana, xs = [], [], []
        for lam, i0 in ((2e-6, 0.5e18), (2e-6, 0.7e18), (1030e-9, 0.9e18)):
            tau = 30e-15
            r, area = ion_cell(i0, lam, tau, "adk")
            P = np.asarray(r["input_power_w"])
            t = np.asarray(r["input_time_s"])
            dt = float(t[1] - t[0])
            rho = rho_n * (1 - np.exp(-np.cumsum(adk_rate(P / area, 15.76)) * dt))
            lc = element(r)["carrierWavelengthM"]
            w0 = 2 * math.pi * C / lc
            rho_c = EPS0 * ME * w0 ** 2 / QE ** 2
            L = 1e-3
            d = np.gradient(rho, dt)
            want = (w0 / C) * L / (2 * rho_c) * float((P * d).sum() / P.sum())
            got = centroid(r["output_omega_rad_s"], r["output_spectrum"]) - centroid(r["input_omega_rad_s"], r["input_spectrum"])
            fam.check(f"plasma blue shift {label_wl(lam)} I={i0 / 1e4:.1e} W/cm²", got, want, rel=0.05, unit="rad/s",
                      formula="Δω = (k0 L / 2ρ_c) ⟨dρ/dt⟩ (Drude, τ_c ω0 >> 1); 5 %; thin enough that the ionisation loss (<1e-4) does not lower the intensity along the cell")
            eng.append(got)
            ana.append(want)
            xs.append(f"{label_wl(lam)}\n{i0 / 1e4:.1e}")
        if f.axes is not None:
            top, bot = f.axes
            x = np.arange(len(xs))
            top.plot(x, eng, "o-", label="engine")
            top.plot(x, ana, "--k", label="Drude + ADK")
            top.set_ylabel("mean shift Δω (rad/s)")
            top.legend(frameon=False, fontsize=8)
            top.grid(alpha=0.3)
            bot.plot(x, (np.array(eng) - np.array(ana)) / np.array(ana), "o-")
            bot.axhline(0, color="k", lw=0.6)
            bot.set_ylabel("relative residual")
            bot.set_xticks(x)
            bot.set_xticklabels(xs, fontsize=7)


# ======================================================================================================= bulk Kerr

def kerr_cell(lam, tau, b_target, L=1e-3, area=1e-8, n2=3e-20, ss=False, index=1.45, shape="gaussian"):
    """A dispersion-free Kerr slab: constant index, given n2; the peak intensity is set for B = k0 n2 I L = b_target."""
    k0 = 2 * math.pi / lam
    i0 = b_target / (k0 * n2 * L)
    p0 = i0 * area
    mat = b.material(index=index, n2=n2)
    el = b.bulk("slab", "m", L, beam=b.constant_beam(area_m2=area))
    r = uf.run(b.system("k", [el], materials={"m": mat}),
               b.run_spec(b.pulse(lam, tau, energy=energy_gauss_peak(p0, tau)), physics={"selfSteepening": ss, "raman": False}))
    return r, i0


def bulk_kerr(fam: Family):
    with fam.figure("kerr-broadening", "Bulk Kerr slab (constant index): SPM broadening against B = k0 n2 I L, three carriers",
                    "σ/σ0 = sqrt(1 + 4 B²/(3√3)) for a Gaussian; the reported B equals k0 n2 I_peak L") as f:
        for lam in CARRIERS_M:
            for tau in DURATIONS_S:
                if lam == 2e-6 and tau == 10e-15:
                    continue                                              # see media.py: fibre_spm (known auto-grid problem)
                Bs = [0.5, 1.5, 3.0]
                eng, ana = [], []
                for B_ in Bs:
                    r, i0 = kerr_cell(lam, tau, B_)
                    ratio = rms_width(r["output_omega_rad_s"], r["output_spectrum"]) / rms_width(r["input_omega_rad_s"], r["input_spectrum"])
                    want = math.sqrt(1 + 4 * B_ ** 2 / (3 * math.sqrt(3)))
                    tag = f"{label_wl(lam)} {label_dur(tau)} B={B_:g}"
                    fam.check(f"Kerr SPM broadening {tag}", ratio, want, rel=4e-3, formula="sqrt(1 + 4B²/(3√3))")
                    fam.check(f"Kerr reported B {tag}", element(r)["bIntegralRad"], B_, rel=2e-3, unit="rad", formula="B = k0 n2 I_peak L")
                    eng.append(ratio)
                    ana.append(want)
                if tau == 1e-12 or lam == 1030e-9:
                    overlay(f, Bs, eng, ana, label=f"{label_wl(lam)} {label_dur(tau)}", xlabel="B = k0 n2 I L (rad)", ylabel="σ/σ0", marker="o", residual="rel",
                            analytic_label="sqrt(1 + 4B²/(3√3))" if (lam, tau) == (CARRIERS_M[0], 1e-12) else None)

    with fam.figure("kerr-selfsteepening", "Bulk Kerr slab with self-steepening: delay of the pulse centroid against 3B/(2√2 ω0)",
                    "∂P/∂z = -(3γ/ω0) P ∂P/∂t  ⇒  d⟨t⟩/dz = (3γ/2ω0) ∫P²/∫P = 3γP0/(2√2 ω0) for a Gaussian (γ P0 L = B)") as f:
        for lam in CARRIERS_M:
            for tau in (100e-15, 10e-15):
                if lam == 2e-6 and tau == 10e-15:
                    continue
                Bs = np.array([0.25, 0.5, 1.0])
                eng, ana = [], []
                for B_ in Bs:
                    r, _ = kerr_cell(lam, tau, float(B_), ss=True)
                    t = np.asarray(r["output_time_s"])
                    shift = centroid(t, r["output_power_w"]) - centroid(np.asarray(r["input_time_s"]), r["input_power_w"])
                    lc = element(r)["carrierWavelengthM"]
                    w0 = 2 * math.pi * C / lc
                    want = 3 * B_ / (2 * math.sqrt(2) * w0)
                    fam.check(f"self-steepening delay {label_wl(lam)} {label_dur(tau)} B={B_:g}", shift, want, rel=0.03, unit="s",
                              formula="⟨t⟩ shift = 3B/(2√2 ω0) (first order in the shock; 3 % covers the shock distortion at B = 1)")
                    eng.append(shift * 1e15)
                    ana.append(want * 1e15)
                if tau == 100e-15:
                    overlay(f, Bs, eng, ana, label=f"{label_wl(lam)}, 100 fs", xlabel="B (rad)", ylabel="centroid delay (fs)", marker="o", residual="rel",
                            analytic_label="3B/(2√2 ω0)" if lam == CARRIERS_M[0] else None)
    # without the term nothing moves
    r, _ = kerr_cell(1030e-9, 100e-15, 1.0, ss=False)
    shift = centroid(np.asarray(r["output_time_s"]), r["output_power_w"]) - centroid(np.asarray(r["input_time_s"]), r["input_power_w"])
    fam.check("no self-steepening switched off: centroid stays", shift, 0.0, abs=2e-18, unit="s")
    # same in the fibre element
    for lam in CARRIERS_M:
        if lam == 2e-6:
            continue
        p0, tau, B_ = 1e3, 50e-15, 1.0
        r = run1(b.fiber("f", 1.0, betas=[0.0], gamma=B_ / p0, raman="none"), b.pulse(lam, tau, energy=energy_gauss_peak(p0, tau)), selfSteepening=True)
        shift = centroid(np.asarray(r["output_time_s"]), r["output_power_w"]) - centroid(np.asarray(r["input_time_s"]), r["input_power_w"])
        w0 = 2 * math.pi * C / element(r)["carrierWavelengthM"]
        fam.check(f"fibre self-steepening delay {label_wl(lam)} 50 fs B=1", shift, 3 * B_ / (2 * math.sqrt(2) * w0), rel=0.03, unit="s", formula="3B/(2√2 ω0)")


FUNCS = ["fiber_spm", "fiber_dispersion", "fiber_solitons", "fiber_loss_gain", "fiber_dispersive_wave", "gain", "gases", "ionisation", "bulk_kerr"]


def main() -> int:
    fam = Family("media", "Waveguides, gain, gases, ionisation and bulk Kerr / Raman")
    for name in FUNCS:
        fam.guard(name, lambda name=name: globals()[name](fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
