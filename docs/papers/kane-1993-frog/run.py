"""Kane & Trebino 1993, FROG: self-diffraction / polarisation-gate FROG traces of engine pulses, Appendix-C laws, retrieval.

    python run.py --tier fast|full [--figures]

The engine supplies the pulses (chirped Gaussian, Kerr-broadened pulse, a pulse built from a spectrum) and its own FROG
instrument (`shg`, `pg`); the engine has no self-diffraction (SD) FROG, so the SD trace |FT[E^2(t) E*(t-tau)]|^2 of the
paper's geometry is computed here from the engine's complex field. Physics sign convention of the engine:
A(t) exp(-i w0 t), instantaneous frequency w(t) = -d arg(A)/dt, transform kernel exp(+i W t) (docs/concepts.md).
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from paperlib import Paper, BLUE, ORANGE, AQUA, REF, REF2  # noqa: E402

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

p = Paper(__file__)
C0 = 299792458.0
LAM = 620e-9                       # centre wavelength of the experiment (Sec. on the experiment, p. 574)
W0 = 2 * np.pi * C0 / LAM
TAU_TL = 100e-15                   # "compress ... to about 100 fs" (p. 574)
LN2 = np.log(2.0)
PROBE = b.system("probe", [{"id": "x", "type": "probe"}])
N = {}                              # numbers.json


def field(res, prefix="output"):
    """Complex envelope A(t) (physics convention) and time axis from the engine's spectral field A(omega)."""
    t = np.asarray(res[prefix + "_time_s"])
    A = np.asarray(res[prefix + "_ax_re"]) + 1j * np.asarray(res[prefix + "_ax_im"])
    E = np.fft.fftshift(np.fft.fft(np.fft.ifftshift(A)))        # A(t) = sum A(w) exp(-i w t)
    pw = np.asarray(res[prefix + "_power_w"])
    assert np.corrcoef(abs(E) ** 2, pw)[0, 1] > 0.9999, "field/power mismatch"
    return t, E


def omega_axis(t):
    dt = t[1] - t[0]
    return np.fft.fftshift(np.fft.fftfreq(len(t), dt)) * 2 * np.pi


def ft(x):                          # kernel exp(+i W t), W on omega_axis
    return np.fft.fftshift(np.fft.ifft(np.fft.ifftshift(x, axes=-1), axis=-1), axes=-1) * x.shape[-1]


def traces(E, ks):
    """SD, PG and SHG FROG traces [delay, W] for integer delays ks (samples); signal frequencies relative to the carrier."""
    sd, pg, sh = [], [], []
    for k in ks:
        Es = np.roll(E, k)                                       # A(t - k dt)
        sd.append(E ** 2 * np.conj(Es))
        pg.append(E * abs(Es) ** 2)
        sh.append(E * Es)
    return [abs(ft(np.array(s))) ** 2 for s in (sd, pg, sh)]


def centroid_slope(trace, tau, W, window):
    m = abs(tau) <= window
    c = (trace * W).sum(1) / trace.sum(1)
    return np.polyfit(tau[m], c[m], 1)[0], c


def inst_freq(t, E, frac=0.3):
    ph = np.unwrap(np.angle(E))
    m = abs(E) ** 2 > frac * (abs(E) ** 2).max()
    return -np.polyfit(t[m], ph[m], 2)[0] * 2, -np.gradient(ph, t)


def fwhm(x, y):
    y = np.asarray(y) / np.max(y)
    i = np.where(y >= 0.5)[0]
    def cross(a, bb):
        return x[a] + (0.5 - y[a]) * (x[bb] - x[a]) / (y[bb] - y[a])
    lo = cross(i[0] - 1, i[0]) if i[0] > 0 else x[i[0]]
    hi = cross(i[-1], i[-1] + 1) if i[-1] < len(x) - 1 else x[i[-1]]
    return hi - lo


