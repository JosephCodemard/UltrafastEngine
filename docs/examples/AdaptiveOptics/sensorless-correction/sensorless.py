"""Sensorless adaptive optics: sharpen the focus with a deformable mirror using only the camera's peak fluence as the metric
(modal search and SPGD, no wavefront sensor) (python sensorless.py)."""
import time

import numpy as np

import ultrafast as uf
from ultrafast.algorithms import sensorless

uf.plugins.load_python_plugins()
from ultrafast_ao import DmBench                                      # noqa: E402

ABERRATION = {4: -0.25, 5: 0.20, 6: -0.15, 7: 0.20, 8: -0.10, 9: 0.10}   # waves rms at 1030 nm; the correction must be minus this
MODES = sorted(ABERRATION)


def setup():
    bench = DmBench(aberration=ABERRATION)
    ref = bench.reference_peak()
    counter = {"runs": 0}

    def metric(a):
        counter["runs"] += 1
        return bench.peak_fluence(bench.zernike_commands(dict(zip(MODES, a)))) / ref
    return bench, metric, counter


def run(spgd_iterations=80):
    bench, metric, counter = setup()
    a0 = np.zeros(len(MODES))
    out = {"bench": bench, "strehl_start": metric(a0), "truth": np.array([-ABERRATION[j] for j in MODES])}
    counter["runs"] = 0
    t0 = time.time()
    modal = sensorless.modal_search(metric, a0, bias=0.15, sweeps=2)
    out["modal"] = {"result": modal, "runs": counter["runs"], "seconds": time.time() - t0}
    counter["runs"] = 0
    t0 = time.time()
    sp = sensorless.spgd(metric, a0, iterations=spgd_iterations, gain=3.0, amplitude=0.05, seed=3, keep_iterates=True)
    out["spgd"] = {"result": sp, "runs": counter["runs"], "seconds": time.time() - t0}
    out["metric"] = metric
    return out


def main():
    r = run()
    print("hidden aberration %s waves rms; the camera peak fluence is the only measurement" % ABERRATION)
    print("Strehl ratio before: %.2f" % r["strehl_start"])
    for k in ("modal", "spgd"):
        d = r[k]
        x = d["result"].x
        print("%-5s Strehl %.2f after %d engine runs; coefficient error rms %.3f waves (truth %s)"
              % (k, d["result"].history[-1], d["runs"], np.sqrt(np.mean((x - r["truth"]) ** 2)), np.round(r["truth"], 2).tolist()))
    assert r["strehl_start"] < 0.5
    assert r["modal"]["result"].history[-1] > 0.85, "modal search should restore most of the focus"
    assert r["spgd"]["result"].history[-1] > 0.85, "SPGD should climb to a good focus"
    assert np.sqrt(np.mean((r["modal"]["result"].x - r["truth"]) ** 2)) < 0.08
    print("checks passed")


if __name__ == "__main__":
    main()
