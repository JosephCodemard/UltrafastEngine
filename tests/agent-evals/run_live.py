#!/usr/bin/env python3
"""Score a LIVE agent on the evals of evals.json: give it each prompt with a workspace and the ultrafast-mcp server, record the tool calls, check the criteria.

    python3 tests/agent-evals/run_live.py --agent claude --model sonnet [--only ID] [--max-turns 30] [--out results.json]

Agents: ``claude`` (Claude Code in print mode, ``claude -p``; ``--model`` is an alias or a model name). Other agents: write the transcript as JSON lines
of ``{"tool": ..., "args": ..., "result": ...}`` plus ``{"final": "..."}`` and score it with ``score(eval_id, trace, final)``.
The agent gets only the ultrafast MCP tools (no shell, no file access) in a fresh workspace with the agent pack installed (``.claude/skills``).
Criteria that a program can check from the tool calls and the final answer are scored; the others are listed as 'manual' for a human to read in the transcript.
Exit code 0 when every mandatory eval passes every checked criterion. It uses your Claude Code login and spends tokens; run it deliberately.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Any, Callable, Dict, List, Tuple

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_evals import REPO, make_workspace     # noqa: E402

PREFIX = "mcp__ultrafast__"
BUILTIN = ["Bash", "Read", "Write", "Edit", "Glob", "Grep", "WebFetch", "WebSearch", "Task", "NotebookEdit"]


# ----------------------------------------------------------------------------- the agent
def run_claude(prompt: str, ws: Path, model: str, max_turns: int, timeout_s: int) -> Tuple[List[dict], str, dict]:
    """Run ``claude -p``; returns (trace of tool calls, final answer, usage)."""
    cfg = ws.parent / "mcp.json"
    cfg.write_text(json.dumps({"mcpServers": {"ultrafast": {"command": sys.executable, "args": ["-m", "ultrafast.mcp", "--workspace", str(ws), "--budget-s", "240"],
                                                              "env": {"PYTHONPATH": str(REPO / "python" / "src")}}}}), encoding="utf-8")
    subprocess.run([sys.executable, "-m", "ultrafast.agents", "install", "--target", "claude", "--dest", str(ws)], check=True, capture_output=True,
                   env={"PYTHONPATH": str(REPO / "python" / "src"), "PATH": __import__("os").environ.get("PATH", "")})
    cmd = ["claude", "-p", prompt, "--model", model, "--mcp-config", str(cfg), "--strict-mcp-config", "--output-format", "stream-json", "--verbose",
           "--max-turns", str(max_turns), "--allowedTools", PREFIX + "*", "--no-session-persistence", "--max-budget-usd", "3", "--disallowedTools", *BUILTIN]
    p = subprocess.run(cmd, cwd=ws, capture_output=True, text=True, timeout=timeout_s)
    calls: Dict[str, dict] = {}
    trace: List[dict] = []
    final, usage = "", {}
    for line in p.stdout.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if ev.get("type") == "assistant":
            for c in ev.get("message", {}).get("content", []):
                if c.get("type") == "tool_use" and str(c.get("name", "")).startswith(PREFIX):
                    calls[c["id"]] = {"tool": c["name"][len(PREFIX):], "args": c.get("input", {}), "result": None}
                    trace.append(calls[c["id"]])
        elif ev.get("type") == "user":
            for c in (ev.get("message", {}).get("content") or []):
                if isinstance(c, dict) and c.get("type") == "tool_result" and c.get("tool_use_id") in calls:
                    body = c.get("content")
                    calls[c["tool_use_id"]]["result"] = body if isinstance(body, str) else json.dumps(body)
                    calls[c["tool_use_id"]]["isError"] = bool(c.get("is_error"))
        elif ev.get("type") == "result":
            final, usage = ev.get("result", ""), {k: ev.get(k) for k in ("num_turns", "total_cost_usd", "duration_ms")}
    if not trace and not final:
        raise RuntimeError("the agent produced no transcript (is `claude` logged in?): %s" % p.stderr[-400:])
    return trace, final, usage


# ----------------------------------------------------------------------------- scoring
Check = Tuple[str, bool]
ADVISORY = "advisory"      # a third element marks a check that is printed but does not fail the eval (derived numbers such as differences and rates are legitimate)


def _calls(trace, *tools):
    return [c for c in trace if c["tool"] in tools]


def _text(c) -> str:
    return c.get("result") or ""


def _checks_quick_then_confirm(trace, final) -> List[Check]:
    runs = _calls(trace, "scan", "run")
    levels = [c["args"].get("fidelity") or "full" for c in runs]      # no fidelity argument means the default, 'full'
    fin = final.lower()
    first_quick = bool(levels) and levels[0] == "quick"
    confirmed = "full" in levels and (levels.index("full") > levels.index("quick") if "quick" in levels else False)
    return [("explores with scan or run in fidelity quick", first_quick),
            ("re-runs the reported point(s) with run in fidelity full", confirmed and any(c["tool"] == "run" and (c["args"].get("fidelity") or "full") == "full" for c in runs)),
            ("states which numbers came from quick and which from full", "quick" in fin and "full" in fin),
            ("no number is quoted that no tool returned", _numbers_traceable(trace, final), ADVISORY)]


def _checks_refuses(trace, final) -> List[Check]:
    runs = _calls(trace, "run", "validate", "scan")
    refused = [c for c in runs if "user code" in _text(c) or "refused" in _text(c)]
    fin = final.lower()
    later_ok = [c for c in runs[runs.index(refused[0]) + 1:] if refused and "python" in json.dumps(c["args"]).lower()] if refused else []
    return [("the server refuses (user code)", bool(refused)),
            ("the agent says so and offers a built-in instrument instead (spectrometer / pulseSummary)", ("spectrometer" in fin or "pulsesummary" in fin) and ("cannot" in fin or "can't" in fin or "refus" in fin or "not allowed" in fin or "unable" in fin)),
            ("does not try to work around the refusal", not later_ok)]


def _checks_reads_warnings(trace, final) -> List[Check]:
    ex = _calls(trace, "explain_warning")
    fin = final.lower()
    return [("calls explain_warning for each warning or validity code", bool(ex)),
            ("reports the B-integral and the critical-power ratio", ("b-integral" in fin or "b integral" in fin or "b_integral" in fin) and ("critical" in fin)),
            ("does not call the result reliable if validity entries remain", not re.search(r"\b(is|are) (reliable|trustworthy)\b", fin) or "not" in fin)]


def _checks_compare_and_fit(trace, final) -> List[Check]:
    names = [c["tool"] for c in trace]
    cmp_i = names.index("compare_to_measured") if "compare_to_measured" in names else -1
    fit_i = names.index("fit") if "fit" in names else -1
    cmp_c = _calls(trace, "compare_to_measured")
    fit_c = _calls(trace, "fit")
    fin = final.lower()
    return [("compare_to_measured first, in quick", cmp_i >= 0 and (fit_i < 0 or cmp_i < fit_i) and cmp_c[0]["args"].get("fidelity") == "quick"),
            ("fits only after the comparison, with a time budget", fit_i > cmp_i >= 0 and "timeBudgetS" in fit_c[0]["args"]),
            ("reports chi2 before and after and the uncertainties", "chi" in fin and ("before" in fin or "initial" in fin) and ("uncertaint" in fin or "sigma" in fin or "±" in fin)),
            ("confirms the fitted system in full", any(c["tool"] in ("run", "compare_to_measured") and c["args"].get("fidelity") in (None, "full") for c in trace[fit_i + 1:]) if fit_i >= 0 else False)]


def _checks_plot(trace, final) -> List[Check]:
    names = [c["tool"] for c in trace]
    plots = _calls(trace, "plot")
    return [("runs first, then plots the finished result", "run" in names and "plot" in names and names.index("run") < names.index("plot")),
            ("uses the 'overview' or 'instruments' figure", any(c["args"].get("figure", "overview") in ("overview", "instruments") for c in plots))]


def _numbers_traceable(trace, final, tol: float = 0.02) -> bool:
    """Every number with at least three significant digits in the final answer is within ``tol`` of a number in some tool result (or in the prompt's inputs)."""
    blob = " ".join(_text(c) + json.dumps(c["args"]) for c in trace)
    have = [float(x) for x in re.findall(r"-?\d+\.?\d*(?:[eE][-+]?\d+)?", blob)]
    for tok in re.findall(r"(?<![\w.])-?\d+\.?\d*(?:[eE][-+]?\d+)?", final):
        digits = len(tok.replace("-", "").replace(".", "").lstrip("0").split("e")[0].split("E")[0])
        if digits < 3:
            continue
        v = float(tok)
        if not any(abs(v - h) <= tol * max(abs(h), 1e-30) or abs(v - 1e6 * h) <= tol * abs(1e6 * h) or abs(v - 1e15 * h) <= tol * abs(1e15 * h) or abs(v - 1e-6 * h) <= tol * abs(1e-6 * h) for h in have):
            return False
    return True


def _checks_study(trace, final) -> List[Check]:
    st = _calls(trace, "study", "scan")
    runs = _calls(trace, "run")
    return [("uses the study tool (mode optimize or scan) in fidelity quick", bool(st) and st[0]["args"].get("fidelity") == "quick"),
            ("confirms the chosen thickness with the run tool in fidelity full", any(c["args"].get("fidelity") in (None, "full") for c in runs)),
            ("no number is quoted that no tool returned", _numbers_traceable(trace, final), ADVISORY)]


def _checks_twin(trace, final) -> List[Check]:
    names = [c["tool"] for c in trace if c["tool"].startswith("twin_")]
    first = {n: names.index(n) for n in set(names)}
    fin = final.lower()
    return [("calls twin_fit, then twin_validate, then twin_predict", set(first) >= {"twin_fit", "twin_validate", "twin_predict"} and first["twin_fit"] < first["twin_validate"] < first["twin_predict"]),
            ("states the uncertainty (credible band) of the prediction", "band" in fin or "uncertaint" in fin or "sigma" in fin or "±" in fin)]


CHECKS: Dict[str, Callable[[list, str], List[Check]]] = {"quick-then-confirm": _checks_quick_then_confirm, "refuses-user-code": _checks_refuses, "reads-warnings": _checks_reads_warnings,
                                                         "compare-and-fit": _checks_compare_and_fit, "plot-and-report": _checks_plot, "optimise-with-study": _checks_study, "twin-predict": _checks_twin}


def score(eval_id: str, trace: list, final: str) -> List[Check]:
    return CHECKS[eval_id](trace, final)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--agent", default="claude", choices=["claude"])
    ap.add_argument("--model", default="sonnet")
    ap.add_argument("--only")
    ap.add_argument("--max-turns", type=int, default=30)
    ap.add_argument("--timeout-s", type=int, default=900)
    ap.add_argument("--out", help="write the transcripts and scores here (JSON)")
    a = ap.parse_args(argv)
    evals = json.loads((HERE / "evals.json").read_text(encoding="utf-8"))["evals"]
    report, bad = [], 0
    for e in evals:
        if a.only and e["id"] != a.only:
            continue
        with tempfile.TemporaryDirectory() as td:
            ws = make_workspace(Path(td))
            t0 = time.time()
            try:
                trace, final, usage = run_claude(e["prompt"], ws, a.model, a.max_turns, a.timeout_s)
                checks = score(e["id"], trace, final)
            except Exception as ex:      # noqa: BLE001
                trace, final, usage, checks = [], "", {}, [("the agent ran", False)]
                print("ERROR %s: %s" % (e["id"], ex))
        failed = [c[0] for c in checks if not c[1] and len(c) == 2]
        advisory = [c[0] for c in checks if not c[1] and len(c) == 3]
        manual = [c for c in e["criteria"] if c not in {k[0] for k in checks}]
        bad += bool(failed and e["mandatory"])
        print("%s  %-20s %s model=%s %.0fs tools=%s" % ("PASS" if not failed else "FAIL", e["id"], "(mandatory)" if e["mandatory"] else "", a.model, time.time() - t0,
                                                         ",".join(c["tool"] for c in trace)))
        if failed:
            print("      calls:", " ".join("%s:%s" % (c["tool"], c["args"].get("fidelity", "-")) for c in trace))
        for c in failed:
            print("      not met:", c)
        for c in advisory:
            print("      advisory (a number not found in a tool result; derived numbers are fine, check by eye):", c)
        for c in manual:
            print("      manual :", c)
        report.append({"id": e["id"], "model": a.model, "passed": not failed, "failed": failed, "advisory": advisory, "manual": manual, "usage": usage, "tools": [c["tool"] for c in trace],
                       "calls": ["%s:%s" % (c["tool"], c["args"].get("fidelity", "-")) for c in trace], "final": final})
    if a.out:
        Path(a.out).write_text(json.dumps(report, indent=1), encoding="utf-8")
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(main())
