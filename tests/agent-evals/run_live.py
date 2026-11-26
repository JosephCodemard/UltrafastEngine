#!/usr/bin/env python3
"""Score a LIVE agent on the evals of evals.json: give it each prompt with a workspace and the ultrafast-mcp server, record the tool calls, check the criteria.

    python3 tests/agent-evals/run_live.py --agent claude|codex|cursor --model NAME [--only ID] [--max-turns 30] [--out results.json]
    python3 tests/agent-evals/run_live.py --transcript FILE [--agent claude|codex|cursor] [--only ID]     (scores a saved transcript; runs nothing)

Agents: ``claude`` (Claude Code, ``claude -p --output-format stream-json``), ``codex`` (OpenAI Codex CLI, ``codex exec --json``), ``cursor`` (Cursor agent CLI,
``cursor-agent -p --output-format stream-json``). Each has an adapter (``parse_claude``, ``parse_codex``, ``parse_cursor``) that turns the agent's transcript into the
common structure the scorers use: ``trace`` (a list of ``{"tool", "args", "result", "isError"}`` for the ultrafast MCP tools), the final answer text and the files written.
``--transcript`` reads a saved stdout of any of the three (the format is detected when ``--agent`` is not given); one transcript is one eval (``--only ID``, or the id
from the file name ``<eval-id>.<anything>.jsonl``). Other agents: write JSON lines of ``{"tool": ..., "args": ..., "result": ...}`` plus ``{"final": "..."}`` (format ``plain``).
The agent gets only the ultrafast MCP tools in a fresh workspace with the agent pack installed. Criteria that a program can check from the tool calls and the final answer are
scored; the others are listed as 'manual' for a human to read in the transcript. Exit code 0 when every mandatory eval passes every checked criterion.
A live run uses the agent's login and spends tokens; run it deliberately (docs/reference/mcp.md, "Agent pack and evals").
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Tuple

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_evals import REPO, make_workspace     # noqa: E402

PREFIX = "mcp__ultrafast__"
BUILTIN = ["Bash", "Read", "Write", "Edit", "Glob", "Grep", "WebFetch", "WebSearch", "Task", "NotebookEdit"]


# ----------------------------------------------------------------------------- transcript adapters
# Each adapter takes the text of a transcript and returns (trace, final, usage); usage["filesWritten"] lists the files the agent wrote.
SERVER = "ultrafast"


def _jsonl(text: str) -> List[dict]:
    out = []
    for line in text.splitlines():
        try:
            ev = json.loads(line)
        except ValueError:
            continue
        if isinstance(ev, dict):
            out.append(ev)
    return out


def _as_text(body: Any) -> str:
    """A tool result body (a string, or a list of content blocks, or a dict) as text."""
    if body is None:
        return ""
    if isinstance(body, str):
        return body
    if isinstance(body, list):
        parts = []
        for b in body:
            t = b.get("text") if isinstance(b, dict) else None
            parts.append(t["text"] if isinstance(t, dict) and "text" in t else t if isinstance(t, str) else json.dumps(b))
        return "\n".join(parts)
    return json.dumps(body)


def parse_claude(text: str) -> Tuple[List[dict], str, dict]:
    """Claude Code ``claude -p --output-format stream-json --verbose``: one JSON event per line.

    Format: https://code.claude.com/docs/en/headless (stream-json), message blocks as in the Messages API (``tool_use`` in an ``assistant`` event, ``tool_result``
    in the following ``user`` event, ``result`` last). MCP tools are named ``mcp__<server>__<tool>``.
    """
    calls: Dict[str, dict] = {}
    trace: List[dict] = []
    files: List[str] = []
    final, usage = "", {}
    for ev in _jsonl(text):
        if ev.get("type") == "assistant":
            for c in ev.get("message", {}).get("content", []):
                if c.get("type") != "tool_use":
                    continue
                name = str(c.get("name", ""))
                if name.startswith(PREFIX):
                    calls[c["id"]] = {"tool": name[len(PREFIX):], "args": c.get("input", {}), "result": None}
                    trace.append(calls[c["id"]])
                elif name in ("Write", "Edit", "NotebookEdit") and "file_path" in (c.get("input") or {}):
                    files.append(c["input"]["file_path"])
        elif ev.get("type") == "user":
            for c in (ev.get("message", {}).get("content") or []):
                if isinstance(c, dict) and c.get("type") == "tool_result" and c.get("tool_use_id") in calls:
                    calls[c["tool_use_id"]]["result"] = _as_text(c.get("content"))
                    calls[c["tool_use_id"]]["isError"] = bool(c.get("is_error"))
        elif ev.get("type") == "result":
            final, usage = ev.get("result", ""), {k: ev.get(k) for k in ("num_turns", "total_cost_usd", "duration_ms")}
    usage["filesWritten"] = files
    return trace, final, usage


def parse_codex(text: str) -> Tuple[List[dict], str, dict]:
    """OpenAI Codex CLI ``codex exec --json``: JSON Lines with ``thread.started``, ``turn.started``, ``item.started`` / ``item.completed``, ``turn.completed``, ``turn.failed``, ``error``.

    Documented (https://developers.openai.com/codex/noninteractive, now https://learn.chatgpt.com/docs/non-interactive-mode): the event names, and the item types
    ``agent_message`` (``text``) and ``command_execution``. The page names but does not show ``mcp_tool_call`` and ``file_change`` items; their fields here follow the
    open-source CLI's event definitions (https://github.com/openai/codex, ``codex-rs/exec/src/exec_events.rs``): ``mcp_tool_call`` has ``server``, ``tool``, ``arguments``,
    ``result`` (``content`` blocks, ``structured_content``), ``error`` (``message``) and ``status``; ``file_change`` has ``changes`` (``path``, ``kind``). The last
    ``agent_message`` is the final answer. If a field name differs in your Codex version, this is the function to adjust.
    """
    trace: List[dict] = []
    files: List[str] = []
    final, usage = "", {}
    for ev in _jsonl(text):
        kind = ev.get("type")
        item = ev.get("item") or {}
        if kind == "item.completed":
            t = item.get("type")
            if t == "agent_message":
                final = item.get("text", "")
            elif t == "mcp_tool_call" and item.get("server", SERVER) == SERVER:
                args = item.get("arguments")
                if isinstance(args, str):
                    try:
                        args = json.loads(args)
                    except ValueError:
                        args = {}
                res = item.get("result") or {}
                err = item.get("error")
                body = (res.get("content") if isinstance(res, dict) else res) or (res.get("structured_content") if isinstance(res, dict) else None)
                trace.append({"tool": item.get("tool", ""), "args": args or {}, "result": _as_text(err.get("message") if isinstance(err, dict) else err) if err else _as_text(body),
                              "isError": bool(err) or item.get("status") == "failed"})
            elif t == "file_change":
                files += [c.get("path", "") for c in item.get("changes", [])]
        elif kind == "turn.completed":
            u = ev.get("usage") or {}
            usage = {"input_tokens": u.get("input_tokens"), "output_tokens": u.get("output_tokens")}
    usage["filesWritten"] = files
    return trace, final, usage


def parse_cursor(text: str) -> Tuple[List[dict], str, dict]:
    """Cursor agent CLI ``cursor-agent -p --output-format stream-json``: NDJSON with ``system`` (``init``), ``user``, ``assistant``, ``tool_call`` (``started`` / ``completed``), ``result``.

    Documented (https://cursor.com/docs/cli/reference/output-format): all of those events; the ``tool_call`` event has ``call_id`` and ``tool_call``, an object keyed by the tool
    (``readToolCall``, ``writeToolCall`` with ``args.path`` and ``result.success``); the ``result`` event carries the whole answer in ``result``. NOT documented: the shape of an
    MCP tool call (the page says other tools "may use ``tool_call.function``" with ``name`` and ``arguments``). Implemented here: ``mcpToolCall`` with ``args`` (``providerIdentifier``,
    ``toolName`` or ``name``, ``args``) and ``result.success`` / ``result.error`` (as written by cursor-agent builds seen in the field), and the documented ``function`` form.
    Where the transcript has no ``result`` event the ``assistant`` text blocks are joined. If your cursor-agent version differs, adjust ``_cursor_call``.
    """
    started: Dict[str, dict] = {}
    trace: List[dict] = []
    files: List[str] = []
    said: List[str] = []
    final, usage = None, {}
    for ev in _jsonl(text):
        kind = ev.get("type")
        if kind == "assistant":
            for c in ev.get("message", {}).get("content", []):
                if c.get("type") == "text" and "timestamp_ms" not in ev:      # skip streaming partials (--stream-partial-output)
                    said.append(c.get("text", ""))
        elif kind == "tool_call":
            cid = ev.get("call_id", "")
            call = _cursor_call(ev.get("tool_call") or {}, ev.get("subtype") == "completed")
            if call is None:
                for k, v in (ev.get("tool_call") or {}).items():
                    if k == "writeToolCall" and ev.get("subtype") == "completed" and isinstance(v, dict):
                        files.append((v.get("args") or {}).get("path", ""))
                continue
            if ev.get("subtype") == "started":
                started[cid] = call
                trace.append(call)
            elif cid in started:
                started[cid].update({k: v for k, v in call.items() if k in ("result", "isError")})
            else:
                trace.append(call)
        elif kind == "result":
            final = ev.get("result", "")
            usage = {"duration_ms": ev.get("duration_ms"), "is_error": ev.get("is_error")}
    usage["filesWritten"] = files
    return trace, (final if final is not None else "".join(said)), usage


def _cursor_call(tc: dict, completed: bool) -> Optional[dict]:
    """The ultrafast MCP call in a cursor ``tool_call`` object, or None for any other tool."""
    mcp = tc.get("mcpToolCall")
    if isinstance(mcp, dict):
        a = mcp.get("args") or {}
        if a.get("providerIdentifier", SERVER) != SERVER:
            return None
        name = a.get("toolName") or a.get("name", "")
        name = name[len(SERVER) + 1:] if name.startswith(SERVER + "-") else name
        out = {"tool": name, "args": a.get("args") or {}, "result": None}
        res = mcp.get("result") or {}
        if "success" in res:
            out["result"] = _as_text((res["success"] or {}).get("content"))
            out["isError"] = bool((res["success"] or {}).get("isError"))
        elif "error" in res:
            out["result"], out["isError"] = _as_text(res["error"]), True
        return out
    fn = tc.get("function")
    if isinstance(fn, dict):
        name = str(fn.get("name", ""))
        for pre in (PREFIX, SERVER + "-", SERVER + "_"):
            if name.startswith(pre):
                args = fn.get("arguments")
                if isinstance(args, str):
                    try:
                        args = json.loads(args)
                    except ValueError:
                        args = {}
                return {"tool": name[len(pre):], "args": args or {}, "result": None}
    return None


def parse_plain(text: str) -> Tuple[List[dict], str, dict]:
    """JSON lines of ``{"tool", "args", "result"}`` plus ``{"final": "..."}`` (a transcript written by hand or by another agent)."""
    trace, final = [], ""
    for ev in _jsonl(text):
        if "tool" in ev:
            trace.append({"tool": ev["tool"], "args": ev.get("args", {}), "result": _as_text(ev.get("result")), "isError": bool(ev.get("isError"))})
        elif "final" in ev:
            final = ev["final"]
    return trace, final, {"filesWritten": []}


ADAPTERS = {"claude": parse_claude, "codex": parse_codex, "cursor": parse_cursor, "plain": parse_plain}


def detect_format(text: str) -> str:
    """Which adapter reads this transcript: Codex lines have ``thread.started`` / ``item.*``, Cursor has ``tool_call`` or a system init without ``tools``... see the rules below."""
    evs = _jsonl(text)
    types = [str(e.get("type", "")) for e in evs]
    if any(t.startswith(("thread.", "turn.", "item.")) for t in types):
        return "codex"
    if "tool_call" in types or any(e.get("type") == "system" and "apiKeySource" in e for e in evs):
        return "cursor"
    if any(t in ("assistant", "user", "result", "system") for t in types):
        return "claude"
    return "plain"


# ----------------------------------------------------------------------------- the agents (live)
def _install_pack(ws: Path, target: str) -> None:
    subprocess.run([sys.executable, "-m", "ultrafast.agents", "install", "--target", target, "--dest", str(ws)], check=True, capture_output=True,
                   env={"PYTHONPATH": str(REPO / "python" / "src"), "PATH": os.environ.get("PATH", "")})


def _server_args(ws: Path) -> List[str]:
    return ["-m", "ultrafast.mcp", "--workspace", str(ws), "--budget-s", "240"]


def run_claude(prompt: str, ws: Path, model: str, max_turns: int, timeout_s: int) -> Tuple[List[dict], str, dict]:
    """Run ``claude -p``; returns (trace of tool calls, final answer, usage)."""
    cfg = ws.parent / "mcp.json"
    cfg.write_text(json.dumps({"mcpServers": {SERVER: {"command": sys.executable, "args": _server_args(ws), "env": {"PYTHONPATH": str(REPO / "python" / "src")}}}}), encoding="utf-8")
    _install_pack(ws, "claude")
    cmd = ["claude", "-p", prompt, "--model", model, "--mcp-config", str(cfg), "--strict-mcp-config", "--output-format", "stream-json", "--verbose",
           "--max-turns", str(max_turns), "--allowedTools", PREFIX + "*", "--no-session-persistence", "--max-budget-usd", "3", "--disallowedTools", *BUILTIN]
    return _finish(subprocess.run(cmd, cwd=ws, capture_output=True, text=True, timeout=timeout_s), parse_claude, "claude")


def run_codex(prompt: str, ws: Path, model: str, max_turns: int, timeout_s: int) -> Tuple[List[dict], str, dict]:
    """Run ``codex exec --json`` with the ultrafast server added through ``-c`` overrides (TOML values). Not exercised by the tests (no live runs); the owner runs it."""
    _install_pack(ws, "codex")
    cmd = ["codex", "exec", "--json", "--skip-git-repo-check", "--sandbox", "read-only", "-C", str(ws), "-m", model,
           "-c", 'mcp_servers.%s.command="%s"' % (SERVER, sys.executable),
           "-c", "mcp_servers.%s.args=%s" % (SERVER, json.dumps(_server_args(ws))),
           "-c", 'mcp_servers.%s.env={PYTHONPATH="%s"}' % (SERVER, REPO / "python" / "src"), prompt]
    return _finish(subprocess.run(cmd, cwd=ws, capture_output=True, text=True, timeout=timeout_s), parse_codex, "codex")


def run_cursor(prompt: str, ws: Path, model: str, max_turns: int, timeout_s: int) -> Tuple[List[dict], str, dict]:
    """Run ``cursor-agent -p --output-format stream-json`` with the server in ``.cursor/mcp.json``. Not exercised by the tests (no live runs); the owner runs it."""
    _install_pack(ws, "cursor")
    (ws / ".cursor").mkdir(exist_ok=True)
    (ws / ".cursor" / "mcp.json").write_text(json.dumps({"mcpServers": {SERVER: {"command": sys.executable, "args": _server_args(ws), "env": {"PYTHONPATH": str(REPO / "python" / "src")}}}}), encoding="utf-8")
    cmd = ["cursor-agent", "-p", prompt, "--model", model, "--output-format", "stream-json", "--approve-mcps", "--workspace", str(ws)]
    return _finish(subprocess.run(cmd, cwd=ws, capture_output=True, text=True, timeout=timeout_s), parse_cursor, "cursor")


def _finish(p, parse, name):
    trace, final, usage = parse(p.stdout)
    if not trace and not final:
        raise RuntimeError("the agent produced no transcript (is `%s` installed and logged in?): %s" % (name, p.stderr[-400:]))
    return trace, final, usage


RUNNERS = {"claude": run_claude, "codex": run_codex, "cursor": run_cursor}


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


def _report_one(e: dict, trace, final, usage, checks, model: str, secs: float) -> Tuple[dict, bool]:
    failed = [c[0] for c in checks if not c[1] and len(c) == 2]
    advisory = [c[0] for c in checks if not c[1] and len(c) == 3]
    manual = [c for c in e["criteria"] if c not in {k[0] for k in checks}]
    print("%s  %-20s %s model=%s %.0fs tools=%s" % ("PASS" if not failed else "FAIL", e["id"], "(mandatory)" if e["mandatory"] else "", model, secs, ",".join(c["tool"] for c in trace)))
    if failed:
        print("      calls:", " ".join("%s:%s" % (c["tool"], c["args"].get("fidelity", "-")) for c in trace))
    for c in failed:
        print("      not met:", c)
    for c in advisory:
        print("      advisory (a number not found in a tool result; derived numbers are fine, check by eye):", c)
    for c in manual:
        print("      manual :", c)
    return ({"id": e["id"], "model": model, "passed": not failed, "failed": failed, "advisory": advisory, "manual": manual, "usage": usage, "tools": [c["tool"] for c in trace],
             "calls": ["%s:%s" % (c["tool"], c["args"].get("fidelity", "-")) for c in trace], "final": final}, bool(failed and e["mandatory"]))


def score_transcript(path: Path, agent: Optional[str], only: Optional[str]) -> Tuple[dict, bool]:
    """Score a saved transcript; the eval is ``only`` or the first dotted part of the file name."""
    evals = {e["id"]: e for e in json.loads((HERE / "evals.json").read_text(encoding="utf-8"))["evals"]}
    eid = only or path.name.split(".")[0]
    if eid not in evals:
        raise SystemExit("cannot tell which eval %s answers: pass --only ID (one of %s)" % (path.name, ", ".join(evals)))
    text = path.read_text(encoding="utf-8")
    fmt = agent or detect_format(text)
    trace, final, usage = ADAPTERS[fmt](text)
    return _report_one(evals[eid], trace, final, usage, score(eid, trace, final), fmt, 0.0)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--agent", choices=["claude", "codex", "cursor"], help="agent to run (default claude); with --transcript the format of the file (default: detected)")
    ap.add_argument("--model", default="sonnet")
    ap.add_argument("--only")
    ap.add_argument("--transcript", type=Path, help="score this saved transcript (the stdout of the agent's JSON mode) and run nothing")
    ap.add_argument("--max-turns", type=int, default=30)
    ap.add_argument("--timeout-s", type=int, default=900)
    ap.add_argument("--out", help="write the transcripts and scores here (JSON)")
    a = ap.parse_args(argv)
    if a.transcript:
        rep, bad = score_transcript(a.transcript, a.agent, a.only)
        if a.out:
            Path(a.out).write_text(json.dumps([rep], indent=1), encoding="utf-8")
        return 1 if bad else 0
    agent = a.agent or "claude"
    evals = json.loads((HERE / "evals.json").read_text(encoding="utf-8"))["evals"]
    report, bad = [], 0
    for e in evals:
        if a.only and e["id"] != a.only:
            continue
        with tempfile.TemporaryDirectory() as td:
            ws = make_workspace(Path(td))
            t0 = time.time()
            try:
                trace, final, usage = RUNNERS[agent](e["prompt"], ws, a.model, a.max_turns, a.timeout_s)
                checks = score(e["id"], trace, final)
            except Exception as ex:      # noqa: BLE001
                trace, final, usage, checks = [], "", {}, [("the agent ran", False)]
                print("ERROR %s: %s" % (e["id"], ex))
        rep, b = _report_one(e, trace, final, usage, checks, "%s:%s" % (agent, a.model), time.time() - t0)
        report.append(rep)
        bad += b
    if a.out:
        Path(a.out).write_text(json.dumps(report, indent=1), encoding="utf-8")
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(main())
