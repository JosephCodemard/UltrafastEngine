"""More instruments alone, each against an analytic or published answer (instruments.py has the autocorrelator, spectrometer
resolution, FROG marginals and the third-order cross-correlator).

Covered: energyMeter (energy, average power E f_rep, wavelength band = Gaussian-spectrum fraction, Malus polariser, detector
calibration); scope (Gaussian impulse response: FWHM adds in quadrature, 0.35 / bandwidth rise time, energy conserved);
polarimeter (Stokes of x, y, +-45, rcp, lcp and an ellipse; s3 = +1 for rcp); interferometric autocorrelator (8:1 and the whole
fringe trace); dscan (SHG signal and FWHM against GDD for a chirped Gaussian, the optimum, and the whole material scan against
the direct integral with the Sellmeier phase of fused silica); wigner (marginals, chirp ridge slope gdd / (T0^4 + gdd^2));
camera (near-field radius, far-field radius and divergence, Strehl of a curved beam, energy in the 1/e^2 ellipse); wavefrontSensor
(radius of curvature of a Gaussian beam, a Zernike coefficient put in by a wavefront element); beamProfile (peak fluence, the
profile, radial and cartesian); delayScan (low-gain OPA, FWHM = sqrt(tau_pump^2 + tau_signal^2)); FROG with retrieval
(pcgpa recovers the FWHM and the GDD of a chirped pulse); spectrometer filter / detector models (Gaussian filter, photon
counting, response table, calibration, bit depth).

Sign and unit conventions: docs/concepts.md (E = Re{A e^{-i w0 t}}, positive GDD = up-chirp), docs/reference/instruments.md.
References: _analytic_instruments.py (independent quadratures), _analytic.py (Sellmeier x Ciddor).

Run: python tests/components/instruments_more.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
"""
from __future__ import annotations

import math
import sys

import _analytic as AN
import _analytic_instruments as AI
from _common import C, CARRIERS_M, DURATIONS_S, Family, b, centroid, fwhm, label_dur, label_wl, np, overlay, rms_width, uf

LN2 = math.log(2.0)
FS = 1e-15


def iv(res, name):
    d = res.instrument(name)
    return d.get("values", d)


def run(pulse, instruments, system=None, **spec):
    return uf.run(system or b.system("free", [b.probe("p")]), b.run_spec(pulse, instruments=instruments, **spec))


def spectrum_arrays(res):
    return np.asarray(res["input_omega_rad_s"], float), np.asarray(res["input_spectrum"], float)


# ----------------------------------------------------------------------------------------------------------- energyMeter
def energy_meter(fam: Family):
    with fam.figure("energymeter-band", "energyMeter wavelength band on a 100 fs, 1030 nm pulse: transmitted fraction against the band half-width",
                    "fraction = Phi((w_hi - w0)/sigma) - Phi((w_lo - w0)/sigma),  sigma = (4 ln2 / tau) / (2 sqrt(2 ln2))") as f:
        for lam in CARRIERS_M:
            for tau in DURATIONS_S:
                r = run(b.pulse(lam, tau, energy=1e-6, rep_rate_hz=80e6), [b.instrument("energyMeter", name="em")])
                v = iv(r, "em")
                fam.check(f"energy {label_wl(lam)} {label_dur(tau)}", v["energyJ"], 1e-6, rel=1e-9, unit="J")
                fam.check(f"average power {label_wl(lam)} {label_dur(tau)}", v["averagePowerW"], 1e-6 * 80e6, rel=1e-9, unit="W", formula="E f_rep")
        lam, tau = 1030e-9, 100e-15
        w0, sig = 2 * math.pi * AN.C / lam, AI.gauss_sigma_omega(tau)
        half = np.array([2, 5, 10, 15, 20, 30, 50]) * 1e-9
        eng, ana = [], []
        for h in half:
            r = run(b.pulse(lam, tau, energy=1e-6), [b.instrument("energyMeter", name="em", rangeM=[lam - h, lam + h])])
            wl, wh = 2 * math.pi * AN.C / (lam + h), 2 * math.pi * AN.C / (lam - h)
            # the band edges are reference sharp; the engine's pulse carrier sits on the frequency grid (snapped), so use the actual spectrum centre
            wgrid, sgrid = spectrum_arrays(r)
            wc = float(wgrid[np.argmax(sgrid)])
            a = float(AI.norm_cdf((wh - wc) / sig) - AI.norm_cdf((wl - wc) / sig))
            dw = float(wgrid[1] - wgrid[0])
            bound = 2 * 0.5 * dw / (math.sqrt(2 * math.pi) * sig)            # a whole bin in or out at each of the two edges
            eng.append(iv(r, "em")["energyJ"] / 1e-6)
            ana.append(a)
            fam.check(f"band +-{h * 1e9:g} nm fraction (quantisation bound)", eng[-1], a, abs=bound,
                      formula="Gaussian spectrum between the band edges; bound = two half-bins x peak density (the old whole-bin snapping error; edge bins are now weighted by their fraction)")
            fam.check(f"band +-{h * 1e9:g} nm fraction (sharp edges)", eng[-1], a, abs=1e-3,
                      formula="Gaussian spectrum between the band edges (edge bins weighted by the fraction inside the band)")
        overlay(f, half * 1e9, eng, ana, label="engine", xlabel="band half-width (nm)", ylabel="transmitted fraction", marker="o", analytic_label="Gaussian CDF")
    with fam.figure("energymeter-malus", "energyMeter behind a polariser on x-polarised light: Malus' law",
                    "E(theta) = E cos^2(theta)") as f:
        th = np.arange(0, 181, 15)
        eng = []
        for t in th:
            r = run(b.pulse(1030e-9, 100e-15, energy=1e-6), [b.instrument("energyMeter", name="em", polarizerDeg=float(t))])
            eng.append(iv(r, "em")["energyJ"] / 1e-6)
        ana = np.cos(np.radians(th)) ** 2
        for t, e, a in zip(th, eng, ana):
            fam.check(f"Malus theta={t} deg", e, a, abs=1e-9)
        overlay(f, th, eng, ana, label="engine", xlabel="polariser angle (deg)", ylabel="E / E0", marker="o", analytic_label="cos^2")
    r = run(b.pulse(1030e-9, 100e-15, energy=1e-6), [b.instrument("energyMeter", name="em", detector=b.detector(calibration=1.03))])
    fam.check("energyMeter detector calibration 1.03", iv(r, "em")["energyJ"], 1.03e-6, rel=1e-9, unit="J", formula="reading = calibration x true value")


