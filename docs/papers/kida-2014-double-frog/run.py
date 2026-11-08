"""Kida et al. 2014, "double FROG": SHG-FROG of two pulses plus one XFROG trace analysed together.

    python run.py --tier fast|full [--figures]

Engine part: the two pulses (800 nm reference, 1200 nm test through the 3 mm fused-silica dichroic substrate), the engine's
`frog` (shg) and `xfrog` instruments (traces), `uf.retrieve` (pcgpa, kinds shg-frog / xfrog) and `uf.retrieve_multi` (multiFrog:
the double FROG), the GDD of the substrate, and the BBO phase-matching calculator for the 5 um crystal at 33.2 deg (block "ENGINE").
The numpy sum-frequency trace and numpy version of the Fig. 6 algorithm stay as an independent cross-check (keys without the eng_
prefix). Physics sign convention: A(t) exp(-i w0 t); GDD > 0 is a
positive chirp; transform kernel exp(+i W t).
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
LN2 = np.log(2.0)
N = {}
NG, DT = 128, 3e-15                  # retrieval grid: 128 x 128, 3 fs (window 384 fs)
tg = (np.arange(NG) - NG // 2) * DT
NOISE = float(os.environ.get('KIDA_NOISE', 0.004))                        # additive noise on the peak-normalised traces (assumption: reproduces a G floor of ~0.4 %)

# ------------------------------------------------------------------ pulses from the engine
def engine_field(lam, fwhm, gdd, bulk_len=0.0):
    elems = [{"id": "x", "type": "probe"}] if bulk_len == 0 else [b.bulk("sub", "FusedSilica", bulk_len)]
    r = uf.run(uf.System.load(b.system("p", elems)), b.run_spec(b.pulse(lam, fwhm, energy=1e-9, gdd_s2=gdd), grid={"windowS": 2e-12}))
    A = np.asarray(r["output_ax_re"]) + 1j * np.asarray(r["output_ax_im"])
    E = np.fft.fftshift(np.fft.fft(np.fft.ifftshift(A)))
    t = np.asarray(r["output_time_s"])
    Ei = np.interp(tg, t, E.real) + 1j * np.interp(tg, t, E.imag)
    return r, Ei / np.sqrt((abs(Ei) ** 2).sum())


T0SQ = lambda tau: tau ** 2 / (4 * LN2)
REF_TL, REF_GDD = 30e-15, 195e-30            # assumption: gives the 35 fs of the paper's laser (p. 3006) with a small positive chirp
TEST_TL, TEST_GDD = 40e-15, 580e-30          # assumption: ~58 fs (paper, p. 3007) after the 3 mm substrate, positive chirp
r_ref, r0 = engine_field(800e-9, REF_TL, REF_GDD)
r_test, s0 = engine_field(1200e-9, TEST_TL, TEST_GDD, bulk_len=3e-3)
N["ref_fwhm_fs"] = r_ref.summary["output"]["fwhmS"] * 1e15
N["test_fwhm_fs"] = r_test.summary["output"]["fwhmS"] * 1e15
N["substrate_gdd_fs2"] = r_test.summary["elements"][0]["own"]["gddS2"] * 1e30
N["test_input_gdd_fs2"] = TEST_GDD * 1e30

def ft(x):
    return np.fft.fftshift(np.fft.ifft(np.fft.ifftshift(x, axes=-1), axis=-1), axes=-1) * x.shape[-1]

def ift(x):
    return np.fft.fftshift(np.fft.fft(np.fft.ifftshift(x, axes=-1), axis=-1), axes=-1) / x.shape[-1]

K = np.arange(NG)
IDX_T = np.arange(NG)[None, :]
J = (IDX_T - K[:, None]) % NG       # j = i - k

def sig_shg(r):
    return r[None, :] * r[J]

def sig_x(s, r):                    # s(t) r(t - tau_k)
    return s[None, :] * r[J]

def trace_of(sig):
    return abs(ft(sig)) ** 2

def replace_mag(sig, Imeas):
    S = ft(sig)
    mod = abs(S) ** 2
    mu = (Imeas * mod).sum() / max((mod ** 2).sum(), 1e-300)
    return ift(np.sqrt(Imeas / max(mu, 1e-300)) * S / np.maximum(abs(S), 1e-300))

def gerr(Imeas, sig):
    mod = trace_of(sig)
    a = (Imeas * mod).sum() / max((mod ** 2).sum(), 1e-300)
    return float(np.sqrt(np.mean((Imeas / Imeas.max() - a * mod / Imeas.max()) ** 2)))

def pcgpa_shg(r, Imeas):            # one generalised-projection + principal-component step
    sp = replace_mag(sig_shg(r), Imeas)
    M = np.zeros((NG, NG), complex)
    M[np.broadcast_to(IDX_T, (NG, NG)), J] = sp
    v = M @ (M.conj().T @ r)
    return v / np.linalg.norm(v)

def ls_update_s(s, r, Imeas):       # test pulse from the XFROG trace, reference fixed
    sp = replace_mag(sig_x(s, r), Imeas)
    v = (sp * np.conj(r[J])).sum(0) / (abs(r) ** 2).sum()
    return v / np.linalg.norm(v)

def ls_update_r(s, r, Imeas):       # reference from the XFROG trace, test fixed
    sp = replace_mag(sig_x(s, r), Imeas)
    num = np.zeros(NG, complex)
    for k in range(NG):
        num += np.roll(sp[k] * np.conj(s), -k)       # term r(t') = Esig'[k, t'+k] conj(s(t'+k))
    v = num / (abs(s) ** 2).sum()
    return v / np.linalg.norm(v)

def gdd_of(E):
    S = ft(E)                                          # A(W) = sum A(t) exp(+i W t)
    I = abs(S) ** 2
    W = np.fft.fftshift(np.fft.fftfreq(NG, DT)) * 2 * np.pi
    m = I > 0.05 * I.max()
    Wm = (W * I).sum() / I.sum()
    ph = np.unwrap(np.angle(S))
    c = np.polyfit(W[m] - Wm, ph[m], 2, w=np.sqrt(I[m]))
    return 2 * c[0]

def fwhm_t(E):
    I = abs(E) ** 2
    x = np.linspace(tg[0], tg[-1], 8 * NG)
    Ii = np.interp(x, tg, I)
    ii = np.where(Ii >= Ii.max() / 2)[0]
    return (x[ii[-1]] - x[ii[0]])

def intensity_rms_error(E, Etrue):
    I, It = abs(E) ** 2, abs(Etrue) ** 2
    c = np.sum(I * tg) / I.sum(); ct = np.sum(It * tg) / It.sum()
    Is = np.interp(tg + c - ct, tg, I)
    return float(np.sqrt(np.mean((Is / Is.max() - It / It.max()) ** 2)))

# Environment overrides KIDA_NOISE / KIDA_ITERS exist only for the sensitivity statements in the README.
# synthetic measurement: the three traces with additive noise
rng = np.random.default_rng(2014)
def noisy(I):
    I = I / I.max()
    return np.maximum(I + NOISE * rng.standard_normal(I.shape), 0)

I_ref = noisy(trace_of(sig_shg(r0)))
I_test = noisy(trace_of(sig_shg(s0)))
I_x = noisy(trace_of(sig_x(s0, r0)))
N["noise_rms_fraction_of_peak"] = NOISE

# ------------------------------------------------------------------ engine: SHG-FROG of the reference, PCGPA, direction ambiguity
instr = [{"kind": "frog", "name": "sh", "type": "shg", "points": 128}]
rr = uf.run(uf.System.load(b.system("p", [{"id": "x", "type": "probe"}])), b.run_spec(b.pulse(800e-9, REF_TL, energy=1e-9, gdd_s2=REF_GDD), instruments=instr))
tr = {"delay_s": rr["sh_delay_s"], "omega_rad_s": rr["sh_omega_rad_s"], "trace": rr["sh_trace"]}
sgn = []
for sd in range(1, 7 if p.fast else 13):
    o = uf.retrieve("pcgpa", tr, kind="shg-frog", seed=sd)
    sp = o["pulse"]["spectrum"]
    lam, I, ph = np.array(sp["wavelengthsM"]), np.array(sp["intensity"]), np.array(sp["phaseRad"])
    w = 2 * np.pi * C0 / lam; oo = np.argsort(w); w, I, ph = w[oo], I[oo], np.unwrap(ph[oo])
    wm = (w * I).sum() / I.sum()
    sgn.append((o["error"], o["fwhmS"] * 1e15, 2 * np.polyfit(w - wm, ph, 2, w=np.sqrt(I))[0] * 1e30))
N["engine_shg_pcgpa_gdd_fs2_list"] = [x[2] for x in sgn]
N["engine_shg_pcgpa_has_both_signs"] = int(any(x[2] > 0 for x in sgn) and any(x[2] < 0 for x in sgn))
N["engine_shg_pcgpa_fwhm_fs_list"] = [x[1] for x in sgn]
N["engine_shg_pcgpa_error_max"] = max(x[0] for x in sgn)
N["ref_true_gdd_fs2"] = rr.summary["output"]["pulseGddS2"] * 1e30
# my numpy SHG trace vs the engine's: delay marginal widths (engine fwhm marginal in summary)
ins = {i["name"]: i for i in rr.summary["instruments"]}
N["engine_shg_delay_marginal_fwhm_fs"] = ins["sh"]["values"]["delayMarginalFwhmS"] * 1e15
dm = I_ref.sum(1)
dm = np.roll(dm, NG // 2) / dm.max()
tau = (np.arange(NG) - NG // 2) * DT
xx = np.linspace(tau[0], tau[-1], 4096); dmi = np.interp(xx, tau, dm); ii = np.where(dmi >= 0.5)[0]
N["numpy_shg_delay_marginal_fwhm_fs"] = (xx[ii[-1]] - xx[ii[0]]) * 1e15

# ------------------------------------------------------------------ BBO phase matching, 5 um, theta = 33.2 deg (paper p. 3007)
pm = {}
for nm, kw in (("shg800", dict(process="shg", fundamental=800e-9)), ("shg1200", dict(process="shg", fundamental=1200e-9)), ("sfg", dict(process="sfg", a=800e-9, b=1200e-9))):
    q = uf.crystal.phase_match("BBO", type="I", length=5e-6, theta_deg=33.2, phi_deg=90, **kw)
    best = uf.crystal.phase_match("BBO", type="I", length=5e-6, **kw)["best"]
    dk = q["best"]["deltaKPerM"]
    pm[nm] = (best["thetaDeg"], float(np.sinc(dk * 5e-6 / 2 / np.pi) ** 2))
    N["pm_theta_deg_" + nm] = pm[nm][0]
    N["pm_sinc2_at_33p2deg_" + nm] = pm[nm][1]
N["pm_sinc2_min"] = min(v[1] for v in pm.values())

# ------------------------------------------------------------------ retrievals
n_starts = 6 if p.fast else 16
iters = int(os.environ.get('KIDA_ITERS', 500 if p.fast else 1500))
true_sign_r, true_sign_s = np.sign(gdd_of(r0)), np.sign(gdd_of(s0))
N["ref_gdd_retrieval_grid_fs2"] = gdd_of(r0) * 1e30
N["test_gdd_retrieval_grid_fs2"] = gdd_of(s0) * 1e30

def init(rng_):
    g = np.exp(-(tg / 40e-15) ** 2) * (1 + 0.3 * rng_.standard_normal(NG)) * np.exp(1j * 0.6 * rng_.standard_normal(NG))
    return g / np.linalg.norm(g)

def run_algo(kind, seed, ref_override=None, use_x_for_r=True):
    rg = np.random.default_rng(seed)
    r, s = init(rg), init(rg)
    if kind == "conventional":         # step 1: SHG-FROG of the reference alone; step 2: XFROG with that reference
        for _ in range(iters):
            r = pcgpa_shg(r, I_ref)
        gate = r if ref_override is None else ref_override(r)
        for _ in range(iters):
            s = ls_update_s(s, gate, I_x)
        r = gate
    elif kind == "two":                # double-blind style: ref SHG + XFROG only, interleaved
        for _ in range(iters):
            r = pcgpa_shg(r, I_ref)
            s = ls_update_s(s, r, I_x)
    elif kind == "double":             # Fig. 6: ref SHG, XFROG -> s, test SHG, XFROG -> r
        for _ in range(iters):
            r = pcgpa_shg(r, I_ref)
            s = ls_update_s(s, r, I_x)
            s = pcgpa_shg(s, I_test)
            r = ls_update_r(s, r, I_x)
    return r, s

def reverse_conj(E):                    # E*(-t): the SHG-FROG twin of E
    return np.conj(E[(-np.arange(NG)) % NG])

def summarise(r, s):
    return dict(Gr=gerr(I_ref, sig_shg(r)), Gs=gerr(I_test, sig_shg(s)), Gx=gerr(I_x, sig_x(s, r)),
                dir_r=int(np.sign(gdd_of(r)) == true_sign_r), dir_s=int(np.sign(gdd_of(s)) == true_sign_s),
                fr=fwhm_t(r) * 1e15, fs=fwhm_t(s) * 1e15, er=intensity_rms_error(r, r0))

# G error floor: the truth against the noisy traces
N["G_floor_ref"] = gerr(I_ref, sig_shg(r0)); N["G_floor_test"] = gerr(I_test, sig_shg(s0)); N["G_floor_x"] = gerr(I_x, sig_x(s0, r0))
N["true_fwhm_ref_fs"] = fwhm_t(r0) * 1e15
N["true_fwhm_test_fs"] = fwhm_t(s0) * 1e15
# the wrong-direction reference with the right XFROG trace: what the time-reversed twin does to the test pulse
rev = reverse_conj(r0)
def fit_s(gate, seed):
    s_ = init(np.random.default_rng(seed))
    for _ in range(iters):
        s_ = ls_update_s(s_, gate, I_x)
    return gerr(I_x, sig_x(s_, gate)), fwhm_t(s_) * 1e15, np.sign(gdd_of(s_))

wrong = [fit_s(rev, 500 + i) for i in range(6)]
right = [fit_s(r0, 500 + i) for i in range(6)]
N["xfrog_wrong_ref_Gx"] = float(np.mean([x[0] for x in wrong]))
N["xfrog_wrong_ref_Gx_min"] = float(min(x[0] for x in wrong))
N["xfrog_wrong_ref_test_fwhm_fs_list"] = [x[1] for x in wrong]
N["xfrog_wrong_ref_test_fwhm_fs"] = float(np.mean([x[1] for x in wrong]))
N["xfrog_wrong_ref_test_gdd_sign_list"] = [x[2] for x in wrong]
N["xfrog_right_ref_Gx"] = float(np.mean([x[0] for x in right]))
N["xfrog_right_ref_Gx_max"] = float(max(x[0] for x in right))
N["xfrog_right_ref_test_fwhm_fs_list"] = [x[1] for x in right]
N["xfrog_right_ref_test_fwhm_fs"] = float(np.mean([x[1] for x in right]))

res_all = {}
for kind in ("conventional", "two", "double"):
    rows = [summarise(*run_algo(kind, 1000 + i)) for i in range(n_starts)]
    res_all[kind] = rows
    A = lambda k: np.array([x[k] for x in rows])
    N[kind + "_dir_correct_ref_fraction"] = float(A("dir_r").mean())
    N[kind + "_dir_correct_test_fraction"] = float(A("dir_s").mean())
    N[kind + "_both_correct_fraction"] = float(np.mean(A("dir_r") * A("dir_s")))
    N[kind + "_Gx_list"] = A("Gx"); N[kind + "_Gr_list"] = A("Gr"); N[kind + "_Gs_list"] = A("Gs")
    N[kind + "_Gx_mean"] = float(A("Gx").mean()); N[kind + "_Gx_max"] = float(A("Gx").max())
    N[kind + "_test_fwhm_fs_list"] = A("fs"); N[kind + "_ref_fwhm_fs_list"] = A("fr")
    N[kind + "_test_fwhm_fs_std"] = float(A("fs").std())
    good = A("Gx") < 3 * N["G_floor_x"]
    N[kind + "_fraction_converged_Gx_lt_3floor"] = float(good.mean())
N["n_starts"] = n_starts
_gm = lambda x: max(x["Gr"], x["Gs"], x["Gx"])
_ok = [_gm(x) for x in res_all["double"] if x["dir_r"] and x["dir_s"]]
_bad = [_gm(x) for x in res_all["double"] if not (x["dir_r"] and x["dir_s"])]
N["double_right_direction_Gmax"] = max(_ok) if _ok else None
N["double_wrong_direction_Gmin"] = min(_bad) if _bad else None
N["double_wrong_direction_count"] = len(_bad)

# ------------------------------------------------------------------ ENGINE: xfrog instrument + multi-trace retrieval (uf.retrieve_multi)
# Same synthetic experiment, but the traces come from the engine's `frog` (shg) and `xfrog` instruments and all retrievals are the engine's
# (`pcgpa` for single traces, `multiFrog` for the double FROG). The numpy code above is kept as an independent cross-check.
E_REF = {k: v for k, v in b.pulse(800e-9, REF_TL, energy=1e-9, gdd_s2=REF_GDD).items() if k != "energyJ"}
SYS_P = uf.System.load(b.system("p", [{"id": "x", "type": "probe"}]))
SYS_S = uf.System.load(b.system("s", [b.bulk("sub", "FusedSilica", 3e-3)]))
_ref_pulse = b.pulse(800e-9, REF_TL, energy=1e-9, gdd_s2=REF_GDD)
_test_pulse = b.pulse(1200e-9, TEST_TL, energy=1e-9, gdd_s2=TEST_GDD)
e_rr = uf.run(SYS_P, b.run_spec(_ref_pulse, instruments=[{"kind": "frog", "name": "fr", "type": "shg", "points": 128}]))
e_rt = uf.run(SYS_S, b.run_spec(_test_pulse, instruments=[{"kind": "frog", "name": "ft", "type": "shg", "points": 128}]))
e_rx = uf.run(SYS_S, b.run_spec(_test_pulse, instruments=[{"kind": "xfrog", "name": "fx", "points": 128, "reference": E_REF}]))
erng = np.random.default_rng(2014)

def e_trace(r, nm, noisy_=True):
    tr = np.asarray(r[nm + "_trace"], float)
    tr = tr / tr.max()
    if noisy_:
        tr = np.maximum(tr + NOISE * erng.standard_normal(tr.shape), 0)
    d = {"delay_s": np.asarray(r[nm + "_delay_s"]), "omega_rad_s": np.asarray(r[nm + "_omega_rad_s"]), "trace": tr}
    return d

T_REF, T_TEST, T_X = e_trace(e_rr, "fr"), e_trace(e_rt, "ft"), e_trace(e_rx, "fx")
REF_ARR = {k: np.asarray(e_rx["fx_" + k]) for k in ("reference_time_s", "reference_field_re", "reference_field_im", "reference_omega_rad_s")}
EMAP = lambda d, kind, **kw: dict(trace=d, kind=kind, **kw)

def pulse_gdd(pulse, thr=0.05):
    sp = pulse["spectrum"]
    lam, I, ph = np.array(sp["wavelengthsM"]), np.array(sp["intensity"]), np.array(sp["phaseRad"])
    w = 2 * np.pi * C0 / lam
    o = np.argsort(w); w, I, ph = w[o], I[o], np.unwrap(ph[o])
    wm = (w * I).sum() / I.sum()
    k = I > thr * I.max()
    return 2 * np.polyfit(w[k] - wm, ph[k], 2, w=np.sqrt(I[k]))[0] * 1e30

def reference_arrays(pulse):
    """Field of a retrieved reference pulse, packed like the xfrog instrument's reference arrays (so it can gate an XFROG retrieval)."""
    r_ = uf.run(SYS_P, b.run_spec(pulse, grid={"windowS": 2e-12}))
    A = np.asarray(r_["output_ax_re"]) + 1j * np.asarray(r_["output_ax_im"])
    Et = np.fft.fftshift(np.fft.fft(np.fft.ifftshift(A)))
    return {"reference_time_s": np.asarray(r_["output_time_s"]), "reference_field_re": Et.real, "reference_field_im": Et.imag,
            "reference_omega_rad_s": REF_ARR["reference_omega_rad_s"]}

