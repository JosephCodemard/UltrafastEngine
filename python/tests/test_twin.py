"""The twin plugin driven from Python: a synthetic lab with hidden truth, the multi-condition fit, validation against held-out conditions, the placement and
experiment planner, the what-if explorer, drift tracking and the benchmark of design tasks. Reference values are in each docstring."""
import json
import sys
from pathlib import Path

import numpy as np
import pytest

from conftest import requires_engine, slow

PLUGIN_PY = Path(__file__).resolve().parents[2] / "plugins" / "twin" / "python"
if str(PLUGIN_PY) not in sys.path:
    sys.path.insert(0, str(PLUGIN_PY))

import ultrafast as uf                                    # noqa: E402

pytestmark = requires_engine

import ultrafast_twin as tw                               # noqa: E402
from ultrafast_twin import design, goals, synthetic      # noqa: E402


@pytest.fixture(scope="module")
def linear(tmp_path_factory):
    folder = tmp_path_factory.mktemp("twin") / "lab"
    proj = tw.init(folder, "synthetic-lab")
    return proj, proj.fit("fit")


def _param(twin, name):
    return next(p for p in twin.params if p["name"] == name)


def test_the_fit_recovers_the_hidden_truth_of_the_synthetic_lab(linear):
    """Five training energies, one shared filter and one slope. Truth: centre 1033.0 nm, FWHM 8.5 nm, slope 3.0 rad/uJ. Measured 1033.008, 8.497, 2.9998 (sigma 0.006, 0.010,
    0.002); asserted within 3 sigma plus 0.02 nm, and 0.01 rad/uJ for the slope."""
    _, twin = linear
    c, w = _param(twin, "filter.centerNm"), _param(twin, "filter.fwhmNm")
    s = next(p for p in twin.params if p["name"].startswith("kerr.bRad") and p.get("origin") == "dependence")
    assert abs(c["value"] - synthetic.CENTRE_NM) < 3 * c["sigma"] + 0.02
    assert abs(w["value"] - synthetic.FWHM_NM) < 3 * w["sigma"] + 0.02
    assert abs(s["value"] - synthetic.SLOPE_RAD_PER_UJ) < 0.01


def test_held_out_energies_are_predicted_inside_and_outside_the_training_range_and_the_report_says_ok(linear):
    """Held out: 1.4 uJ (inside 0.4-2.0) and 3.0 uJ (outside). Measured chi2 per point 0.98 and 0.90, 95 % and 96 % of points inside the 2-sigma band; the verdict is ok
    (rules in ultrafast_twin.validate: flag above 3 x training chi2 or below 80 % coverage)."""
    _, twin = linear
    r = twin.validate(n_samples=6, write=True)
    assert r["verdict"] == "ok", r["flags"]
    held = [c for c in r["conditions"] if c["heldOut"]]
    assert len(held) == 2 and all(c["reducedChi2"] < 1.5 and c["coverage2sigma"] > 0.8 for c in held)
    assert (twin.project.folder / "reports" / "validation.json").is_file()


def test_a_twin_that_only_fits_its_training_data_is_flagged():
    """A lab whose phase saturates (b = slope E / (1 + E / 2.5 uJ)) while the twin assumes linear: the held-out chi2 per point is 11.9 and 180 (measured), far above
    3 x the training value, so the verdict is fits-training-only. This is the acceptance case of the roadmap (S4)."""
    import tempfile
    proj = tw.init(Path(tempfile.mkdtemp(prefix="uf-twin-sat-")) / "lab", "synthetic-lab-saturating")
    r = proj.fit("fit").validate(n_samples=4, write=False)
    assert r["verdict"] == "fits-training-only" and r["flags"]
    assert all(c["status"] == "flagged" for c in r["conditions"] if c["heldOut"])


def test_stored_dependences_reproduce_the_fitted_values_of_every_condition(linear):
    """``fitted-conditions.json`` stores, per condition, the dependent values the engine used and a dependence in a form ``DependenceModel`` evaluates: the two agree
    to 1e-9 (relative)."""
    _, twin = linear
    data = json.loads((twin.dir / "fitted-conditions.json").read_text())
    models = [tw.DependenceModel(d) for d in data["dependences"]]
    n = 0
    for name, cond in data.items():
        if name in ("description", "dependences"):
            continue
        for m in models:
            for path, val in m.values(cond["variables"][m.variable]).items():
                ref = cond["fitted"][path]
                assert abs(val - ref) <= 1e-9 * max(1.0, abs(ref)), (name, path, val, ref)
                n += 1
    assert n >= 5, "five training conditions at least"