# ----------------------------------------------------------------------------------------------------------- scope
def scope(fam: Family):
    k_rise = 2 * math.sqrt(2 * LN2) / 2.563     # engine: Gaussian impulse response, 10-90 % rise = 2.563 sigma -> FWHM = 0.9188 rise
    with fam.figure("scope", "Scope on a 1 ps Gaussian pulse: detected FWHM against the 10-90 % rise time",
                    "impulse response Gaussian of FWHM 0.9188 t_rise (10-90 % rise = 2.563 sigma);  FWHM_out = sqrt(tau^2 + FWHM_h^2);  t_rise = 0.35 / B") as f:
        tau = 1e-12
        rises = np.array([0, 0.5, 1, 2, 3, 5]) * 1e-12
        eng, ana = [], []
        for tr in rises:
            r = run(b.pulse(1030e-9, tau, energy=1e-6), [b.instrument("scope", name="sc", riseTimeS=float(tr), points=2048)])
            v = iv(r, "sc")
            want = math.sqrt(tau ** 2 + (k_rise * tr) ** 2)
            fam.check(f"scope FWHM, rise {tr * 1e12:g} ps", v["fwhmS"], want, rel=3e-3, unit="s", formula="sqrt(tau^2 + (0.9188 t_rise)^2)")
            t = np.asarray(r["sc_time_s"]); y = np.asarray(r["sc_signal_w"])
            fam.check(f"scope energy conserved, rise {tr * 1e12:g} ps", float(np.trapezoid(y, t)), 1e-6, rel=3e-3, unit="J", formula="integral of the detected power = E")
            eng.append(v["fwhmS"] * 1e12)
            ana.append(want * 1e12)
        overlay(f, rises * 1e12, eng, ana, label="engine", xlabel="rise time (ps)", ylabel="detected FWHM (ps)", marker="o", analytic_label="quadrature")
    for B in (0.35e9, 1e9, 5e9):
        r = run(b.pulse(1030e-9, 1e-12, energy=1e-6), [b.instrument("scope", name="sc", bandwidthHz=B, points=2048)])
        fam.check(f"scope rise time from bandwidth {B / 1e9:g} GHz", iv(r, "sc")["riseTimeS"], 0.35 / B, rel=1e-9, unit="s", formula="0.35 / B")
    # peak of a Gaussian pulse convolved with a Gaussian response: P0 tau / sqrt(tau^2 + h^2)
    tau, tr = 1e-12, 2e-12
    r = run(b.pulse(1030e-9, tau, energy=1e-6), [b.instrument("scope", name="sc", riseTimeS=tr, points=2048)])
    p0 = 1e-6 * math.sqrt(4 * LN2 / math.pi) / tau
    fam.check("scope peak power with response", iv(r, "sc")["peakW"], p0 * tau / math.sqrt(tau ** 2 + (k_rise * tr) ** 2), rel=3e-3, unit="W",
              formula="P0 tau / sqrt(tau^2 + FWHM_h^2)")


# ----------------------------------------------------------------------------------------------------------- polarimeter
def polarimeter(fam: Family):
    cases = [("x", "x", (1, 0, 0), 0, 0), ("y", "y", (-1, 0, 0), 90, 0), ("+45", "45", (0, 1, 0), 45, 0), ("-45", "-45", (0, -1, 0), -45, 0),
             ("rcp", "rcp", (0, 0, 1), None, 45), ("lcp", "lcp", (0, 0, -1), None, -45)]
    with fam.figure("polarimeter", "Polarimeter on pulses of known polarisation: Stokes parameters (s1, s2, s3) at the spectral centre",
                    "s1 = cos2chi cos2psi, s2 = cos2chi sin2psi, s3 = sin2chi;  rcp: s3 = +1 (docs/reference/instruments.md)") as f:
        eng, ana = [], []
        ell = [("30 deg linear", {"angleDeg": 30, "ellipticity": 0.0}, 30.0, 0.0), ("ellipse 20 deg, b/a 0.3", {"angleDeg": 20, "ellipticity": 0.3}, 20.0, math.degrees(math.atan(0.3)))]
        allc = [(n, p, s, az, el) for n, p, s, az, el in cases]
        for name, pol, s, az, el in allc:
            r = run(b.pulse(1030e-9, 100e-15, energy=1e-9, polarization=pol), [b.instrument("polarimeter", name="pm")])
            v = iv(r, "pm")
            i = len(v["s1"]) // 2
            for k, key in enumerate(("s1", "s2", "s3")):
                fam.check(f"{name} {key}", float(v[key][i]), s[k], abs=1e-9)
                eng.append(float(v[key][i])); ana.append(s[k])
            fam.check(f"{name} degree of polarisation", v["degreeOfPolarization"], 1.0, abs=1e-9)
            fam.check(f"{name} ellipticity angle", v["ellipticityAngleDeg"], el, abs=1e-6, unit="deg")
            if az is not None:
                fam.check(f"{name} azimuth", v["azimuthDeg"], az, abs=1e-6, unit="deg")
        for name, pol, psi, chi in ell:
            r = run(b.pulse(1030e-9, 100e-15, energy=1e-9, polarization=pol), [b.instrument("polarimeter", name="pm")])
            v = iv(r, "pm")
            i = len(v["s1"]) // 2
            ps, ch = math.radians(psi), math.radians(chi)
            want = (math.cos(2 * ch) * math.cos(2 * ps), math.cos(2 * ch) * math.sin(2 * ps), math.sin(2 * ch))
            for k, key in enumerate(("s1", "s2", "s3")):
                fam.check(f"{name} {key}", float(v[key][i]), want[k], abs=1e-9)
                eng.append(float(v[key][i])); ana.append(want[k])
            fam.check(f"{name} ellipticity angle", v["ellipticityAngleDeg"], chi, abs=1e-6, unit="deg")
        overlay(f, np.arange(len(eng)), eng, ana, label="s1, s2, s3 of 8 states", xlabel="state x 3 + component", ylabel="Stokes parameter",
                marker="o", style="", analytic_label="Jones calculus")


