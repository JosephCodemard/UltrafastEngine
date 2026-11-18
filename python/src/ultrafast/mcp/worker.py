"""The sandboxed worker of the MCP server: one persistent subprocess that runs the engine.

The server process only parses JSON-RPC and applies the policy; every call that loads the engine goes to this child, so a run that
hangs or exhausts memory can be killed without taking the server down.

* :class:`Worker` ``(workspace_root, command=None, home=None)``; ``call(op, args, timeout_s)`` returns the child's result, raises
  :class:`WorkerFailure` (the operation raised: message for the agent), :class:`WorkerTimeout` (killed: the next call starts a new
  child) or :class:`~ultrafast.mcp.policy.Refused` (request too large, memory pre-flight).
* The child (``python -m ultrafast.mcp._worker_main``) has an **empty private** ``ULTRAFAST_HOME`` (no user plugins, materials or
  settings), the workspace as its working directory, and talks newline-delimited JSON over its pipes:
  ``{"id", "op", "args"}`` -> ``{"id", "ok", "result" | "error"}``.
* Limits: ``max_request_bytes`` (size of the arguments), and in the child a memory pre-flight for ``run`` and ``scan``
  (:func:`ultrafast.estimate_memory`, refused above ``memory_limit_bytes`` or 60 % of the free memory).
"""
from __future__ import annotations

import json
import os
import queue
import shutil
import subprocess
import sys
import tempfile
import threading
from pathlib import Path
from typing import Any, Dict, List, Optional

from .policy import Refused


class WorkerFailure(Exception):
    """The operation failed inside the worker (bad input, engine error); the message is for the agent."""


class WorkerTimeout(Exception):
    """The time budget ran out; the worker was killed and the next call starts a fresh one."""


class Worker:
    def __init__(self, workspace_root: Any, command: Optional[List[str]] = None, *, max_request_bytes: int = 4_000_000,
                 memory_limit_bytes: int = 8_000_000_000, env: Optional[Dict[str, str]] = None):
        self.root = Path(workspace_root).resolve()
        self.command = list(command) if command else [sys.executable, "-u", "-m", "ultrafast.mcp._worker_main"]
        self.max_request_bytes = int(max_request_bytes)
        self.memory_limit_bytes = int(memory_limit_bytes)
        self._extra_env = dict(env or {})
        self._home: Optional[str] = None
        self._proc: Optional[subprocess.Popen] = None
        self._lines: "queue.Queue[Optional[bytes]]" = queue.Queue()
        self._next_id = 0
        self.spawns = 0

    # ----------------------------------------------------------------- process
    def _spawn(self) -> None:
        if self._home is None:
            self._home = tempfile.mkdtemp(prefix="ultrafast-mcp-home-")
        env = dict(os.environ)
        env.update({"ULTRAFAST_HOME": self._home, "PYTHONIOENCODING": "utf-8", "PYTHONUNBUFFERED": "1",
                    "ULTRAFAST_MCP_MEMORY_LIMIT_BYTES": str(self.memory_limit_bytes), "MPLBACKEND": "Agg"})
        env.update(self._extra_env)
        flags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0
        self._lines = queue.Queue()
        self._err = open(os.path.join(self._home, "worker.log"), "ab")
        self._proc = subprocess.Popen(self.command, cwd=str(self.root), env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                      stderr=self._err, creationflags=flags)
        self.spawns += 1
        threading.Thread(target=self._pump, args=(self._proc, self._lines), daemon=True).start()

    @staticmethod
    def _pump(proc: subprocess.Popen, q: "queue.Queue[Optional[bytes]]") -> None:
        try:
            for line in iter(proc.stdout.readline, b""):  # type: ignore[union-attr]
                q.put(line)
        finally:
            q.put(None)

    @property
    def pid(self) -> Optional[int]:
        return self._proc.pid if self._proc is not None and self._proc.poll() is None else None

    def kill(self) -> None:
        p, self._proc = self._proc, None
        if p is None:
            return
        try:
            p.kill()
        except OSError:
            pass
        try:
            p.wait(timeout=10)
        except Exception:
            pass
        for stream in (p.stdin, p.stdout, getattr(self, "_err", None)):
            try:
                if stream is not None:
                    stream.close()
            except Exception:
                pass

    def close(self) -> None:
        self.kill()
        if self._home:
            shutil.rmtree(self._home, ignore_errors=True)
            self._home = None

    # -------------------------------------------------------------------- call
    def call(self, op: str, args: Dict[str, Any], timeout_s: float) -> Any:
        payload = json.dumps(args, default=str)
        if len(payload.encode("utf-8")) > self.max_request_bytes:
            raise Refused("request is %.1f MB, above the limit of %.1f MB: put large data in a file in the workspace and pass its path"
                          % (len(payload) / 1e6, self.max_request_bytes / 1e6))
        if self._proc is None or self._proc.poll() is not None:
            self.kill()
            self._spawn()
        self._next_id += 1
        rid = self._next_id
        msg = json.dumps({"id": rid, "op": op, "args": args}, default=str) + "\n"
        try:
            self._proc.stdin.write(msg.encode("utf-8"))   # type: ignore[union-attr]
            self._proc.stdin.flush()                      # type: ignore[union-attr]
        except OSError:
            self.kill()
            raise WorkerFailure("the worker process stopped; try again") from None
        try:
            while True:
                line = self._lines.get(timeout=timeout_s)
                if line is None:
                    code = self._proc.poll() if self._proc else None
                    self.kill()
                    raise WorkerFailure("the worker process exited unexpectedly (code %s), probably out of memory; try again with a smaller problem" % code)
                try:
                    reply = json.loads(line)
                except ValueError:
                    continue   # stray output of a library is not a reply
                if reply.get("id") == rid:
                    break
        except queue.Empty:
            self.kill()
            raise WorkerTimeout("time budget of %g s used up for '%s'; the worker was stopped. Use fidelity \"quick\", fewer points, "
                                "a shorter system or a coarser grid." % (timeout_s, op)) from None
        if reply.get("ok"):
            return reply.get("result")
        err = reply.get("error") or {}
        if err.get("type") == "Refused":
            raise Refused(err.get("message", "refused"))
        raise WorkerFailure(err.get("message", "worker error"))
