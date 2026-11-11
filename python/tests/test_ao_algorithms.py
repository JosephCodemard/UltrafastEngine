"""AO algorithms that need no engine: holography, wavefront sensing and control, sensorless optimisers (reference values in each docstring)."""
import numpy as np
import pytest

from ultrafast.algorithms import control, holography, sensorless, zernike


# ----------------------------------------------------------------------------------------------- holography
def _ring_target(n=64, r0=14, w=2.0):
    y, x = np.indices((n, n)) - n // 2
    return np.exp(-((np.hypot(x, y) - r0) / w) ** 2)


def test_gs_error_does_not_increase():
    """Fienup, Appl. Opt. 21, 2758 (1982): the error-reduction algorithm never increases the Fourier-plane error (here: rms of |G| - T before each constraint)."""
    res = holography.gerchberg_saxton(_ring_target(), iterations=60, seed=3)
    e = np.array(res.error)
    assert np.all(np.diff(e) <= 1e-12 * e[0]), "GS error must be non-increasing"
    assert e[-1] < 0.5 * e[0]


def test_gs_recovers_a_known_phase_from_its_far_field_amplitude():
    """A hologram made by GS for the far-field amplitude of a known smooth phase reproduces that amplitude (the phase is recovered up to the stated
    ambiguities: constant, and the conjugate-reversal for a symmetric problem); the achieved rms amplitude error is below 2 % of the peak."""
    n = 64
    y, x = np.indices((n, n)) - n // 2
    true_phase = 3.0 * np.exp(-(x ** 2 + y ** 2) / 200.0) + 0.05 * x          # smooth, asymmetric
    T = np.abs(holography.focal_field(true_phase, np.ones((n, n))))
    res = holography.gerchberg_saxton(T, iterations=400, seed=5, initial_phase=true_phase + 0.4 * np.random.default_rng(0).normal(size=(n, n)))
    A = np.abs(res.field)
    assert np.sqrt(np.mean((A / np.linalg.norm(A) - T / np.linalg.norm(T)) ** 2)) < 0.02 * (T / np.linalg.norm(T)).max()
    d = np.angle(np.exp(1j * (res.phase - true_phase)))                       # wrapped difference; remove the unknown constant
    d = np.angle(np.exp(1j * (d - np.angle(np.exp(1j * d).mean()))))
    assert d.std() < 0.05, d.std()                                            # measured 0.018 rad rms


def test_wgs_equalises_a_spot_array_better_than_gs():
    """Di Leonardo et al., Opt. Express 15, 1913 (2007): weighting the spots equalises them. Uniformity (1 - (Imax - Imin)/(Imax + Imin)) is above 0.95 for WGS
    and better than plain GS (measured over seeds 0-9 on this 7-spot target: GS 0.82-0.86, WGS 0.996-0.999; seed 1 is used)."""
    n = 64
    mask = np.zeros((n, n)); pts = [(20, 20), (20, 44), (44, 20), (44, 44), (32, 32), (10, 32), (32, 54)]
    for p in pts:
        mask[p] = 1
    w = holography.weighted_gs(mask, iterations=40, seed=1)
    g = holography.gerchberg_saxton(mask, iterations=40, seed=1)
    gs_int = np.abs(g.field[mask > 0]) ** 2
    gs_uni = 1 - (gs_int.max() - gs_int.min()) / (gs_int.max() + gs_int.min())
    assert w.uniformity > 0.95
    assert w.uniformity > gs_uni


def test_gs_with_binary_quantisation_in_the_loop_beats_quantising_afterwards():
    """Quantisation-aware refinement helps where the quantisation is coarse: with 2 phase levels the final rms amplitude error is smaller than rounding a continuous
    hologram to 2 levels, on each of 6 seeds (measured on this ring target: 0.0084 against 0.0100 on average). At 4 or 8 levels it did NOT help on this target
    (0.0083 against 0.0081, 0.0064 against 0.0058): continuous rounding is already good there, so only the coarse case is asserted."""
    T = _ring_target(); Tn = T / np.linalg.norm(T)
    def err(ph):
        A = np.abs(holography.focal_field(ph)); A /= np.linalg.norm(A); return np.sqrt(np.mean((A - Tn) ** 2))
    for seed in range(6):
        late = holography.gerchberg_saxton(T, iterations=100, seed=seed)
        aware = holography.gerchberg_saxton(T, iterations=100, seed=seed, levels=2)
        assert err(aware.phase) < err(holography.quantise_phase(late.phase, 2)), seed