def gdd_from_pulse_dict(pulse, thr=0.0):
    sp = pulse["spectrum"]
    lam, I, ph = np.array(sp["wavelengthsM"]), np.array(sp["intensity"]), np.array(sp["phaseRad"])
    w = 2 * np.pi * C0 / lam
    o = np.argsort(w)
    w, I, ph = w[o], I[o], np.unwrap(ph[o])
    wm = (w * I).sum() / I.sum()
    k = I > thr * I.max()                 # thr > 0: ignore the noisy phase in the spectral wings (ePIE leaves some)
    c = np.polyfit(w[k] - wm, ph[k], 2, w=np.sqrt(I[k]))
    return 2 * c[0]


# ------------------------------------------------------------------ A. linearly chirped Gaussian, 100 fs -> 200 fs
GDD = np.sqrt(3.0) * TAU_TL ** 2 / (4 * LN2)           # lengthens a 100 fs Gaussian to exactly 200 fs
T0sq = TAU_TL ** 2 / (4 * LN2)
b_an = GDD / (T0sq ** 2 + GDD ** 2)                      # analytic chirp rate d(omega)/dt of a chirped Gaussian
fwhm_an = TAU_TL * np.sqrt(1 + (GDD / T0sq) ** 2)
instr = [{"kind": "frog", "name": "pg", "type": "pg", "points": 128}, {"kind": "frog", "name": "sh", "type": "shg", "points": 128},
         {"kind": "frog", "name": "sd", "type": "sd", "points": 128}]
res = uf.run(uf.System.load(PROBE), b.run_spec(b.pulse(LAM, TAU_TL, energy=1e-9, gdd_s2=GDD), instruments=instr))
t, E = field(res)
dt = t[1] - t[0]
W = omega_axis(t)
ks = np.arange(-60, 61)
tau = ks * dt
sd, pg, sh = traces(E, ks)
b_eng, wt = inst_freq(t, E)
N["chirp_ratio_fwhm"] = res.summary["output"]["fwhmS"] / TAU_TL
N["chirp_rate_engine_over_analytic"] = b_eng / b_an
s_sd, c_sd = centroid_slope(sd, tau, W, 150e-15)
s_pg, c_pg = centroid_slope(pg, tau, W, 150e-15)
s_sh, c_sh = centroid_slope(sh, tau, W, 150e-15)
N["sd_slope_over_chirp"] = s_sd / b_eng               # Appendix C: 4/3
N["pg_slope_over_chirp"] = s_pg / b_eng               # Appendix C: 2/3
N["shg_slope_over_chirp"] = s_sh / b_eng              # Appendix B: symmetric trace -> 0
N["shg_asymmetry"] = float(abs(sh - sh[::-1]).max() / sh.max())
N["pg_asymmetry"] = float(abs(pg - pg[::-1]).max() / pg.max())
N["sd_asymmetry"] = float(abs(sd - sd[::-1]).max() / sd.max())

# delay marginals: engine instruments vs the analytic third-order / second-order correlation of a Gaussian
ins = {i["name"]: i for i in res.summary["instruments"]}
N["pg_delay_marginal_fwhm_fs_engine"] = ins["pg"]["values"]["delayMarginalFwhmS"] * 1e15
N["pg_delay_marginal_fwhm_fs_analytic"] = np.sqrt(1.5) * fwhm_an * 1e15
N["shg_delay_marginal_fwhm_fs_engine"] = ins["sh"]["values"]["delayMarginalFwhmS"] * 1e15
N["shg_delay_marginal_fwhm_fs_analytic"] = np.sqrt(2.0) * fwhm_an * 1e15
I_t = abs(E) ** 2
g3 = np.array([np.sum(I_t ** 2 * np.roll(I_t, k)) for k in ks])
N["sd_marginal_fwhm_fs"] = fwhm(tau, sd.sum(1)) * 1e15
N["g3_fwhm_fs"] = fwhm(tau, g3) * 1e15
N["g3_fwhm_fs_analytic"] = np.sqrt(1.5) * fwhm_an * 1e15
N["sd_marginal_vs_g3_maxdiff"] = float(abs(sd.sum(1) / sd.sum(1).max() - g3 / g3.max()).max())

