#!/usr/bin/env python3
"""Replay the reference trajectory of each eval in evals.json through the real ultrafast-mcp server and check that it can do what the prompt needs.

    python3 tests/agent-evals/run_evals.py [--only ID]      (needs the engine: python3 python/tools/build_engine.py)

This does not run a language model. It proves that the tools return what each criterion needs (fidelity levels, warnings, validity, refusals,
comparisons, figures). Evaluate a live agent by giving it each prompt with a workspace made by ``make_workspace`` and scoring the transcript
against the criteria. Exit code 0 when every replay passes.
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "python" / "src"))

from ultrafast.mcp import McpServer, policy          # noqa: E402
from ultrafast.mcp.worker import Worker              # noqa: E402

PLATE = REPO / "docs" / "examples" / "GettingStarted" / "spm-plate"
LINE = REPO / "docs" / "examples" / "Diagnostics" / "measured-data"


def make_workspace(root: Path) -> Path:
    ws = root / "ws"
    ws.mkdir()
    for f in ("plate.ufs", "run.json"):
        shutil.copy(PLATE / f, ws / f)
    shutil.copy(LINE / "line.ufs", ws / "line.ufs")
    shutil.copytree(LINE / "data", ws / "data")
    shutil.copy(LINE / "run-compare.json", ws / "run-compare.json")
    shutil.copy(LINE / "run-fit.json", ws / "run-fit.json")
    return ws


class Session:
    def __init__(self, ws: Path):
        self.srv = McpServer(ws, worker=Worker(ws, env={"PYTHONPATH": str(REPO / "python" / "src") + __import__("os").pathsep + str(REPO / "plugins" / "twin" / "python")}), budget_s=300)
        self.calls: list = []

    def call(self, tool: str, **args):
        r = self.srv.handle({"jsonrpc": "2.0", "id": len(self.calls) + 1, "method": "tools/call", "params": {"name": tool, "arguments": args}})["result"]
        self.calls.append((tool, args, r))
        return r

    def close(self):
        self.srv.worker.close()


def _ok(r):
    assert not r["isError"], r["content"][0]["text"]
    return r["structuredContent"]


def quick_then_confirm(s: Session):
    sc = _ok(s.call("scan", system="plate.ufs", run="run.json", axes={"pulse/energyJ": [1e-6, 2e-6, 4e-6, 8e-6]}, outputs=["pulse:end/fwhmS"], fidelity="quick"))
    assert "advice" in sc and "quick" in sc["advice"]
    best = sc["outputs"]["pulse:end/fwhmS"]
    assert len(best) == 4 and all(v is not None for v in best)
    full = _ok(s.call("run", system="plate.ufs", run="run.json", name="best", fidelity="full"))
    assert (full.get("fidelityReport") or {}).get("level", "full") == "full" and "warnings" in full and "validity" in full
    assert [c[1].get("fidelity") for c in s.calls if c[0] in ("scan", "run")] == ["quick", "full"]


def refuses_user_code(s: Session):
    r = s.call("run", system="plate.ufs", run={"instruments": [{"kind": "python", "name": "mine"}]}, name="x")
    assert r["isError"] and "user code" in r["content"][0]["text"] and s.srv.worker.spawns == 0


def reads_warnings(s: Session):
    spec = json.loads(policy.strip_jsonc((PLATE / "run.json").read_text(encoding="utf-8")))
    spec["pulse"]["energyJ"] = 40e-6
    out = _ok(s.call("run", system="plate.ufs", run=spec, name="hot", fidelity="quick"))
    codes = [v if isinstance(v, str) else v.get("code", "") for v in out["validity"]]
    for text in [w for w in out["warnings"]][:3] + codes[:3]:
        e = _ok(s.call("explain_warning", **({"code": text} if text in ("zeroFrequency", "criticalPower", "kerrLensCollapse", "lidt", "windowEdge") else {"warning": text})))
        assert "meaning" in e or "matched" in e
    assert out["elements"] and "bIntegralRad" in out["elements"][0]


def compare_and_fit(s: Session):
    def load(n):
        return json.loads(policy.strip_jsonc((s.srv.ws.root / n).read_text(encoding="utf-8")))
    c = _ok(s.call("compare_to_measured", system="line.ufs", run="run-compare.json", fidelity="quick"))
    assert c["totalChi2"] > 0 and any(x.get("notes") for x in c["comparisons"])
    run = load("run-fit.json"); run["fit"]["maxIterations"] = 3
    f = _ok(s.call("fit", system="line.ufs", run=run, name="fitted", fidelity="quick", timeBudgetS=250))
    assert f["fit"]["chi2Final"] < f["fit"]["chi2Initial"] and all("sigma" in p or "uncertainty" in p or "value" in p for p in f["fit"]["parameters"])


def plot_and_report(s: Session):
    _ok(s.call("run", system="plate.ufs", run="run.json", name="shown", fidelity="quick"))
    p = s.call("plot", result="shown", figure="overview", style="ultrafast", dpi=60)
    assert not p["isError"] and any(c["type"] == "image" for c in p["content"])


def optimise_with_study(s: Session):
    st = _ok(s.call("study", system="plate.ufs", run="run.json", parameters=[{"path": "element:plate/thicknessM", "min": 0.5e-3, "max": 4e-3}], outputs=["record:plate/BIntegralRad"],
                    mode="optimize", objective={"output": "record:plate/BIntegralRad", "goal": "target", "target": 1.0, "maxEvaluations": 30}, fidelity="quick"))
    assert st["rowCount"] >= 2 and "advice" in st and "quick" in st["advice"]
    best = min(st["rows"], key=lambda r: abs(r["record:plate/BIntegralRad"] - 1.0))
    spec = json.loads(policy.strip_jsonc((s.srv.ws.root / "run.json").read_text(encoding="utf-8")))
    system = json.loads(policy.strip_jsonc((s.srv.ws.root / "plate.ufs").read_text(encoding="utf-8")))
    for e in system["elements"]:
        if e.get("id") == "plate":
            e["thicknessM"] = best["element:plate/thicknessM"]
    full = _ok(s.call("run", system=system, run=spec, name="best", fidelity="full"))
    assert full["elements"] and "bIntegralRad" in full["elements"][0]


def twin_predict(s: Session):
    sys.path.insert(0, str(REPO / "plugins" / "twin" / "python"))
    import ultrafast_twin as tw
    tw.init(s.srv.ws.root / "lab", "synthetic-lab")
    f = _ok(s.call("twin_fit", folder="lab", name="f1"))
    assert f["parameters"]
    v = _ok(s.call("twin_validate", folder="lab", samples=4))
    assert v["verdict"] and v["conditions"] and "flags" in v
    p = _ok(s.call("twin_predict", folder="lab", variables={"energyJ": 2e-6}, set={"pulse/energyJ": 2e-6}, samples=3))
    assert p["scalars"]["energyJ"]["sigma"] >= 0 and p["pointsReturned"] <= 200


REPLAYS = {"optimise-with-study": optimise_with_study, "twin-predict": twin_predict, "quick-then-confirm": quick_then_confirm, "refuses-user-code": refuses_user_code, "reads-warnings": reads_warnings,
           "compare-and-fit": compare_and_fit, "plot-and-report": plot_and_report}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only")
    a = ap.parse_args(argv)
    evals = json.loads((Path(__file__).with_name("evals.json")).read_text(encoding="utf-8"))["evals"]
    bad = 0
    for e in evals:
        if a.only and e["id"] != a.only:
            continue
        with tempfile.TemporaryDirectory() as td:
            s = Session(make_workspace(Path(td)))
            try:
                REPLAYS[e["id"]](s)
                print("PASS  %-20s %s" % (e["id"], "(mandatory)" if e["mandatory"] else ""))
            except Exception as ex:   # noqa: BLE001  one failure must not hide the others
                bad += 1
                print("FAIL  %-20s %s: %s" % (e["id"], type(ex).__name__, re.sub(r"\s+", " ", str(ex))[:300]))
            finally:
                s.close()
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(main())
