"""MCP server (ultrafast.mcp): handshake, the six tools, every refusal, a call over budget (killed, next call works), packaging.

Light: the server is driven with a fake worker (no engine) except one small end-to-end run marked ``requires_engine``.
"""
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from conftest import SRC, requires_engine
from ultrafast.mcp import McpServer
from ultrafast.mcp import policy, resources, tools
from ultrafast.mcp.explain import explain
from ultrafast.mcp.worker import Worker

REPO = Path(__file__).resolve().parents[2]
FAKE = '''
import json, os, sys, time
for line in sys.stdin:
    r = json.loads(line); a = r["args"]; s = a.get("system")
    if isinstance(s, dict) and s.get("sleep"):
        time.sleep(s["sleep"])
    sys.stdout.write(json.dumps({"id": r["id"], "ok": True, "result": {"op": r["op"], "pid": os.getpid(), "home": os.environ.get("ULTRAFAST_HOME")}}) + "\\n")
    sys.stdout.flush()
'''
TOOL_NAMES = {"describe", "validate", "run", "scan", "explain_warning", "plot"}


@pytest.fixture
def server(tmp_path):
    fake = tmp_path / "fake_worker.py"
    fake.write_text(FAKE, encoding="utf-8")
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "plate.ufs").write_text("{}", encoding="utf-8")
    (ws / "run.json").write_text("{}", encoding="utf-8")
    srv = McpServer(ws, worker=Worker(ws, command=[sys.executable, str(fake)]), budget_s=2)
    yield srv
    srv.worker.close()


def rpc(srv, method, params=None, rid=1):
    msg = {"jsonrpc": "2.0", "id": rid, "method": method}
    if params is not None:
        msg["params"] = params
    return srv.handle(msg)


def call(srv, name, args):
    r = rpc(srv, "tools/call", {"name": name, "arguments": args})
    assert "result" in r, r
    return r["result"]


def text(res):
    return res["content"][0]["text"]