# engine PG trace vs this script's PG trace: same shape on common axes?
eg_tau, eg_w, eg_tr = np.asarray(res["pg_delay_s"]), np.asarray(res["pg_omega_rad_s"]) - W0, np.asarray(res["pg_trace"])
eg_c = (eg_tr * eg_w).sum(1) / eg_tr.sum(1)
m = abs(eg_tau) <= 150e-15
N["engine_pg_slope_over_chirp"] = float(np.polyfit(eg_tau[m], eg_c[m], 1)[0] / b_eng)
# engine self-diffraction instrument (frog type "sd"): centroid slope, delay marginal, agreement with the numpy SD trace above
sd_tau, sd_w, sd_tr = np.asarray(res["sd_delay_s"]), np.asarray(res["sd_omega_rad_s"]) - W0, np.asarray(res["sd_trace"])
sd_c = (sd_tr * sd_w).sum(1) / sd_tr.sum(1)
m = abs(sd_tau) <= 150e-15
N["engine_sd_slope_over_chirp"] = float(np.polyfit(sd_tau[m], sd_c[m], 1)[0] / b_eng)
N["engine_sd_delay_marginal_fwhm_fs"] = ins["sd"]["values"]["delayMarginalFwhmS"] * 1e15
N["engine_sd_asymmetry"] = float(abs(sd_tr[1:] - sd_tr[1:][::-1]).max() / sd_tr.max())
# centroid of the numpy SD trace interpolated to the engine's delays (different grids): rms difference over the +-150 fs window, in units of the max
N["engine_sd_vs_numpy_centroid_rms_over_max"] = float(np.sqrt(np.mean((sd_c[m] - np.interp(sd_tau[m], tau, c_sd)) ** 2)) / np.abs(c_sd[abs(tau) <= 150e-15]).max())
eg_shc = np.asarray(res["sh_trace"])
N["engine_shg_asymmetry"] = float(abs(eg_shc[1:] - eg_shc[1:][::-1]).max() / eg_shc.max())   # delay axis runs -N/2..N/2-1: skip the unpaired sample

# Appendix B, directly: a pulse and its time-reversed twin (GDD -> -GDD) give the same SHG trace, different PG traces
def trace_pair(gdd):
    r_ = uf.run(uf.System.load(PROBE), b.run_spec(b.pulse(LAM, TAU_TL, energy=1e-9, gdd_s2=gdd), instruments=instr))
    return np.asarray(r_["sh_trace"]), np.asarray(r_["pg_trace"])
shp, pgp = trace_pair(GDD)
shm, pgm = trace_pair(-GDD)
N["engine_shg_trace_plus_minus_gdd_maxdiff"] = float(abs(shp / shp.max() - shm / shm.max()).max())
N["engine_pg_trace_plus_minus_gdd_maxdiff"] = float(abs(pgp / pgp.max() - pgm / pgm.max()).max())

# retrieval (engine PCGPA) from the engine's PG and SHG traces
def retrieve(prefix, kind, seed=None, method="pcgpa", thr=0.0):
    opt = {} if seed is None else {"seed": seed}
    tr = {"delay_s": res[prefix + "_delay_s"], "omega_rad_s": res[prefix + "_omega_rad_s"], "trace": res[prefix + "_trace"]}
    out = uf.retrieve(method, tr, kind=kind, **opt)
    return out["error"], out["fwhmS"], gdd_from_pulse_dict(out["pulse"], thr)