def reversed_reference(arr):
    t_ = arr["reference_time_s"]
    E_ = arr["reference_field_re"] + 1j * arr["reference_field_im"]
    Er = np.conj(np.interp(-t_, t_, E_.real) + 1j * np.interp(-t_, t_, E_.imag))
    return {**arr, "reference_field_re": Er.real, "reference_field_im": Er.imag}

E_ITERS = int(os.environ.get("KIDA_ENG_ITERS", 600 if p.fast else 1000))
G_TRUE_REF, G_TRUE_TEST = N["ref_true_gdd_fs2"], e_rt.summary["output"]["pulseGddS2"] * 1e30
N["eng_true_gdd_ref_fs2"], N["eng_true_gdd_test_fs2"] = G_TRUE_REF, G_TRUE_TEST
N["eng_test_fwhm_true_fs"] = e_rt.summary["output"]["fwhmS"] * 1e15
N["eng_noise_floor_note"] = "traces: peak-normalised, additive gaussian noise %.4f" % NOISE

def eng_xfrog_fit(ref_arr, seed):
    o = uf.retrieve("pcgpa", {**T_X, **ref_arr}, kind="xfrog", seed=seed, maxIterations=E_ITERS)
    return o["error"], o["fwhmS"] * 1e15, pulse_gdd(o["pulse"])

