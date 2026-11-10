"""Build a digital twin of a (synthetic) lab: fit five energies with one set of shared parameters, recover hidden values, predict two held-out energies
with credible bands, and catch a twin that only fits its training data (python build_twin.py)."""
import shutil
import time
from pathlib import Path

import numpy as np

import ultrafast as uf

uf.plugins.load_python_plugins()
import ultrafast_twin as tw                                            # noqa: E402
from ultrafast_twin import synthetic                                   # noqa: E402

HERE = Path(__file__).resolve().parent


def build(template, folder):
    folder = Path(folder)
    if folder.exists():
        shutil.rmtree(folder)
    proj = tw.init(folder, template)
    t0 = time.time()
    twin = proj.fit("fit")
    return proj, twin, time.time() - t0


def run():
    out = {}
    for key, template in (("linear", "synthetic-lab"), ("saturating", "synthetic-lab-saturating")):
        proj, twin, secs = build(template, HERE / "results" / key)
        report = twin.validate(n_samples=12)
        preds = {c["name"]: twin.predict(name=c["name"], n_samples=12, data=proj.data_path(c)) for c in proj.conditions()}
        out[key] = {"project": proj, "twin": twin, "seconds": secs, "report": report, "pred": preds}
    return out


def parameter(twin, name):
    return next(p for p in twin.params if p["name"] == name)


def main():
    r = run()
    L = r["linear"]
    tw_ = L["twin"]
    fw, ce = parameter(tw_, "filter.fwhmNm"), parameter(tw_, "filter.centerNm")
    slope = next(p for p in tw_.params if p["name"].startswith("kerr.bRad") and p.get("origin") == "dependence")
    print("synthetic lab: a spectral filter (hidden centre %.1f nm, FWHM %.1f nm) and a nonlinear phase of %.1f rad/uJ; the description says 1030 nm, 12 nm, 2 rad/uJ"
          % (synthetic.CENTRE_NM, synthetic.FWHM_NM, synthetic.SLOPE_RAD_PER_UJ))
    print("fit: five energies %s uJ, one shared filter and one slope" % list(synthetic.TRAIN_UJ))
    print("recovered centre %.3f +- %.3f nm (truth %.1f), FWHM %.3f +- %.3f nm (truth %.1f), slope %.4f +- %.4f rad/uJ (truth %.1f)"
          % (ce["value"], ce["sigma"], synthetic.CENTRE_NM, fw["value"], fw["sigma"], synthetic.FWHM_NM, slope["value"], slope["sigma"], synthetic.SLOPE_RAD_PER_UJ))
    for row in L["report"]["conditions"]:
        if row["heldOut"]:
            print("held-out %-9s chi2 per point %.2f, %.0f %% of points inside the 2-sigma band: %s" % (row["name"], row["reducedChi2"], 100 * row["coverage2sigma"], row["status"]))
    print("verdict of the validation report: %s" % L["report"]["verdict"])
    S = r["saturating"]["report"]
    for row in S["conditions"]:
        if row["heldOut"]:
            print("saturating lab, held-out %-9s chi2 per point %.1f: %s" % (row["name"], row["reducedChi2"], row["status"]))
    print("saturating lab verdict: %s" % S["verdict"])
    assert abs(ce["value"] - synthetic.CENTRE_NM) < 3 * ce["sigma"] + 0.02 and abs(fw["value"] - synthetic.FWHM_NM) < 3 * fw["sigma"] + 0.02
    assert abs(slope["value"] - synthetic.SLOPE_RAD_PER_UJ) < 0.01
    assert L["report"]["verdict"] == "ok" and S["verdict"] == "fits-training-only"
    print("checks passed")


if __name__ == "__main__":
    main()
