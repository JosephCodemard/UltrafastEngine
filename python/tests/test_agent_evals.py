"""The agent evals' reference trajectories replay through the real MCP server (tests/agent-evals/run_evals.py)."""
import importlib.util
import json
from pathlib import Path

import pytest

from conftest import requires_engine

EVALS = Path(__file__).resolve().parents[2] / "tests" / "agent-evals"


def _load():
    spec = importlib.util.spec_from_file_location("run_evals", EVALS / "run_evals.py")
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def test_every_eval_has_a_prompt_criteria_and_a_replay():
    m = _load()
    evals = json.loads((EVALS / "evals.json").read_text(encoding="utf-8"))["evals"]
    assert {e["id"] for e in evals} == set(m.REPLAYS) and len(evals) >= 7
    assert all(e["prompt"] and len(e["criteria"]) >= 3 for e in evals)
    assert any(e["id"] == "quick-then-confirm" and e["mandatory"] for e in evals)


@requires_engine
@pytest.mark.parametrize("eval_id", ["quick-then-confirm", "refuses-user-code", "reads-warnings", "compare-and-fit", "plot-and-report"])
def test_reference_trajectory_replays(eval_id, tmp_path):
    m = _load()
    s = m.Session(m.make_workspace(tmp_path))
    try:
        m.REPLAYS[eval_id](s)
    finally:
        s.close()


def _live():
    import sys
    sys.path.insert(0, str(EVALS))
    import run_live
    return run_live


def test_live_scorer_accepts_a_good_trajectory_and_rejects_a_bad_one():
    live = _live()
    good = [{"tool": "scan", "args": {"fidelity": "quick"}, "result": "fwhm 1.5e-13"},
            {"tool": "run", "args": {"fidelity": "full"}, "result": "fwhm 1.52e-13 warnings []"}]
    ok = live.score("quick-then-confirm", good, "The quick scan gave 150 fs; the full run confirmed 152 fs.")
    assert all(c[1] for c in ok), ok
    bad = live.score("quick-then-confirm", [{"tool": "run", "args": {"fidelity": "full"}, "result": "x"}], "It is 77.7 fs.")
    assert not all(c[1] for c in bad)
    twin = live.score("twin-predict", [{"tool": "twin_predict", "args": {}, "result": "x"}, {"tool": "twin_fit", "args": {}, "result": "y"}], "fine")
    assert not twin[0][1]                       # the order is checked
    refuse = live.score("refuses-user-code", [{"tool": "run", "args": {}, "result": "refused: user code is not allowed"}],
                        "The server refuses user code, so I cannot run it; use the built-in spectrometer instead.")
    assert all(c[1] for c in refuse)


def test_every_eval_has_a_live_scorer():
    live = _live()
    evals = json.loads((EVALS / "evals.json").read_text(encoding="utf-8"))["evals"]
    assert {e["id"] for e in evals} == set(live.CHECKS)


def test_skills_have_one_source():
    import subprocess
    import sys
    script = Path(__file__).resolve().parents[2] / "build" / "sync_skills.py"
    r = subprocess.run([sys.executable, str(script), "--check"], capture_output=True, text=True)
    assert r.returncode == 0, r.stdout + "  (run python build/sync_skills.py)"