# ----------------------------------------------------------------------------------------------------------- interferometric AC
def interferometric_ac(fam: Family):
    with fam.figure("iac", "Interferometric autocorrelator, 1030 nm, 30 fs Gaussian: the fringe trace and the 8:1 peak-to-background",
                    "S(d) = int |a(t) + a(t-d) e^{i w0 d}|^4 dt / (2 int a^4 dt);  S(0) = 16/2 = 8") as f:
        for lam in CARRIERS_M:
            for tau in (100 * FS, 30 * FS):
                r = run(b.pulse(lam, tau, energy=1e-9), [b.instrument("autocorrelator", type="interferometric", name="iac")])
                v = iv(r, "iac")
                d_ = np.asarray(r["iac_delay_s"])
                wg, sg = spectrum_arrays(r)
                wc_ = centroid(wg, sg)          # the carrier sits on the frequency grid (snapped): use the actual spectral centre
                full = AI.iac_trace(d_, tau, wc_)
                ratio_samples = float(full.max() / full[0])   # the instrument's own definition: highest delay sample over the window edge
                fam.check(f"IAC peak-to-background vs the analytic trace at the engine's delay samples {label_wl(lam)} {label_dur(tau)}", v["peakToBackground"],
                          ratio_samples, rel=2e-3, formula="max_d S(d) / S(window edge), S analytic, d = the instrument's delay samples")
                fam.check(f"IAC peak-to-background {label_wl(lam)} {label_dur(tau)}", v["peakToBackground"], 8.0, rel=2e-3, formula="16 / 2")
        lam, tau = 1030e-9, 30 * FS
        r = run(b.pulse(lam, tau, energy=1e-9), [b.instrument("autocorrelator", type="interferometric", name="iac")])
        d, s = np.asarray(r["iac_delay_s"]), np.asarray(r["iac_signal"])
        m = np.abs(d) < 60 * FS
        wg, sg = spectrum_arrays(r)
        w0_ = centroid(wg, sg)
        ref = AI.iac_trace(d[m], tau, w0_) / AI.iac_trace([d[0]], tau, w0_)[0]
        fam.check("IAC trace max |engine - analytic| (|delay| < 60 fs, engine's normalisation)", float(np.max(np.abs(s[m] - ref))), 0.0, abs=0.04,
                  formula="fringe-resolved trace, divided by its value at the window edge; 0.04 of 8 = a fringe phase error of 1.3e-3 rad (carrier from the spectral centroid)")
        fam.check("IAC minimum of the trace", float(np.min(np.abs(s[np.abs(d) < 3 * FS]))), 0.0, abs=0.05, formula="full destructive interference near d = 0")
        o = slice(None, None, 4)
        overlay(f, d[m][o] * 1e15, s[m][o], ref[o], label="30 fs, 1030 nm", xlabel="delay (fs)", ylabel="IAC signal (background = 1)", analytic_label="direct integral")


# ----------------------------------------------------------------------------------------------------------- dscan
def dscan(fam: Family):
    with fam.figure("dscan-gdd", "d-scan against added GDD, 1030 nm, pulse 100 fs with GDD -4000 fs2: SHG signal and pulse FWHM",
                    "FWHM(g) = tau sqrt(1 + (4 ln2 g_tot / tau^2)^2);  SHG(g) / SHG_max = 1 / sqrt(1 + (4 ln2 g_tot / tau^2)^2),  g_tot = g_pulse + g_scan") as f:
        for lam, tau in ((1030e-9, 100 * FS), (515e-9, 30 * FS), (2e-6, 100 * FS)):
            g0 = -0.4 * tau ** 2                                  # 4 ln2 g0 / tau^2 = -1.11: a pulse 1.5 x longer than its transform limit
            scan = np.linspace(-1.0, 3.0, 41) * abs(g0) / 1.0
            r = run(b.pulse(lam, tau, energy=1e-9, gdd_s2=g0), [b.instrument("dscan", name="ds", gddRangeS2=[float(scan[0]), float(scan[-1])], steps=len(scan))])
            v = iv(r, "ds")
            g = np.asarray(r["ds_gdd_s2"]); shg = np.asarray(r["ds_shg_total"]); fw = np.asarray(r["ds_fwhm_s"])
            x = 4 * LN2 * (g0 + g) / tau ** 2
            ana_shg = 1 / np.sqrt(1 + x ** 2)
            ana_fw = tau * np.sqrt(1 + x ** 2)
            fam.check(f"dscan SHG vs GDD {label_wl(lam)} max|dS|", float(np.max(np.abs(shg / shg.max() - ana_shg))), 0.0, abs=3e-3,
                      formula="1/sqrt(1 + x^2), x = 4 ln2 g_tot/tau^2")
            fam.check(f"dscan FWHM vs GDD {label_wl(lam)} max rel", float(np.max(np.abs(fw / ana_fw - 1))), 0.0, abs=5e-3, formula="tau sqrt(1 + x^2)")
            fam.check(f"dscan optimum GDD {label_wl(lam)}", v["optimalGddS2"], -g0, abs=0.01 * abs(g0), unit="s2",
                      formula="the scan cancels the pulse GDD (1 % of it; the scan step is 10 %, the optimum is interpolated)")
            if lam == 1030e-9:
                overlay(f, g * 1e30, shg / shg.max(), ana_shg, label=f"SHG {label_wl(lam)}", xlabel="added GDD (fs^2)", ylabel="SHG signal / max",
                        analytic_label="1/sqrt(1+x^2)")
    with fam.figure("dscan-glass", "d-scan with fused silica insertion, 1030 nm, 30 fs pulse with GDD -400 fs2: SHG signal against thickness",
                    "SHG(z) = int |A(t)|^4 dt of a Gaussian spectrum with phase gdd w^2/2 + w (n(w) - 1) z / c (Sellmeier x Ciddor), normalised to its maximum") as f:
        lam, tau, g0 = 1030e-9, 30 * FS, -400e-30
        z = np.linspace(0, 30e-3, 31)
        r = run(b.pulse(lam, tau, energy=1e-9, gdd_s2=g0), [b.instrument("dscan", name="ds", material="FusedSilica", insertionM=[0.0, 30e-3], steps=31)])
        zi = np.asarray(r["ds_insertion_m"]); shg = np.asarray(r["ds_shg_total"])
        w0 = 2 * math.pi * AN.C / lam
        sig = AI.gauss_sigma_omega(tau)
        w = w0 + np.linspace(-6, 6, 4001) * sig
        dw = w - w0
        amp = np.exp(-dw ** 2 / (4 * sig ** 2))
        nfun = AN.GLASSES["FusedSilica"]
        ref = []
        for zz in zi:
            ph = g0 * dw ** 2 / 2 + (w * (nfun(2 * math.pi * AN.C / w) - 1.0) * zz / AN.C)
            ph = ph - np.polyval(np.polyfit(dw, ph, 1), dw) + 0      # the group delay shifts the pulse in time: remove the linear term
            ref.append(AI.shg_total_of_phase(w, amp, ph))
        ref = np.array(ref)
        fam.check("dscan glass trace max|dS|", float(np.max(np.abs(shg / shg.max() - ref / ref.max()))), 0.0, abs=0.01,
                  formula="direct integral with the Sellmeier phase (1 % of the maximum)")
        gdd_m = AN.phase_derivatives(nfun, 1e-3, w0)[1]
        zopt = zi[int(np.argmax(shg))]
        fam.check("dscan glass optimum thickness (grid point)", zopt, zi[int(np.argmax(ref))], abs=1.01e-3, unit="m", formula="same maximum as the direct integral (one step 1 mm)")
        fam.check("dscan glass optimum vs -gdd/GDD_glass", v_opt(zi, shg), -g0 / gdd_m * 1e-3, rel=0.03, unit="m", formula="z = -gdd_pulse / (GDD per m of the glass), TOD shifts it")
        overlay(f, zi * 1e3, shg / shg.max(), ref / ref.max(), label="engine", xlabel="fused silica insertion (mm)", ylabel="SHG signal / max", marker="o",
                analytic_label="direct integral")