n_fit = 6
right_ref = [eng_xfrog_fit(REF_ARR, 500 + i) for i in range(n_fit)]
wrong_ref = [eng_xfrog_fit(reversed_reference(REF_ARR), 500 + i) for i in range(n_fit)]
N["eng_xfrog_right_ref_G_list"] = [x[0] for x in right_ref]
N["eng_xfrog_wrong_ref_G_list"] = [x[0] for x in wrong_ref]
N["eng_xfrog_right_ref_G_mean"] = float(np.mean([x[0] for x in right_ref])); N["eng_xfrog_right_ref_G_max"] = max(x[0] for x in right_ref)
N["eng_xfrog_wrong_ref_G_mean"] = float(np.mean([x[0] for x in wrong_ref])); N["eng_xfrog_wrong_ref_G_min"] = min(x[0] for x in wrong_ref)
N["eng_xfrog_G_ratio_wrong_over_right"] = N["eng_xfrog_wrong_ref_G_mean"] / N["eng_xfrog_right_ref_G_mean"]
N["eng_xfrog_right_ref_test_fwhm_fs_list"] = [x[1] for x in right_ref]
N["eng_xfrog_wrong_ref_test_fwhm_fs_list"] = [x[1] for x in wrong_ref]
N["eng_xfrog_right_ref_test_fwhm_fs"] = float(np.mean([x[1] for x in right_ref]))
N["eng_xfrog_wrong_ref_test_fwhm_fs"] = float(np.mean([x[1] for x in wrong_ref]))
N["eng_xfrog_wrong_ref_test_gdd_fs2_list"] = [x[2] for x in wrong_ref]
N["eng_xfrog_right_ref_test_gdd_fs2_list"] = [x[2] for x in right_ref]