err, fw, g = retrieve("pg", "pg-frog")
N["pg_retrieval_error"], N["pg_retrieval_fwhm_fs"], N["pg_retrieval_gdd_fs2"] = err, fw * 1e15, g * 1e30
# SD-FROG: the paper's own geometry, now retrieved by the engine (PCGPA and ePIE); no time-direction ambiguity
for meth in ("pcgpa", "epie"):
    err, fw, g = retrieve("sd", "sd-frog", method=meth, thr=0.05)
    N["sd_retrieval_%s_error" % meth], N["sd_retrieval_%s_fwhm_fs" % meth], N["sd_retrieval_%s_gdd_fs2" % meth] = err, fw * 1e15, g * 1e30
sd_signs = []
for sd_ in range(1, 7):
    sd_signs.append(retrieve("sd", "sd-frog", seed=sd_, thr=0.05)[2] * 1e30)
sd_signs_epie = [retrieve("sd", "sd-frog", seed=sd_, method="epie", thr=0.05)[2] * 1e30 for sd_ in range(1, 7)]
N["sd_retrieval_epie_gdd_fs2_list"] = sd_signs_epie
N["sd_retrieval_epie_all_positive"] = int(all(x > 0 for x in sd_signs_epie))
N["sd_retrieval_gdd_fs2_list"] = sd_signs
N["sd_retrieval_all_positive"] = int(all(x > 0 for x in sd_signs))
# the wrong kind on an SD trace (PG model) fits worse: the kind matters
N["sd_trace_as_pg_error"] = retrieve("sd", "pg-frog")[0]
shg_g = []
for sd_ in range(1, 7 if p.fast else 13):
    e_, f_, g_ = retrieve("sh", "shg-frog", seed=sd_)
    shg_g.append((e_, f_ * 1e15, g_ * 1e30))
N["shg_retrieval_gdd_fs2_list"] = [x[2] for x in shg_g]
N["shg_retrieval_fwhm_fs_list"] = [x[1] for x in shg_g]
N["shg_retrieval_error_max"] = max(x[0] for x in shg_g)
N["shg_retrieval_abs_gdd_fs2_mean"] = float(np.mean([abs(x[2]) for x in shg_g]))
N["true_gdd_fs2"] = GDD * 1e30
N["shg_retrieval_has_both_signs"] = int(any(x[2] > 0 for x in shg_g) and any(x[2] < 0 for x in shg_g))

# ------------------------------------------------------------------ B. Kerr SPM (Figs 3, 4): peak SPM phase 1 and 8 rad
def spm_case(bint):
    area, L, n2 = 1e-7, 3e-3, 3.4e-20
    P0 = bint * LAM * area / (2 * np.pi * n2 * L)
    sysd = b.system("k", [b.bulk("nl", "flat", L, beam=b.constant_beam(area_m2=area))], materials={"flat": {"index": 1.45, "n2": n2}})
    spec = b.run_spec(b.pulse(LAM, TAU_TL, peak_power_w=P0), physics={"selfSteepening": False, "raman": False})
    r = uf.run(uf.System.load(sysd), spec)
    tt, EE = field(r)
    return r, tt, EE