# ---------------------------------------------------------------- handshake and protocol
def test_initialize_legacy(server):
    r = rpc(server, "initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}})["result"]
    assert r["protocolVersion"] == "2025-06-18" and "tools" in r["capabilities"] and r["serverInfo"]["name"] == "ultrafast-mcp"
    assert "resultType" not in r
    assert rpc(server, "initialize", {"protocolVersion": "1999-01-01"})["result"]["protocolVersion"] == "2025-11-25"
    assert server.handle({"jsonrpc": "2.0", "method": "notifications/initialized"}) is None


def test_discover_and_modern_requests(server):
    meta = {"io.modelcontextprotocol/protocolVersion": "2026-07-28", "io.modelcontextprotocol/clientCapabilities": {}}
    r = rpc(server, "server/discover", {"_meta": meta})["result"]
    assert r["resultType"] == "complete" and "2026-07-28" in r["supportedVersions"] and "2025-11-25" in r["supportedVersions"]
    assert r["_meta"]["io.modelcontextprotocol/serverInfo"]["name"] == "ultrafast-mcp"
    r = rpc(server, "tools/list", {"_meta": meta})["result"]
    assert r["resultType"] == "complete" and len(r["tools"]) == 6
    bad = rpc(server, "tools/list", {"_meta": {"io.modelcontextprotocol/protocolVersion": "1900-01-01", "io.modelcontextprotocol/clientCapabilities": {}}})
    assert bad["error"]["code"] == -32022 and "2026-07-28" in bad["error"]["data"]["supported"]
    assert rpc(server, "tools/list", {"_meta": {"io.modelcontextprotocol/protocolVersion": "2026-07-28"}})["error"]["code"] == -32602


def test_ping_unknown_method_notifications_and_bad_messages(server):
    assert rpc(server, "ping")["result"] == {}
    assert rpc(server, "no/such/method")["error"]["code"] == -32601
    assert server.handle({"jsonrpc": "2.0", "method": "no/such/notification", "params": {}}) is None
    assert server.handle({"jsonrpc": "2.0", "method": "ping"}) is None            # a request without id is a notification
    assert server.handle([{"jsonrpc": "2.0", "id": 1, "method": "ping"}])["error"]["code"] == -32600
    assert server.handle({"id": 1, "method": "ping"})["error"]["code"] == -32600
    assert rpc(server, "tools/list", rid=None)["error"]["code"] == -32600
    assert rpc(server, "tools/call", {"name": "nope", "arguments": {}})["error"]["code"] == -32602


def test_tools_list_has_six_tools_with_schemas(server):
    ts = rpc(server, "tools/list")["result"]["tools"]
    assert {t["name"] for t in ts} == TOOL_NAMES and len(ts) == 6
    for t in ts:
        assert t["description"] and t["inputSchema"]["type"] == "object"
    run = next(t for t in ts if t["name"] == "run")
    assert "fidelity" in run["inputSchema"]["properties"]
    assert "fidelity" in next(t for t in ts if t["name"] == "scan")["inputSchema"]["properties"]


# --------------------------------------------------------------------------- refusals
GOOD_RUN = {"pulse": {"durationFwhmS": 1e-13}}
REFUSALS = [
    ("run", {"system": "plate.ufs", "run": {"pulse": {"energyJ": "@energy"}}, "name": "a"}, "'@'"),
    ("run", {"system": "plate.ufs", "run": {"instruments": [{"kind": "callback"}]}, "name": "a"}, "user code"),
    ("run", {"system": "plate.ufs", "run": {"instruments": [{"kind": "python"}]}, "name": "a"}, "user code"),
    ("run", {"system": "plate.ufs", "run": {"x": {"kind": "expression"}}, "name": "a"}, "user code"),
    ("validate", {"system": {"elements": [{"id": "e", "type": "user"}]}}, "user code"),
    ("validate", {"system": "@fn"}, "callbacks"),
    ("run", {"system": "plate.ufs", "run": {"fit": {}}, "name": "a"}, "'fit'"),
    ("run", {"system": "plate.ufs", "run": {"measured": {"dataFile": "../../outside.csv"}}, "name": "a"}, "outside the workspace"),
    ("validate", {"system": "../plate.ufs"}, "outside the workspace"),
    ("validate", {"system": "missing.ufs"}, "not found"),
    ("run", {"system": "plate.ufs", "run": "run.json", "name": "../escape"}, "bad name"),
    ("run", {"system": "plate.ufs", "run": "run.json", "name": ""}, "bad name"),
    ("run", {"system": "plate.ufs", "run": "run.json", "name": "a/b"}, "bad name"),
    ("run", {"system": "plate.ufs", "run": "run.json", "name": "a", "extra": 1}, "unknown argument"),
    ("run", {"system": "plate.ufs", "run": "run.json"}, "missing required"),
    ("run", {"system": "plate.ufs", "run": "run.json", "name": "a", "fidelity": "turbo"}, "must be one of"),
    ("scan", {"system": "plate.ufs", "run": "run.json", "axes": {"material:glass/n2": [1e-20]}, "outputs": ["pulse:end/fwhmS"]}, "not allowed"),
    ("scan", {"system": "plate.ufs", "run": "run.json", "axes": {"physics/raman": [1]}, "outputs": ["pulse:end/fwhmS"]}, "not allowed"),
    ("scan", {"system": "plate.ufs", "run": "run.json", "axes": {"pulse/energyJ": list(range(1, 402))}, "outputs": ["pulse:end/fwhmS"]}, "above the limit"),
    ("scan", {"system": "plate.ufs", "run": "run.json", "axes": {"pulse/energyJ": [1e-6]}, "outputs": ["rm -rf /"]}, "not valid"),
    ("scan", {"system": "plate.ufs", "run": "run.json", "axes": {"pulse/energyJ": []}, "outputs": ["pulse:end/fwhmS"]}, "non-empty"),
    ("plot", {"result": "../../etc"}, "bad name"),
    ("plot", {"result": "a", "figure": "bogus"}, "must be one of"),
    ("explain_warning", {}, "give 'warning'"),
]


@pytest.mark.parametrize("tool,args,fragment", REFUSALS, ids=[f"{i}-{r[0]}" for i, r in enumerate(REFUSALS)])
def test_refusals_are_tool_errors_and_never_reach_the_worker(server, tool, args, fragment):
    res = call(server, tool, args)
    assert res["isError"] is True and fragment in text(res), text(res)
    assert server.worker.spawns == 0


def test_absolute_path_outside_workspace_and_symlink_refused(server, tmp_path):
    outside = tmp_path / "outside.ufs"
    outside.write_text("{}", encoding="utf-8")
    res = call(server, "validate", {"system": str(outside)})
    assert res["isError"] and "outside the workspace" in text(res)
    link = server.ws.root / "link"
    try:
        os.symlink(tmp_path, link, target_is_directory=True)
    except (OSError, NotImplementedError):
        if os.name != "nt" or subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(tmp_path)], capture_output=True).returncode != 0:
            pytest.skip("cannot create symbolic links or junctions here")   # Windows without the privilege: a junction needs none
    res = call(server, "validate", {"system": "link/outside.ufs"})
    assert res["isError"] and "symbolic link or junction" in text(res)
    assert server.worker.spawns == 0
    try:                                  # remove the link itself first: it points back at a parent of the workspace
        os.unlink(link)
    except OSError:
        os.rmdir(link)


