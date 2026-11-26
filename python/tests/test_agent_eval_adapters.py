"""The live runner's transcript adapters (tests/agent-evals/run_live.py): Claude Code, Codex and Cursor transcripts of the same behaviour give the same trace and the same verdict.

Offline: the replays in tests/agent-evals/replays/ are hand-made transcripts in each agent's format (<eval id>.<pass|fail>.<agent>.jsonl); no model is called.
"""
import importlib.util
import json
import sys
from pathlib import Path

import pytest

EVALS = Path(__file__).resolve().parents[2] / "tests" / "agent-evals"
REPLAYS = EVALS / "replays"
AGENTS = ["claude", "codex", "cursor"]
CASES = [("quick-then-confirm", "pass"), ("quick-then-confirm", "fail"), ("refuses-user-code", "pass"), ("refuses-user-code", "fail")]


@pytest.fixture(scope="module")
def live():
    sys.path.insert(0, str(EVALS))
    spec = importlib.util.spec_from_file_location("run_live_under_test", EVALS / "run_live.py")
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def _read(eid, variant, agent):
    return (REPLAYS / ("%s.%s.%s.jsonl" % (eid, variant, agent))).read_text(encoding="utf-8")


@pytest.mark.parametrize("eid,variant", CASES)
def test_same_trace_and_verdict_in_all_formats(live, eid, variant):
    results = []
    for agent in AGENTS:
        text = _read(eid, variant, agent)
        assert live.detect_format(text) == agent
        trace, final, _ = live.ADAPTERS[agent](text)
        checks = live.score(eid, trace, final)
        results.append(([(c["tool"], c["args"], c["result"], c["isError"]) for c in trace], final, [(c[0], c[1]) for c in checks]))
    assert results[0] == results[1] == results[2]
    assert (all(ok for name, ok in results[0][2])) == (variant == "pass")
    assert results[0][0], "the adapters found no tool call"


def test_cli_scores_a_transcript_without_running_anything(live, tmp_path, capsys):
    for agent in AGENTS:
        assert live.main(["--transcript", str(REPLAYS / ("quick-then-confirm.pass.%s.jsonl" % agent))]) == 0
        assert live.main(["--transcript", str(REPLAYS / ("quick-then-confirm.fail.%s.jsonl" % agent)), "--agent", agent]) == 1
    out = tmp_path / "r.json"
    live.main(["--transcript", str(REPLAYS / "refuses-user-code.pass.codex.jsonl"), "--out", str(out)])
    assert json.loads(out.read_text())[0]["passed"] is True


def test_files_written_and_partial_streams(live):
    t, f, u = live.parse_cursor("\n".join(json.dumps(e) for e in [
        {"type": "tool_call", "subtype": "completed", "call_id": "w", "tool_call": {"writeToolCall": {"args": {"path": "a.ufs"}, "result": {"success": {}}}}},
        {"type": "assistant", "message": {"content": [{"type": "text", "text": "hi"}]}}]))
    assert u["filesWritten"] == ["a.ufs"] and f == "hi" and t == []
    t, f, u = live.parse_codex(json.dumps({"type": "item.completed", "item": {"type": "file_change", "changes": [{"path": "b.ufs", "kind": "add"}]}}))
    assert u["filesWritten"] == ["b.ufs"]
    assert live.parse_codex("not json\n") == ([], "", {"filesWritten": []})
