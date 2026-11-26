"""MCP server (ultrafast.mcp): handshake, the sixteen tools, prompts, every refusal, a call over budget (killed, next call works), packaging.

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
    f = a.get("folder")
    if f and os.path.isfile(os.path.join(f, "sleep")):       # a twin project folder with a 'sleep' file: a slow twin call
        time.sleep(float(open(os.path.join(f, "sleep")).read()))
    sys.stdout.write(json.dumps({"id": r["id"], "ok": True, "result": {"op": r["op"], "pid": os.getpid(), "home": os.environ.get("ULTRAFAST_HOME")}}) + "\\n")
    sys.stdout.flush()
'''
TOOL_NAMES = {"describe", "validate", "run", "scan", "explain_warning", "plot", "compare_to_measured", "fit", "study", "backward", "twin_fit", "twin_validate", "twin_predict",
              "twin_design", "twin_track", "twin_init"}


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
    assert r["resultType"] == "complete" and len(r["tools"]) == 16
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


def test_tools_list_has_sixteen_tools_with_schemas(server):
    ts = rpc(server, "tools/list")["result"]["tools"]
    assert {t["name"] for t in ts} == TOOL_NAMES and len(ts) == 16
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
    ("fit", {"system": "plate.ufs", "run": {"pulse": {}}, "name": "f"}, "no 'fit' section"),
    ("fit", {"system": "plate.ufs", "run": {"fit": {}, "study": {}}, "name": "f"}, "'study'"),
    ("fit", {"system": "plate.ufs", "run": {"fit": {"callback": "x"}}, "name": "f"}, "user code"),
    ("fit", {"system": "plate.ufs", "run": {"fit": {}, "measured": {"dataFile": "/etc/x.csv"}}, "name": "f"}, "outside the workspace"),
    ("fit", {"system": "plate.ufs", "run": {"fit": {}}, "name": "f", "timeBudgetS": 0}, "must be greater"),
    ("fit", {"system": "plate.ufs", "run": {"fit": {}}, "name": "../f"}, "bad name"),
    ("compare_to_measured", {"system": "plate.ufs", "run": {"pulse": {}}}, "no instrument"),
    ("compare_to_measured", {"system": "plate.ufs", "run": {"fit": {}, "instruments": [{"kind": "spectrometer", "measured": {"csv": "a.csv"}}]}}, "use the fit tool"),
    ("compare_to_measured", {"system": "plate.ufs", "run": {"instruments": [{"kind": "spectrometer", "measured": {"csv": "../a.csv"}}]}}, "outside the workspace"),
    ("compare_to_measured", {"system": "plate.ufs", "run": {"instruments": [{"kind": "spectrometer", "measured": {"csv": "a.csv"}}]}, "name": "a b"}, "bad name"),
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


FILE_REFUSALS = [
    ("run.json", '// comment\n{"pulse": {"energyJ": "@energy"}, }', "starts with '@'"),
    ("run.json", '{"instruments": [{"kind": "python"}]}', "user code"),
    ("run.json", '{"fit": {"parameters": []}}', "'fit'"),
    ("run.json", '{"measured": {"dataFile": "../../outside.csv"}}', "outside the workspace"),
    ("run.json", '{"pulse": {"spectrum": {"x": "../outside.csv"}}}', "outside the workspace"),
    ("run.json", '{"phaseTargetRad": "C:\\\\Windows\\\\x.npz"}', "outside the workspace"),
    ("run.json", '{"phaseTargetRad": "/etc/passwd.dat"}', "outside the workspace"),
    ("plate.ufs", '{"elements": [{"id": "e", "type": "user"}]}', "user code"),
    ("plate.ufs", '{"elements": [{"id": "e", "type": "plate", "beam": {"mapFile": "../m.npz"}}]}', "outside the workspace"),
    ("plate.ufs", 'not json', "not valid JSON"),
]


@pytest.mark.parametrize("fname,content,fragment", FILE_REFUSALS, ids=[f"{i}-{r[0]}" for i, r in enumerate(FILE_REFUSALS)])
def test_file_arguments_get_the_same_policy_as_inline_ones(server, fname, content, fragment):
    """A file in the workspace is read and checked too (the engine will read the same text)."""
    (server.ws.root / fname).write_text(content, encoding="utf-8")
    res = call(server, "run", {"system": "plate.ufs", "run": "run.json", "name": "a"})
    assert res["isError"] is True and fragment in text(res), text(res)
    assert server.worker.spawns == 0


def test_relative_paths_in_a_file_are_taken_from_the_files_folder(server):
    sub = server.ws.root / "data"
    sub.mkdir()
    (sub / "run.json").write_text('{"measured": {"dataFile": "spec.csv"}, "$schema": "../../schemas/run.schema.json"}', encoding="utf-8")
    res = call(server, "validate", {"system": "plate.ufs", "run": "data/run.json"})
    assert not res["isError"], text(res)
    (sub / "run.json").write_text('{"measured": {"dataFile": "../../spec.csv"}}', encoding="utf-8")
    assert call(server, "validate", {"system": "plate.ufs", "run": "data/run.json"})["isError"]


def test_ordinary_strings_are_not_taken_for_paths():
    for ok in ("~5 fs", "pulse/energyJ", "element:plate/lengthM", "m/s", "a..b", "FusedSilica", "x"):
        assert not policy.looks_like_path(ok), ok
    for bad in ("../x", "..\\x", "/abs", "C:\\x", "C:/x", "data.CSV", "map.npz"):
        assert policy.looks_like_path(bad), bad


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


def test_fit_and_compare_reach_the_worker_when_the_spec_is_fit_for_them(server):
    run = {"fit": {}, "instruments": [{"kind": "spectrometer", "name": "osa", "measured": {"csv": "osa.csv"}}]}
    res = call(server, "fit", {"system": "plate.ufs", "run": run, "name": "f1", "fidelity": "quick", "timeBudgetS": 1.5})
    assert not res["isError"] and res["structuredContent"]["op"] == "fit", text(res)
    run.pop("fit")
    res = call(server, "compare_to_measured", {"system": "plate.ufs", "run": run})
    assert not res["isError"] and res["structuredContent"]["op"] == "compare", text(res)


def test_fit_is_killed_at_its_own_time_budget_and_the_next_call_works(server):
    server.budget_s = 30
    run = {"fit": {}}
    slow = call(server, "fit", {"system": {"sleep": 30}, "run": run, "name": "f2", "timeBudgetS": 1})
    assert slow["isError"] and "time budget" in text(slow)
    assert server.worker.pid is None
    assert not call(server, "validate", {"system": {"sleep": 0}})["isError"]


# -------------------------------------------------------------------------------- prompts
def test_prompts_list_and_get(server):
    ps = rpc(server, "prompts/list")["result"]["prompts"]
    assert {p["name"] for p in ps} == {"explore-in-quick", "compare-to-measurement"}
    assert all(p["description"] and p["arguments"] for p in ps)
    g = rpc(server, "prompts/get", {"name": "explore-in-quick", "arguments": {"system": "plate.ufs", "goal": "the shortest pulse"}})["result"]
    body = g["messages"][0]["content"]["text"]
    assert "plate.ufs" in body and "the shortest pulse" in body and "fidelity 'full'" in body
    assert "compare_to_measured" in rpc(server, "prompts/get", {"name": "compare-to-measurement", "arguments": {"system": "a.ufs", "recording": "osa.csv"}})["result"]["messages"][0]["content"]["text"]
    assert rpc(server, "prompts/get", {"name": "explore-in-quick", "arguments": {"system": "a.ufs"}})["error"]["code"] == -32602
    assert rpc(server, "prompts/get", {"name": "nope"})["error"]["code"] == -32602
    assert "prompts" in rpc(server, "initialize", {"protocolVersion": "2025-11-25"})["result"]["capabilities"]
    assert server.worker.spawns == 0


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
    assert len(lines[1]["result"]["tools"]) == 16 and lines[2]["error"]["code"] == -32601 and lines[3]["error"]["code"] == -32700
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


@requires_engine
def test_real_compare_to_measured_and_fit(tmp_path):
    """A small recorded-data example: the comparison returns chi2 per instrument; a short fit lowers chi2 and writes its folder; a fit over a tiny budget is killed and the server lives."""
    ex = REPO / "docs" / "examples" / "Diagnostics" / "measured-data"
    ws = tmp_path / "ws"
    ws.mkdir()
    shutil.copy(ex / "line.ufs", ws / "line.ufs")
    shutil.copytree(ex / "data", ws / "data")
    def load(name):
        return json.loads(policy.strip_jsonc((ex / name).read_text(encoding="utf-8")))
    cmp_run, fit_run = load("run-compare.json"), load("run-fit.json")
    fit_run["fit"]["maxIterations"] = 3
    srv = McpServer(ws, worker=Worker(ws, env={"PYTHONPATH": str(SRC)}), budget_s=240)
    try:
        c = call(srv, "compare_to_measured", {"system": "line.ufs", "run": cmp_run, "fidelity": "quick", "name": "cmp"})
        assert not c["isError"], text(c)
        s = c["structuredContent"]
        assert {x["instrument"] for x in s["comparisons"]} >= {"fibre_spectrum", "ac_out"} and s["totalChi2"] > 0 and "advice" in s
        assert (ws / ".ultrafast-mcp" / "cmp" / "summary.json").is_file()
        f = call(srv, "fit", {"system": "line.ufs", "run": fit_run, "name": "fit1", "fidelity": "quick", "timeBudgetS": 200})
        assert not f["isError"], text(f)
        fit = f["structuredContent"]["fit"]
        assert fit["chi2Final"] < fit["chi2Initial"] and len(fit["parameters"]) == 3
        assert (ws / ".ultrafast-mcp" / "fit1" / "summary.json").is_file()
        slow = call(srv, "fit", {"system": "line.ufs", "run": fit_run, "name": "fit2", "timeBudgetS": 0.2})
        assert slow["isError"] and "time budget" in text(slow)
        assert not call(srv, "validate", {"system": "line.ufs"})["isError"]            # the next call respawns the worker
    finally:
        srv.worker.close()


def test_twin_serve_starts_the_mcp_server(tmp_path):
    twin = REPO / "plugins" / "twin" / "python"
    env = dict(os.environ, PYTHONPATH=os.pathsep.join([str(SRC), str(twin)]))
    p = subprocess.run([sys.executable, "-m", "ultrafast_twin", "serve", str(tmp_path)], input=b'{"jsonrpc":"2.0","id":1,"method":"tools/list"}\n',
                       capture_output=True, env=env, timeout=60)
    assert p.returncode == 0, p.stderr.decode()
    assert len(json.loads(p.stdout.decode().splitlines()[0])["result"]["tools"]) == 16


# ------------------------------------------------ study, backward and the twin tools: refusals (fake worker)
PLATE = {"format": "ultrafast-system", "version": 2, "name": "p", "elements": [{"id": "plate", "type": "plate", "material": "FusedSilica", "thicknessM": 3e-3}]}
PAR = {"path": "element:plate/thicknessM", "min": 1e-3, "max": 5e-3, "points": 3}


def test_study_is_checked_before_the_worker(server):
    ok = call(server, "study", {"system": "plate.ufs", "run": "run.json", "parameters": [PAR], "outputs": ["pulse:end/fwhmS"]})
    assert not ok["isError"] and ok["structuredContent"]["op"] == "study"
    base = {"system": "plate.ufs", "run": "run.json", "outputs": ["pulse:end/fwhmS"]}
    for args, word in [
        ({"parameters": [dict(PAR, points=900)]}, "above the limit"),
        ({"parameters": [{"path": "system/name", "min": 0, "max": 1}]}, "is not allowed"),
        ({"parameters": [dict(PAR, python="x")]}, "unknown key"),
        ({"parameters": [dict(PAR, min=float("nan"))]}, "finite"),
        ({"parameters": [PAR], "mode": "optimize"}, "objective"),
        ({"parameters": [PAR], "mode": "optimize", "objective": {"output": "pulse:end/fwhmS", "goal": "best"}}, "objective"),
        ({"parameters": [PAR], "mode": "sensitivity", "sensitivity": {"method": "x"}}, "method"),
        ({"parameters": [PAR] * 5}, "1 to 4"),
        ({"parameters": [PAR], "sampling": "random", "samples": 401}, None),
    ]:
        r = call(server, "study", dict(base, **args))
        assert r["isError"], args
        if word:
            assert word in text(r), (args, text(r))
    # an optimiser is capped at 400 evaluations
    r = call(server, "study", dict(base, parameters=[PAR], mode="optimize", objective={"output": "pulse:end/fwhmS", "goal": "min", "maxEvaluations": 10 ** 6}))
    assert not r["isError"]
    # a study section inside the run spec is not run through another tool
    r = call(server, "run", {"system": "plate.ufs", "run": {"study": {}}, "name": "x"})
    assert r["isError"] and "study tool" in text(r)


def test_backward_needs_its_section_and_other_tools_refuse_it(server):
    r = call(server, "backward", {"system": "plate.ufs", "run": {"pulse": {}}, "name": "b"})
    assert r["isError"] and "no 'backward' section" in text(r)
    r = call(server, "backward", {"system": "plate.ufs", "run": {"backward": {"from": "end", "to": "input"}}, "name": "b"})
    assert not r["isError"] and r["structuredContent"]["op"] == "backward"
    r = call(server, "run", {"system": "plate.ufs", "run": {"backward": {}}, "name": "x"})
    assert r["isError"] and "backward tool" in text(r)
    r = call(server, "backward", {"system": "plate.ufs", "run": {"backward": {}, "fit": {}}, "name": "b"})
    assert r["isError"] and "fit tool" in text(r)


def test_twin_tools_stay_in_the_workspace_and_read_a_project(server, tmp_path):
    ws = server.ws.root
    r = call(server, "twin_validate", {"folder": "nope"})
    assert r["isError"]
    r = call(server, "twin_validate", {"folder": "../x"})
    assert r["isError"] and "outside the workspace" in text(r)
    (ws / "lab").mkdir()
    r = call(server, "twin_validate", {"folder": "lab"})
    assert r["isError"] and "twin.json" in text(r)
    (ws / "lab" / "twin.json").write_text(json.dumps({"format": "ultrafast-twin", "system": "system.ufs", "run": {"pulse": {"kind": "callback"}}}), encoding="utf-8")
    (ws / "lab" / "system.ufs").write_text("{}", encoding="utf-8")
    r = call(server, "twin_predict", {"folder": "lab"})
    assert r["isError"] and "callback" in text(r)
    (ws / "lab" / "twin.json").write_text(json.dumps({"format": "ultrafast-twin", "system": "system.ufs", "run": {}}), encoding="utf-8")
    for tool, args, op in [("twin_fit", {"name": "f1"}, "twin_fit"), ("twin_validate", {"samples": 6}, "twin_validate"),
                           ("twin_predict", {"variables": {"energyJ": 1e-6}, "set": {"pulse/energyJ": 1e-6}}, "twin_predict")]:
        r = call(server, tool, dict(args, folder="lab"))
        assert not r["isError"] and r["structuredContent"]["op"] == op, (tool, text(r))
    for args in ({"set": {"system/name": 1}}, {"set": {"pulse/energyJ": "x"}}, {"variables": {"e": True}}):
        assert call(server, "twin_predict", dict(args, folder="lab"))["isError"]
    assert call(server, "twin_fit", {"folder": "lab", "name": "../evil"})["isError"]
    assert call(server, "twin_validate", {"folder": "lab", "samples": 100})["isError"]       # the schema's maximum


# ------------------------------------------------ twin_design, twin_track, twin_init: policy and budget (fake worker)
LAB_SYSTEM = {"elements": [{"id": "filter", "type": "transform"}, {"id": "kerr", "type": "transform"}]}


@pytest.fixture
def lab(server):
    """A twin project folder the policy accepts (the fake worker never reads it)."""
    d = server.ws.root / "lab"
    (d / "data").mkdir(parents=True)
    (d / "twin.json").write_text(json.dumps({"format": "ultrafast-twin", "system": "system.ufs", "run": {}}), encoding="utf-8")
    (d / "system.ufs").write_text(json.dumps(LAB_SYSTEM), encoding="utf-8")
    (d / "data" / "day1.csv").write_text("1030,1\n", encoding="utf-8")
    return d


def test_twin_design_track_and_init_reach_the_worker(server, lab):
    for tool, args in [("twin_design", {"variable": "energyJ", "values": [0.5e-6, 2e-6], "setKey": "pulse/energyJ", "picks": 2, "timeBudgetS": 1.5}),
                       ("twin_design", {"placement": True}),
                       ("twin_design", {"candidates": [{"name": "a", "variables": {"energyJ": 1e-6}, "set": {"pulse/energyJ": 1e-6, "instrument:spec/at": "kerr"}}],
                                        "weights": [1, 0, 0], "noiseSigma": 0.02}),
                       ("twin_track", {"measurements": [{"data": "data/day1.csv", "variables": {"energyJ": 1.2e-6}, "label": "day 1"}],
                                       "process": {"filter.centerNm": 0.3}, "members": 8, "alarmSigmas": 3, "seed": 2, "timeBudgetS": 2})]:
        r = call(server, tool, dict(args, folder="lab"))
        assert not r["isError"] and r["structuredContent"]["op"] == tool, (tool, text(r))
    r = call(server, "twin_init", {"folder": "new-lab", "template": "synthetic-lab-saturating"})
    assert not r["isError"] and r["structuredContent"]["op"] == "twin_init", text(r)
    assert not call(server, "twin_init", {"folder": "lab", "force": True})["isError"]


TWIN_REFUSALS = [
    ("twin_design", {}, "exactly one of"),
    ("twin_design", {"variable": "energyJ", "placement": True}, "exactly one of"),
    ("twin_design", {"variable": "energyJ"}, "'values'"),
    ("twin_design", {"variable": "energyJ", "values": [1e-6, float("nan")]}, "finite"),
    ("twin_design", {"variable": "energyJ", "values": [1e-6] * 13}, "above the limit"),
    ("twin_design", {"variable": "energy J", "values": [1e-6]}, "not a valid name"),
    ("twin_design", {"variable": "energyJ", "values": [1e-6], "setKey": "physics/raman"}, "not allowed"),
    ("twin_design", {"placement": True, "values": [1]}, "go with 'variable'"),
    ("twin_design", {"variable": "energyJ", "values": [1e-6], "picks": 2}, "more than"),
    ("twin_design", {"candidates": [{"name": "a"}, {"name": "a"}]}, "unique"),
    ("twin_design", {"candidates": [{"name": "a", "python": "x"}]}, "unknown key"),
    ("twin_design", {"candidates": [{"name": "a", "set": {"system/name": 1}}]}, "not allowed"),
    ("twin_design", {"candidates": [{"name": "a", "set": {"pulse/energyJ": "@e"}}]}, "finite number"),
    ("twin_design", {"candidates": [{"name": "a", "set": {"instrument:spec/at": "../x"}}]}, "element id"),
    ("twin_design", {"candidates": [{"name": "a", "variables": {"energyJ": True}}]}, "finite number"),
    ("twin_design", {"candidates": [{"variables": {}}]}, "'name'"),
    ("twin_design", {"variable": "energyJ", "values": [1e-6], "weights": [-1]}, "non-negative"),
    ("twin_design", {"variable": "energyJ", "values": [1e-6], "noiseSigma": 0}, "must be greater"),
    ("twin_design", {"variable": "energyJ", "values": [1e-6], "timeBudgetS": -1}, "must be greater"),
    ("twin_design", {"variable": "energyJ", "values": [1e-6], "extra": 1}, "unknown argument"),
    ("twin_track", {}, "missing required"),
    ("twin_track", {"measurements": []}, "non-empty"),
    ("twin_track", {"measurements": [{"data": "data/missing.csv"}]}, "not found"),
    ("twin_track", {"measurements": [{"data": "../../outside.csv"}]}, "outside the workspace"),
    ("twin_track", {"measurements": [{"data": "data"}]}, "not a file"),
    ("twin_track", {"measurements": [{"data": "data/day1.csv", "python": 1}]}, "unknown key"),
    ("twin_track", {"measurements": [{"data": "data/day1.csv"}] * 13}, "above the limit"),
    ("twin_track", {"measurements": [{"data": "data/day1.csv"}] * 7, "members": 32}, "engine runs is above"),
    ("twin_track", {"measurements": [{"data": "data/day1.csv"}], "members": 2}, "between"),
    ("twin_track", {"measurements": [{"data": "data/day1.csv"}], "process": {"filter.centerNm": 0}}, "positive number"),
    ("twin_track", {"measurements": [{"data": "data/day1.csv", "set": {"physics/raman": 1}}]}, "not allowed"),
    ("twin_track", {"measurements": [{"data": "data/day1.csv"}], "parameters": []}, "non-empty list"),
    ("twin_init", {"folder": "lab"}, "already holds"),
    ("twin_init", {"folder": "../elsewhere"}, "outside the workspace"),
    ("twin_init", {"folder": "plate.ufs"}, "not a folder"),
    ("twin_init", {"folder": ".ultrafast-mcp/x"}, "results folder"),
    ("twin_init", {"folder": "lab2", "template": "my-lab"}, "must be one of"),
]


@pytest.mark.parametrize("tool,args,fragment", TWIN_REFUSALS, ids=[f"{i}-{r[0]}" for i, r in enumerate(TWIN_REFUSALS)])
def test_twin_design_track_init_refusals_never_reach_the_worker(server, lab, tool, args, fragment):
    a = dict(args) if tool == "twin_init" else dict(args, folder="lab")
    res = call(server, tool, a)
    assert res["isError"] is True and fragment in text(res), text(res)
    assert server.worker.spawns == 0


def test_twin_design_and_track_need_a_project(server, lab):
    for tool, args in [("twin_design", {"placement": True}), ("twin_track", {"measurements": [{"data": "lab/data/day1.csv"}]})]:
        r = call(server, tool, dict(args, folder="."))
        assert r["isError"] and "twin_init" in text(r), text(r)
    big = {"elements": [{"id": "e%d" % i, "type": "transform"} for i in range(11)]}
    (lab / "system.ufs").write_text(json.dumps(big), encoding="utf-8")
    r = call(server, "twin_design", {"folder": "lab", "placement": True})
    assert r["isError"] and "13 candidates is above the limit" in text(r)


@pytest.mark.parametrize("tool,args", [("twin_design", {"variable": "energyJ", "values": [1e-6]}),
                                       ("twin_track", {"measurements": [{"data": "data/day1.csv"}]})])
def test_twin_design_and_track_over_their_budget_are_killed_and_the_next_call_works(server, lab, tool, args):
    server.budget_s = 30
    (lab / "sleep").write_text("30", encoding="utf-8")
    slow = call(server, tool, dict(args, folder="lab", timeBudgetS=1))
    assert slow["isError"] and "time budget of 1 s" in text(slow), text(slow)
    assert server.worker.pid is None                                              # killed
    (lab / "sleep").unlink()
    again = call(server, tool, dict(args, folder="lab"))
    assert not again["isError"] and again["structuredContent"]["op"] == tool
    server.budget_s = 1                                                           # the server's budget caps a larger timeBudgetS
    (lab / "sleep").write_text("30", encoding="utf-8")
    assert "time budget of 1 s" in text(call(server, tool, dict(args, folder="lab", timeBudgetS=100)))
    assert server.worker.spawns == 2


# ------------------------------------------------ the same tools against the real engine
@requires_engine
def test_real_study_backward_and_twin(tmp_path):
    ws = tmp_path / "ws"
    ws.mkdir()
    sys.path.insert(0, str(REPO / "plugins" / "twin" / "python"))
    import ultrafast_twin as tw
    tw.init(ws / "lab", "synthetic-lab")
    run = {"format": "ultrafast-run", "version": 2, "pulse": {"shape": "gaussian", "centerWavelengthM": 1.03e-6, "durationFwhmS": 1e-13, "energyJ": 1e-6},
           "grid": {"auto": False, "n": 2048, "windowS": 4e-12}}
    env = {"PYTHONPATH": os.pathsep.join([str(SRC), str(REPO / "plugins" / "twin" / "python")])}
    srv = McpServer(ws, worker=Worker(ws, env=env), budget_s=240)
    try:
        s = call(srv, "study", {"system": PLATE, "run": run, "parameters": [PAR], "outputs": ["pulse:end/fwhmS"], "fidelity": "quick"})
        assert not s["isError"], text(s)
        rows = s["structuredContent"]["rows"]
        assert len(rows) == 3 and rows[0]["pulse:end/fwhmS"] > 0 and "advice" in s["structuredContent"]
        o = call(srv, "study", {"system": PLATE, "run": run, "parameters": [{"path": "element:plate/thicknessM", "min": 1e-3, "max": 5e-3}], "outputs": ["pulse:end/fwhmS"],
                                "mode": "optimize", "objective": {"output": "pulse:end/fwhmS", "goal": "min", "maxEvaluations": 8}})
        assert not o["isError"], text(o)
        assert 1 <= o["structuredContent"]["rowCount"] <= 9      # maxEvaluations 8 plus the starting point
        back = dict(run, backward={"from": "end", "to": "input", "regularization": 1e-3}, instruments=[{"kind": "pulseSummary", "name": "seed", "at": "input"}])
        b = call(srv, "backward", {"system": PLATE, "run": back, "name": "back1"})
        assert not b["isError"], text(b)
        assert b["structuredContent"]["backward"]["steps"][0]["element"] == "plate" and (ws / ".ultrafast-mcp" / "back1" / "summary.json").is_file()
        f = call(srv, "twin_fit", {"folder": "lab", "name": "f1"})
        assert not f["isError"], text(f)
        assert (ws / "lab" / "fits" / "f1").is_dir() and f["structuredContent"]["parameters"]
        v = call(srv, "twin_validate", {"folder": "lab", "samples": 4})
        assert not v["isError"], text(v)
        assert v["structuredContent"]["verdict"] and v["structuredContent"]["conditions"]
        p = call(srv, "twin_predict", {"folder": "lab", "variables": {"energyJ": 2e-6}, "set": {"pulse/energyJ": 2e-6}, "samples": 3})
        assert not p["isError"], text(p)
        pc = p["structuredContent"]
        assert pc["pointsReturned"] <= 200 and len(pc["x"]) == len(pc["y"]) == len(pc["ySigma"]) and pc["scalars"]["energyJ"]["mean"] > 0
    finally:
        srv.worker.close()


@requires_engine
def test_real_twin_init_design_and_track(tmp_path):
    """The synthetic lab through the server: twin_init writes the project, twin_fit fits it, twin_design ranks two energies (the higher one sees the
    energy-dependent phase better: larger gain, the reference of test_twin.test_the_planner_ranks...), twin_track assimilates a held-out recording
    of the unchanged lab (no drift flagged)."""
    ws = tmp_path / "ws"
    ws.mkdir()
    env = {"PYTHONPATH": os.pathsep.join([str(SRC), str(REPO / "plugins" / "twin" / "python")])}
    srv = McpServer(ws, worker=Worker(ws, env=env), budget_s=300)
    try:
        i = call(srv, "twin_init", {"folder": "lab"})
        assert not i["isError"], text(i)
        ic = i["structuredContent"]
        assert (ws / "lab" / "twin.json").is_file() and len(ic["conditions"]) == 7 and sum(c["heldOut"] for c in ic["conditions"]) == 2
        assert call(srv, "twin_init", {"folder": "lab"})["isError"]                 # exists: refused
        f = call(srv, "twin_fit", {"folder": "lab", "name": "f1"})
        assert not f["isError"], text(f)
        d = call(srv, "twin_design", {"folder": "lab", "variable": "energyJ", "values": [1e-12, 2e-6], "setKey": "pulse/energyJ"})
        assert not d["isError"], text(d)
        dc = d["structuredContent"]
        assert dc["engineRuns"] == 2 * (len(f["structuredContent"]["parameters"]) + 1) and len(dc["ranking"]) == 2
        assert dc["chosen"][0]["name"].startswith("energyJ=2e-06") and dc["ranking"][0]["gain"] > dc["ranking"][1]["gain"] >= 0
        assert (ws / "lab" / "reports" / "design.json").is_file()
        t = call(srv, "twin_track", {"folder": "lab", "measurements": [{"data": "data/E1.4uJ.csv", "variables": {"energyJ": 1.4e-6}, "set": {"pulse/energyJ": 1.4e-6}}],
                                     "parameters": ["filter.centerNm"], "process": {"filter.centerNm": 0.3}, "members": 8, "seed": 2})
        assert not t["isError"], text(t)
        tc = t["structuredContent"]
        assert len(tc["history"]) == 2 and tc["drifted"] == [] and abs(tc["history"][-1]["mean"]["filter.centerNm"] - 1033.0) < 0.3
        assert (ws / "lab" / "reports" / "track.json").is_file()
        bad = call(srv, "twin_track", {"folder": "lab", "measurements": [{"data": "data/E1.4uJ.csv"}], "process": {"nope": 1.0}, "members": 4})
        assert bad["isError"] and "not tracked" in text(bad), text(bad)
    finally:
        srv.worker.close()
