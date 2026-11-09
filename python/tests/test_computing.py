"""The Computing examples (docs/examples/Computing): a lens at critical sampling is the DFT, a 4f system with a filter is the
convolution theorem, and each example's own checks pass."""
import importlib
import sys
from pathlib import Path

import numpy as np
import pytest

from conftest import requires_engine

COMPUTING = Path(__file__).resolve().parents[2] / "docs" / "examples" / "Computing"


@pytest.fixture(scope="module")
def optics(tmp_path_factory):
    sys.path.insert(0, str(COMPUTING))
    import _optics
    yield _optics
    sys.path.remove(str(COMPUTING))


def _example(name, module):
    sys.path.insert(0, str(COMPUTING))
    sys.path.insert(0, str(COMPUTING / name))
    try:
        return importlib.import_module(module)
    finally:
        sys.path.remove(str(COMPUTING / name))
        sys.path.remove(str(COMPUTING))


def test_dft_matrix_and_the_numpy_twin_agree_with_fft(optics):
    n = 16
    x = np.random.default_rng(1).random(n) + 1j * np.random.default_rng(2).random(n)
    # the matrix has its origin at index n/2 on both sides
    assert np.allclose(optics.dft_matrix(n) @ x, np.fft.fftshift(np.fft.fft(np.fft.ifftshift(x))), atol=1e-12)
    # no filter: the twin of a 4f system is the object inverted through the origin
    field = np.zeros((n, n), complex); field[3, 5] = 1.0
    out = optics.reference_4f(field, None)
    assert out[n - 3, n - 5] == pytest.approx(1.0) and out.sum() == pytest.approx(1.0)
    assert np.allclose(optics.upright(out), np.abs(field) ** 2, atol=1e-12)


@requires_engine
def test_a_lens_at_critical_sampling_is_the_dft_matrix(optics, tmp_path):
    """f = N dx^2 / lambda: pixels of the Fourier plane are the DFT bins (Goodman, Introduction to Fourier Optics, ch. 5)."""
    s = optics.Setup(n=128, dx=20e-6, wavelength=800e-9, ny=4)
    x = np.zeros(s.n, complex)
    x[40:50] = 1.0
    x[90] = 2j
    sf = optics.save_scene(tmp_path / "v.npz", s, np.tile(x[:, None], (1, s.ny_)))
    img = optics.run_intensity(optics.system_lens(s, str(sf)), s)[:, s.ny_ // 2]
    ref = np.abs(optics.dft_matrix(s.n) @ x) ** 2
    assert np.corrcoef(img, ref)[0, 1] > 0.9999
    assert np.linalg.norm(img / img.sum() - ref / ref.sum()) / np.linalg.norm(ref / ref.sum()) < 0.02


@requires_engine
def test_a_shifting_filter_moves_the_image_by_the_shift_theorem(optics, tmp_path):
    """H = exp(-2 pi i fx a) translates the (inverted) image by a: here three pixels along x."""
    s = optics.Setup(n=128, dx=20e-6, wavelength=800e-9)
    X, Y = np.meshgrid(s.x, s.x, indexing="ij")
    scene = np.exp(-((X + 0.2e-3) ** 2 + Y ** 2) / (2 * (0.15e-3) ** 2)).astype(complex)
    sf = optics.save_scene(tmp_path / "s.npz", s, scene)
    FX, FY = optics.fourier_axes(s)
    H = np.exp(-2j * np.pi * FX * 3 * s.dx)
    optics.save_filter(tmp_path / "h.npz", s, H)
    img = optics.upright(optics.run_intensity(optics.system_4f(s, str(sf), str(tmp_path / "h.npz")), s))
    plain = optics.upright(optics.run_intensity(optics.system_4f(s, str(sf), None), s))
    cx = lambda m: (m.sum(1) * np.arange(s.n)).sum() / m.sum()
    assert abs(abs(cx(img) - cx(plain)) - 3.0) < 0.05


@requires_engine
@pytest.mark.parametrize("folder, module", [("4f-correlator", "correlator"), ("optical-convolution", "convolution"),
                                             ("matrix-vector", "matrix_vector")])
def test_the_examples_pass_their_own_checks(folder, module, capsys):
    mod = _example(folder, module)
    mod.main()
    assert "checks passed" in capsys.readouterr().out


@requires_engine
def test_scaling_example_small(capsys):
    mod = _example("parallelism-and-time", "scaling")
    rows = mod.main(["--max", "256"])
    assert [r["n"] for r in rows] == [64, 128, 256] and all(r["errorL2"] < 0.05 for r in rows)
    assert rows[1]["transitS"] == pytest.approx(2 * rows[0]["transitS"])      # f = N dx^2 / lambda: the path doubles with N
