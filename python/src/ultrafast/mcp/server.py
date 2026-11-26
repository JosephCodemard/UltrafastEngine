"""``ultrafast-mcp``: a local MCP server on stdio (decision E8: workspace-sandboxed, hand-written JSON-RPC 2.0, no ``mcp`` package).

Run it with ``ultrafast-mcp --workspace DIR`` (or ``python -m ultrafast.mcp``). It reads one JSON-RPC message per line from stdin and writes one per
line to stdout; nothing else is ever written to stdout (logs go to stderr). Speaks both protocol eras: the stateless revision 2026-07-28
(``server/discover``, per-request ``_meta``) and the handshake revisions 2025-11-25, 2025-06-18, 2025-03-26 and 2024-11-05 (``initialize``).

Classes and functions:

* :class:`McpServer` ``(workspace, worker=None, budget_s=300)``: ``handle(message) -> reply | None`` (None for notifications), ``serve(stdin, stdout)``.
  Methods: ``initialize``, ``server/discover``, ``ping``, ``tools/list``, ``tools/call``, ``resources/list``, ``resources/read``,
  ``resources/templates/list``, ``prompts/list``, ``prompts/get``; any other request gets -32601; notifications never get a reply.
* A refusal (:class:`~ultrafast.mcp.policy.Refused`), a time-out or an engine error is a tool result with ``isError: true`` that tells the agent what to do.
* :func:`main` ``(argv)``: the command line (``--workspace``, ``--budget-s``, ``--memory-limit-gb``).
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional

from . import explain as _explain
from . import prompts as _prompts
from . import resources as _resources
from . import tools as _tools
from .policy import Refused, Workspace, check_arguments
from .worker import Worker, WorkerFailure, WorkerTimeout

MODERN_VERSIONS = ["2026-07-28"]
LEGACY_VERSIONS = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"]
SUPPORTED_VERSIONS = MODERN_VERSIONS + LEGACY_VERSIONS
_META = "io.modelcontextprotocol/"

INSTRUCTIONS = (
    "UltrafastEngine: nonlinear ultrafast pulse propagation through optical systems. Files live in the workspace; results are written to .ultrafast-mcp/<name>/. "
    "RULE: the default fidelity is 'full' (slow). Every exploratory call (scan, study, run, compare_to_measured, fit) must pass fidelity 'quick'; run only the points you will report "
    "again in fidelity 'full', and say in the answer which numbers came from which level. "
    "Workflow: describe or validate the system, explore in fidelity 'quick', confirm the reported points in 'full', "
    "read the warnings and 'validity' of every result (explain_warning), then plot. SI units everywhere, the unit is in the key name (lengthM, energyJ). "
    "Digital twin: twin_init (or a hand-written twin.json), twin_fit, twin_validate, then twin_predict, twin_design (the next measurement) and twin_track (drift). "
    "Read the schema resources before writing a file. This server runs no user code: no callbacks ('@name'), no python or expression kinds.")


def _version() -> str:
    try:
        from .. import __version__
        return str(__version__)
    except Exception:
        return "1.0.0"


def _error(rid: Any, code: int, message: str, data: Any = None) -> Dict[str, Any]:
    e: Dict[str, Any] = {"code": code, "message": message}
    if data is not None:
        e["data"] = data
    return {"jsonrpc": "2.0", "id": rid, "error": e}


def _tool_result(text: str, is_error: bool = False, structured: Any = None, extra: Optional[List[dict]] = None) -> Dict[str, Any]:
    r: Dict[str, Any] = {"content": [{"type": "text", "text": text}] + (extra or []), "isError": is_error}
    if structured is not None and not is_error:
        r["structuredContent"] = structured
    return r


class McpServer:
    def __init__(self, workspace: Any, worker: Optional[Worker] = None, budget_s: float = 300.0, memory_limit_bytes: int = 8_000_000_000):
        self.ws = Workspace(workspace)
        self.worker = worker or Worker(self.ws.root, memory_limit_bytes=memory_limit_bytes)
        self.budget_s = float(budget_s)

    # ------------------------------------------------------------------ dispatch
    def handle(self, msg: Any) -> Optional[Dict[str, Any]]:
        if isinstance(msg, list):
            return _error(None, -32600, "batches are not supported")
        if not isinstance(msg, dict) or msg.get("jsonrpc") != "2.0":
            return _error(msg.get("id") if isinstance(msg, dict) else None, -32600, "not a JSON-RPC 2.0 message")
        if "id" not in msg:
            return None                      # a notification: never answered
        rid, method = msg["id"], msg.get("method")
        if rid is None or isinstance(rid, bool) or not isinstance(rid, (str, int)):
            return _error(None, -32600, "the request id must be a string or an integer")
        if not isinstance(method, str):
            return _error(rid, -32600, "missing method")
        params = msg.get("params")
        if params is None:
            params = {}
        if not isinstance(params, dict):
            return _error(rid, -32602, "params must be an object")
        meta = params.get("_meta") if isinstance(params.get("_meta"), dict) else {}
        modern = _META + "protocolVersion" in meta
        if modern:
            ver = meta[_META + "protocolVersion"]
            if ver not in SUPPORTED_VERSIONS:
                return _error(rid, -32022, "Unsupported protocol version", {"supported": SUPPORTED_VERSIONS, "requested": ver})
            if _META + "clientCapabilities" not in meta and ver in MODERN_VERSIONS:
                return _error(rid, -32602, "missing _meta['%sclientCapabilities']" % _META)
        try:
            result = self._dispatch(method, params, modern)
        except KeyError as e:
            if method == "resources/read":
                return _error(rid, -32602, "unknown resource: %s" % e.args[0])
            raise
        except _Invalid as e:
            return _error(rid, -32602, str(e))
        except _NoMethod:
            return _error(rid, -32601, "method not found: %s" % method)
        except Exception as e:                      # a bug here must not kill the server
            sys.stderr.write("ultrafast-mcp: internal error in %s: %r\n" % (method, e))
            return _error(rid, -32603, "internal error: %s" % e)
        if modern:
            result = dict(result)
            result.setdefault("resultType", "complete")
            result.setdefault("_meta", {})[_META + "serverInfo"] = {"name": "ultrafast-mcp", "version": _version()}
        return {"jsonrpc": "2.0", "id": rid, "result": result}

    def _dispatch(self, method: str, params: Dict[str, Any], modern: bool) -> Dict[str, Any]:
        if method == "initialize":
            want = params.get("protocolVersion")
            ver = want if want in LEGACY_VERSIONS else LEGACY_VERSIONS[0]
            return {"protocolVersion": ver, "capabilities": {"tools": {"listChanged": False}, "resources": {"listChanged": False, "subscribe": False}, "prompts": {"listChanged": False}},
                    "serverInfo": {"name": "ultrafast-mcp", "version": _version()}, "instructions": INSTRUCTIONS}
        if method == "server/discover":
            return {"supportedVersions": SUPPORTED_VERSIONS, "capabilities": {"tools": {}, "resources": {}, "prompts": {}},
                    "_meta": {_META + "serverInfo": {"name": "ultrafast-mcp", "version": _version()}}, "instructions": INSTRUCTIONS}
        if method == "ping":
            return {}
        if method == "tools/list":
            return {"tools": _tools.definitions()}
        if method == "tools/call":
            return self._call_tool(params)
        if method == "prompts/list":
            return {"prompts": _prompts.list_prompts()}
        if method == "prompts/get":
            try:
                return _prompts.get_prompt(params.get("name"), params.get("arguments") or {})
            except KeyError as e:
                raise _Invalid("unknown prompt: %s" % e.args[0])
            except ValueError as e:
                raise _Invalid(str(e))
        if method == "resources/list":
            return {"resources": _resources.list_resources(self.ws)}
        if method == "resources/templates/list":
            return {"resourceTemplates": []}
        if method == "resources/read":
            uri = params.get("uri")
            if not isinstance(uri, str):
                raise _Invalid("resources/read needs a 'uri'")
            try:
                return {"contents": [_resources.read_resource(uri, self.ws)]}
            except FileNotFoundError as e:
                raise _Invalid(str(e))
        raise _NoMethod()

    # ------------------------------------------------------------------- tools
    def _call_tool(self, params: Dict[str, Any]) -> Dict[str, Any]:
        name = params.get("name")
        if name not in _tools.TOOLS:
            raise _Invalid("unknown tool: %r (tools: %s)" % (name, ", ".join(_tools.TOOLS)))
        tool = _tools.TOOLS[name]
        try:
            args = check_arguments(name, tool.schema, params.get("arguments"))
            if tool.op is None:
                if not args.get("warning") and not args.get("code"):
                    raise Refused("explain_warning: give 'warning' (the text) or 'code'")
                res = _explain.explain(args.get("warning"), args.get("code"))
            else:
                wargs = _tools.prepare(name, args, self.ws)
                if "timeBudgetS" in wargs:            # fit, twin_design, twin_track: a budget of their own, at most the server's
                    timeout = min(float(wargs.pop("timeBudgetS")), self.budget_s)
                elif tool.op in _LONG_OPS:
                    timeout = self.budget_s
                else:
                    timeout = min(tool.timeout_s, self.budget_s)
                res = self.worker.call(tool.op, wargs, timeout)
        except Refused as e:
            return _tool_result("refused: %s" % e, True)
        except WorkerTimeout as e:
            return _tool_result("timeout: %s" % e, True)
        except WorkerFailure as e:
            return _tool_result("error: %s" % e, True)
        extra: List[dict] = []
        if isinstance(res, dict) and "_imageBase64" in res:
            extra.append({"type": "image", "data": res.pop("_imageBase64"), "mimeType": "image/png"})
        return _tool_result(json.dumps(res, indent=1, default=str), False, res, extra)

    # ------------------------------------------------------------------- stdio
    def serve(self, stdin=None, stdout=None) -> int:
        stdin = stdin or sys.stdin.buffer
        stdout = stdout or sys.stdout.buffer
        try:
            for raw in stdin:
                raw = raw.strip()
                if not raw:
                    continue
                try:
                    msg = json.loads(raw.decode("utf-8-sig"))
                except (ValueError, UnicodeDecodeError):
                    reply: Optional[Dict[str, Any]] = _error(None, -32700, "parse error")
                else:
                    reply = self.handle(msg)
                if reply is not None:
                    stdout.write((json.dumps(reply, ensure_ascii=False, separators=(",", ":"), default=str) + "\n").encode("utf-8"))
                    stdout.flush()
        finally:
            self.worker.close()
        return 0


_LONG_OPS = frozenset({"run", "scan", "compare", "fit", "study", "backward", "twin_fit", "twin_validate", "twin_predict", "twin_design", "twin_track", "twin_init"})


class _Invalid(Exception):
    pass


class _NoMethod(Exception):
    pass


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(prog="ultrafast-mcp", description="Local MCP server (stdio) for UltrafastEngine; sandboxed to one workspace folder (docs/agents.md).")
    ap.add_argument("--workspace", default=".", help="the only folder the server reads from and writes to (default: the current folder)")
    ap.add_argument("--budget-s", type=float, default=300.0, help="time budget of one run or scan, seconds (default 300); the worker is killed after it")
    ap.add_argument("--memory-limit-gb", type=float, default=8.0, help="refuse a run or scan whose estimated memory is above this (default 8)")
    a = ap.parse_args(argv)
    if a.budget_s <= 0 or a.memory_limit_gb <= 0:
        ap.error("--budget-s and --memory-limit-gb must be positive")
    try:
        srv = McpServer(Path(a.workspace), budget_s=a.budget_s, memory_limit_bytes=int(a.memory_limit_gb * 1e9))
    except ValueError as e:
        print("ultrafast-mcp: %s" % e, file=sys.stderr)
        return 2
    print("ultrafast-mcp: workspace %s" % srv.ws.root, file=sys.stderr)
    return srv.serve()


if __name__ == "__main__":
    raise SystemExit(main())