spm = {}
for bint in (1.0, 8.0):
    r, tt, EE = spm_case(bint)
    d_t = tt[1] - tt[0]
    kk = np.arange(-60, 61)
    s_sd2, s_pg2, _ = traces(EE, kk)
    om_t = -np.gradient(np.unwrap(np.angle(EE)), tt)
    # Appendix C prediction for SD:  W(tau) = 2 w(tau/3) - w(-2 tau/3)  (shifts tau/3 rounded to samples)
    tau2 = kk * d_t
    Ip = abs(EE) ** 2
    pred = 2 * np.interp(tau2 / 3, tt, om_t) - np.interp(-2 * tau2 / 3, tt, om_t)
    cen = (s_sd2 * omega_axis(tt)).sum(1) / s_sd2.sum(1)
    pred_pg = np.interp(2 * tau2 / 3, tt, om_t)
    cen_pg = (s_pg2 * omega_axis(tt)).sum(1) / s_pg2.sum(1)
    pred_w = np.array([np.sum(Ip ** 2 * np.roll(Ip, k) * (2 * om_t - np.roll(om_t, k))) / np.sum(Ip ** 2 * np.roll(Ip, k)) for k in kk])
    use = abs(tau2) <= 100e-15
    sc = np.abs(cen[use]).max()
    N["spm%d_B_engine_rad" % bint] = float(r.summary["elements"][0]["bIntegralRad"])
    N["spm%d_sd_centroid_rms_dev_over_max" % bint] = float(np.sqrt(np.mean((cen[use] - pred[use]) ** 2)) / sc)
    N["spm%d_sd_centroid_rms_dev_weighted_over_max" % bint] = float(np.sqrt(np.mean((cen[use] - pred_w[use]) ** 2)) / sc)
    N["spm%d_pg_centroid_rms_dev_over_max" % bint] = float(np.sqrt(np.mean((cen_pg[use] - pred_pg[use]) ** 2)) / np.abs(cen_pg[use]).max())
    spm[bint] = (tau2, omega_axis(tt), s_sd2, cen, pred, pred_w)

# ------------------------------------------------------------------ C. the experiment's medium: 3 mm BK-7, 6 uJ, 25 GW/cm^2 (p. 574)
E_pulse = 6e-6
P0 = 0.9394 * E_pulse / 200e-15
area = P0 / (25e9 * 1e4)       # 25 GW/cm^2 = 2.5e14 W/m^2
sys_bk7 = b.system("bk7", [b.bulk("win", "N-BK7", 3e-3, beam=b.constant_beam(area_m2=area))])
r7 = uf.run(uf.System.load(sys_bk7), b.run_spec(b.pulse(LAM, TAU_TL, energy=E_pulse, gdd_s2=GDD), physics={"selfSteepening": False, "raman": False}))
v = r7.summary["elements"][0]
N["bk7_B_rad"] = float(v["bIntegralRad"])
N["bk7_B_rad_from_paper_intensity"] = 2 * np.pi / LAM * 3.4e-20 * 25e9 * 1e4 * 3e-3      # 2 pi/lambda n2 I L, I = 25 GW/cm^2 (p. 574), n2 = library value
N["bk7_gdd_fs2"] = v["own"]["gddS2"] * 1e30
N["bk7_fwhm_change_percent"] = 100 * (r7.summary["output"]["fwhmS"] / r7.summary["input"]["fwhmS"] - 1)
N["bk7_spectral_width_change_percent"] = 100 * (r7.summary["output"]["spectralFwhmM"] / r7.summary["input"]["spectralFwhmM"] - 1)
N["bk7_peak_intensity_GW_cm2"] = r7.summary["input"]["peakPowerW"] / area / 1e13
N["bk7_peak_power_MW"] = r7.summary["input"]["peakPowerW"] / 1e6

# independent check of the engine's Kerr step on that chirped pulse: dispersion-free medium, same B, numpy: A -> A exp(+i B I/Ipk)
flat = b.system("flat", [b.bulk("nl", "flat", 3e-3, beam=b.constant_beam(area_m2=area))], materials={"flat": {"index": 1.5, "n2": 3.4e-20}})
rf = uf.run(uf.System.load(flat), b.run_spec(b.pulse(LAM, TAU_TL, energy=E_pulse, gdd_s2=GDD), physics={"selfSteepening": False, "raman": False}))
tf_, Ef_ = field(rf, "input")
_, Eo_ = field(rf, "output")
Bf = rf.summary["elements"][0]["bIntegralRad"]
Enp = Ef_ * np.exp(1j * Bf * abs(Ef_) ** 2 / (abs(Ef_) ** 2).max())
def spec_rms(E_, t_):
    S_ = abs(np.fft.fftshift(np.fft.ifft(np.fft.ifftshift(E_)))) ** 2
    w_ = omega_axis(t_)
    m_ = (S_ * w_).sum() / S_.sum()
    return np.sqrt((S_ * (w_ - m_) ** 2).sum() / S_.sum())
