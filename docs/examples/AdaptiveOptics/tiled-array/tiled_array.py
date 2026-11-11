"""Coherent tiled-aperture combining: 7 and 61 beams, piston errors, SPGD phase locking and delay mismatch for a 30 fs pulse
(python tiled_array.py)."""
import time

import numpy as np

import ultrafast as uf
from ultrafast import build as b
from ultrafast.algorithms import sensorless

uf.plugins.load_python_plugins()
from ultrafast_ao import tiled_array, combining_metrics               # noqa: E402

LAM, F, TP = 800e-9, 0.5, 30e-15
TILE_R, PITCH = 0.5e-3, 3e-3


def focus(profile, pulse, n=256, half=5e-3, ss=1, f=F):
    g = lambda ip=None: b.transverse("cartesian", nx=n, ny=n, half_width_m=half, spectral_samples=ss, absorbing_boundary=True, initial_profile=ip)
    sysd = b.system("tiles", [b.gap("src", 0.0, medium="Vacuum", transverse=g(profile)), b.lens("lens", f), b.gap("z", f, medium="Vacuum", transverse=g())])
    d = uf.run(sysd, b.run_spec(pulse, instruments=[b.instrument("beamProfile", at="z", name="cam")])).instrument("cam")
    return np.asarray(d["fluence_map_j_m2"]), np.asarray(d["x_m"]), np.asarray(d["y_m"])


def array(rings, **kw):
    return tiled_array(TILE_R, layout="hex", rings=rings, pitch_m=PITCH, **kw)


def run():
    cw = b.cw(LAM, 1.0)
    out = {}
    c = 128
    single, x, y = focus(b.tiles_profile(TILE_R, positions_m=[[0.0, 0.0]]), cw)
    ref7, _, _ = focus(array(1), cw)
    rng = np.random.default_rng(4)
    phi = rng.normal(0, 0.5, 7)
    err7, _, _ = focus(array(1, piston_rad=phi.tolist()), cw)
    out["inphase"] = {"single": single, "ref": ref7, "err": err7, "x": x, "phi": phi, "gain": ref7[c, c] / single[c, c],
                      "piston_ratio": err7[c, c] / ref7[c, c], "piston_theory": abs(np.exp(1j * phi).sum()) ** 2 / 49}
    out["metrics"] = combining_metrics(ref7, x, y, wavelength_m=LAM, focal_length_m=F, pitch_m=PITCH, n_tiles=7, reference_axis_fluence=ref7[c, c])
    # 61 tiles on a larger window
    kw = dict(n=512, half=12e-3, f=1.5)                   # a 17 mm array needs a longer lens so that the focal spot (70 um) spans 1.5 pixels of the window
    single61, x61, _ = focus(b.tiles_profile(0.4e-3, positions_m=[[0.0, 0.0]]), cw, **kw)
    ref61, _, _ = focus(tiled_array(0.4e-3, layout="hex", rings=4, pitch_m=2e-3), cw, **kw)
    out["n61"] = {"gain": ref61[256, 256] / single61[256, 256], "ref": ref61, "x": x61}
    # delay mismatch (30 fs pulse, 16 spectral samples)
    pulse = b.pulse(LAM, TP, energy=1e-3)
    refp, _, _ = focus(array(1), pulse, ss=16)
    rng = np.random.default_rng(0)
    rows = []
    for rms in (0, 10e-15, 25e-15, 40e-15, 60e-15):
        tau = rng.normal(0, rms, 7)
        v, _, _ = focus(array(1, delays_s=tau.tolist()), pulse, ss=16)
        D = tau[:, None] - tau[None, :]
        rows.append({"rms_fs": rms * 1e15, "engine": v[c, c] / refp[c, c], "formula": float(np.exp(-np.log(2) * D ** 2 / TP ** 2).sum() / 49)})
    out["delay"] = rows
    # SPGD locking, 128 x 128 cells so that every trial is quick. The lens is f = 1 m here: at f = 0.5 m the lens phase at the outer tiles (3.7 rad per cell at 3 mm) is
    # under-sampled on 128 cells, the outer tiles reach the axis with other amplitudes and phases, and the zero-piston sum is not the maximum (1.015 x it, README)
    refl, _, _ = focus(array(1), cw, n=128, f=1.0)
    phi0 = np.random.default_rng(3).uniform(0, 2 * np.pi, 7)
    cl = 64

    def metric(u):
        return focus(array(1, piston_rad=(phi0 + u).tolist()), cw, n=128, f=1.0)[0][cl, cl] / refl[cl, cl]
    start = metric(np.zeros(7))
    t0 = time.time()
    lock = sensorless.spgd(metric, np.zeros(7), iterations=120, gain=10.0, amplitude=0.2, seed=1, keep_iterates=True)
    out["lock"] = {"start": start, "result": lock, "phi0": phi0, "seconds": time.time() - t0, "ref": refl}
    return out


def main():
    r = run()
    ip = r["inphase"]
    print("7 tiles of radius %.1f mm on a hex lattice of pitch %.0f mm, lambda %.0f nm, f = %.1f m" % (TILE_R * 1e3, PITCH * 1e3, LAM * 1e9, F))
    print("in phase: peak fluence %.2f x one tile at the same total power (N = 7)" % ip["gain"])
    print("pistons of 0.5 rad rms: axis fluence %.3f of in phase (|sum exp(i phi)|^2 / N^2 = %.3f)" % (ip["piston_ratio"], ip["piston_theory"]))
    print("61 tiles (4 rings): peak %.1f x one tile (N = 61)" % r["n61"]["gain"])
    print("energy in the central lobe %.2f, in the first grating lobes %.2f" % (r["metrics"]["central_lobe_fraction"], r["metrics"]["grating_lobe_fraction"]))
    print("delay mismatch of the 30 fs tiles: axis fluence over the aligned value (engine, formula)")
    for d in r["delay"]:
        print("  rms %2.0f fs   %.3f   %.3f" % (d["rms_fs"], d["engine"], d["formula"]))
    k = r["lock"]
    print("SPGD locking of the 7 pistons: axis fluence %.2f -> %.2f of the in-phase value in %d iterations (%d engine runs)"
          % (k["start"], k["result"].history[-1], len(k["result"].history), k["result"].evaluations))
    assert abs(ip["gain"] / 7 - 1) < 0.01 and abs(r["n61"]["gain"] / 61 - 1) < 0.02
    assert abs(ip["piston_ratio"] / ip["piston_theory"] - 1) < 0.01
    assert all(abs(d["engine"] / d["formula"] - 1) < 0.02 for d in r["delay"])
    assert k["start"] < 0.3 and 0.95 < k["result"].history[-1] < 1.005, "the lock must reach the in-phase value and not exceed it (the zero-piston sum is the maximum when the lens is resolved)"
    print("checks passed")


if __name__ == "__main__":
    main()