def v_opt(x, y):
    """Maximum of a sampled curve by a parabola through the three highest neighbouring points."""
    i = int(np.argmax(y))
    i = min(max(i, 1), len(y) - 2)
    c = np.polyfit(x[i - 1:i + 2], y[i - 1:i + 2], 2)
    return float(-c[1] / (2 * c[0]))


# ----------------------------------------------------------------------------------------------------------- wigner
def wigner(fam: Family):
    lam, tau, gdd = 1030e-9, 100 * FS, 4000e-30
    with fam.figure("wigner", "Wigner distribution of a chirped Gaussian (100 fs, GDD +4000 fs2, 1030 nm): ridge omega(t) and marginals",
                    "omega_inst(t) = w0 + gdd t / (T0^4 + gdd^2), T0 = tau / (2 sqrt(ln2));  marginals: |A(t)|^2 (FWHM tau sqrt(1+x^2)) and the spectrum (FWHM 4 ln2 / tau)") as f:
        r = run(b.pulse(lam, tau, energy=1e-9, gdd_s2=gdd), [b.instrument("wigner", name="wg", points=128)])
        t = np.asarray(r["wg_time_s"]); w = np.asarray(r["wg_omega_rad_s"]); W = np.asarray(r["wg_distribution"])
        # the distribution is normalised to max|W|; its time marginal is |A(t)|^2 and its frequency marginal the spectrum
        pos = np.clip(W, 0, None)
        Pt, Pw = W.sum(1), W.sum(0)
        want_fw = AI.chirped_gaussian_fwhm(tau, gdd)
        fam.check("wigner time marginal FWHM", fwhm(t, Pt), want_fw, rel=0.01, unit="s", formula="tau sqrt(1 + (4 ln2 gdd/tau^2)^2)")
        fam.check("wigner frequency marginal FWHM", fwhm(w, Pw), 4 * LN2 / tau, rel=0.01, unit="rad/s", formula="4 ln2 / tau (a phase does not change the spectrum)")
        mask = Pt > 0.1 * Pt.max()
        wc = np.array([np.sum(W[i] * w) / np.sum(W[i]) for i in range(len(t))])
        sl = np.polyfit(t[mask], wc[mask], 1)[0]
        want = AI.wigner_ridge_slope(tau, gdd)
        fam.check("wigner ridge slope d omega_inst / dt", sl, want, rel=0.01, unit="rad/s^2", formula="gdd / (T0^4 + gdd^2); positive GDD = up-chirp")
        fam.check("wigner ridge slope sign (up-chirp, positive)", math.copysign(1.0, sl), 1.0, abs=0)
        w0 = 2 * math.pi * AN.C / lam
        fam.check("wigner ridge crosses the carrier at t = 0", float(np.interp(0.0, t[mask], wc[mask]) - np.mean(w)) , 0.0,
                  abs=0.03 * 4 * LN2 / tau, unit="rad/s", formula="mean of the omega axis (its centre) within 3 % of the bandwidth")
        overlay(f, t[mask] * 1e15, wc[mask] - wc[mask].mean(), want * (t[mask] - t[mask].mean()), label="centroid of W(t, w)", xlabel="t (fs)",
                ylabel="omega - mean (rad/s)", analytic_label="ridge")
        # a transform-limited pulse has no ridge slope
        r0 = run(b.pulse(lam, tau, energy=1e-9), [b.instrument("wigner", name="wg", points=128)])
        W0 = np.asarray(r0["wg_distribution"]); t0 = np.asarray(r0["wg_time_s"]); w_0 = np.asarray(r0["wg_omega_rad_s"])
        m0 = W0.sum(1) > 0.1 * W0.sum(1).max()
        wc0 = np.array([np.sum(W0[i] * w_0) / np.sum(W0[i]) for i in range(len(t0))])
        fam.check("wigner ridge slope of a transform-limited pulse", float(np.polyfit(t0[m0], wc0[m0], 1)[0]), 0.0, abs=0.01 * abs(want), unit="rad/s^2")
        fam.check("wigner negative fraction of a Gaussian (non-negative W)", iv(r0, "wg")["negativeFraction"], 0.0, abs=1e-3)