N["kerr_check_B_rad"] = Bf
N["kerr_check_spectral_rms_engine_over_input"] = spec_rms(Eo_, tf_) / spec_rms(Ef_, tf_)
N["kerr_check_spectral_rms_numpy_over_input"] = spec_rms(Enp, tf_) / spec_rms(Ef_, tf_)
N["kerr_check_field_rel_diff"] = float(np.sqrt(np.sum(abs(Eo_ - Enp) ** 2) / np.sum(abs(Enp) ** 2)))

# ------------------------------------------------------------------ D. the paper's fitted pulse, Eqs. (5)-(7), through the engine's spectrum input
TREF = 76e-15
a_, b_, c_, d_ = 0.133, 0.41, 1.60, -1.18
al, be, ga, de, ep = -0.058, -0.271, -0.00044, -0.0007, -0.00003
tg = (np.arange(4096) - 2048) * 2e-15
x = tg / TREF
Ad = np.exp(-a_ * x ** 2) + b_ * np.exp(-c_ * (x - d_) ** 2)
Ed = Ad * np.exp(1j * (al * x + be * x ** 2 + ga * x ** 3 + de * x ** 4 + ep * x ** 5))
Id = abs(Ed) ** 2
fwhm_d = fwhm(tg, Id)
Sw = np.fft.fftshift(np.fft.ifft(np.fft.ifftshift(Ed)))      # A(w) = sum A(t) exp(+i w t)
wd = omega_axis(tg)
Sw_flat = np.fft.fftshift(np.fft.ifft(np.fft.ifftshift(Ad.astype(complex))))
def rms_w(S):
    I = abs(S) ** 2
    m = (I * wd).sum() / I.sum()
    return np.sqrt((I * (wd - m) ** 2).sum() / I.sum())
N["derived_fwhm_fs"] = fwhm_d * 1e15
N["derived_spectrum_rms_over_constant_phase_rms"] = rms_w(Sw) / rms_w(Sw_flat)
def rms_t(E_):
    I_ = abs(E_) ** 2
    m_ = (I_ * tg).sum() / I_.sum()
    return np.sqrt((I_ * (tg - m_) ** 2).sum() / I_.sum())
Etl = np.fft.fftshift(np.fft.fft(np.fft.ifftshift(abs(Sw))))       # same spectrum, constant phase: transform-limited pulse
N["derived_rms_duration_over_transform_limit"] = rms_t(Ed) / rms_t(Etl)
N["derived_fwhm_over_transform_limit_fwhm"] = fwhm_d / fwhm(tg, abs(Etl) ** 2)
# the same pulse as an engine input (spectrum + phase) -> engine pulse FWHM must equal the direct one
sel = abs(Sw) ** 2 > 1e-6 * (abs(Sw) ** 2).max()
wsel = W0 + wd[sel]
lam_sel = 2 * np.pi * C0 / wsel
Ispec = abs(Sw[sel]) ** 2 * 2 * np.pi * C0 / lam_sel ** 2                 # per unit wavelength
o = np.argsort(lam_sel)
# phase in the engine's convention: A(w) = sum A(t) exp(+i w t) -> d(phi)/d(w) = group delay (docs/concepts.md)
phi = np.unwrap(np.angle(Sw[sel]))
pulse_d = b.measured_spectrum(lam_sel[o][::4], Ispec[o][::4], energy=1e-9, phase_rad=phi[o][::4], center_wavelength_m=LAM)
rd = uf.run(uf.System.load(PROBE), b.run_spec(pulse_d, grid={"windowS": 4e-12}, instruments=[{"kind": "frog", "name": "pg", "type": "pg", "points": 128}]))
N["derived_engine_fwhm_fs"] = rd.summary["output"]["fwhmS"] * 1e15
N["derived_engine_gdd_fs2"] = rd.summary["output"]["pulseGddS2"] * 1e30
td, Ed_eng = field(rd)
N["derived_engine_over_direct_fwhm"] = N["derived_engine_fwhm_fs"] / N["derived_fwhm_fs"]
N["derived_engine_fwhm_fs_from_field"] = fwhm(td, abs(Ed_eng) ** 2) * 1e15
Ie = abs(Ed_eng) ** 2
Ic = abs(Ed) ** 2
N["derived_direct_centroid_fs"] = float((tg * Ic).sum() / Ic.sum() * 1e15)
N["derived_engine_centroid_fs"] = float((td * Ie).sum() / Ie.sum() * 1e15)   # same sign as the direct one: the time axis is not reversed
ksd = np.arange(-40, 41)
sd_d = traces(Ed_eng, ksd)[0]
tau_d = ksd * (td[1] - td[0])
g3d = np.array([np.sum(Ie ** 2 * np.roll(Ie, k)) for k in ksd])
N["derived_g3_fwhm_fs"] = fwhm(tau_d, g3d) * 1e15

