"""The modulator element and the tile source through the Python API: arrays, callbacks and files give the same device; builders keep their types."""
import numpy as np

import ultrafast as uf
from ultrafast import build as b
from ultrafast.components import Modulator
from ultrafast.sources import TilesProfile

LAM, N, DX, P, F = 1030e-9, 1024, 4e-6, 16e-6, 0.1
CELLS = N // 4


def _grid(initial=None):
    return b.transverse("cartesian", nx=N, ny=1, half_width_m=N * DX / 2, spectral_samples=2, absorbing_boundary=True, initial_profile=initial)


def _focal(mod, **kw):
    system = b.system("m", [b.gap("src", 0.0, medium="Vacuum", transverse=_grid(b.initial_profile("gaussian", radius_m=0.3e-3))),
                            mod, b.lens("lens", F), b.gap("z", F, medium="Vacuum", transverse=_grid())])
    res = uf.run(system, b.run_spec(b.cw(LAM, 1.0), instruments=[b.instrument("beamProfile", at="z", name="bp")]), **kw)
    return np.array(res.instrument("bp")["fluence_x_j_m2"])


def test_numpy_callback_and_inline_list_are_the_same_device():
    k, theta = 2 * np.pi / LAM, 4e-3
    x = (np.arange(CELLS) - (CELLS - 1) / 2) * P
    phase = (k * theta * x)[:, None]
    common = dict(pitch_m=P, pitch_y_m=1.0, design_wavelength_m=LAM, offset_x_m=0.5 * DX)
    a = _focal(Modulator("m", phase_rad=phase, **common))
    b_ = _focal(Modulator("m", phase_rad=phase.tolist(), **common))
    c = _focal(Modulator("m", phase_rad="@ramp", cells_x=CELLS, cells_y=1, **common), fields={"ramp": lambda X, Y, Z: k * theta * X})
    assert np.array_equal(a, b_)
    assert np.allclose(a, c, rtol=0, atol=1e-12 * a.max())
    xs = (np.arange(N) - N / 2) * DX
    assert abs((xs * a).sum() / a.sum() - F * theta) < 0.01 * F * theta       # a pure tilt moves the focus by f * theta


def test_builders_return_plain_dicts_with_the_schema_keys():
    m = Modulator("m", 8e-6, phase_rad=np.zeros((4, 2)), quantisation_levels=256, phase_wrap_rad=2 * np.pi, fill_factor=0.95)
    assert m["type"] == "modulator" and m["quantisationLevels"] == 256 and m["phaseRad"] == [[0.0, 0.0]] * 4
    t = TilesProfile(40e-6, layout="hex", rings=2, pitch_m=200e-6, piston_rms_rad=0.3, seed=2)
    assert t["kind"] == "tiles" and t["rings"] == 2 and t["pistonRmsRad"] == 0.3
    assert uf.load  # the package imports without a display or an engine call


def test_a_loop_reads_the_callback_each_trial_and_index_mode_with_constant_dn_is_opd():
    k = 2 * np.pi / LAM
    common = dict(pitch_m=P, pitch_y_m=1.0, design_wavelength_m=LAM, offset_x_m=0.5 * DX, cells_x=CELLS, cells_y=1)
    xs = (np.arange(N) - N / 2) * DX
    for theta in (2e-3, -3e-3):                                                 # one compiled run per trial; the callback is read each time
        a = _focal(Modulator("m", phase_rad="@ramp", **common), fields={"ramp": lambda X, Y, Z, t=theta: k * t * X})
        assert abs((xs * a).sum() / a.sum() - F * theta) < 0.01 * abs(F * theta)
    ramp = "@ramp"; f = {"ramp": lambda X, Y, Z: k * 2e-3 * X}
    opd = _focal(Modulator("m", phase_rad=ramp, **common), fields=f)
    idx = _focal(Modulator("m", phase_rad=ramp, chromatic="index", delta_index_cauchy=[0.2], **common), fields=f)
    assert np.allclose(opd, idx, rtol=0, atol=1e-12 * opd.max())
    t = b.transverse("cartesian", nx=8, frequency_block=2)
    assert t["frequencyBlock"] == 2