def test_workspace_must_be_a_folder(tmp_path):
    with pytest.raises(ValueError):
        policy.Workspace(tmp_path / "nope")


def test_valid_calls_reach_the_worker_with_private_home(server):
    res = call(server, "validate", {"system": "plate.ufs", "run": "run.json"})
    assert not res["isError"]
    out = res["structuredContent"]
    assert out["op"] == "validate" and out["home"] != os.environ.get("ULTRAFAST_HOME")
    assert set(os.listdir(out["home"])) <= {"worker.log"}                        # empty private ULTRAFAST_HOME
    res = call(server, "run", {"system": "plate.ufs", "run": GOOD_RUN, "name": "r1", "fidelity": "quick"})
    assert not res["isError"] and res["structuredContent"]["op"] == "run"


# -------------------------------------------------------- over budget: killed, next call works
def test_call_over_budget_is_killed_and_the_next_call_works(server):
    server.budget_s = 1
    ok = call(server, "validate", {"system": {"sleep": 0}})
    pid1 = ok["structuredContent"]["pid"]
    slow = call(server, "validate", {"system": {"sleep": 30}})
    assert slow["isError"] and "time budget" in text(slow)
    assert server.worker.pid is None                                              # killed
    again = call(server, "validate", {"system": {"sleep": 0}})
    assert not again["isError"] and again["structuredContent"]["pid"] != pid1
    assert server.worker.spawns == 2


def test_request_size_refused_before_the_worker(tmp_path):
    w = Worker(tmp_path, command=[sys.executable, "-c", "pass"], max_request_bytes=100)
    with pytest.raises(policy.Refused, match="MB"):
        w.call("run", {"system": {"x": "y" * 500}}, 1)
    assert w.spawns == 0


# --------------------------------------------------------------- explain and resources
def test_explain_warning():
    assert explain(code="zeroFrequency")["matched"] == "zeroFrequency"
    assert explain("Raman dropped (quick): B = 4 rad and a 100 fs pulse")["matched"] == "quickRaman"
    assert explain("quick: spatial model collapsed, peak power 0.5 of the critical power")["matched"] == "quickSpatial"
    assert explain("something nobody has seen")["matched"] is None


def test_explain_tool_needs_no_worker(server):
    res = call(server, "explain_warning", {"warning": "strongly nonlinear: B = 5 rad"})
    assert res["structuredContent"]["matched"] == "quickStronglyNonlinear" and server.worker.spawns == 0


def test_resources(server):
    uris = {r["uri"] for r in rpc(server, "resources/list")["result"]["resources"]}
    assert uris == {"ultrafast://schema/run", "ultrafast://schema/system", "ultrafast://fidelity", "ultrafast://examples"}
    fid = rpc(server, "resources/read", {"uri": "ultrafast://fidelity"})["result"]["contents"][0]
    assert "quick" in fid["text"] and fid["mimeType"] == "text/markdown"
    if (REPO / "schemas" / "run.schema.json").is_file():
        sch = rpc(server, "resources/read", {"uri": "ultrafast://schema/run"})["result"]["contents"][0]
        assert json.loads(sch["text"])
    assert rpc(server, "resources/read", {"uri": "ultrafast://nope"})["error"]["code"] == -32602
    assert "examples" in resources._examples_index(server.ws).lower()