def classify(gref, gtest):
    return int(np.sign(gref) == np.sign(G_TRUE_REF)), int(np.sign(gtest) == np.sign(G_TRUE_TEST))

def eng_conventional(seed):
    o = uf.retrieve("pcgpa", T_REF, kind="shg-frog", seed=seed, maxIterations=E_ITERS)
    arr = reference_arrays(o["pulse"])
    ox = uf.retrieve("pcgpa", {**T_X, **arr}, kind="xfrog", seed=seed, maxIterations=E_ITERS)
    gr, gt = pulse_gdd(o["pulse"]), pulse_gdd(ox["pulse"])
    return dict(G=ox["error"], fs=ox["fwhmS"] * 1e15, gr=gr, gt=gt, dirs=classify(gr, gt), Gs=[o["error"], None, ox["error"]])

def multi(terms, seed):
    o = uf.retrieve_multi(terms, maxIterations=E_ITERS, seed=seed)
    gr, gt = pulse_gdd(o["pulses"]["ref"]["pulse"]), pulse_gdd(o["pulses"]["test"]["pulse"])
    return dict(G=o["traceErrors"][-1], Gs=list(o["traceErrors"]), fs=o["pulses"]["test"]["fwhmS"] * 1e15, fr=o["pulses"]["ref"]["fwhmS"] * 1e15,
                gr=gr, gt=gt, dirs=classify(gr, gt))

