"""The ao plugin driven from Python: the deformable-mirror bench (calibrate, correct, loop, sensorless), the NumPy device twins against engine runs, the tiled-aperture
array and closed-loop pulse shaping. Reference values are in each docstring."""
import sys
from pathlib import Path

import numpy as np
import pytest

from conftest import requires_engine, slow

PLUGIN_PY = Path(__file__).resolve().parents[2] / "plugins" / "ao" / "python"
if str(PLUGIN_PY) not in sys.path:
    sys.path.insert(0, str(PLUGIN_PY))

import ultrafast as uf                                    # noqa: E402
from ultrafast import build as b                          # noqa: E402
from ultrafast.algorithms import sensorless, zernike      # noqa: E402

from ultrafast_ao import DmBench, devices, tiled_array, combining_metrics  # noqa: E402

pytestmark = requires_engine


def _amp_weights(bench):
    X, Y = np.meshgrid(bench.x, bench.x, indexing="ij")
    a = np.exp(-(X ** 2 + Y ** 2) / bench.beam_radius_m ** 2)
    return a / a.sum()


def test_the_plugin_is_installed_and_its_python_module_loads():
    assert "ao" in [p.id for p in uf.plugins.installed()]
    from ultrafast_ao import DeformableMirror, Slm, SpectralShaper, Aopdf
    assert DeformableMirror("dm", pitch_m=1e-3, actuators_x=4)["type"] == "deformableMirror"
    assert Slm("s", 8e-6, pixels_x=4, pixels_y=2, grey_level=np.zeros((4, 2)))["greyLevel"] == [[0.0, 0.0]] * 4
    assert SpectralShaper("sh", pixels=8, pixel_pitch_m=1e-5, dispersion_m_per_m=1e5, spot_radius_m=1e-6)["type"] == "spectralShaper"
    assert Aopdf("a", gdd_fs2=100.0)["gddFs2"] == 100.0


def test_strehl_of_an_aberrated_focus_follows_the_exact_axis_field_and_marechal():
    """The on-axis focal field of a Gaussian beam is the amplitude-weighted mean of exp(i phi): S_axis = |<a e^{i phi}>|^2 (exact, evaluated on the phase screen);
    the engine's focal peak is at or just above it (the peak may leave the axis) and within 2.5 % for these rotationally symmetric aberrations. Marechal
    exp(-sigma^2) (sigma the amplitude-weighted rms phase) holds to 1 % at sigma = 0.25 rad."""
    for ab, tol_exact in (({4: 0.04, 5: 0.03}, 0.025), ({4: 0.08, 5: 0.06}, 0.025), ({4: 0.05, 11: 0.06, 6: 0.04}, 0.025)):
        bn = DmBench(aberration=ab)
        a = _amp_weights(bn)
        ph = bn.screen_phase
        exact = abs((a * np.exp(1j * ph)).sum()) ** 2
        s = bn.strehl()
        assert exact * 0.995 <= s <= exact * (1 + tol_exact), (ab, s, exact)
        if ab == {4: 0.04, 5: 0.03}:
            sig2 = ((ph - (ph * a).sum()) ** 2 * a).sum()
            assert abs(s / np.exp(-sig2) - 1) < 0.01


def test_device_twin_matches_the_engine_deformable_mirror():
    """The NumPy influence matrix reproduces the engine's wavefront for one driven actuator: correlation above 0.999, rms difference below 4 % of the rms (the sensor
    averages over lenslets and the modulator over its 4 x 4 sub-cells), peak within 5 %; the surface-to-wavefront factor 2 cos(aoi) is in both."""
    for aoi in (0.0, 0.8):
        bn = DmBench(actuators=16, pitch_m=0.25e-3, lenslets=16, incidence_angle_rad=aoi)
        cmd = np.zeros(256); cmd[8 * 16 + 8] = 100e-9
        w, x, y = bn.wavefront_waves(cmd)
        X, Y = np.meshgrid(x, y, indexing="ij")
        tw = 2 * np.cos(aoi) * (devices.dm_influence_matrix(bn.positions, X, Y, pitch_m=bn.pitch_m, coupling=bn.coupling) @ cmd).reshape(X.shape) / bn.wavelength_m
        m = np.hypot(X, Y) <= 1.3e-3
        d, t = (w - w[m].mean())[m], (tw - tw[m].mean())[m]
        assert np.corrcoef(d, t)[0, 1] > 0.999
        assert np.std(d - t) < 0.04 * np.std(t)
        assert abs(w.max() / tw.max() - 1) < 0.05


