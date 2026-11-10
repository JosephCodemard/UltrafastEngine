"""Zernike polynomials (Noll), the decomposition figure and the animation writer."""
import math

import numpy as np
import pytest

from conftest import requires_engine

import ultrafast as uf
from ultrafast import build as b
from ultrafast.algorithms import zernike as zk


def test_noll_table_first_eleven_terms():
    expect = {1: (0, 0), 2: (1, 1), 3: (1, -1), 4: (2, 0), 5: (2, -2), 6: (2, 2), 7: (3, -1), 8: (3, 1), 9: (3, -3), 10: (3, 3), 11: (4, 0)}
    assert {j: zk.noll_to_nm(j) for j in expect} == expect


def test_orthonormal_over_the_unit_disc_by_polar_quadrature():
    # analytic: (1/pi) integral Z_i Z_j dA = delta_ij. Gauss-Legendre in rho (weight rho) x uniform theta is exact for these polynomials.
    g, w = np.polynomial.legendre.leggauss(40)
    rho, wr = 0.5 * (g + 1), 0.5 * w
    nt = 64
    th = 2 * np.pi * (np.arange(nt) + 0.5) / nt
    R, T = np.meshgrid(rho, th, indexing="ij")
    W = (wr * rho)[:, None] * (1.0 / nt) * 2.0 * np.ones((1, nt))  # integral Z Z rho drho dtheta / pi
    js = range(1, 22)
    Z = np.stack([zk.value(j, R, T) for j in js])
    G = np.einsum("irt,jrt,rt->ij", Z, Z, W)
    assert np.abs(G - np.eye(len(Z))).max() < 1e-12


def test_fit_recovers_coefficients_and_rms_is_their_quadrature_sum():
    x = np.linspace(-1e-3, 1e-3, 161)
    c = np.zeros(15)
    c[3], c[5], c[7], c[10] = 0.25, 0.1, -0.08, 0.05
    w = zk.evaluate_map(c, x, x, 1e-3)
    got, resid = zk.fit(w, x, x, 1e-3, max_noll=15, return_residual=True)
    assert np.abs(got - c).max() < 1e-12 and np.nanmax(np.abs(resid)) < 1e-12
    assert zk.rms(c) == pytest.approx(math.sqrt(0.25 ** 2 + 0.1 ** 2 + 0.08 ** 2 + 0.05 ** 2), rel=1e-12)
    assert zk.strehl_marechal(0.05) == pytest.approx(math.exp(-(2 * math.pi * 0.05) ** 2))


def test_fit_skips_nan_points_and_an_off_centre_pupil():
    x = np.linspace(-2e-3, 2e-3, 201)
    c = np.zeros(11); c[3] = 0.2; c[6] = 0.07
    w = zk.evaluate_map(c, x, x, 1e-3, centre=(0.3e-3, -0.2e-3))
    w[:40, :] = np.nan
    got = zk.fit(w, x, x, 1e-3, max_noll=11, centre=(0.3e-3, -0.2e-3))
    assert np.abs(got - c).max() < 1e-10


def test_decomposition_figure_reports_rms_and_strehl_for_one_and_two_wavefronts():
    import matplotlib
    matplotlib.use("Agg")
    from ultrafast.plotting import zernike as zp
    x = np.linspace(-1e-3, 1e-3, 97)
    c = np.zeros(15); c[3] = 0.2; c[7] = 0.1
    before = (zk.evaluate_map(c, x, x, 1e-3), x, x)
    after = (zk.evaluate_map(0.2 * c, x, x, 1e-3), x, x)
    fig = zp.decomposition(before, radius=1e-3)
    assert fig.zernike[0]["rms_waves"] == pytest.approx(math.hypot(0.2, 0.1), rel=1e-9)
    assert fig.zernike[0]["strehl"] == pytest.approx(zk.strehl_marechal(math.hypot(0.2, 0.1)))
    fig2 = zp.decomposition([before, after], ["before", "after"], radius=1e-3, remove_tilt=True)
    assert [r["label"] for r in fig2.zernike] == ["before", "after"]
    assert fig2.zernike[1]["rms_waves"] < fig2.zernike[0]["rms_waves"]
    # a complex field: the phase is the wavefront
    X, Y = np.meshgrid(x, x, indexing="ij")
    field = np.exp(2j * np.pi * zk.evaluate_map(c, x, x, 1e-3, outside=0.0)) * (np.hypot(X, Y) <= 1e-3)
    fig3 = zp.decomposition(field, x=x, y=x, wavelength=800e-9, radius=1e-3)
    assert fig3.zernike[0]["rms_waves"] == pytest.approx(math.hypot(0.2, 0.1), rel=0.02)


