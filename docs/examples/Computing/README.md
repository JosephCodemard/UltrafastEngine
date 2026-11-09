# Computing

What an optical system does in parallel, and what that costs. Each example builds the processor from elements the engine already has
(`lens`, `gap`, a `mask` read from a file, a cartesian grid, the `beamProfile` instrument) and checks the output against the same
operation in numpy.

| example | what | reference |
|---|---|---|
| [4f-correlator](4f-correlator/README.md) | find a pattern in a scene in one pass: a matched filter in the Fourier plane | correlation theorem; peak positions and heights against numpy |
| [optical-convolution](optical-convolution/README.md) | the same layout as a convolution engine: blur, edge detection, motion blur | convolution theorem |
| [matrix-vector](matrix-vector/README.md) | y = A x by light: a lens is the DFT matrix, a 4f system a circulant matrix | `A @ x` |
| [parallelism-and-time](parallelism-and-time/README.md) | engine wall time, digital FFT time and the time light needs, for N from 64 to 1024 | the scaling of each |

**The one rule they share.** On a grid of N x N points with pitch dx, a thin lens followed by a gap of its focal length is the discrete
Fourier transform when the Fourier-plane pixels are the DFT bins. That fixes the focal length: **f = N dx² / λ** (critical sampling).
Then `lens + gap(f)` is exactly `np.fft.fft2`, and a 4f system with a filter H in the middle is `flip(ifft2(fft2(object) * H))`: the
object convolved with the impulse response of H, inverted through the origin. `_optics.py` holds this layout and its numpy twin.

**What these examples do not claim.** The engine *simulates* the light on a digital machine. Its wall time is the cost of that
simulation, not the time light needs. The "optical time" in `parallelism-and-time` is a model (path length over c, plus an assumed
sensor exposure), and a real processor also pays for loading the scene and the filter and for reading the result. The comparison is
about how the cost scales with N.

A general matrix-vector multiplier (Goodman's layout) needs cylindrical lenses; the engine's `lens` is round, so only the
shift-invariant matrices are shown.

Run everything: `python docs/examples/make_all.py --group Computing` (about 15 s). Each example commits one hero figure (`hero.png`)
and one animation (`animation.gif`) next to its README.
