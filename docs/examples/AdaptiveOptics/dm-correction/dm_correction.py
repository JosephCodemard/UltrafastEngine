"""Deformable-mirror correction of a Zernike aberration: measure, calibrate, correct, close the loop (python dm_correction.py)."""
import numpy as np

import ultrafast as uf
from ultrafast.algorithms import control

uf.plugins.load_python_plugins()
from ultrafast_ao import DmBench                                      # noqa: E402

ABERRATION = {4: 0.30, 5: -0.20, 7: 0.15}          # defocus, astigmatism, coma (waves rms at 1030 nm)


def run():
    bench = DmBench(aberration=ABERRATION)
    out = {"bench": bench, "strehl_before": bench.strehl()}
    out["waves_before"], out["x"], out["y"] = bench.wavefront_waves()
    D = bench.calibrate()
    out["D"] = D
    c_zonal, sv = bench.correct(D)
    out["c_zonal"], out["sv"] = c_zonal, sv
    out["strehl_zonal"] = bench.strehl(c_zonal)
    out["waves_zonal"] = bench.wavefront_waves(c_zonal)[0]
    # modal: command that produces the negative of the fitted Zernike content
    c_modal = bench.zernike_commands({j: -c for j, c in ABERRATION.items()})
    out["strehl_modal"] = bench.strehl(c_modal)
    out["c_modal"] = c_modal
    # closed loop, gain 0.7: record the focal Strehl and the wavefront at every step
    G, _ = control.control_matrix(D, regularisation=1e-3)
    c = np.zeros(bench.n_actuators)
    hist = []
    for k in range(7):
        m = bench.slopes(c)
        waves = bench.wavefront_waves(c)[0]
        hist.append({"step": k, "slope_rms": float(np.sqrt(np.mean(m ** 2))), "strehl": bench.strehl(c), "waves": waves, "commands": c.copy()})
        c = c - 0.7 * (G @ m)
    out["loop"] = hist
    return out


def main():
    r = run()
    b = r["bench"]
    print("aberration (Noll index: waves rms) %s, DM %d x %d actuators of %.1f mm" % (ABERRATION, b.actuators, b.actuators, b.pitch_m * 1e3))
    print("Strehl ratio: aberrated %.2f, one-shot zonal %.2f, one-shot modal %.2f" % (r["strehl_before"], r["strehl_zonal"], r["strehl_modal"]))
    sr = [h["slope_rms"] for h in r["loop"]]
    print("closed loop, gain 0.7: slope rms per step " + " ".join("%.2e" % s for s in sr))
    print("closed loop, gain 0.7: Strehl per step " + " ".join("%.2f" % h["strehl"] for h in r["loop"]))
    ratio = sr[1] / sr[0]
    print("slope rms falls by %.2f per step (integrator: 1 - gain = 0.30)" % ratio)
    assert r["strehl_before"] < 0.4, "the aberration should degrade the focus"
    assert r["strehl_zonal"] > 0.8 and r["strehl_modal"] > 0.9, "the corrections should restore the focus"
    assert abs(ratio - 0.30) < 0.1, "the integrator should reduce the slopes by 1 - gain per step"
    print("checks passed")


if __name__ == "__main__":
    main()
