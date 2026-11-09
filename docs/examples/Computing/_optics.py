"""Shared by the Computing examples (not an example): a 4f / single-lens processor built from existing elements, and its numpy twin.

The engine propagates on a cartesian grid of N x N points with pitch dx, x_i = (i - N/2) dx. A thin lens followed by a gap of its
focal length f is the discrete Fourier transform when the Fourier-plane pixels are the DFT bins: f = N dx^2 / lambda ("critical
sampling", :func:`focal_length`). Then

* one lens (object against the lens, screen at f) gives the DFT of the object (:func:`dft_matrix` is its matrix);
* a 4f system (gap f, lens, gap f, **filter**, gap f, lens, gap f) gives ``flip( IFFT( FFT(object) * H ) )``: the object convolved
  with the impulse response of the filter H, and flipped through the origin because two Fourier transforms in a row invert the image.

:func:`reference_4f` is the same operation in numpy, in the engine's orientation, so the two can be compared pixel by pixel.
The engine *simulates* the optics on a digital machine: its wall time is not the time light needs (see parallelism-and-time).
"""
from __future__ import annotations

import os
from dataclasses import dataclass

import numpy as np

import ultrafast as uf
from ultrafast import build as b

C = 299792458.0


@dataclass
class Setup:
    n: int = 256                 # grid points per axis (x and y)
    dx: float = 20e-6            # pixel pitch (m)
    wavelength: float = 800e-9   # m
    ny: int = 0                  # 0: square (n x n); a small number (e.g. 4): a "1-D" system (the field is constant along y)

    @property
    def ny_(self) -> int:
        return self.ny or self.n

    @property
    def focal_length(self) -> float:
        """Critical sampling: Fourier-plane pixels are the DFT bins."""
        return self.n * self.dx ** 2 / self.wavelength

    @property
    def x(self) -> np.ndarray:
        return (np.arange(self.n) - self.n / 2) * self.dx

    @property
    def y(self) -> np.ndarray:
        return (np.arange(self.ny_) - self.ny_ / 2) * self.dx

    def transverse(self, initial_profile=None):
        # halfWidthYM keeps the y pitch equal to dx for a thin ("1-D") grid of a few rows
        return b.transverse("cartesian", nx=self.n, ny=self.ny_, half_width_m=self.n * self.dx / 2, spectral_samples=1,
                            initial_profile=initial_profile, halfWidthYM=self.ny_ * self.dx / 2)


def save_scene(path, setup: Setup, field: np.ndarray) -> str:
    """The object as an NPZ map (amplitude and phase_rad) for ``initial_profile(kind="measured")``."""
    field = np.asarray(field)
    amp = np.abs(field)
    np.savez(path, x_m=setup.x, y_m=setup.y, amplitude=amp, phase_rad=np.angle(field))
    return str(path)


def save_filter(path, setup: Setup, H: np.ndarray) -> float:
    """The Fourier-plane filter H (complex, FFT-ordered like ``np.fft.fft2`` output) as a ``mask`` file. Returns the scale that was divided out
    (a passive mask cannot amplify: the largest |H| becomes 1)."""
    Hs = np.fft.fftshift(H)                      # plane coordinates: origin at index N/2
    scale = float(np.abs(Hs).max())
    Hs = Hs / scale
    np.savez(path, x_m=setup.x, y_m=setup.y, amplitude=np.abs(Hs), phase_rad=np.angle(Hs))
    return scale


def system_4f(setup: Setup, scene_file: str, filter_file=None):
    f, lam = setup.focal_length, setup.wavelength
    g = lambda name: b.gap(name, f, medium="Vacuum", transverse=setup.transverse())
    els = [b.gap("object", 0.0, medium="Vacuum", transverse=setup.transverse(b.initial_profile("measured", file=scene_file))),
           g("to_lens1"), b.lens("lens1", f), g("to_fourier_plane")]
    if filter_file is not None:
        els.append(b.mask("filter", "file", file=filter_file, design_wavelength_m=lam))
    els += [g("to_lens2"), b.lens("lens2", f), g("to_image")]
    return b.system("4f", els)