def test_mraf_cleans_the_signal_region():
    """MRAF (Pasienski and DeMarco 2008): with a free noise region the error inside the signal region is smaller than plain GS on the same target, at lower efficiency."""
    n = 64
    y, x = np.indices((n, n)) - n // 2
    sig = (np.abs(x) < 10) & (np.abs(y) < 10)
    T = np.where(sig, 1.0, 0.0)
    gs = holography.gerchberg_saxton(T, iterations=80, seed=4)
    mr = holography.mraf(T, sig, iterations=80, mixing=0.5, seed=4)
    # relative amplitude non-uniformity inside the signal region
    def nonuni(f):
        a = np.abs(f[sig]); return a.std() / a.mean()
    assert nonuni(mr.field) < nonuni(gs.field)
    assert mr.efficiency < 1.0


def test_grey_levels_invert_a_nonuniform_curve_and_match_the_engine_rule():
    levels = 64
    lut = 2 * np.pi * (0.5 + 0.5 * np.tanh(2 * (np.arange(levels) / levels - 0.5)) / np.tanh(1.0))
    assert np.array_equal(holography.grey_levels(lut + 1e-9, lut), np.arange(levels))
    assert holography.grey_levels(np.array([2 * np.pi - 1e-6]), lut)[0] == 0                # wraps to level 0
    assert np.allclose(holography.quantise_phase(np.array([0.1, 6.2]), 8), [0.0, 0.0])         # 6.2 rad rounds to the top half-step -> level 0


def test_grey_levels_use_the_circular_distance_when_the_curve_exceeds_two_pi():
    lut = 2.5 * np.pi * np.arange(101) / 100
    assert holography.grey_levels(np.array([2 * np.pi - 0.01]), lut)[0] in (0, 80)        # the same phase
    assert holography.grey_levels(np.array([0.45 * np.pi]), lut)[0] in (18, 98)
    assert holography.grey_levels(np.array([0.0]), lut)[0] in (0, 80)


def test_double_phase_encoding_reproduces_a_smooth_complex_field():
    """A e^{i phi} = (e^{i(phi + a)} + e^{i(phi - a)})/2 with a = arccos A: averaging the two checkerboard sublattices returns the field (exact for a locally constant field)."""
    n = 64
    y, x = np.indices((n, n))
    field = (0.2 + 0.7 * np.exp(-((x - 32) ** 2 + (y - 32) ** 2) / 300.0)) * np.exp(1j * 2.0 * np.exp(-((x - 32) ** 2) / 400.0))
    ph = holography.double_phase_encode(field)
    h = np.exp(1j * ph)
    avg = (h + np.roll(h, 1, 0) + np.roll(h, 1, 1) + np.roll(np.roll(h, 1, 0), 1, 1)) / 4 * 1.0     # 2 x 2 block average
    # the 2 x 2 average of a checkerboard is the mean of the two sublattice values: A e^{i phi} to O(gradient)
    assert np.sqrt(np.mean(np.abs(avg[2:-2, 2:-2] - field[2:-2, 2:-2]) ** 2)) < 0.03
    const = holography.double_phase_encode(np.full((8, 8), 0.6 * np.exp(0.7j)))
    hc = np.exp(1j * const)
    assert abs((hc[0, 0] + hc[0, 1]) / 2 - 0.6 * np.exp(0.7j)) < 1e-12


def test_bolduc_first_order_carries_the_complex_field():
    """Bolduc et al., Opt. Lett. 38, 3546 (2013): Psi = M mod(phi + 2 pi x / L, 2 pi) - pi (M - 1) with sinc(pi (M - 1)) = A has first diffraction order A e^{i phi}
    (its Fourier coefficient over one period, 256 samples per period)."""
    L = 256
    x = np.arange(L)
    for A, phi in [(1.0, 0.0), (0.8, 1.1), (0.5, -2.0), (0.2, 3.0)]:
        psi = holography.bolduc_encode(np.full((L, 1), A * np.exp(1j * phi)), period_pixels=L)[:, 0]
        c1 = np.mean(np.exp(1j * psi) * np.exp(-2j * np.pi * x / L))
        assert abs(c1 - A * np.exp(1j * phi)) < 5e-3, (A, phi, c1)