TERMS_DOUBLE = [EMAP(T_REF, "shg-frog", pulse="ref"), EMAP(T_TEST, "shg-frog", pulse="test"), EMAP(T_X, "xfrog", pulse="test", gate="ref")]
TERMS_TWO = [EMAP(T_REF, "shg-frog", pulse="ref"), EMAP(T_X, "xfrog", pulse="test", gate="ref")]
eng_rows = {"conventional": [], "two": [], "double": []}
for i in range(n_starts):
    sd_ = 1000 + i
    eng_rows["conventional"].append(eng_conventional(sd_))
    eng_rows["two"].append(multi(TERMS_TWO, sd_))
    eng_rows["double"].append(multi(TERMS_DOUBLE, sd_))
for kind, rows in eng_rows.items():
    N["eng_%s_both_correct_fraction" % kind] = float(np.mean([r["dirs"][0] * r["dirs"][1] for r in rows]))
    N["eng_%s_ref_correct_fraction" % kind] = float(np.mean([r["dirs"][0] for r in rows]))
    N["eng_%s_test_correct_fraction" % kind] = float(np.mean([r["dirs"][1] for r in rows]))
    N["eng_%s_Gx_list" % kind] = [r["G"] for r in rows]
    N["eng_%s_Gx_mean" % kind] = float(np.mean([r["G"] for r in rows]))
    N["eng_%s_test_fwhm_fs_list" % kind] = [r["fs"] for r in rows]
    N["eng_%s_test_fwhm_fs_std" % kind] = float(np.std([r["fs"] for r in rows]))
    N["eng_%s_test_gdd_fs2_list" % kind] = [r["gt"] for r in rows]
    N["eng_%s_ref_gdd_fs2_list" % kind] = [r["gr"] for r in rows]
