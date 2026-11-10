"""Use the digital twin without beam time: what-if comparisons with credible bands, the most informative next measurement, and drift tracking
with an ensemble Kalman filter (python explore.py)."""
import shutil
from pathlib import Path

import numpy as np

import ultrafast as uf

uf.plugins.load_python_plugins()
import ultrafast_twin as tw                                            # noqa: E402
from ultrafast_twin import design, synthetic                           # noqa: E402

HERE = Path(__file__).resolve().parent
DRIFT_NM = (0.0, 0.0, 0.25, 0.5, 0.5, 0.5)                              # the hidden filter centre moves between measurement days


def setup():
    folder = HERE / "results" / "lab"
    if folder.exists():
        shutil.rmtree(folder)
    proj = tw.init(folder, "synthetic-lab")
    return proj, proj.fit("fit")


def drifted_files(proj, rng):
    """Spectrometer files of the hidden lab on later days, with its filter centre shifted (the true system the twin is meant to follow)."""
    files = []
    for day, shift in enumerate(DRIFT_NM):
        sysd = synthetic.lab_system(synthetic.CENTRE_NM + shift, synthetic.FWHM_NM, synthetic.true_b(1.2, False))
        run = synthetic.base_run(); run["pulse"]["energyJ"] = 1.2e-6
        res = uf.run(sysd, run)
        lam, s = res["spec_wavelength_m"][::4], res["spec_spectrum"][::4]
        path = HERE / "results" / ("day%d.csv" % day)
        np.savetxt(path, np.c_[lam * 1e9, s + 0.01 * s.max() * rng.standard_normal(s.size)], delimiter=",", header="wavelength_nm,intensity", comments="")
        files.append(path)
    return files


def run():
    proj, twin = setup()
    out = {"proj": proj, "twin": twin}
    # what-if: the energy law at three energies, and a narrower filter
    out["whatif"] = twin.whatif({"1.0 uJ": {"variables": {"energyJ": 1e-6}, "set": {"pulse/energyJ": 1e-6}},
                                 "2.0 uJ": {"variables": {"energyJ": 2e-6}, "set": {"pulse/energyJ": 2e-6}},
                                 "3.5 uJ": {"variables": {"energyJ": 3.5e-6}, "set": {"pulse/energyJ": 3.5e-6}}}, n_samples=10)
    # which measurement would shrink the uncertainty of the nonlinear-phase slope most?
    cands = design.energy_candidates(twin, "energyJ", [0.0, 0.5e-6, 1e-6, 2e-6, 3e-6, 4e-6], set_key="pulse/energyJ")
    out["design"] = twin.design(cands)
    out["placement"] = twin.design(design.placement_candidates(twin))
    # drift tracking
    rng = np.random.default_rng(11)
    files = drifted_files(proj, rng)
    trk = twin.tracker(parameters=["filter.centerNm"], members=16, process_sigma={"filter.centerNm": 0.3}, seed=2)
    for k, f in enumerate(files):
        trk.update(str(f), variables={"energyJ": 1.2e-6}, set={"pulse/energyJ": 1.2e-6}, label="day %d" % k)
    out["tracker"] = trk
    return out


def main():
    r = run()
    sc = r["whatif"]["scalars"]
    print("what-if (no beam time): predicted output pulse of the lab at three energies, +- 1 sigma of the twin")
    for k, row in sc.items():
        print("  %s: FWHM %.1f +- %.1f fs, spectral FWHM %.2f +- %.2f nm" % (k, row["fwhmS"]["value"] * 1e15, row["fwhmS"]["sigma"] * 1e15, row["spectralFwhmM"]["value"] * 1e9, row["spectralFwhmM"]["sigma"] * 1e9))
    d = r["design"]
    print("next measurement to learn the slope: expected information gain per candidate energy")
    for c in d["ranking"]:
        print("  %-22s gain %.2f  -> slope sigma %.5f" % (c["name"], c["gain"], c["posteriorSigma"].get("kerr.bRad[1]", c["posteriorSigma"].get(next(k for k in c["posteriorSigma"] if k.startswith("kerr")), 0))))
    print("best: %s (current slope sigma %.5f)" % (d["chosen"][0]["name"], d["currentSigma"].get(next(k for k in d["currentSigma"] if k.startswith("kerr")))))
    p = r["placement"]
    print("where to put the spectrometer: " + ", ".join("%s %.2f" % (c["name"], c["gain"]) for c in p["ranking"]))
    h = r["tracker"].history
    print("drift tracking of the filter centre (hidden shifts %s nm):" % list(DRIFT_NM))
    for k, s in enumerate(h[1:]):
        print("  day %d: %.3f +- %.3f nm%s" % (k, s["mean"]["filter.centerNm"], s["sigma"]["filter.centerNm"], "   DRIFT FLAGGED" if s["drifted"] else ""))
    assert h[-1]["drifted"] and not h[1]["drifted"]
    assert abs(h[-1]["mean"]["filter.centerNm"] - (synthetic.CENTRE_NM + DRIFT_NM[-1])) < 0.15
    assert d["chosen"][0]["gain"] >= max(c["gain"] for c in d["ranking"]) - 1e-9
    print("checks passed")


if __name__ == "__main__":
    main()