# ----------------------------------------------------------------------------------------------- sensing and control
def _field(n=96, half=3.0e-3, w=1.2e-3, tilt=0.0, coefs=None, wavelength=1030e-9, radius=2.0e-3):
    x = (np.arange(n) - (n - 1) / 2) * (2 * half / n)
    X, Y = np.meshgrid(x, x, indexing="ij")
    amp = np.exp(-(X ** 2 + Y ** 2) / w ** 2)
    W = np.zeros_like(X)
    if coefs is not None:
        for j, c in coefs.items():
            W += c * np.nan_to_num(zernike.evaluate_map([1.0], x, x, radius, first=j, outside=0.0))
    W += tilt * X
    return x, amp * np.exp(2j * np.pi / wavelength * W), W


def test_shack_hartmann_spot_shift_is_focal_length_times_the_wavefront_slope():
    """A wavefront tilt W = a x displaces every spot by f a (geometric optics): the slope map is a within the centroid's sampling error (1.5 %)."""
    for a in (0.5e-3, 1.0e-3, 2.0e-3):
        x, E, W = _field(tilt=a)
        s = control.shack_hartmann(E, x, x, wavelength_m=1030e-9, lenslet_pitch_m=6 * (x[1] - x[0]), focal_length_m=0.05)
        assert s.valid.sum() > 20
        assert np.allclose(s.slope_x[s.valid], a, rtol=1.5e-2), a           # measured worst case 1.2 % (centroid pixelisation, 4 FFT bins per lambda / D)
        assert np.allclose(s.slope_y[s.valid], 0.0, atol=1.5e-2 * a)


def test_modal_fit_recovers_zernike_coefficients_exactly_without_noise():
    """Noiseless slopes of a wavefront inside the basis give its Zernike coefficients (analytic slopes, least squares): 1e-9 of the coefficient."""
    radius = 2.0e-3
    x = np.linspace(-radius, radius, 33)
    X, Y = np.meshgrid(x, x, indexing="ij")
    coefs = {4: 120e-9, 5: -80e-9, 7: 60e-9, 11: 40e-9}            # metres rms: defocus, astigmatism, coma, spherical
    from ultrafast.algorithms.control import SlopeMap, modal_matrices
    valid = np.hypot(X, Y) <= 0.95 * radius
    sm = SlopeMap(np.zeros_like(X), np.zeros_like(X), x, x, valid)
    A, js = modal_matrices(sm, radius, 15)
    c = np.zeros(len(js)); [c.__setitem__(js.index(j), v) for j, v in coefs.items()]
    b = A @ c
    n = int(valid.sum())
    sm.slope_x[valid], sm.slope_y[valid] = b[:n], b[n:]
    got, _ = control.modal_fit(sm, radius, 15)
    assert np.allclose(got, c, atol=1e-9 * 120e-9, rtol=0)


def test_shack_hartmann_then_modal_fit_recovers_low_order_zernikes_end_to_end():
    """Field with defocus 150 nm, astigmatism -100 nm and 60 nm (rms, Noll 4, 5, 6) -> lenslet FFT + windowed centroid -> least-squares Zernike fit. Noll <= 6 have linear
    gradients, so the subaperture mean equals the centre value: the error is the centroid's (measured: 145.6, -96.3, 61.4 nm; tip and tilt below 1 nm): 8 nm tolerance (about 5 %)."""
    n, half, w, lam, radius = 96, 3e-3, 1.2e-3, 1030e-9, 2e-3
    xs = (np.arange(n) - (n - 1) / 2) * (2 * half / n)
    X, Y = np.meshgrid(xs, xs, indexing="ij")
    coefs = {4: 150e-9, 5: -100e-9, 6: 60e-9}
    W = sum(c * zernike.basis([j], xs, xs, radius)[0] for j, c in coefs.items())
    E = np.exp(-(X ** 2 + Y ** 2) / w ** 2) * np.exp(2j * np.pi / lam * W)
    s = control.shack_hartmann(E, xs, xs, wavelength_m=lam, lenslet_pitch_m=6 * (xs[1] - xs[0]), focal_length_m=0.05)
    c, js = control.modal_fit(s, radius, 6)
    got = dict(zip(js, c))
    for j, v in coefs.items():
        assert abs(got[j] - v) < 8e-9, (j, got[j], v)
    assert abs(got[2]) < 2e-9 and abs(got[3]) < 2e-9