def system_lens(setup: Setup, scene_file: str):
    f = setup.focal_length
    return b.system("lens-dft", [
        b.gap("object", 0.0, medium="Vacuum", transverse=setup.transverse(b.initial_profile("measured", file=scene_file))),
        b.lens("lens", f), b.gap("to_fourier_plane", f, medium="Vacuum", transverse=setup.transverse())])


def run_intensity(system, setup: Setup, power_w: float = 1.0) -> np.ndarray:
    """The intensity (fluence of the cw source) at the end of the system, as a map [x][y]."""
    res = uf.run(system, b.run_spec(b.cw(setup.wavelength, power_w),
                                    instruments=[b.instrument("beamProfile", at="end", name="out")]))
    return np.array(res.instrument("out")["fluence_map_j_m2"])


def reference_4f(field: np.ndarray, H: np.ndarray | None) -> np.ndarray:
    """|flip(IFFT(FFT(field) * H))|^2 in the engine's orientation (origin at index N/2, FFT-ordered H)."""
    s = np.fft.ifftshift(field)
    out = np.fft.ifft2(np.fft.fft2(s) * (H if H is not None else 1.0))
    # two forward transforms in a row: the image is inverted through the origin
    inv = np.roll(out[::-1, ::-1], (1, 1), (0, 1))
    return np.abs(np.fft.fftshift(inv)) ** 2


def dft_matrix(n: int) -> np.ndarray:
    """The matrix the lens applies: y = A x with A[k, j] = exp(-2 pi i (k - n/2)(j - n/2) / n), origin at index n/2 on both sides."""
    idx = np.arange(n) - n // 2
    return np.exp(-2j * np.pi * np.outer(idx, idx) / n)


def match(a: np.ndarray, ref: np.ndarray) -> dict:
    """How well two intensity maps agree: correlation coefficient and the relative l2 error after scaling to the same total."""
    a = np.asarray(a, float)
    ref = np.asarray(ref, float)
    an, rn = a / a.sum(), ref / ref.sum()
    return {"correlation": float(np.corrcoef(a.ravel(), ref.ravel())[0, 1]),
            "relativeL2": float(np.linalg.norm(an - rn) / np.linalg.norm(rn)),
            "maxDifferenceOverMax": float(np.abs(an - rn).max() / rn.max())}


def upright(image: np.ndarray) -> np.ndarray:
    """Undo the inversion of a 4f image (flip through the origin at index N/2), so pixels line up with the object's."""
    return np.roll(image[::-1, ::-1], (1, 1), (0, 1))


def find_peaks(image: np.ndarray, count: int, exclude: int = 12):
    """The ``count`` highest local maxima as ``(i, j, value)``, each blanking a square of +-``exclude`` pixels around it."""
    m = np.array(image, float)
    out = []
    for _ in range(count):
        i, j = np.unravel_index(m.argmax(), m.shape)
        out.append((int(i), int(j), float(m[i, j])))
        m[max(0, i - exclude):i + exclude + 1, max(0, j - exclude):j + exclude + 1] = -np.inf
    return out


def workdir(example_file: str) -> str:
    """A ``results/`` folder next to the example (git-ignored) for the NPZ maps the engine reads."""
    d = os.path.join(os.path.dirname(os.path.abspath(example_file)), "results")
    os.makedirs(d, exist_ok=True)
    return d


def fourier_axes(setup: Setup):
    """FFT-ordered spatial frequencies (cycles / m) of the grid, as 2-D arrays FX, FY."""
    fx = np.fft.fftfreq(setup.n, setup.dx)
    fy = np.fft.fftfreq(setup.ny_, setup.dx)
    return np.meshgrid(fx, fy, indexing="ij")