# -------------------------------------------------------------------- stdio end to end
def test_stdio_process_handshake(tmp_path):
    env = dict(os.environ, PYTHONPATH=str(SRC))
    p = subprocess.Popen([sys.executable, "-m", "ultrafast.mcp", "--workspace", str(tmp_path)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                         stderr=subprocess.PIPE, env=env)
    msgs = [{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-11-25"}},
            {"jsonrpc": "2.0", "method": "notifications/initialized"},
            {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
            {"jsonrpc": "2.0", "id": 3, "method": "bogus"}]
    out, err = p.communicate("".join(json.dumps(m) + "\n" for m in msgs).encode() + b"not json\n", timeout=60)
    lines = [json.loads(l) for l in out.decode().splitlines()]
    assert [l.get("id") for l in lines] == [1, 2, 3, None]                        # the notification got no reply; stdout holds JSON only
    assert len(lines[1]["result"]["tools"]) == 6 and lines[2]["error"]["code"] == -32601 and lines[3]["error"]["code"] == -32700
    assert b"workspace" in err


def test_entry_point_and_package_are_declared():
    text_ = (REPO / "python" / "pyproject.toml").read_text(encoding="utf-8")
    assert 'ultrafast-mcp = "ultrafast.mcp.server:main"' in text_
    setuptools = pytest.importorskip("setuptools")
    assert "ultrafast.mcp" in setuptools.find_packages(str(REPO / "python" / "src"))
    from ultrafast.mcp.server import main
    assert callable(main)


# ------------------------------------------------------- one small real run (needs the engine)
@requires_engine
def test_real_run_validate_plot(tmp_path):
    ex = REPO / "docs" / "examples" / "GettingStarted" / "spm-plate"
    ws = tmp_path / "ws"
    ws.mkdir()
    shutil.copy(ex / "plate.ufs", ws / "plate.ufs")
    shutil.copy(ex / "run.json", ws / "run.json")
    srv = McpServer(ws, worker=Worker(ws, env={"PYTHONPATH": str(SRC)}), budget_s=240)
    try:
        v = call(srv, "validate", {"system": "plate.ufs", "run": "run.json"})
        assert not v["isError"] and v["structuredContent"]["ok"] is True, text(v)
        d = call(srv, "describe", {"system": "plate.ufs"})
        assert not d["isError"] and d["structuredContent"]["elementCount"] >= 1
        r = call(srv, "run", {"system": "plate.ufs", "run": "run.json", "name": "quick1", "fidelity": "quick"})
        assert not r["isError"], text(r)
        s = r["structuredContent"]
        assert (ws / ".ultrafast-mcp" / "quick1" / "summary.json").is_file()
        assert s["fidelityReport"]["level"] == "quick" and "advice" in s and "validity" in s and s["output"]["energyJ"] > 0
        assert not list(ws.glob(".mcp-*"))                                         # the temporary run file is gone
        pl = call(srv, "plot", {"result": "quick1", "figure": "overview", "style": "paper", "dpi": 60})
        assert not pl["isError"], text(pl)
        assert any(c["type"] == "image" for c in pl["content"]) and (ws / ".ultrafast-mcp" / "quick1" / "plots").is_dir()
    finally:
        srv.worker.close()


def test_twin_serve_starts_the_mcp_server(tmp_path):
    twin = REPO / "plugins" / "twin" / "python"
    env = dict(os.environ, PYTHONPATH=os.pathsep.join([str(SRC), str(twin)]))
    p = subprocess.run([sys.executable, "-m", "ultrafast_twin", "serve", str(tmp_path)], input=b'{"jsonrpc":"2.0","id":1,"method":"tools/list"}\n',
                       capture_output=True, env=env, timeout=60)
    assert p.returncode == 0, p.stderr.decode()
    assert len(json.loads(p.stdout.decode().splitlines()[0])["result"]["tools"]) == 6