_d = eng_rows["double"]
_gmax = lambda r: max(g for g in r["Gs"] if g is not None)
_ok = [_gmax(r) for r in _d if r["dirs"] == (1, 1)]
_bad = [_gmax(r) for r in _d if r["dirs"] != (1, 1)]
N["eng_double_all_traces_Gmax_right_direction"] = max(_ok) if _ok else None
N["eng_double_all_traces_Gmin_wrong_direction"] = min(_bad) if _bad else None
N["eng_double_wrong_direction_count"] = len(_bad)
N["eng_double_test_gdd_rel_err_right_runs"] = float(max([abs(r["gt"] - G_TRUE_TEST) / abs(G_TRUE_TEST) for r in _d if r["dirs"] == (1, 1)] or [np.nan]))
# noise-free double FROG with the engine (does the correct solution reach G ~ 0?)
clean = [dict(trace={**T, "trace": np.asarray(r_[n_ + "_trace"]) / np.asarray(r_[n_ + "_trace"]).max()}, **kw)
         for T, r_, n_, kw in ((T_REF, e_rr, "fr", dict(kind="shg-frog", pulse="ref")), (T_TEST, e_rt, "ft", dict(kind="shg-frog", pulse="test")),
                               (T_X, e_rx, "fx", dict(kind="xfrog", pulse="test", gate="ref")))]