def test_zernike_fit_residual_falls_with_the_actuator_count_and_with_a_moderate_coupling():
    """Fitting higher-order Zernike modes (Noll 7, 11, 12, 13) on the disc with an N x N Gaussian-influence mirror: the rms residual (units of the mode rms) falls
    monotonically with N (4 -> 16: 0.72 -> 0.036 at coupling 0.15) and a coupling of 0.15 fits better than 0.05 for N >= 6. (NumPy only; the engine's mirror matches the twin, test above.)"""
    R = 1.9e-3
    def resid(act, c):
        pitch = 2 * R * 1.05 / act
        pos = devices.dm_actuators("square", pitch_m=pitch, actuators_x=act)
        xs = np.linspace(-R, R, 61); Xs, Ys = np.meshgrid(xs, xs, indexing="ij"); ins = np.hypot(Xs, Ys) <= R
        M = devices.dm_influence_matrix(pos, Xs[ins], Ys[ins], pitch_m=pitch, coupling=c)
        out = []
        for j in (7, 11, 12, 13):
            z = zernike.basis([j], xs, xs, R)[0][ins]
            cf, *_ = np.linalg.lstsq(M, z, rcond=1e-8)
            out.append(np.sqrt(np.mean((M @ cf - z) ** 2)))
        return float(np.mean(out))
    r15 = [resid(a, 0.15) for a in (4, 6, 8, 12, 16)]
    assert all(r15[i + 1] < r15[i] for i in range(4)), r15
    assert r15[-1] < 0.05 and r15[0] > 0.5
    for a in (6, 8, 12, 16):
        assert resid(a, 0.15) < resid(a, 0.05)


def test_one_shot_correction_restores_the_focus_and_matches_the_twin_prediction():
    """Calibrate (push-pull pokes), correct in one shot: the focal Strehl of 0.31 rises above 0.85, and equals the exact on-axis value computed with the NumPy
    twin from the residual phase (screen + the mirror's wavefront) within 3 %."""
    ab = {4: 0.3, 5: -0.2, 7: 0.15}
    bn = DmBench(aberration=ab)
    s0 = bn.strehl()
    D = bn.calibrate()
    cmd, sv = bn.correct(D, regularisation=1e-2)
    s1 = bn.strehl(cmd)
    assert s0 < 0.4 and s1 > 0.85
    X, Y = np.meshgrid(bn.x, bn.x, indexing="ij")
    wf = 2 * (devices.dm_influence_matrix(bn.positions, X, Y, pitch_m=bn.pitch_m, coupling=bn.coupling) @ cmd).reshape(X.shape) / bn.wavelength_m
    resid = bn.screen_phase + 2 * np.pi * wf
    a = _amp_weights(bn)
    assert abs(s1 / abs((a * np.exp(1j * resid)).sum()) ** 2 - 1) < 0.03
    assert np.all(np.abs(cmd) < 1e-6)                                    # within a 1 micron stroke


def test_closed_loop_integrator_reduces_the_slopes_geometrically():
    """c <- c - g G m on a linear mirror and sensor: the rms slope falls by (1 - g) each step (g = 0.7: ratio 0.3 within 5 % while above the sensor's numerical floor)."""
    bn = DmBench(aberration={4: 0.3, 5: -0.2, 7: 0.15})
    D = bn.calibrate()
    c, hist = bn.loop(D, gain=0.7, steps=4, regularisation=1e-2)
    r = [hist[i + 1] / hist[i] for i in range(3)]
    assert all(abs(x - 0.3) < 0.05 for x in r[:2]), r
    assert hist[-1] < 0.05 * hist[0]