def test_southwell_is_exact_for_a_quadratic_wavefront_and_converges_second_order():
    """Southwell's trapezoid equations are exact for a quadratic wavefront (the slope is linear, its average over a cell is the midpoint value): defocus + astigmatism + tilt
    reconstruct to 1e-12 of their rms. For a sinusoid the error falls as h^2: halving the lenslet pitch cuts it by 4 (3.6-4.2 accepted)."""
    def recon(n, field):
        h = 8e-3 / n; x = (np.arange(n) - (n - 1) / 2) * h
        X, Y = np.meshgrid(x, x, indexing="ij")
        W, sx, sy = field(X, Y)
        R = control.southwell_reconstructor(control.SlopeMap(sx, sy, x, x, np.ones((n, n), bool)))
        return np.sqrt(np.mean((R - (W - W.mean())) ** 2)), np.sqrt(np.mean((W - W.mean()) ** 2))
    quad = lambda X, Y: (1e-7 * (X ** 2 + 0.3 * X * Y - 0.6 * Y ** 2) / 1e-6 + 2e-4 * X, 1e-7 * (2 * X + 0.3 * Y) / 1e-6 + 2e-4, 1e-7 * (0.3 * X - 1.2 * Y) / 1e-6)
    e, r = recon(16, quad)
    assert e < 1e-12 * r
    k = 2 * np.pi / 6e-3
    sine = lambda X, Y: (50e-9 * np.sin(k * X) * np.cos(0.7 * k * Y), 50e-9 * k * np.cos(k * X) * np.cos(0.7 * k * Y), -50e-9 * 0.7 * k * np.sin(k * X) * np.sin(0.7 * k * Y))
    e1, _ = recon(16, sine); e2, _ = recon(32, sine)
    assert 3.6 < e1 / e2 < 4.2, e1 / e2


def test_interaction_and_control_matrices_correct_a_linear_system_in_one_shot():
    """For a linear actuator -> sensor map D, the poked interaction matrix is D to round-off and command = -G m cancels any aberration in its range;
    TSVD with fewer modes leaves exactly the removed modes."""
    rng = np.random.default_rng(3)
    D0 = rng.normal(size=(40, 12))
    meas = lambda c: D0 @ c
    D = control.interaction_matrix(meas, 12, poke=0.1)
    assert np.allclose(D, D0, atol=1e-12)
    G, s = control.control_matrix(D, method="tikhonov", regularisation=1e-12)
    c_true = rng.normal(size=12)
    m = D0 @ c_true
    cmd = control.correct(G, m)
    assert np.allclose(D0 @ cmd + m, 0, atol=1e-9)
    G2, _ = control.control_matrix(D, method="tsvd", n_modes=8)
    resid = D0 @ control.correct(G2, m) + m
    U, S, Vt = np.linalg.svd(D0, full_matrices=False)
    removed = U[:, 8:] @ (U[:, 8:].T @ m)
    assert np.allclose(resid, removed, atol=1e-9)


def test_integrator_loop_converges_geometrically_for_gain_below_two():
    """c[k+1] = c[k] - g G m[k] on a static aberration with G D = I: the residual is (1 - g)^k times the start (exact)."""
    D = np.eye(5)
    G, _ = control.control_matrix(D, regularisation=1e-14)
    dist = np.array([1.0, -2.0, 0.5, 0.3, 1.5])
    hist = control.integrator_loop(lambda c, k: D @ c + dist, G, 5, gain=0.5, steps=8)
    assert np.allclose(np.linalg.norm(hist, axis=1), 0.5 ** np.arange(8) * np.linalg.norm(dist), rtol=1e-6)


# ----------------------------------------------------------------------------------------------- sensorless
def test_spgd_phase_locks_an_array():
    """SPGD (Vorontsov and Sivokon 1998) on the coherent-sum metric of N = 16 channels with random piston: the normalised sum (1 = locked) exceeds 0.99 after 400
    iterations on each of 10 seeds (gain 10, amplitude 0.2 rad; measured minimum 1.000; gain 20 with this amplitude diverges, which the docs warn about)."""
    N = 16
    for seed in range(10):
        phi = np.random.default_rng(100 + seed).uniform(0, 2 * np.pi, N)
        metric = lambda u: abs(np.exp(1j * (phi + u)).sum()) ** 2 / N ** 2
        r = sensorless.spgd(metric, np.zeros(N), iterations=400, gain=10.0, amplitude=0.2, seed=seed)
        assert r.history[-1] > 0.99, seed


def _spgd_problems():
    """(name, metric factory(scale, seed), n, iterations, amplitude): phase locking of 16 and 7 channels (random piston) and a 6-mode Strehl-like peak exp(-2 |x - x*|^2)."""
    def lock(N, iters):
        def make(scale, seed):
            phi = np.random.default_rng(100 + seed).uniform(0, 2 * np.pi, N)
            return lambda u: scale * abs(np.exp(1j * (phi + u)).sum()) ** 2 / N ** 2
        return make, N, iters, 0.2
    xs = np.array([-0.25, 0.2, -0.15, 0.2, -0.1, 0.1])
    strehl = (lambda scale, seed: (lambda a: scale * np.exp(-2.0 * np.sum((a - xs) ** 2)))), 6, 120, 0.05
    return {"lock16": lock(16, 300), "lock7": lock(7, 300), "strehl6": strehl}


