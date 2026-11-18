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
    assert {e["id"] for e in evals} == set(m.REPLAYS) and len(evals) >= 5
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