def test_modal_search_corrects_the_focus_from_the_peak_fluence_alone():
    """Sensorless: the metric is the focal peak fluence (no sensor). Modal search over six mirror modes (Zernike Noll 4, 5, 6, 7, 8, 11 commands from the twin's fit)
    raises the Strehl from 0.31 to above 0.9 in three sweeps (measured 0.943 with bias 0.2 waves, 54 evaluations; with bias 0.1 and four sweeps it reached 0.835: the
    peak-type metric is convex far from the optimum, so the search climbs by one bias per sweep until the parabola closes)."""
    ab = {4: 0.3, 5: -0.2, 7: 0.15}
    bn = DmBench(aberration=ab)
    modes = [4, 5, 6, 7, 8, 11]
    basis = np.array([bn.zernike_commands({j: 1.0}) for j in modes])           # commands per waves rms of each mode
    metric = lambda a: bn.peak_fluence(a @ basis)
    r = sensorless.modal_search(metric, np.zeros(len(modes)), bias=0.2, sweeps=3)
    s = bn.peak_fluence(r.x @ basis) / bn.reference_peak()
    assert bn.strehl() < 0.4 and s > 0.9, s
    assert r.evaluations <= 1 + 3 * (3 * len(modes) + 1)
    assert np.allclose(r.x[[0, 1, 3]], [-0.3, 0.2, -0.15], atol=0.12)         # it found the aberration: -(0.3, -0.2, 0.15) in defocus, astigmatism, coma-like modes (modes 4, 5, 7)


# ----------------------------------------------------------------------------------------------- holograms through the slm element
def test_a_gs_hologram_written_with_256_grey_levels_focuses_like_the_fourier_prediction():
    """Gerchberg-Saxton hologram for a ring of radius 0.45 mm in the focal plane (64 x 64 pixels of 40 um, Gaussian illumination), encoded to 256 grey levels
    with the SLM's own rule and run through the engine's slm element + lens (f = 0.2 m): the focal fluence equals the Fourier transform of the written field,
    evaluated at the engine's focal-plane points (x / (lambda f) the spatial frequency), to a correlation above 0.9995 within 1 mm of the axis (measured 0.99993), and
    more than 85 % of the energy lies on the ring (measured 94 %; the Fourier prediction at the same points gives 91 %, the GS design on its own lattice 83 %)."""
    from ultrafast.algorithms import holography
    from ultrafast_ao import Slm
    n, dx, w, lam, f = 64, 40e-6, 0.6e-3, 800e-9, 0.2
    y, x = np.indices((n, n)) - n // 2
    X = (np.arange(n) - (n - 1) / 2) * dx
    illum = np.exp(-(X[:, None] ** 2 + X[None, :] ** 2) / w ** 2)
    pix = lam * f / (n * dx)                                             # the FFT lattice of the hologram; the engine's focal plane is sampled at dx instead
    target = np.exp(-((np.hypot(x, y) - 0.45e-3 / pix) / 1.2) ** 2)
    res = holography.gerchberg_saxton(target, illum, iterations=100, seed=2)
    lut = devices.slm_lut(256)
    grey = holography.grey_levels(res.phase, lut)
    g = lambda ip=None: b.transverse("cartesian", nx=n, ny=n, half_width_m=n * dx / 2, spectral_samples=1, absorbing_boundary=True, initial_profile=ip)
    sysd = b.system("h", [b.gap("src", 0.0, medium="Vacuum", transverse=g(b.initial_profile("gaussian", radius_m=w))),
                          Slm("slm", dx, pixels_x=n, pixels_y=n, grey_level=grey.astype(float), dispersion="none"), b.lens("lens", f), b.gap("z", f, medium="Vacuum", transverse=g())])
    d = uf.run(sysd, b.run_spec(b.cw(lam, 1.0), instruments=[b.instrument("beamProfile", at="z", name="cam")])).instrument("cam")
    xf, F = np.asarray(d["x_m"]), np.asarray(d["fluence_map_j_m2"])
    K = np.exp(-2j * np.pi * np.outer(xf, X) / (lam * f))
    P = np.abs(K @ (illum * np.exp(1j * lut[grey])) @ K.T) ** 2
    XF, YF = np.meshgrid(xf, xf, indexing="ij"); R = np.hypot(XF, YF)
    assert np.corrcoef(P[R <= 1e-3], F[R <= 1e-3])[0, 1] > 0.9995
    assert F[np.abs(R - 0.45e-3) < 0.12e-3].sum() / F.sum() > 0.85


