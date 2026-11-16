"""Which model of an unknown energy law should a twin use? The hidden lab saturates (b = 3 E / (1 + E / 2.5 uJ)); the same five training energies
are fitted with a proportional law, a quadratic, a Chebyshev series and a small constrained network, and each is asked for the held-out energies 1.4 uJ
(inside the training range) and 3.0 uJ (outside it) (python compare.py)."""
import copy
import json
import shutil
from pathlib import Path

import numpy as np

import ultrafast as uf

uf.plugins.load_python_plugins()
import ultrafast_twin as tw                                            # noqa: E402
from ultrafast_twin import synthetic                                   # noqa: E402

HERE = Path(__file__).resolve().parent
REF = 1e-6   # the condition variable is energyJ; coefficients and polynomials use u = E / 1 uJ

MODELS = {
    "proportional": {"kind": "proportional", "coefficients": [{"initial": 2.0, "min": 0.0, "max": 10.0}]},
    "quadratic": {"kind": "polynomial", "degree": 2, "coefficients": [{"initial": 0.0, "min": -5.0, "max": 5.0}, {"initial": 2.0, "min": -5.0, "max": 10.0}, {"initial": 0.0, "min": -5.0, "max": 5.0}]},
    "chebyshev": {"kind": "chebyshev", "degree": 3, "coefficients": [{"initial": 2.0, "min": -10.0, "max": 10.0}, {"initial": 1.0, "min": -10.0, "max": 10.0},
                                                                    {"initial": 0.0, "min": -10.0, "max": 10.0}, {"initial": 0.0, "min": -10.0, "max": 10.0}]},
    "network (H=2)": {"kind": "network", "hidden": 2, "output": "positive", "monotone": "increasing", "weightDecay": 1e-4},
    "network (H=3)": {"kind": "network", "hidden": 3, "output": "positive", "monotone": "increasing", "weightDecay": 1e-4},
}


def fit_one(key, dependence):
    folder = HERE / "results" / key.replace(" ", "").replace("(", "-").replace(")", "").replace("=", "")
    if folder.exists():
        shutil.rmtree(folder)
    tw.init(folder, "synthetic-lab-saturating")
    path = folder / "twin.json"
    spec = json.loads(path.read_text())
    param = next(p for p in spec["parameters"] if p["name"] == "kerr.bRad")
    dep = copy.deepcopy(dependence)
    dep.update(variable="energyJ", reference=REF)
    param["dependence"] = dep
    path.write_text(json.dumps(spec, indent=2))
    proj = tw.TwinProject(folder)
    twin = proj.fit("fit")
    report = twin.validate(n_samples=12)
    rows = {r["name"]: r for r in report["conditions"]}
    return {"twin": twin, "report": report, "rows": rows, "params": len(twin.params)}


def run():
    return {key: fit_one(key, dep) for key, dep in MODELS.items()}


def main():
    r = run()
    print("hidden law: b = 3 E / (1 + E / 2.5 uJ) rad; trained on %s uJ" % list(synthetic.TRAIN_UJ))
    print("%-13s %6s %24s %26s  %s" % ("model", "params", "held-out 1.4 uJ (inside)", "held-out 3.0 uJ (outside)", "verdict"))
    for key, d in r.items():
        a, b = d["rows"]["E1.4uJ"], d["rows"]["E3.0uJ"]
        print("%-13s %6d %12.2f %-11s %14.2f %-11s %s" % (key, d["params"], a["reducedChi2"], a["status"], b["reducedChi2"], b["status"], d["report"]["verdict"]))
    ok = lambda d, n: d["rows"][n]["status"] == "ok"        # noqa: E731
    assert not ok(r["proportional"], "E1.4uJ") and not ok(r["proportional"], "E3.0uJ"), "a straight line cannot follow the saturation"
    assert ok(r["quadratic"], "E1.4uJ") and not ok(r["quadratic"], "E3.0uJ"), "a quadratic interpolates but does not extrapolate"
    assert ok(r["chebyshev"], "E1.4uJ") and ok(r["chebyshev"], "E3.0uJ") and r["chebyshev"]["report"]["verdict"] == "ok"
    assert ok(r["network (H=2)"], "E1.4uJ") and not ok(r["network (H=2)"], "E3.0uJ"), "the 2-unit network interpolates; tanh units saturate outside the range"
    assert not ok(r["network (H=3)"], "E1.4uJ"), "the 3-unit network stops in a poor local minimum from the default start"
    print("checks passed")
    return r


if __name__ == "__main__":
    main()