cl = [multi(clean, 1000 + i) for i in range(3)]
N["eng_double_noisefree_G_max_list"] = [max(r["Gs"]) for r in cl]
N["eng_double_noisefree_G_max_list_max"] = max(N["eng_double_noisefree_G_max_list"])
N["eng_double_noisefree_dirs_ok"] = [int(r["dirs"] == (1, 1)) for r in cl]

# ------------------------------------------------------------------ figures
fig, axs = p.figure(ncols=3, width_in=9.6, height_in=3.2)
for a, (T, nm) in zip(axs, ((T_REF, "SHG-FROG, 800 nm reference"), (T_TEST, "SHG-FROG, 1200 nm test"), (T_X, "XFROG (sum frequency)"))):
    om = T["omega_rad_s"] / 2 / np.pi * 1e-15          # signal frequency (PHz)
    a.imshow(T["trace"].T, origin="lower", aspect="auto", cmap="viridis",
             extent=[T["delay_s"][0] * 1e15, T["delay_s"][-1] * 1e15, om[0], om[-1]])
    a.set_xlim(-150, 150); a.grid(False); a.set_title(nm, fontsize=9); a.set_xlabel("delay (fs)")
axs[0].set_ylabel("signal frequency (PHz)")
p.save(fig, "synthetic-traces")