# ----------------------------------------------------------------------------------------------- tiled array
LAM, N, HALF, F, TP = 800e-9, 256, 5e-3, 0.5, 30e-15


def _tile_axis_fluence(prof, pulse, ss=1):
    g = lambda ip=None: b.transverse("cartesian", nx=N, ny=N, half_width_m=HALF, spectral_samples=ss, absorbing_boundary=True, initial_profile=ip)
    sysd = b.system("t", [b.gap("src", 0.0, medium="Vacuum", transverse=g(prof)), b.lens("lens", F), b.gap("z", F, medium="Vacuum", transverse=g())])
    d = uf.run(sysd, b.run_spec(pulse, instruments=[b.instrument("beamProfile", at="z", name="cam")])).instrument("cam")
    return np.asarray(d["fluence_map_j_m2"]), np.asarray(d["x_m"]), np.asarray(d["y_m"])


def test_tiled_array_in_phase_peak_is_n_times_a_single_tile_and_pistons_cost_the_coherent_sum():
    """N in-phase tiles of total power P give a peak N P_tile^2-fold: N x the single-tile peak at the same total power (each tile carries P/N, the sum adds amplitudes:
    N^2 x (1/N)); non-overlapping tiles (pitch 6 w): 0.3 %. With piston phi_k the on-axis fluence is |sum_k exp(i phi_k)|^2 / N^2 of the in-phase value (exact, tiles'
    far fields coincide on axis): 0.5 % for pistons of sigma = 0.5 rad (and its expectation (1 + (N - 1) exp(-sigma^2)) / N over draws)."""
    base = dict(layout="hex", rings=1, pitch_m=3e-3)
    cw = b.cw(LAM, 1.0)
    single, _, _ = _tile_axis_fluence(b.tiles_profile(0.5e-3, positions_m=[[0.0, 0.0]]), cw)
    ref, x, y = _tile_axis_fluence(tiled_array(0.5e-3, **base), cw)
    c = N // 2
    assert abs(ref[c, c] / (7 * single[c, c]) - 1) < 0.003
    rng = np.random.default_rng(4)
    phi = rng.normal(0, 0.5, 7)
    F2, _, _ = _tile_axis_fluence(tiled_array(0.5e-3, piston_rad=phi.tolist(), **base), cw)
    assert abs(F2[c, c] / ref[c, c] / (abs(np.exp(1j * phi).sum()) ** 2 / 49) - 1) < 0.005
    m = combining_metrics(ref, x, y, wavelength_m=LAM, focal_length_m=F, pitch_m=3e-3, n_tiles=7, reference_axis_fluence=ref[c, c])
    assert abs(m["axis_over_reference"] - 1) < 1e-12 and 0 < m["central_lobe_fraction"] < 1
    # expectation over many draws (numpy only): the formula the docs quote
    sig = 0.5
    draws = [abs(np.exp(1j * rng.normal(0, sig, 7)).sum()) ** 2 / 49 for _ in range(4000)]
    assert abs(np.mean(draws) / ((1 + 6 * np.exp(-sig ** 2)) / 7) - 1) < 0.01


