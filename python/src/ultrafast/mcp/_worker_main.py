"""Entry point of the MCP worker child: ``python -m ultrafast.mcp._worker_main``.

Reads one JSON request per line from stdin (``{"id", "op", "args"}``), runs it with :mod:`ultrafast.mcp.ops` and writes one JSON reply per
line to stdout. Anything a library prints to stdout is redirected to stderr so it cannot corrupt the stream.
"""
from __future__ import annotations

import json
import sys
import traceback


def main() -> int:
    out = sys.stdout.buffer
    sys.stdout = sys.stderr          # library prints go to the log, not into the reply stream
    from .policy import Refused
    from . import ops
    for raw in sys.stdin.buffer:
        try:
            req = json.loads(raw)
        except ValueError:
            continue
        rid = req.get("id")
        try:
            fn = ops.OPS[req["op"]]
            reply = {"id": rid, "ok": True, "result": fn(req.get("args") or {})}
        except Refused as e:
            reply = {"id": rid, "ok": False, "error": {"type": "Refused", "message": str(e)}}
        except Exception as e:   # engine errors are for the agent to read
            sys.stderr.write(traceback.format_exc())
            reply = {"id": rid, "ok": False, "error": {"type": type(e).__name__, "message": "%s: %s" % (type(e).__name__, e)}}
        out.write((json.dumps(reply, default=str) + "\n").encode("utf-8"))
        out.flush()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