fig, axs = p.figure(ncols=2, width_in=9.0, height_in=3.8)
names = {"conventional": "SHG-FROG, then XFROG", "two": "ref SHG + XFROG together", "double": "double FROG (3 traces)"}
for i, (kind, col) in enumerate((("conventional", BLUE), ("two", ORANGE), ("double", AQUA))):
    rows = res_all[kind]; er = eng_rows[kind]
    x0 = np.full(len(rows), i) + 0.08 * np.arange(len(rows)) / len(rows)
    x1 = np.full(len(er), i) - 0.12 + 0.08 * np.arange(len(er)) / len(er)
    axs[0].plot(x0, [x["fs"] for x in rows], "o", mfc="none", color=col, label="numpy cross-check" if i == 0 else None)
    axs[0].plot(x1, [x["fs"] for x in er], "o", color=col, label="engine `multiFrog` / `xfrog`" if i == 0 else None)
    axs[1].plot(x0, [100 * x["Gx"] for x in rows], "o", mfc="none", color=col)
    axs[1].plot(x1, [100 * x["G"] for x in er], "o", color=col)
axs[0].axhline(N["true_fwhm_test_fs"], color=REF, ls="--", label="true test pulse (%.0f fs)" % N["true_fwhm_test_fs"])
axs[0].axhline(58, color=REF2, ls=":", label="paper: 58 fs (right direction)")
axs[0].axhline(34, color=REF2, ls="-.", label="paper: 34 fs (time-reversed reference)")
axs[1].axhline(100 * N["G_floor_x"], color=REF, ls="--", label="numpy noise floor (true pulses; numpy G only)")
axs[1].axhspan(0.4, 0.5, color=REF2, alpha=0.25, label="paper: 0.4-0.5 % (right ref.)")
axs[1].axhspan(0.9, 1.0, color=REF2, alpha=0.5, label="paper: 0.9-1.0 % (reversed ref.)")
for a in axs:
    a.set_xticks([0, 1, 2]); a.set_xticklabels([names[k] for k in ("conventional", "two", "double")], rotation=15, fontsize=8)
    a.legend(fontsize=7)
axs[0].set_ylabel("retrieved test-pulse FWHM (fs)"); axs[1].set_ylabel("XFROG G error (%) (last trace of the multi-trace fit)")
p.save(fig, "retrievals")

fig, ax = p.figure(width_in=6.4, height_in=3.8)
for i, (kind, col) in enumerate((("conventional", BLUE), ("two", ORANGE), ("double", AQUA))):
    ax.bar(i - 0.2, 100 * N["eng_%s_both_correct_fraction" % kind], width=0.4, color=col, label="engine" if i == 0 else None)
    ax.bar(i + 0.2, 100 * N[kind + "_both_correct_fraction"], width=0.4, color=col, alpha=0.4, hatch="//", label="numpy cross-check" if i == 0 else None)
ax.plot([0, 1, 2], [50, 50, 100], "D", color=REF, ms=9, label="paper: random (~50 %), random, 6 of 6")
ax.set_xticks([0, 1, 2]); ax.set_xticklabels([names[k] for k in ("conventional", "two", "double")], fontsize=8)
ax.set_ylabel("retrievals with the right time direction (%)"); ax.axhline(50, color=REF2, ls=":"); ax.legend(fontsize=8, loc="center right")
p.save(fig, "time-direction")

p.write_numbers(N)