def test_the_planner_ranks_the_placements_and_a_zero_energy_shot_does_not_see_the_slope(linear):
    """Placement: an instrument before the filter sees nothing of the parameters (gain 0, last); after the nonlinear phase it sees all. A phase proportional to the energy
    is invisible at zero energy: d(reading)/d(slope) < 1e-9 at 1e-12 J and > 0.05 at 0.3 uJ (the construction of the synthetic lab)."""
    _, twin = linear
    pl = design.rank_candidates(twin, design.placement_candidates(twin))["ranking"]
    assert abs(pl[-1]["gain"]) < 1e-6 and pl[-1]["name"].endswith("input")
    assert pl[0]["name"].split()[-1] in ("kerr", "end")
    c0, c1 = design.energy_candidates(twin, "energyJ", [1e-12, 0.3e-6], "pulse/energyJ")
    col = lambda c: float(np.linalg.norm(design.jacobian(twin, c)[:, 2]))
    assert col(c0) < 1e-9 and col(c1) > 0.05


def test_what_if_uses_paired_draws_so_the_difference_to_the_first_variant_is_tight(linear):
    """The same parameter draws serve every variant: the sigma of a difference is the sigma of the paired draws, never above the quadrature sum of the two sigmas (so the
    comparison is at least as sharp as two independent predictions)."""
    _, twin = linear
    w = twin.whatif({"a": {"variables": {"energyJ": 1e-6}, "set": {"pulse/energyJ": 1e-6}}, "b": {"variables": {"energyJ": 2e-6}, "set": {"pulse/energyJ": 2e-6}}}, n_samples=6)
    row = w["scalars"]["b"]["spectralFwhmM"]
    assert row["delta"] > 0                                                   # more nonlinear phase, a broader spectrum
    sa, sb = w["scalars"]["a"]["spectralFwhmM"]["sigma"], row["sigma"]
    assert row["deltaSigma"] <= np.hypot(sa, sb) * 1.001


def test_the_drift_tracker_follows_a_shifted_filter_centre_and_flags_it(linear, tmp_path):
    """The hidden filter centre moves by 0.5 nm after the fit. With the process sigma set to the expected drift (0.3 nm; with the fitted sigma, 0.006 nm, the tracker followed only
    17 % of a 0.5 nm shift in three steps: S4 notes), one update follows it to within 0.15 nm and the alarm (3 fitted sigmas) goes off; an unshifted day does not."""
    proj, twin = linear
    rng = np.random.default_rng(11)
    files = []
    for k, shift in enumerate((0.0, 0.5)):
        sysd = synthetic.lab_system(synthetic.CENTRE_NM + shift, synthetic.FWHM_NM, synthetic.true_b(1.2, False))
        run = synthetic.base_run(); run["pulse"]["energyJ"] = 1.2e-6
        res = uf.run(sysd, run)
        lam, s = res["spec_wavelength_m"][::4], res["spec_spectrum"][::4]
        p = tmp_path / f"day{k}.csv"
        np.savetxt(p, np.c_[lam * 1e9, s + 0.01 * s.max() * rng.standard_normal(s.size)], delimiter=",", header="wavelength_nm,intensity", comments="")
        files.append(p)
    trk = twin.tracker(parameters=["filter.centerNm"], members=16, process_sigma={"filter.centerNm": 0.3}, seed=2)
    v = {"energyJ": 1.2e-6}; st = {"pulse/energyJ": 1.2e-6}
    a = trk.update(str(files[0]), variables=v, set=st, label="day 0")
    b_ = trk.update(str(files[1]), variables=v, set=st, label="day 1")
    assert not a["drifted"]
    assert b_["drifted"] == ["filter.centerNm"]
    assert abs(b_["mean"]["filter.centerNm"] - (synthetic.CENTRE_NM + 0.5)) < 0.15


def test_the_benchmark_of_design_tasks_with_known_answers_passes():
    """``benchmark()``: placement, sensitivity and saturation tasks whose answers follow from the construction of the lab (measured: 3 of 3)."""
    r = goals.benchmark()
    assert r["passed"], [t for t in r["tasks"] if not t["passed"]]


def test_a_goal_file_checks_the_twin(linear, tmp_path):
    """A goal file of checks (verdict ok, slope 3.0 within 2 %, centre sigma below 0.05 nm) passes on the linear lab and fails when a target is wrong."""
    _, twin = linear
    ok = {"format": "ultrafast-twin-goals", "checks": [{"id": "held-out", "type": "validationVerdict", "equals": "ok"},
                                                      {"id": "precision", "type": "parameterSigma", "name": "filter.centerNm", "max": 0.05}]}
    p = tmp_path / "goals.json"; p.write_text(json.dumps(ok))
    assert goals.check(twin, goals.load_goals(p))["passed"]
    bad = {"format": "ultrafast-twin-goals", "checks": [{"id": "centre", "type": "parameter", "name": "filter.centerNm", "value": 1030.0, "relTol": 0.0001}]}
    p.write_text(json.dumps(bad))
    assert not goals.check(twin, goals.load_goals(p))["passed"]
