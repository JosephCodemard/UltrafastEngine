"""Cross-check of the spatial engine against LightPipes (an independent Fresnel propagator).

LightPipes is a development-time check only: it is never a dependency and is never shipped. Install it in a private
virtual environment (``pip install LightPipes``); these tests skip without it.

The comparison is the total variation distance between the normalised fluence maps, 0.5 sum |a - b|: the fraction of the
light that is in the wrong place. Propagated fluence depends on the phase as well, so a phase error shows up here.

Fields are written with explicit exp(+i k z) formulas where LightPipes' own helpers use another convention: its ``Tilt`` and
``Zernike`` have the opposite sign for some terms, and the engine matches the explicit formulas.
"""
import math

import numpy as np
import pytest

from conftest import requires_engine

L = pytest.importorskip("LightPipes")

import ultrafast as uf  # noqa: E402
from ultrafast import build as b  # noqa: E402

pytestmark = requires_engine

LAM, N, SIZE = 1030e-9, 256, 16e-3
K = 2 * math.pi / LAM
X = (np.arange(N) - N // 2) * (SIZE / N)
XX, YY = np.meshgrid(X, X)      # LightPipes arrays are [y][x]


def grid(profile=None, absorbing=None):
    return b.transverse("cartesian", nx=N, ny=N, half_width_m=SIZE / 2, spectral_samples=2, initial_profile=profile, absorbing_boundary=absorbing)


def source(**profile):
    kind = profile.pop("kind", "gaussian")
    return b.gap("source", 0.0, medium="Vacuum", transverse=grid(b.initial_profile(kind, **profile)))


def screen(z, id="screen", medium="Vacuum"):
    return b.gap(id, z, medium=medium, transverse=grid())


def fluence(elements, branched=False):
    system = b.system("x", b.Branched(elements) if branched else elements)
    spec = b.run_spec(b.pulse(LAM, 1e-12, energy=1e-3), instruments=[b.instrument("beamProfile", at="end", name="p")])
    res = uf.run(system, spec)
    return res, np.array(res.instrument("p")["fluence_map_j_m2"]).reshape(N, N)


def begin(w, wavelength=LAM):
    return L.GaussBeam(L.Begin(SIZE, wavelength, N), w)


def distance(engine, field):
    other = np.array(L.Intensity(field, 0)).T      # to [x][y]
    return 0.5 * np.abs(engine / engine.sum() - other / other.sum()).sum()


def phase(field, phi):
    return L.MultPhase(field, phi)


def noll(j, rho, th):
    return {4: 3 ** .5 * (2 * rho ** 2 - 1), 5: 6 ** .5 * rho ** 2 * np.sin(2 * th), 6: 6 ** .5 * rho ** 2 * np.cos(2 * th),
            7: 8 ** .5 * (3 * rho ** 3 - 2 * rho) * np.sin(th), 8: 8 ** .5 * (3 * rho ** 3 - 2 * rho) * np.cos(th),
            9: 8 ** .5 * rho ** 3 * np.sin(3 * th), 10: 8 ** .5 * rho ** 3 * np.cos(3 * th), 11: 5 ** .5 * (6 * rho ** 4 - 6 * rho ** 2 + 1)}[j]


def test_free_space_and_lens():
    _, f = fluence([source(radius_m=1e-3), screen(1.0)])
    assert distance(f, L.Forvard(begin(1e-3), 1.0)) < 1e-3
    _, f = fluence([source(radius_m=1e-3), b.lens("l", 0.5, medium="Vacuum"), screen(0.3)])
    assert distance(f, L.Forvard(L.Lens(begin(1e-3), 0.5), 0.3)) < 1e-3


def test_curved_mirror_is_a_lens_of_half_its_radius():
    _, f = fluence([source(radius_m=1e-3), b.mirror("m", radiusOfCurvatureM=1.0), screen(0.3)])
    assert distance(f, L.Forvard(L.Lens(begin(1e-3), 0.5), 0.3)) < 2e-3


def test_cylindrical_lens():
    _, f = fluence([source(radius_m=1e-3), {"id": "l", "type": "lens", "focalLengthM": 1.0, "focalLengthXM": 0.5, "focalLengthYM": 1e12}, screen(0.3)])
    assert distance(f, L.Forvard(L.CylindricalLens(begin(1e-3), 0.5), 0.3)) < 2e-3


def test_tilt_moves_the_beam_towards_positive_x():
    _, f = fluence([source(radius_m=1e-3, tiltXRad=1e-3, tiltYRad=-5e-4), screen(1.0)])
    assert distance(f, L.Forvard(phase(begin(1e-3), K * (1e-3 * XX - 5e-4 * YY)), 1.0)) < 1e-3
    assert X[np.unravel_index(f.argmax(), f.shape)[0]] == pytest.approx(1e-3, abs=SIZE / N)


def test_decentred_beam_on_a_lens():
    _, f = fluence([source(radius_m=1e-3, decenterXM=1e-3, decenterYM=-0.5e-3), b.lens("l", 1.0, medium="Vacuum"), screen(0.5)])
    assert distance(f, L.Forvard(L.Lens(L.GaussBeam(L.Begin(SIZE, LAM, N), 1e-3, x_shift=1e-3, y_shift=-0.5e-3), 1.0), 0.5)) < 5e-3


def test_hermite_gauss_mode():
    _, f = fluence([source(kind="hermiteGauss", radius_m=1e-3, mode_x=2, mode_y=1), screen(1.0)])
    assert distance(f, L.Forvard(L.GaussHermite(L.Begin(SIZE, LAM, N), 1e-3, m=2, n=1), 1.0)) < 1e-3


@pytest.mark.parametrize("j", [4, 5, 6, 7, 8, 9, 10, 11])
def test_zernike_terms(j):
    # the engine's polynomial continues beyond radius_m, so the phase here does too
    r0 = 3e-3
    phi = 0.3 * 2 * math.pi * noll(j, np.hypot(XX, YY) / r0, np.arctan2(YY, XX))
    _, f = fluence([source(radius_m=2e-3), b.wavefront("z", [0.0] * (j - 1) + [0.3], radius_m=r0), screen(2.0)])
    assert distance(f, L.Forvard(phase(begin(2e-3), phi), 2.0)) < 0.015


def test_vortex_is_right_handed():
    # a decentred window makes the pattern depend on the sign of the charge
    _, f = fluence([source(radius_m=1e-3), b.mask("v", "vortex", charge=1), b.aperture("w", half_width_x_m=1.5e-3, half_width_y_m=0.7e-3, decenter_x_m=0.4e-3), screen(0.8)])
    far = L.Forvard(L.RectAperture(phase(begin(1e-3), np.arctan2(YY, XX)), 3e-3, 1.4e-3, 0.4e-3, 0), 0.8)
    assert distance(f, far) < 0.03


def test_axicon():
    th = 1e-3
    _, f = fluence([source(radius_m=2e-3), b.mask("x", "axicon", cone_angle_rad=th), screen(0.5)])
    assert distance(f, L.Forvard(phase(begin(2e-3), -K * np.hypot(XX, YY) * math.sin(th)), 0.5)) < 2e-3


def test_iris_and_decentred_rectangle():
    # hard edges: LightPipes' 0/1 mask and the engine's area-weighted mask differ by about a cell of edge light, so this limit is
    # the grid's (0.02 at N = 512); the exact Fresnel integral is the engine's reference for these (test_diffraction.py, example 30)
    _, f = fluence([source(radius_m=2e-3), b.aperture("a", radius_m=1e-3), screen(1.0)])
    assert distance(f, L.Forvard(L.CircAperture(begin(2e-3), 1e-3), 1.0)) < 0.06
    _, f = fluence([source(radius_m=2e-3), b.aperture("a", half_width_x_m=1e-3, half_width_y_m=0.5e-3, decenter_x_m=0.7e-3, decenter_y_m=-0.3e-3), screen(0.5)])
    assert distance(f, L.Forvard(L.RectAperture(begin(2e-3), 2e-3, 1e-3, 0.7e-3, -0.3e-3), 0.5)) < 0.06


def _slits(field, count, width, period):
    open_ = np.zeros_like(XX)
    for k in range(count):
        open_ = np.maximum(open_, np.abs(XX - (k - (count - 1) / 2) * period) <= width / 2)
    return L.MultIntensity(field, open_)


def test_young_double_slit():
    # 0/1 slits on 62 um cells against area-weighted ones; the exact integral is the reference for the engine (test_diffraction.py)
    width, separation = 0.6e-3, 2e-3
    _, f = fluence([source(radius_m=3e-3), b.mask("s", "slits", count=2, width_m=width, separation_m=separation), screen(1.0)])
    assert distance(f, L.Forvard(_slits(begin(3e-3), 2, width, separation), 1.0)) < 0.1


def test_talbot_self_image():
    # a grating of period p images itself at z = 2 p^2 / lambda
    period, count = 0.64e-3, 15
    z = 2 * period ** 2 / LAM
    _, f = fluence([source(radius_m=4e-3), b.mask("s", "slits", count=count, width_m=0.32e-3, separation_m=period), screen(z)])
    assert distance(f, L.Forvard(_slits(begin(4e-3), count, 0.32e-3, period), z)) < 0.1
    cut = f[:, N // 2]
    spectrum = np.abs(np.fft.rfft(cut - cut.mean()))
    assert spectrum[2:].argmax() + 2 == pytest.approx(SIZE / period, abs=1.5), "the period survives at the Talbot distance"


@pytest.mark.parametrize("medium,bulk", [("FusedSilica", False), ("FusedSilica", True)])
def test_diffraction_in_glass_uses_the_wavelength_in_the_medium(medium, bulk):
    um = LAM * 1e6
    n = math.sqrt(1 + .6961663 * um ** 2 / (um ** 2 - .0684043 ** 2) + .4079426 * um ** 2 / (um ** 2 - .1162414 ** 2) + .8974794 * um ** 2 / (um ** 2 - 9.896161 ** 2))
    last = b.bulk("screen", medium, 1.0, nonlinear=False, transverse=grid()) if bulk else screen(1.0, medium=medium)
    _, f = fluence([source(radius_m=2e-3), b.aperture("a", radius_m=1e-3), last])
    assert distance(f, L.Forvard(L.CircAperture(begin(2e-3, LAM / n), 1e-3), 1.0)) < 0.06


@pytest.mark.parametrize("tilt,cycles", [(5e-4, 0.0), (5e-4, 0.25), (5e-4, 0.125), (0.0, 1 / 3)])
def test_branched_mach_zehnder_matches_beam_mix(tilt, cycles):
    # port c of the second splitter: t t a - r r b, so the arms subtract. cycles = delay in optical periods, which fixes the fringe's sign
    tau = cycles * LAM / 299792458.0
    arm = b.delay("b_", delay_s=tau, tilt_x_rad=tilt if tilt else None)
    _, f = fluence([b.link(source(radius_m=2e-3), "bs1"), b.link(b.beam_splitter("bs1"), {"c": "a", "d": "b_"}), b.link(b.delay("a", delay_s=0.0), "bs2:1"), b.link(arm, "bs2:2"),
                    b.link(b.beam_splitter("bs2"), {"c": "s", "d": "virtual"}), b.link(screen(0.5, "s"), "end")], branched=True)
    half = 0.5 * np.ones((N, N))
    a = L.MultIntensity(L.MultIntensity(begin(2e-3), half), half)
    other = phase(begin(2e-3), K * tilt * XX + 2 * math.pi * cycles + math.pi)
    other = L.MultIntensity(L.MultIntensity(other, half), half)
    mixed = L.Forvard(L.BeamMix(a, other), 0.5)
    assert distance(f, mixed) < 2e-3
    assert f.sum() * (SIZE / N) ** 2 / 1e-3 == pytest.approx(np.array(L.Intensity(mixed, 0)).sum() / np.array(L.Intensity(begin(2e-3), 0)).sum(), rel=2e-3)


def test_tracked_gaussian_equals_the_wave_calculation():
    res = uf.run(b.system("t", [b.lens("l", 0.5, medium="Vacuum"), b.gap("g", 0.3, medium="Vacuum")]), b.run_spec(b.pulse(LAM, 1e-12, energy=1e-3), input_beam=b.gaussian_beam(1e-3)))
    w = res.elements()[-1]["values"]["trackedBeamRadiusXM"]
    far = np.array(L.Intensity(L.Forvard(L.Lens(begin(1e-3), 0.5), 0.3), 0))
    assert w == pytest.approx(2 * math.sqrt((far * XX ** 2).sum() / far.sum()), rel=1e-6)