def test_spgd_default_is_scale_free():
    """The default step is normalised by the metric's own recent range, so J, 1e-3 J and 1e3 J give the same iterates (the classical fixed gain is in units of 1 / metric)."""
    make, n, iters, amp = _spgd_problems()["lock16"]
    ref = sensorless.spgd(make(1.0, 0), np.zeros(n), iterations=60, amplitude=amp, seed=1, keep_iterates=True)
    for scale in (1e-3, 1e3):
        r = sensorless.spgd(make(scale, 0), np.zeros(n), iterations=60, amplitude=amp, seed=1, keep_iterates=True)
        assert np.allclose(np.array(r.iterates), np.array(ref.iterates), rtol=1e-9, atol=1e-12), scale
    # the fixed gain is not: the same gain that locks J = 1 does nothing for 1e-3 J
    fixed = lambda sc: sensorless.spgd(make(sc, 0), np.zeros(n), iterations=300, gain=10.0, amplitude=amp, seed=1).history[-1] / sc
    assert fixed(1.0) > 0.99 and fixed(1e-3) < 0.1


def test_spgd_default_reaches_0p9_of_the_optimum_at_any_metric_scale():
    """Default SPGD (step 0.4 amplitude) on phase locking of 16 and 7 channels and a 6-mode Strehl peak, metric scales 1e-3, 1 and 1e3, 10 seeds each: the lowest final
    value over the optimum is at least 0.9 (measured minimum 0.98; the fixed gains that lock at scale 1 give 0.00 to 0.04 at 1e-3 and 1e3)."""
    for name, (make, n, iters, amp) in _spgd_problems().items():
        for scale in (1e-3, 1.0, 1e3):
            worst = min(sensorless.spgd(make(scale, seed), np.zeros(n), iterations=iters, amplitude=amp, seed=seed).history[-1] / scale for seed in range(10))
            assert worst > 0.9, (name, scale, worst)


def test_modal_search_is_exact_for_a_quadratic_separable_metric_in_one_sweep():
    """Parabola vertex from three points: exact for J = -sum_i a_i (x_i - x_i*)^2 in one sweep (2 N + 1 evaluations)."""
    a = np.array([1.0, 2.0, 0.5, 3.0]); xs = np.array([0.4, -0.7, 0.9, 0.1])
    J = lambda x: -float(np.sum(a * (x - xs) ** 2))
    r = sensorless.modal_search(J, np.zeros(4), bias=0.5, sweeps=1)
    assert np.allclose(r.x, xs, atol=1e-12)
    assert r.evaluations == 1 + 3 * 4


def test_hill_climb_reaches_the_optimum_of_a_bowl():
    xs = np.array([0.4, -0.7, 0.9])
    J = lambda x: -float(np.sum((x - xs) ** 2))
    r = sensorless.hill_climb(J, np.zeros(3), step=0.3, sweeps=40)
    assert np.allclose(r.x, xs, atol=2e-3)


def test_transmission_matrix_phases_are_exact_and_focusing_gains_pi_over_four_n():
    """Phase stepping from intensities only (mixed second difference): the phases of t_mk are recovered to 1e-9 rad for every output, and focusing on an output
    of a random medium (circular Gaussian t) gains on average (pi/4)(N - 1) + 1 (Vellekoop and Mosk, Opt. Lett. 32, 2309 (2007)); 3000 outputs, seed 1,
    standard error 0.07 % of the mean (tolerance 0.6 %, ~8 sigma)."""
    rng = np.random.default_rng(1)
    N, M = 64, 3000
    T = (rng.normal(size=(M, N)) + 1j * rng.normal(size=(M, N))) / np.sqrt(2)
    H = sensorless.transmission_matrix(lambda p: np.abs(T @ np.exp(1j * p)) ** 2, N)
    ref = T * np.exp(-1j * np.angle(T[:, :1]))
    assert np.abs(np.angle(H[:, 1:] / ref[:, 1:])).max() < 1e-9
    eta = np.array([abs(T[m] @ np.exp(1j * sensorless.focus_phases(H, m))) ** 2 / np.sum(np.abs(T[m]) ** 2) for m in range(M)])
    assert abs(eta.mean() / ((np.pi / 4) * (N - 1) + 1) - 1) < 0.006
