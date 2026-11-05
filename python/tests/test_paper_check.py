"""Tests of the paper-replication harness build/paper_check.py on tiny synthetic paper folders (no engine needed).

Each fixture paper has a run.py that writes results/numbers.json directly, and a checks.json with checks of every kind
(number absolute / relative, ordering, location by window and by expected +- tolerance, argmin), a failing hard check, a
failing soft check and a full-tier-only check.
"""
import importlib.util
import json
import sys
from pathlib import Path

import pytest

HARNESS = Path(__file__).resolve().parents[2] / "build" / "paper_check.py"


def _harness():
    spec = importlib.util.spec_from_file_location("paper_check", HARNESS)
    mod = importlib.util.module_from_spec(spec)
    sys.path.insert(0, str(HARNESS.parent))
    spec.loader.exec_module(mod)
    return mod


pc = _harness()

RUN_PY = '''
import argparse, json, os
ap = argparse.ArgumentParser(); ap.add_argument("--tier", default="fast"); a = ap.parse_args()
assert os.environ.get("ULTRAFAST_STRICT_OPTIONS") == "1"
os.makedirs("results", exist_ok=True)
n = {"c": 12.6, "pair": {"low": 0.3, "high": 0.9}, "curve": {"x": [1, 2, 3, 4], "y": [0.1, 0.7, 0.4, 0.2]}, "tier": a.tier}
json.dump(n, open("results/numbers.json", "w"))
'''

GOOD = [
    {"id": "abs", "kind": "number", "key": "c", "expected": 12.8, "tolerance": 0.3},
    {"id": "rel", "kind": "number", "key": "c", "expected": 12.8, "tolerance": 0.02, "relative": True},
    {"id": "order", "kind": "ordering", "a": "pair.low", "b": "pair.high", "margin": 0.5},
    {"id": "peak", "kind": "location", "key": "curve", "window": [1.5, 2.5]},
    {"id": "peak2", "kind": "location", "key": "curve", "expected": 2.0, "tolerance": 0.1},
    {"id": "trough", "kind": "location", "key": "curve", "min": True, "expected": 1.0, "tolerance": 0.1},
    {"id": "soft", "kind": "number", "key": "c", "expected": 100, "tolerance": 1, "hard": False},
    {"id": "fullonly", "kind": "number", "key": "c", "expected": -1, "tolerance": 0.1, "tiers": ["full"]},
]


def make_paper(root: Path, name: str, checks, run_py=RUN_PY):
    d = root / name
    d.mkdir(parents=True)
    (d / "run.py").write_text(run_py)
    (d / "checks.json").write_text(json.dumps({"checks": checks}))
    return d


def test_every_kind_evaluates_and_soft_failures_do_not_fail(tmp_path, capsys):
    make_paper(tmp_path, "a-2000-good", GOOD)
    assert pc.main(["--root", str(tmp_path), "--report", str(tmp_path / "r.json"), "--markdown", str(tmp_path / "r.md")]) == 0
    rep = json.loads((tmp_path / "r.json").read_text())
    rows = {r["id"]: r for r in rep[0]["checks"]}
    assert set(rows) == {"abs", "rel", "order", "peak", "peak2", "trough", "soft"}, "the full-tier check is skipped in the fast tier"
    assert all(rows[k]["verdict"] == "pass" for k in ("abs", "rel", "order", "peak", "peak2", "trough"))
    assert rows["soft"]["verdict"] == "soft-fail"
    assert rows["peak"]["ours"] == 2
    assert "| peak | location |" in (tmp_path / "r.md").read_text()


def test_a_failing_hard_check_gives_exit_1(tmp_path):
    make_paper(tmp_path, "b-2000-bad", [{"id": "off", "kind": "number", "key": "c", "expected": 20, "tolerance": 1}])
    assert pc.main(["--root", str(tmp_path)]) == 1


def test_full_tier_runs_full_checks(tmp_path):
    make_paper(tmp_path, "a-2000-good", GOOD)
    assert pc.main(["--root", str(tmp_path), "--full"]) == 1, "the full-only check fails on purpose"
    numbers = json.loads((tmp_path / "a-2000-good" / "results" / "numbers.json").read_text())
    assert numbers["tier"] == "full", "run.py is called with --tier full"


def test_missing_key_is_an_error_and_a_crashing_run_fails(tmp_path):
    make_paper(tmp_path, "c-2000-missing", [{"id": "nokey", "kind": "number", "key": "nope", "expected": 1, "tolerance": 1}])
    make_paper(tmp_path, "d-2000-crash", GOOD[:1], run_py="raise SystemExit(3)\n")
    assert pc.main(["--root", str(tmp_path), "c-"]) == 1
    assert pc.main(["--root", str(tmp_path), "d-"]) == 1
    r = pc.evaluate({"kind": "number", "key": "nope", "expected": 1, "tolerance": 1}, {})
    assert r["ok"] is None and "KeyError" in r["detail"]


def test_no_run_uses_existing_results(tmp_path):
    d = make_paper(tmp_path, "e-2000-norun", GOOD[:1], run_py="raise SystemExit(5)\n")
    (d / "results").mkdir()
    (d / "results" / "numbers.json").write_text(json.dumps({"c": 12.7}))
    assert pc.main(["--root", str(tmp_path), "--no-run"]) == 0


@pytest.mark.parametrize("check,ok", [
    ({"kind": "ordering", "a": "x", "b": "y"}, True),
    ({"kind": "ordering", "a": "y", "b": "x"}, False),
    ({"kind": "number", "key": "x", "expected": 1.0, "tolerance": 0.0}, True),
    ({"kind": "bogus", "key": "x"}, None),
])
def test_evaluate(check, ok):
    assert pc.evaluate(check, {"x": 1.0, "y": 2.0})["ok"] is ok
