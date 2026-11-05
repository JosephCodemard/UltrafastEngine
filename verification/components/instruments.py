"""Instruments alone, on transform-limited pulses: intensity autocorrelator (FWHM ratio √2 Gaussian, 1.5427 sech²), the
spectrometer's Gaussian resolution (FWHM² adds), SHG-FROG marginals (delay marginal = intensity autocorrelation,
frequency marginal = autoconvolution of the spectrum: centred at 2ω0, rms √2 σ_ω), and the third-order
cross-correlator (FWHM √(3/2) τ for a Gaussian, flat floor at the stated dynamic range).
Carriers 515 nm, 1030 nm, 2 µm; pulses of 1 ps and 10 fs.

Run: python verification/components/instruments.py [--no-plots] [--out DIR]. Exit code 1 when a check fails.
"""
from __future__ import annotations

import math
import sys

from _common import C, CARRIERS_M, DURATIONS_S, Family, b, centroid, fwhm, label_dur, label_wl, np, overlay, rms_width, uf

SECH2_AC = 1.5427   # FWHM ratio of the intensity autocorrelation of sech² (Diels & Rudolph, Table 9.1)


def iv(res, name):
    """The values of an instrument (Result.instrument returns them flat or under "values")."""
    d = res.instrument(name)
    return d.get("values", d)


def run(pulse, instruments):
    return uf.run(b.system("free", [b.probe("p")]), b.run_spec(pulse, instruments=instruments))


def autocorrelator(fam: Family):
    with fam.figure("autocorrelation", "Intensity autocorrelation of a 100 fs pulse at 1030 nm: Gaussian and sech²",
                    "G(τ) = ∫ I(t) I(t − τ) dt;  FWHM_AC / FWHM = √2 (Gaussian), 1.5427 (sech²)") as f:
        for shape, ratio in (("gaussian", math.sqrt(2)), ("sech2", SECH2_AC)):
            for lam in CARRIERS_M:
                for tau in DURATIONS_S:
                    r = run(b.pulse(lam, tau, energy=1e-9, shape=shape), [b.instrument("autocorrelator")])
                    v = iv(r, "autocorrelator")
                    fam.check(f"AC/FWHM {shape} {label_wl(lam)} {label_dur(tau)}", v["acFwhmS"] / tau, ratio, rel=3e-3,
                              formula="√2 / 1.5427")
                    key = "pulseFwhmGaussianS" if shape == "gaussian" else "pulseFwhmSech2S"
                    fam.check(f"deconvolved FWHM {shape} {label_wl(lam)} {label_dur(tau)}", v[key], tau, rel=3e-3, unit="s")
            # the trace against the analytic autocorrelation at 1030 nm, 100 fs
            r = run(b.pulse(1030e-9, 100e-15, energy=1e-9, shape=shape), [b.instrument("autocorrelator")])
            d, g = np.asarray(r["autocorrelator_delay_s"]), np.asarray(r["autocorrelator_signal"])
            g = g / g.max()
            if shape == "gaussian":
                want = np.exp(-4 * math.log(2) * (d / (math.sqrt(2) * 100e-15)) ** 2)
            else:
                T = 100e-15 / 1.7627
                x = np.where(np.abs(d) < 1e-18, 1e-18, d) / T
                want = 3 * (x * np.cosh(x) - np.sinh(x)) / np.sinh(x) ** 3
            fam.check(f"AC trace {shape} max |residual|", float(np.max(np.abs(g - want))), 0.0, abs=2e-3,
                      formula="Gaussian exp(−4ln2 τ²/(2τ_p²)); sech² 3(x cosh x − sinh x)/sinh³x")
            m = np.abs(d) < 400e-15
            overlay(f, d[m] * 1e15, g[m], want[m], label=shape, xlabel="delay (fs)", ylabel="autocorrelation (normalised)",
                    analytic_label="analytic" if shape == "gaussian" else None)