# ----------------------------------------------------------------------------------------------------------- camera
def spatial_system(model, grid_kwargs, w0, z, gid="g"):
    tr = lambda **kw: b.transverse(model, **grid_kwargs, **kw)
    return b.system("beam", [b.gap("src", 0.0, medium="Vacuum", transverse=tr(initial_profile=b.initial_profile("gaussian", radius_m=w0))),
                             b.gap(gid, z, medium="Vacuum", transverse=tr())])


def camera(fam: Family):
    w0, z = 0.5e-3, 0.2
    with fam.figure("camera-radius", "Camera on a Gaussian beam (w0 = 0.5 mm, 0.2 m after the waist): near-field radius and far-field radius at three carriers",
                    "near: w(z) = w0 sqrt(1 + (z/zR)^2);  far (lens f = 0.5 m): w_f = lam f / (pi w0),  divergence lam / (pi w0),  Strehl = (w0 / w)^2,  E(1/e^2) = 1 - e^-2") as f:
        eng_n, ana_n, eng_f, ana_f, lams = [], [], [], [], []
        for lam in CARRIERS_M:
            zr = math.pi * w0 ** 2 / lam
            w = w0 * math.sqrt(1 + (z / zr) ** 2)
            grid = dict(nx=128, ny=128, half_width_m=3e-3, spectral_samples=1)
            sysd = spatial_system("cartesian", grid, w0, z)
            r = uf.run(sysd, b.run_spec(b.cw(lam, 1.0), instruments=[b.instrument("camera", name="cn"),
                                                                   b.instrument("camera", name="cf", imaging={"plane": "far", "focalLengthM": 0.5})]))
            n, fr = iv(r, "cn"), iv(r, "cf")
            fam.check(f"camera near radius {label_wl(lam)}", n["radiusXM"], w, rel=3e-3, unit="m", formula="w0 sqrt(1 + (z/zR)^2) (pixel sampling of the second moment)")
            fam.check(f"camera near radius y = x {label_wl(lam)}", n["radiusYM"], n["radiusXM"], rel=1e-9, unit="m")
            fam.check(f"camera near energy {label_wl(lam)}", n["energyJ"], 1e-9, rel=1e-4, unit="J", formula="cw 1 W x 1 ns")
            fam.check(f"camera near M2 {label_wl(lam)}", n["m2"], 1.0, abs=2e-3)
            fam.check(f"camera far radius {label_wl(lam)}", fr["radiusXM"], lam * 0.5 / (math.pi * w0), rel=3e-3, unit="m", formula="lam f / (pi w0)")
            fam.check(f"camera far divergence {label_wl(lam)}", fr["divergenceHalfAngleXRad"], lam / (math.pi * w0), rel=3e-3, unit="rad", formula="lam / (pi w0)")
            fam.check(f"camera far Strehl of the curved beam {label_wl(lam)}", fr["strehlRatio"], (w0 / w) ** 2, rel=3e-3, formula="1 / (1 + (z/zR)^2) = (w0/w)^2")
            fam.check(f"camera far energy inside 1/e^2 {label_wl(lam)}", fr["energyInside1e2Fraction"], 1 - math.exp(-2), rel=3e-3, formula="1 - e^-2 for a Gaussian")
            eng_n.append(n["radiusXM"] * 1e3); ana_n.append(w * 1e3); eng_f.append(fr["radiusXM"] * 1e6); ana_f.append(lam * 0.5 / (math.pi * w0) * 1e6); lams.append(lam * 1e9)
        overlay(f, lams, eng_f, ana_f, label="far-field radius", xlabel="carrier wavelength (nm)", ylabel="far-field radius (um)", marker="o", analytic_label="lam f / (pi w0)")
    # pixel pitch, magnification and an elliptical beam
    grid = dict(nx=128, ny=128, half_width_m=3e-3, spectral_samples=1)
    el = b.system("ell", [b.gap("src", 0.0, medium="Vacuum", transverse=b.transverse("cartesian", **grid, initial_profile=b.initial_profile("gaussian", radius_x_m=0.6e-3, radius_y_m=0.3e-3)))])
    r = uf.run(el, b.run_spec(b.cw(LAM, 1.0), instruments=[b.instrument("camera", name="c", sensor={"pixels": [256, 256], "pixelSizeM": 40e-6}, imaging={"magnification": 2})]))
    c = iv(r, "c")
    # the camera resamples the source grid (dx = 46.9 um) bilinearly: linear interpolation adds dx^2 / 6 to the variance of a sampled Gaussian,
    # so the D4sigma radius is 2 sqrt((w/2)^2 + dx^2/6) -- the engine's number to 4 digits
    dx = 6e-3 / 128
    wb = lambda w_: 2 * math.sqrt((w_ / 2) ** 2 + dx ** 2 / 6)
    fam.check("camera elliptical beam radius x (with bilinear resampling)", c["radiusXM"], wb(0.6e-3), rel=5e-4, unit="m", formula="2 sqrt((w/2)^2 + dx^2/6)")
    fam.check("camera elliptical beam radius y (with bilinear resampling)", c["radiusYM"], wb(0.3e-3), rel=5e-4, unit="m", formula="2 sqrt((w/2)^2 + dx^2/6)")
    fam.check("camera elliptical beam radius x", c["radiusXM"], 0.6e-3, rel=3e-3, unit="m", formula="w_x (0.2 % = the resampling broadening)")
    fam.check("camera elliptical beam radius y", c["radiusYM"], 0.3e-3, rel=1e-2, unit="m", formula="w_y (0.8 % = the resampling broadening at 6 source samples per w)")
    fam.check("camera ellipticity", c["ellipticity"], wb(0.3e-3) / wb(0.6e-3), rel=5e-4, formula="w_y / w_x with the resampling broadening")
    fam.check("camera pixel size in the object plane (magnification 2)", c["pixelSizeObjectM"], 20e-6, rel=1e-9, unit="m", formula="pixel / magnification")


LAM = 1030e-9