# ------------------------------------------------------------------ figures
fig, ax = p.figure(width_in=6.4, height_in=4.2)
ax.plot(tau * 1e15, c_sd * 1e-12, color=BLUE, label="SD-FROG (numpy cross-check)")
ax.plot(tau * 1e15, c_pg * 1e-12, color=ORANGE, label="PG-FROG (numpy cross-check)")
ax.plot(tau * 1e15, c_sh * 1e-12, color=AQUA, label="SHG-FROG (computed)")
ax.plot(eg_tau * 1e15, eg_c * 1e-12, "o", ms=2.5, color=ORANGE, mfc="none", label="engine `frog` instrument, type pg")
ax.plot(sd_tau * 1e15, sd_c * 1e-12, "o", ms=2.5, color=BLUE, mfc="none", label="engine `frog` instrument, type sd")
ax.plot(tau * 1e15, 4 / 3 * b_an * tau * 1e-12, "--", color=REF, label="App. C: (4/3) b tau (SD)")
ax.plot(tau * 1e15, 2 / 3 * b_an * tau * 1e-12, "--", color=REF2, label="App. C: (2/3) b tau (PG)")
ax.set_xlim(-200, 200)
ax.set_ylim(-35, 35)
ax.set_xlabel("delay (fs)")
ax.set_ylabel("centroid frequency shift (10$^{12}$ rad/s)")
ax.legend(fontsize=7.5, loc="upper left")
ax.set_title("Chirped Gaussian, 100 fs -> 200 fs: frequency centroid vs delay", fontsize=10)
p.save(fig, "centroid-vs-delay")

fig, axs = p.figure(ncols=3, width_in=9.6, height_in=3.4)
for a, (trc, nm) in zip(axs, ((sd, "SD (paper's gate)"), (pg, "PG (polarisation gate)"), (sh, "SHG"))):
    a.imshow(trc.T / trc.max(), origin="lower", aspect="auto", cmap="viridis",
             extent=[tau[0] * 1e15, tau[-1] * 1e15, W[0] / 2 / np.pi * 1e-12, W[-1] / 2 / np.pi * 1e-12])
    a.set_xlim(-250, 250)
    a.set_ylim(-40, 40)
    a.set_title(nm, fontsize=10)
    a.set_xlabel("delay (fs)")
    if nm.startswith(("SD", "PG")):
        fac = 4 / 3 if nm.startswith("SD") else 2 / 3
        a.plot(tau * 1e15, fac * b_an * tau / (2 * np.pi) * 1e-12, "--", color="white", lw=1.2)
    a.grid(False)
axs[0].set_ylabel("signal frequency - carrier (THz)")
axs[1].text(-240, -35, "dashed: App. C slope", color="white", fontsize=8)
p.save(fig, "traces")