def spectrometer(fam: Family):
    with fam.figure("spectrometer", "Spectrometer resolution on a 1 ps pulse at 1030 nm: measured FWHM against resolution",
                    "FWHM_meas = √(FWHM² + R²) (Gaussian line and Gaussian resolution)") as f:
        Rs = np.array([0, 0.5e-9, 1e-9, 2e-9, 4e-9])
        eng, ana = [], []
        f0 = None
        for R in Rs:
            r = run(b.pulse(1030e-9, 1e-12, energy=1e-9), [b.instrument("spectrometer", resolutionM=float(R), normalize="none")])
            v = iv(r, "spectrometer")
            if f0 is None:
                f0 = v["fwhmM"]
            want = math.sqrt(f0 ** 2 + R ** 2)
            fam.check(f"spectrometer FWHM, R = {R * 1e9:g} nm", v["fwhmM"], want, rel=3e-3, unit="m")
            fam.check(f"spectrometer energy, R = {R * 1e9:g} nm", v["energyJ"], 1e-9, rel=1e-6, unit="J", formula="normalize none: ∫ S dλ = E")
            eng.append(v["fwhmM"] * 1e9)
            ana.append(want * 1e9)
        overlay(f, Rs * 1e9, eng, ana, label="measured FWHM", xlabel="resolution FWHM (nm)", ylabel="spectral FWHM (nm)", marker="o",
                analytic_label="√(F² + R²)")
        lam_tl = 1030e-9
        f_tl = 0.4413 * lam_tl ** 2 / (C * 1e-12)
        fam.check("spectrometer FWHM, transform limit of 1 ps at 1030 nm", f0, f_tl, rel=5e-3, unit="m", formula="Δλ = 0.441 λ² / (c τ)")


def frog(fam: Family):
    for lam in CARRIERS_M:
        tau = 100e-15
        r = run(b.pulse(lam, tau, energy=1e-9), [b.instrument("frog", points=256), b.instrument("autocorrelator")])
        v = iv(r, "frog")
        ac = iv(r, "autocorrelator")
        fam.check(f"FROG delay marginal FWHM = AC FWHM {label_wl(lam)}", v["delayMarginalFwhmS"], ac["acFwhmS"], rel=3e-3, unit="s")
        w, m = np.asarray(r["frog_omega_rad_s"]), np.asarray(r["frog_frequency_marginal"])
        wi, si = np.asarray(r["input_omega_rad_s"]), np.asarray(r["input_spectrum"])
        w0, sig = centroid(wi, si), rms_width(wi, si)
        fam.check(f"FROG frequency marginal centre {label_wl(lam)}", centroid(w, m), 2 * w0, rel=1e-4, unit="rad/s", formula="2 ω0")
        fam.check(f"FROG frequency marginal rms {label_wl(lam)}", rms_width(w, m), math.sqrt(2) * sig, rel=1e-2, unit="rad/s",
                  formula="autoconvolution: √2 σ_ω")


def cross_correlator(fam: Family):
    with fam.figure("tocc", "Third-order cross-correlator, 1030 nm, 100 fs Gaussian, 110 dB floor, no noise",
                    "C(τ) = ∫ I(t) I²(t − τ) dt: FWHM √(3/2) τ_p; reading = C / C_max + floor") as f:
        for lam in CARRIERS_M:
            for tau in DURATIONS_S:
                r = run(b.pulse(lam, tau, energy=1e-9), [b.instrument("crossCorrelator", dynamicRangeDb=110, noise=False, points=801)])
                v = iv(r, "crossCorrelator")
                fam.check(f"TOCC FWHM / τ {label_wl(lam)} {label_dur(tau)}", v["fwhmS"] / tau, math.sqrt(1.5), rel=5e-3, formula="√(3/2)")
                fam.check(f"TOCC pre-pulse contrast {label_wl(lam)} {label_dur(tau)}", v["prePulseContrastDb"], -110, abs=0.01, unit="dB")
        r = run(b.pulse(1030e-9, 100e-15, energy=1e-9), [b.instrument("crossCorrelator", dynamicRangeDb=110, noise=False, points=801)])
        d, s = np.asarray(r["crossCorrelator_delay_s"]), np.asarray(r["crossCorrelator_signal"])
        s = s / s.max()
        want = np.exp(-4 * math.log(2) * d ** 2 / (1.5 * (100e-15) ** 2)) + 1e-11   # the floor adds to the signal
        m = np.abs(d) < 600e-15
        # 1 dB: on the −80 to −100 dB flanks the slope is ~0.5 dB/fs, so 1 fs of delay sampling is 0.5 dB
        fam.check("TOCC trace max |log10 residual| within ±600 fs", float(np.max(np.abs(np.log10(s[m]) - np.log10(want[m])))), 0.0, abs=0.1,
                  unit="decades")
        overlay(f, d[m] * 1e15, 10 * np.log10(s[m]), 10 * np.log10(want[m]), label="reading", xlabel="delay (fs)",
                ylabel="signal (dB)", analytic_label="Gaussian (√1.5 τ) + 1e-11 floor")


def main() -> int:
    fam = Family("instruments", "Instruments: autocorrelator, spectrometer, FROG marginals, third-order cross-correlator")
    for fn in (autocorrelator, spectrometer, frog, cross_correlator):
        fam.guard(fn.__name__, lambda fn=fn: fn(fam))
    return fam.finish()


if __name__ == "__main__":
    sys.exit(main())