# ----------------------------------------------------------------------------------------------------------- wavefront sensor
def wavefront_sensor(fam: Family):
    w0, z = 0.5e-3, 0.2
    with fam.figure("wavefront-curvature", "Wavefront sensor on a diverging Gaussian beam: radius of curvature R(z) at three carriers and z",
                    "R(z) = z + zR^2 / z  (R > 0 diverging);  wavefront from the Zernike defocus: W = r^2 / 2R") as f:
        zs = np.array([0.1, 0.2, 0.4, 0.8])
        for lam in CARRIERS_M:
            zr = math.pi * w0 ** 2 / lam
            eng, ana = [], []
            for zz in zs:
                grid = dict(nx=128, ny=128, half_width_m=3e-3, spectral_samples=1)
                r = uf.run(spatial_system("cartesian", grid, w0, float(zz)), b.run_spec(b.cw(lam, 1.0), instruments=[b.instrument("wavefrontSensor", name="ws")]))
                R = iv(r, "ws")["radiusOfCurvatureM"]
                want = zz + zr ** 2 / zz
                fam.check(f"wavefront R(z={zz} m) {label_wl(lam)}", R, want, rel=0.01, unit="m", formula="z + zR^2/z (1 %: documented accuracy of the lenslet fit)")
                eng.append(R); ana.append(want)
            overlay(f, zs, eng, ana, label=label_wl(lam), xlabel="z after the waist (m)", ylabel="R (m)", marker="o", analytic_label="z + zR^2/z" if lam == CARRIERS_M[0] else None)
    # a known Zernike aberration put in by a wavefront element on a collimated beam: recovered by the sensor (Noll index, waves rms)
    with fam.figure("wavefront-zernike", "Wavefront sensor: Zernike coefficients (waves rms) put in by a wavefront element and read back",
                    "coefficient c_j (Noll j) in waves rms over the aperture radius; piston and tilt removed") as f:
        w, ra = 1.5e-3, 1.0e-3
        coeffs = {5: 0.05, 7: 0.04, 8: 0.03, 11: 0.03, 4: 0.02}
        zlist = [0.0] * 11
        for j, c_ in coeffs.items():
            zlist[j - 1] = c_
        grid = dict(nx=128, ny=128, half_width_m=3e-3, spectral_samples=1)
        sysd = b.system("zern", [b.gap("src", 0.0, medium="Vacuum", transverse=b.transverse("cartesian", **grid, initial_profile=b.initial_profile("gaussian", radius_m=w))),
                                 b.wavefront("wf", zernike_noll_waves=zlist, radius_m=ra), b.gap("end", 0.0, medium="Vacuum", transverse=b.transverse("cartesian", **grid))])
        r = uf.run(sysd, b.run_spec(b.cw(LAM, 1.0), instruments=[b.instrument("wavefrontSensor", name="ws", apertureRadiusM=ra, zernikeTerms=15)]))
        v = iv(r, "ws")
        zw = np.asarray(r["ws_zernike_waves"]); zi = np.asarray(r["ws_zernike_index"])
        got = {int(j): float(c_) for j, c_ in zip(zi, zw)}
        eng, ana = [], []
        for j in range(4, 12):
            want = coeffs.get(j, 0.0)
            fam.check(f"Zernike Z{j} read back", got.get(j, float("nan")), want, abs=2e-3, unit="waves", formula="input coefficient (abs 2e-3 waves: 6 % of the smallest)")
            eng.append(got.get(j, float("nan"))); ana.append(want)
        rms = math.sqrt(sum(c_ ** 2 for c_ in coeffs.values()))
        fam.check("wavefront rms (sum of squares of the coefficients)", v["rmsWaves"], rms, rel=0.03, unit="waves", formula="sqrt(sum c_j^2)")
        overlay(f, np.arange(4, 12), eng, ana, label="engine", xlabel="Noll index j", ylabel="coefficient (waves rms)", marker="o", style="", analytic_label="input")