fig, axs = p.figure(ncols=2, width_in=9.0, height_in=3.8)
for bint, col in ((1.0, BLUE), (8.0, ORANGE)):
    tau2, Wk, s2, cen, pred, pred_w = spm[bint]
    axs[0].plot(tau2 * 1e15, cen * 1e-12, color=col, label="B = %g: SD centroid (computed from engine field)" % bint)
    axs[0].plot(tau2 * 1e15, pred * 1e-12, "--", color=REF, lw=1.3, label="App. C point law 2w(tau/3) - w(-2tau/3)" if bint == 1.0 else None)
    axs[0].plot(tau2 * 1e15, pred_w * 1e-12, ":", color=REF2, lw=2.2, label="signal-weighted mean of 2w(t) - w(t-tau)" if bint == 1.0 else None)
axs[0].set_xlim(-150, 150)
axs[0].set_xlabel("delay (fs)")
axs[0].set_ylabel("centroid frequency shift (10$^{12}$ rad/s)")
axs[0].legend(fontsize=7.5)
tau2, Wk, s2, cen, pred, pred_w = spm[8.0]
axs[1].imshow(s2.T / s2.max(), origin="lower", aspect="auto", cmap="viridis",
              extent=[tau2[0] * 1e15, tau2[-1] * 1e15, Wk[0] / 2 / np.pi * 1e-12, Wk[-1] / 2 / np.pi * 1e-12])
axs[1].set_xlim(-250, 250)
axs[1].set_ylim(-60, 60)
axs[1].set_xlabel("delay (fs)")
axs[1].set_ylabel("signal frequency - carrier (THz)")
axs[1].set_title("SD trace, peak SPM phase 8 rad (Kerr element)", fontsize=10)
axs[1].grid(False)
p.save(fig, "spm-sd-traces")

fig, ax = p.figure(width_in=6.4, height_in=3.8)
ax.plot(tau * 1e15, g3 / g3.max(), color=BLUE, label="int SD trace dW (engine field)")
ax.plot(tau * 1e15, np.exp(-4 * LN2 * tau ** 2 / (np.sqrt(1.5) * fwhm_an) ** 2), "--", color=REF, label="analytic Gaussian, FWHM = sqrt(3/2) x 200 fs")
ax.plot(eg_tau * 1e15, eg_tr.sum(1) / eg_tr.sum(1).max(), ".", ms=3, color=ORANGE, label="engine PG instrument delay marginal")
ax.set_xlabel("delay (fs)")
ax.set_ylabel("third-order autocorrelation (norm.)")
ax.legend(fontsize=8)
p.save(fig, "third-order-autocorrelation")

fig, axs = p.figure(ncols=2, width_in=9.0, height_in=3.8)
sel_t = abs(td) < 700e-15
axs[0].plot(td[sel_t] * 1e15, Ie[sel_t] / Ie.max(), color=BLUE, label="engine output (from spectrum + phase)")
axs[0].plot(tg * 1e15 - N["derived_direct_centroid_fs"] * 0 , Id / Id.max(), "--", color=REF, label="Eqs. (5)-(7), coefficients from the paper")
axs[0].set_xlim(-700, 700)
axs[0].set_xlabel("time (fs)")
axs[0].set_ylabel("intensity (norm.)")
axs[0].legend(fontsize=8)
axs[1].plot(wd / 2 / np.pi * 1e-12, abs(Sw) ** 2 / (abs(Sw) ** 2).max(), color=BLUE, label="with fitted phase")
axs[1].plot(wd / 2 / np.pi * 1e-12, abs(Sw_flat) ** 2 / (abs(Sw_flat) ** 2).max(), "--", color=REF, label="same envelope, constant phase")
axs[1].set_xlim(-15, 15)
axs[1].set_xlabel("frequency - carrier (THz)")
axs[1].set_ylabel("spectral intensity (norm.)")
axs[1].legend(fontsize=8, loc="upper right")
p.save(fig, "fitted-pulse")

p.write_numbers(N)