def test_tiled_array_delay_mismatch_costs_the_pulse_overlap():
    """Tiles of a 30 fs Gaussian pulse with different delays tau_k (envelope shifts): the on-axis fluence over the aligned value is
    (1/N^2) sum_kl exp(-ln2 (tau_k - tau_l)^2 / tau_p^2) (overlap of two Gaussian envelopes, FWHM tau_p; derived in docs/plugins/ao.md): 1 % for rms delays of 10, 25 and 40 fs
    (measured 0.3 %, 0.04 %, 0.65 %)."""
    pulse = b.pulse(LAM, TP, energy=1e-3)
    base = dict(layout="hex", rings=1, pitch_m=3e-3)
    ref, _, _ = _tile_axis_fluence(tiled_array(0.5e-3, **base), pulse, ss=16)
    c = N // 2
    rng = np.random.default_rng(0)
    for rms in (10e-15, 25e-15, 40e-15):
        tau = rng.normal(0, rms, 7)
        v, _, _ = _tile_axis_fluence(tiled_array(0.5e-3, delays_s=tau.tolist(), **base), pulse, ss=16)
        D = tau[:, None] - tau[None, :]
        pred = np.exp(-np.log(2) * D ** 2 / TP ** 2).sum() / 49
        assert abs(v[c, c] / ref[c, c] / pred - 1) < 0.01, (rms, v[c, c] / ref[c, c], pred)


def test_spgd_locks_the_tile_pistons_through_the_engine():
    """Phase locking by SPGD with the engine in the loop: seven tiles with random pistons (start 0.04 of the in-phase value), metric = on-axis fluence over the in-phase
    value (a pinhole detector), 150 iterations (gain 10, amplitude 0.2 rad); the combined fluence exceeds 0.95. 128 x 128 cells: the metric is a ratio of two runs on the same grid."""
    global N
    n_saved, N = N, 128
    try:
        base = dict(layout="hex", rings=1, pitch_m=3e-3)
        cw = b.cw(LAM, 1.0)
        ref, _, _ = _tile_axis_fluence(tiled_array(0.5e-3, **base), cw)
        c = N // 2
        phi0 = np.random.default_rng(3).uniform(0, 2 * np.pi, 7)
        def metric(u):
            F2, _, _ = _tile_axis_fluence(tiled_array(0.5e-3, piston_rad=(phi0 + u).tolist(), **base), cw)
            return F2[c, c] / ref[c, c]
        start = metric(np.zeros(7))
        r = sensorless.spgd(metric, np.zeros(7), iterations=150, gain=10.0, amplitude=0.2, seed=1)
        assert start < 0.2 and r.history[-1] > 0.95, (start, r.history[-1])
    finally:
        N = n_saved


# ----------------------------------------------------------------------------------------------- closed-loop pulse shaping
def _peak_power(gdd_aopdf_fs2, chirp_fs2):
    sysd = b.system("s", [b.gap("g", 0.0, medium="Vacuum"), {"id": "chirp", "type": "aopdf", "gddFs2": chirp_fs2}, {"id": "shape", "type": "aopdf", "gddFs2": float(gdd_aopdf_fs2)}])
    res = uf.run(sysd, b.run_spec(b.pulse(800e-9, 30e-15, energy=1e-6)))
    return float(res.summary["output"]["peakPowerW"])


def test_closed_loop_shaping_finds_the_compensating_gdd_from_the_peak_power():
    """The pulse leaves a source with an unknown GDD (a first aopdf, +900 fs^2); modal search on the second aopdf's GDD with the output peak power as the metric
    finds -900 fs^2 (the compensation is exact: the peak power of a Gaussian pulse is maximal for zero total GDD), within 2 fs^2 = 0.2 % after six sweeps (bias 300 fs^2;
    measured -900.0 to the printed precision: the parabola vertex of a Lorentzian peak lands on the peak once the three points straddle it symmetrically)."""
    chirp = 900.0
    metric = lambda g: _peak_power(g[0], chirp)
    r = sensorless.modal_search(metric, np.array([0.0]), bias=300.0, sweeps=6)
    assert abs(r.x[0] + chirp) < 2.0, r.x