# ----------------------------------------------------------------------------------------------------------- beamProfile
def beam_profile(fam: Family):
    w0, E = 0.5e-3, 1e-3
    with fam.figure("beamprofile", "beamProfile at a Gaussian beam waist: radial fluence profile and peak fluence, radial and cartesian grids",
                    "F(r) = (2E / pi w^2) exp(-2 r^2 / w^2);  peak 2E / (pi w^2);  radius w, M2 = 1") as f:
        for model, grid in (("radial", dict(points=128, radius_m=2.5e-3, spectral_samples=1)), ("cartesian", dict(nx=128, ny=128, half_width_m=2.5e-3, spectral_samples=1))):
            sysd = b.system("bp", [b.gap("src", 0.0, medium="Vacuum", transverse=b.transverse(model, **grid, initial_profile=b.initial_profile("gaussian", radius_m=w0)))])
            r = uf.run(sysd, b.run_spec(b.pulse(LAM, 1e-12, energy=E), instruments=[b.instrument("beamProfile", name="bp")]))
            v = iv(r, "bp")
            fam.check(f"beamProfile radius {model}", v["radiusXM"], w0, rel=1e-3, unit="m")
            fam.check(f"beamProfile energy {model}", v["energyJ"], E, rel=1e-6, unit="J")
            fam.check(f"beamProfile M2 {model}", v["m2"], 1.0, abs=2e-3)
            fam.check(f"beamProfile peak fluence {model}", v["peakFluenceJPerM2"], 2 * E / (math.pi * w0 ** 2), rel=3e-3, unit="J/m2", formula="2E / (pi w^2)")
            if model == "radial":
                rr, F = np.asarray(v["radius_m"]), np.asarray(v["fluence_j_m2"])
            else:
                rr, F = np.asarray(v["x_m"]), np.asarray(v["fluence_x_j_m2"]) if "fluence_x_j_m2" in v else None
                # the cartesian cut along y = 0 is the map's central row; reconstruct the 2-D map's central row from the 1-D cut of the map
                Fm = np.asarray(v["fluence_map_j_m2"])
                F = Fm[:, Fm.shape[1] // 2] if Fm.ndim == 2 else F
            want = 2 * E / (math.pi * w0 ** 2) * np.exp(-2 * rr ** 2 / w0 ** 2)
            msk = np.abs(rr) < 1.5e-3
            fam.check(f"beamProfile profile max|dF|/Fpeak {model}", float(np.max(np.abs(F[msk] - want[msk])) / want.max()), 0.0, abs=5e-3,
                      formula="Gaussian fluence profile (central row; half-pixel offset of an even grid)")
            overlay(f, rr[msk] * 1e3, F[msk], want[msk], label=model, xlabel="r or x (mm)", ylabel="fluence (J/m2)", marker="o" if model == "radial" else "s",
                    analytic_label="Gaussian" if model == "radial" else None)


# ----------------------------------------------------------------------------------------------------------- delayScan
def delay_scan(fam: Family):
    w = 100e-6
    ideal = {"name": "ideal", "no": {"kind": "constant", "value": 1.8}, "ne": {"kind": "constant", "value": 1.7}, "deffPmPerV": 2.0, "n2": 0.0}
    with fam.figure("delayscan", "delayScan on a low-gain OPA of a dispersion-free crystal: gain against pump-signal delay",
                    "delta E_signal(d) ~ int I_p(t) I_s(t - d) dt: a Gaussian of FWHM sqrt(tau_p^2 + tau_s^2) centred on zero delay (undepleted, small gain)") as f:
        def scan(tp, ts, ep):
            aux = b.aux_beam("pump", b.pulse(1030e-9, tp, energy=ep), radius_m=w, delay_s=0.0)
            sysd = b.system("opa", [b.crystal("opa", ideal, 5e-3, process="opa", type="0", cut={"thetaDeg": 90, "phiDeg": 0},
                                              overlap={"model": "planeWave", "radiusM": w}, chi3=False, aux=[aux], output="signal")])
            r = uf.run(sysd, b.run_spec(b.pulse(1550e-9, ts, energy=1e-15), physics={"selfSteepening": False},
                                        instruments=[b.instrument("delayScan", at="opa", name="ds", fromS=-2e-12, toS=2e-12, points=81)]))
            return iv(r, "ds"), np.asarray(r["ds_delay_s"]), np.asarray(r["ds_output_energy_j"])
        base = None
        for tp, ts in ((400e-15, 600e-15), (200e-15, 300e-15)):
            v, d, e = scan(tp, ts, 0.5e-9)
            dE = e - e.min()
            want_fw = math.sqrt(tp ** 2 + ts ** 2)
            fam.check(f"delay scan FWHM, pump {tp * 1e15:g} fs signal {ts * 1e15:g} fs", fwhm(d, dE), want_fw, rel=0.01, unit="s", formula="sqrt(tau_p^2 + tau_s^2)")
            fam.check(f"delay scan optimum delay, pump {tp * 1e15:g} fs", v["optimalDelayS"], 0.0, abs=1e-15, unit="s")
            ana = dE.max() * np.exp(-4 * LN2 * d ** 2 / want_fw ** 2)
            fam.check(f"delay scan profile max|dE|/peak, pump {tp * 1e15:g} fs", float(np.max(np.abs(dE - ana)) / dE.max()), 0.0, abs=0.01, formula="Gaussian correlation")
            if base is None:
                base = (d, dE, ana)
                overlay(f, d * 1e12, dE / dE.max(), ana / dE.max(), label="400 fs pump, 600 fs signal", xlabel="delay (ps)", ylabel="signal gain (normalised)",
                        marker="o", analytic_label="Gaussian, sqrt(tp^2+ts^2)")
        v2, d2, e2 = scan(400e-15, 600e-15, 1.0e-9)
        fam.check("delay scan gain linear in pump energy (small gain)", (e2 - e2.min()).max() / base[1].max(), 2.0, rel=0.01, formula="undepleted: gain ~ I_pump")


# ----------------------------------------------------------------------------------------------------------- FROG retrieval
def frog_retrieval(fam: Family):
    lam, tau = 1030e-9, 100 * FS
    with fam.figure("frog-retrieval", "SHG-FROG and PG-FROG of a chirped Gaussian, retrieved by pcgpa: spectral phase against gdd w^2 / 2",
                    "A(w) = exp(-T0^2 dw^2/2 + i gdd dw^2/2);  FWHM = tau sqrt(1 + (4 ln2 gdd/tau^2)^2);  SHG-FROG cannot tell the sign of the chirp, PG-FROG can") as f:
        for gdd in (2000e-30, -2000e-30):
            for typ, kind in (("shg", None), ("pg", "pg-frog")):
                r = run(b.pulse(lam, tau, energy=1e-9, gdd_s2=gdd), [b.instrument("frog", points=256, name="frog", type=typ)])
                tr = {"delay_s": r["frog_delay_s"], "omega_rad_s": r["frog_omega_rad_s"], "trace": r["frog_trace"]}
                out = uf.retrieve("pcgpa", tr, kind=kind) if kind else uf.retrieve("pcgpa", tr)
                sp = out["pulse"]["spectrum"]
                lam_r, inten, ph = np.asarray(sp["wavelengthsM"]), np.asarray(sp["intensity"]), np.asarray(sp["phaseRad"])
                w = 2 * math.pi * AN.C / lam_r
                o = np.argsort(w)
                w, inten, ph = w[o], inten[o], np.unwrap(ph[o])
                w0 = centroid(w, inten)
                m = inten > 0.05 * inten.max()
                c = np.polyfit(w[m] - w0, ph[m], 2, w=np.sqrt(inten[m]))
                gdd_r = 2 * c[0]
                tag = f"{typ.upper()}-FROG gdd {gdd * 1e30:+.0f} fs2"
                fam.check(f"retrieval error {tag}", out["error"], 0.0, abs=5e-3, formula="FROG error of pcgpa (documented < 5e-3)")
                fam.check(f"retrieved FWHM {tag}", out["fwhmS"], AI.chirped_gaussian_fwhm(tau, gdd), rel=0.01, unit="s", formula="tau sqrt(1 + (4 ln2 gdd/tau^2)^2)")
                if typ == "shg":
                    fam.check(f"retrieved |GDD| {tag}", abs(gdd_r), abs(gdd), rel=0.03, unit="s2", formula="|gdd| (the SHG-FROG sign is ambiguous)")
                else:
                    fam.check(f"retrieved GDD {tag}", gdd_r, gdd, rel=0.03, unit="s2", formula="gdd (signed: the PG-FROG gate is an intensity)")
                if gdd > 0 and typ == "pg":
                    xx = (w - w0) * 1e-12
                    overlay(f, xx[m], ph[m] - ph[m][np.argmin(abs(xx[m]))], 0.5 * gdd * ((w[m] - w0) ** 2 - ((w[m][np.argmin(abs(xx[m]))] - w0) ** 2)),
                            label="retrieved phase (PG)", xlabel="omega - omega0 (Trad/s)", ylabel="spectral phase (rad)", analytic_label="gdd w^2 / 2")


# ----------------------------------------------------------------------------------------------------------- spectrometer models
def spectrometer_models(fam: Family):
    lam, tau, E = 1030e-9, 100 * FS, 1e-6
    sig = AI.gauss_sigma_omega(tau)

    def run_spec_(**kw):
        r = run(b.pulse(lam, tau, energy=E), [b.instrument("spectrometer", name="sp", normalize="none", points=1024, **kw)])
        return r, np.asarray(r["sp_wavelength_m"]), np.asarray(r["sp_spectrum"])

    r0, lw, S0 = run_spec_()
    wg, sg = spectrum_arrays(r0)
    wc = float(centroid(wg, sg))

    def ref_spectrum(lw_, extra=None):
        w = 2 * math.pi * AN.C / lw_
        g = np.exp(-(w - wc) ** 2 / (2 * sig ** 2)) / (math.sqrt(2 * math.pi) * sig)       # per rad/s, unit area
        if extra is not None:
            g = g * extra(w, lw_)
        return E * g * 2 * math.pi * AN.C / lw_ ** 2                                       # J per m
    with fam.figure("spectrometer-filter", "Spectrometer with a Gaussian filter and a detector response: spectral energy density against analytic",
                    "S(lam) = E g(w) 2 pi c / lam^2 with g the Gaussian spectrum; times the filter T(w) = exp(-ln2 ((w - wf)/(FWHM_w/2))^2), response R(lam), or lam/lam_c for photon counting") as f:
        a0 = ref_spectrum(lw)
        fam.check("spectrometer J/m spectrum, no filter: max|dS|/peak", float(np.max(np.abs(S0 - a0)) / a0.max()), 0.0, abs=2e-3, formula="Gaussian spectrum in lambda")
        # Gaussian filter, offset from the carrier
        lf, fw = 1040e-9, 12e-9
        wf = 2 * math.pi * AN.C / lf
        fw_w = 2 * math.pi * AN.C * fw / lf ** 2
        T = lambda w, l: np.exp(-LN2 * ((w - wf) / (fw_w / 2)) ** 2)
        r1, l1, S1 = run_spec_(filter={"kind": "gaussian", "centerWavelengthM": lf, "fwhmM": fw})
        a1 = ref_spectrum(l1, T)
        fam.check("spectrometer behind a Gaussian filter: max|dS|/peak", float(np.max(np.abs(S1 - a1)) / a0.max()), 0.0, abs=3e-3, formula="S(lam) T(w): power transmission")
        fam.check("spectrometer filtered energy", float(np.trapezoid(S1, l1)), float(np.trapezoid(a1, l1)), rel=5e-3, unit="J", formula="integral of S T")
        overlay(f, l1 * 1e9, S1 * 1e-9, a1 * 1e-9, label="Gaussian filter 1040 nm / 12 nm", xlabel="wavelength (nm)", ylabel="spectral energy (J/nm)",
                analytic_label="analytic")
        # detector calibration
        r2, l2, S2 = run_spec_(detector=b.detector(calibration=1.25))
        fam.check("spectrometer detector calibration 1.25", float(np.max(S2)) / float(np.max(S0)), 1.25, rel=1e-6, formula="reading = calibration x true")
        # spectral response table (linear in wavelength)
        resp = b.detector_response([1000e-9, 1060e-9], [0.4, 1.0])
        r3, l3, S3 = run_spec_(detector=b.detector(response=resp))
        a3 = ref_spectrum(l3, lambda w, l: np.interp(l, [1000e-9, 1060e-9], [0.4, 1.0]))
        fam.check("spectrometer with a detector response table: max|dS|/peak", float(np.max(np.abs(S3 - a3)) / a0.max()), 0.0, abs=3e-3,
                  formula="S(lam) R(lam), R linear in lam (held at the ends)")
        # photon counting: counts proportional to lam / lam_c
        r4, l4, S4 = run_spec_(detector=b.detector(counting="photons"))
        lc = 0.5 * (l4[0] + l4[-1])
        a4 = ref_spectrum(l4, lambda w, l: l / lc)
        fam.check("spectrometer photon counting: max|dS|/peak", float(np.max(np.abs(S4 - a4)) / a0.max()), 0.0, abs=3e-3,
                  formula="counts ~ S lam / lam_c, lam_c = centre of the axis")
        # ADC quantisation: readings are multiples of full scale / 2^bits
        bits = 4
        r5, l5, S5 = run_spec_(detector=b.detector(bit_depth=bits))
        step = S5.max() / (2 ** bits - 1)
        levels = np.unique(np.round(S5 / step, 6))
        fam.check("spectrometer 4-bit ADC: at most 2^4 distinct levels", float(len(levels)), float(min(len(levels), 2 ** bits)), abs=0, formula="quantised readings")
        fam.check("spectrometer 4-bit ADC: quantisation error bound", float(np.max(np.abs(S5 - S0)) / S0.max()), 0.0, abs=1.0 / (2 ** bits - 1),
                  formula="|error| <= one level of the full scale")


FUNCS = [energy_meter, scope, polarimeter, interferometric_ac, dscan, wigner, camera, wavefront_sensor, beam_profile, delay_scan, frog_retrieval,
         spectrometer_models]


def main() -> int:
    fam = Family("instruments_more", "More instruments: energy meter, scope, polarimeter, IAC, d-scan, Wigner, camera, wavefront sensor, beam profile, delay scan, FROG retrieval, spectrometer models")
    for fn in FUNCS:
        fam.guard(fn.__name__, lambda fn=fn: fn(fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