@requires_engine
def test_python_fit_agrees_with_the_engines_wavefront_sensor():
    """The same Noll convention: Zernike terms put on a beam with the ``wavefront`` element come back from the engine's sensor and from
    :func:`zernike.fit` of the sensor's own map with the same coefficients (the terms j >= 4 do not depend on the piston and tilt
    that the sensor removes from the map)."""
    lam, R = 800e-9, 1.5e-3
    terms = [0, 0, 0, 0.10, 0.0, 0.08, 0.0, -0.05, 0.0, 0.0, 0.04]            # Noll 1..11, waves rms
    grid = dict(nx=128, ny=128, half_width_m=3e-3, spectral_samples=2, absorbing_boundary=True)
    sys_ = b.system("wfs", [
        b.gap("source", 0.0, medium="Vacuum", transverse=b.transverse("cartesian", **grid, initial_profile=b.initial_profile("gaussian", radius_m=0.9e-3))),
        b.wavefront("ab", zernike_noll_waves=terms, radius_m=R, referenceWavelengthM=lam),
        b.gap("out", 1e-6, medium="Vacuum", transverse=b.transverse("cartesian", **grid)),
    ])
    res = uf.run(sys_, b.run_spec(b.pulse(lam, 100e-15, energy=1e-6), instruments=[
        b.instrument("wavefrontSensor", at="end", name="wfs", pixels=64, apertureRadiusM=R, zernikeTerms=11, halfWidthM=R)]))
    s = res.instrument("wfs")
    engine = np.array(s["zernike_waves"])
    w, x, y = np.array(s["wavefront_waves"]), np.array(s["x_m"]), np.array(s["y_m"])
    mine = zk.fit(w, x, y, R, max_noll=11)
    j = slice(3, 11)    # Noll 4..11
    assert np.abs(mine[j] - engine[j]).max() < 1e-6, (mine[j], engine[j])
    assert np.abs(engine[j] - np.array(terms)[j]).max() < 5e-3     # the sensor recovers what was put on (pixelisation, 1 % threshold)


def test_gif_has_the_frames_size_loop_and_a_held_last_frame(tmp_path):
    pytest.importorskip("matplotlib")
    pytest.importorskip("PIL")
    import matplotlib
    matplotlib.use("Agg")
    from PIL import Image
    from ultrafast.plotting import animate as an
    from ultrafast.plotting.style import _figure

    def frame(k):
        fig = _figure(3.0, 2.0)
        ax = fig.add_subplot()
        ax.plot([0, 1], [0, k]); ax.set_ylim(0, 5)           # a different line in each frame, so no two frames are equal
        return fig

    out = an.animate(frame, range(1, 6), tmp_path / "a.gif", fps=10, dpi=40, pause_last_s=1.0, progress_bar=True)
    assert out["frames"] == 5 and out["mp4"] is None and not out["overBudget"]
    with Image.open(out["gif"]) as im:
        assert im.n_frames == 5 and im.size == (120, 80) and im.info.get("loop") == 0
        durs = []
        for i in range(im.n_frames):
            im.seek(i)
            durs.append(im.info["duration"])
    assert durs[:-1] == [100] * 4 and durs[-1] == 1000


def test_budget_check_flags_big_files(tmp_path):
    from ultrafast.plotting.animate import check_budget
    (tmp_path / "small.png").write_bytes(b"x" * 1000)
    (tmp_path / "big.png").write_bytes(b"x" * 400_000)
    (tmp_path / "big.gif").write_bytes(b"x" * 2_600_000)
    flagged = {p.name for p, _, _ in check_budget(tmp_path)}
    assert flagged == {"big.png", "big.gif"}


def test_animate_decomposition_writes_a_gif(tmp_path):
    pytest.importorskip("PIL")
    import matplotlib
    matplotlib.use("Agg")
    from PIL import Image
    from ultrafast.plotting import zernike as zp
    x = np.linspace(-1e-3, 1e-3, 65)
    c = np.zeros(15); c[3] = 0.3; c[7] = 0.1
    steps = [(zk.evaluate_map(f * c, x, x, 1e-3), x, x) for f in (1, 0.6, 0.3, 0.1)]
    out = zp.animate_decomposition(steps, tmp_path / "z.gif", radius=1e-3, fps=4, dpi=40)
    with Image.open(out["gif"]) as im:
        assert im.n_frames == 4
