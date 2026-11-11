"""Release system (WP8): component resolution, install.py against a local fake release, the ZOS-API gate, the git sparse path.

Everything runs on small synthetic releases in a temporary folder (no network, no .NET): the assets are written by the real
``build/release_components.py`` writer, the manifest by its ``manifest()``, and the installer is the real ``install.py``.
"""
import importlib.util
import io
import json
import shutil
import subprocess
import sys
import tarfile
import zipfile
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]


def _load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


inst = _load("install", REPO / "install.py")
rc = _load("release_components", REPO / "build" / "release_components.py")
pkg = _load("package_for_release_tests", REPO / "build" / "package.py")


# ---------------------------------------------------------------------------------------------------- a fake release
def _defs():
    d = {
        "cli": dict(kind="built", perPlatform=True, description="cli"),
        "python": dict(kind="built", perPlatform=False, description="py", pip=dict(folder="python", distribution="ultrafast-engine", spec="ultrafast-engine[hosted]")),
        "python-native": dict(kind="built", perPlatform=True, description="native", conflicts=["python"], provides=["python"], inAll=False),
        "plugins.fitting": dict(kind="built", description="fit", requires=["cli"]),
        "plugins.twin": dict(kind="built", description="twin", requires=["cli", "plugins.fitting"]),
        "examples.shared": dict(kind="source", description="shared", sourcePaths=["docs/examples"], gitViaParents=True),
        "examples.Branched": dict(kind="source", description="b", requires=["examples.shared", "python"], sourcePaths=["docs/examples/Branched"],
                                  subpath="docs/examples/Branched/{rest}"),
        "examples.Fibres": dict(kind="source", description="f", requires=["examples.shared", "python"], sourcePaths=["docs/examples/Fibres"],
                                subpath="docs/examples/Fibres/{rest}"),
        "media": dict(kind="source", description="m", inAll=False, sourcePaths=["docs/examples"]),
        "tests": dict(kind="source", description="t", requires=["python"], sourcePaths=["tests"]),
    }
    for v in d.values():
        v.setdefault("requires", [])
        v.setdefault("default", False)
    return d


FILES = {
    "docs/examples/_figures.py": "shared",
    "docs/examples/Branched/pump-probe/run.json": "{}",
    "docs/examples/Branched/pump-probe/pp.py": "print(1)",
    "docs/examples/Branched/cavity/run.json": "{}",
    "docs/examples/Branched/make_all.py": "group helper",
    "docs/examples/Fibres/soliton/run.json": "{}",
    "tests/a.py": "a",
}


def make_release(tmp: Path, version="1.0.0", content_tag="") -> Path:
    out = tmp / ("release-" + version)
    out.mkdir(parents=True, exist_ok=True)
    src = tmp / ("src-" + version)
    for rel, text in FILES.items():
        p = src / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text + content_tag)
    defs = _defs()
    assets = {}

    def put(name, plat, files):
        info = rc.write_asset(files, out / rc.asset_name(name, version, plat))
        assets.setdefault(name, []).append(dict(platform=plat, **info))

    for n, d in defs.items():
        if d["kind"] == "source" and n != "media":
            roots = d["sourcePaths"]
            fl = [(r, src / r) for r in FILES if any(r.startswith(x + "/") for x in roots)]
            if n == "examples.shared":
                fl = [(r, src / r) for r in FILES if r.count("/") == 2 and r.startswith("docs/examples/")]
            put(n, "any", fl)
    for plat in ("linux-x64", "osx-arm64"):
        f = src / ("bin-" + plat); f.write_text("exe " + plat + content_tag); f.chmod(0o755)
        put("cli", plat, [("bin/ultrafast", f)])
    w = src / "ultrafast_engine-1-py3-none-any.whl"
    with zipfile.ZipFile(w, "w") as z:
        z.writestr("ultrafast/__init__.py", "wheel" + content_tag)
    put("python", "any", [("python/ultrafast_engine-1-py3-none-any.whl", w)])
    p1 = src / "fit.dll"; p1.write_text("fit" + content_tag)
    put("plugins.fitting", "any", [("bin/plugins/fitting/fit.dll", p1)])
    p2 = src / "twin.dll"; p2.write_text("twin" + content_tag)
    put("plugins.twin", "any", [("bin/plugins/twin/twin.dll", p2)])
    n = src / "native.so"; n.write_text("native")
    put("python-native", "linux-x64", [("python/native.bin", n)])
    m = rc.manifest(version, assets, defs)
    m["components"] = {k: v for k, v in m["components"].items() if v["assets"]}
    (out / "manifest.json").write_text(json.dumps(m))
    return out


def run_install(rel, dest, include, exclude=None, platform="linux-x64", **kw):
    return inst.install(str(rel), include, exclude, dest, platform, use_pip=False, log=lambda *_: None, **kw)


def tree(dest: Path):
    return sorted(p.relative_to(dest).as_posix() for p in dest.rglob("*") if p.is_file() and p.name != inst.RECORD)


# ---------------------------------------------------------------------------------------------------- resolution
def comps(tmp_path):
    return inst.fetch_manifest(str(make_release(tmp_path)))["components"]


def test_dotted_names_dependencies_and_excludes(tmp_path):
    c = comps(tmp_path)
    plan, _ = inst.resolve(c, ["examples"], None, "linux-x64")
    assert sorted(plan) == ["cli".replace("cli", "examples.Branched"), "examples.Fibres", "examples.shared", "python"]
    plan, _ = inst.resolve(c, ["examples.Branched"], None, "linux-x64")
    assert sorted(plan) == ["examples.Branched", "examples.shared", "python"]
    plan, _ = inst.resolve(c, ["plugins.twin"], None, "linux-x64")                      # twin -> fitting -> cli, transitively
    assert sorted(plan) == ["cli", "plugins.fitting", "plugins.twin"]
    plan, _ = inst.resolve(c, ["plugins"], ["plugins.twin"], "linux-x64")
    assert sorted(plan) == ["cli", "plugins.fitting"]
    plan, notes = inst.resolve(c, ["tests"], ["python"], "linux-x64")                  # excluded but needed: kept, with a note
    assert "python" in plan and any("excluded" in n for n in notes)


def test_all_leaves_out_media_unless_named(tmp_path):
    out = make_release(tmp_path)
    # media is declared but has no asset in the fake release: add one
    m = json.loads((out / "manifest.json").read_text())
    f = tmp_path / "m.png"; f.write_text("png")
    m["components"]["media"] = dict(description="m", requires=[], default=False, inAll=False, assets=[dict(platform="any", **rc.write_asset([("docs/examples/a.png", f)], out / "media-1.0.0.tar.gz"))])
    c = m["components"]
    plan, _ = inst.resolve(c, ["all"], None, "linux-x64")
    assert "media" not in plan and "cli" in plan and "python-native" not in plan
    assert "media" in inst.resolve(c, ["all", "media"], None, "linux-x64")[0]
    assert "media" not in inst.resolve(c, ["examples.Branched", "media"], ["media"], "linux-x64")[0]


def test_platform_choice_and_missing_platform(tmp_path):
    c = comps(tmp_path)
    assert inst.resolve(c, ["cli"], None, "osx-arm64")[0]["cli"]["asset"]["platform"] == "osx-arm64"
    with pytest.raises(inst.InstallError, match="not built for win-x64"):
        inst.resolve(c, ["cli"], None, "win-x64")
    plan, notes = inst.resolve(c, ["python-native", "tests"], None, "linux-x64")        # python-native provides what tests need
    assert "python-native" in plan and "python" not in plan
    plan, notes = inst.resolve(c, ["all"], ["python-native"], "osx-arm64")               # python-native only exists for linux-x64: only a note
    assert "python-native" not in plan
    with pytest.raises(inst.InstallError, match="cannot be installed together"):
        inst.resolve(c, ["python", "python-native"], None, "linux-x64")


def test_unknown_name_lists_the_known_ones(tmp_path):
    with pytest.raises(inst.InstallError, match="unknown component 'exampels'"):
        inst.resolve(comps(tmp_path), ["exampels"], None, "linux-x64")


def test_detect_platform_is_a_release_platform():
    assert inst.detect_platform() in ("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-arm64", "osx-x64", "win-x86")


def test_registry_of_the_real_repository_has_every_example_group():
    d = rc.definitions(REPO, ["fitting", "ao", "twin"])
    groups = {p.name for p in (REPO / "docs" / "examples").iterdir() if p.is_dir() and not p.name.startswith(("_", "."))}
    assert {"examples." + g for g in groups} <= set(d)
    assert d["plugins.twin"]["requires"] == ["cli", "plugins.fitting"]
    assert d["media"]["inAll"] is False and d["examples.Branched"]["requires"] == ["examples.shared", "python"]
    plan, _, _, _ = inst.expand(d, ["all"], ["media", "source"])
    assert "cli" in plan and "media" not in plan and "source" not in plan


def test_source_components_use_tracked_files_and_leave_out_media_pdfs_and_agent_notes():
    d = rc.definitions(REPO, [])
    files = rc.tracked_files(REPO)
    ex = {a for a, _ in rc.collect(REPO, "examples.Branched", d, files)}
    media = {a for a, _ in rc.collect(REPO, "media", d, files)}
    assert ex and media and not (ex & media)
    assert all(a.startswith("docs/examples/Branched/") for a in ex)
    assert not [a for a in ex | media if a.endswith(("CLAUDE.md", ".pdf"))]
    assert all(a.lower().endswith(rc.MEDIA_EXT) for a in media)
    shared = {a for a, _ in rc.collect(REPO, "examples.shared", d, files)}
    assert "docs/examples/_figures.py" in shared and all(a.count("/") == 2 for a in shared)
    src = {a for a, _ in rc.collect(REPO, "source", d, files)}
    assert "build/package.py" in src and "CLAUDE.md" in src                               # the whole repository, agent notes included
    assert not [a for a in src if rc.forbidden(a)]


# ---------------------------------------------------------------------------------------------------- install / uninstall
def test_install_a_selection_then_add_then_uninstall_leaves_nothing(tmp_path):
    rel, dest = make_release(tmp_path), tmp_path / "dest"
    run_install(rel, dest, ["python", "tests", "examples.Branched"])
    assert tree(dest) == ["docs/examples/Branched/cavity/run.json", "docs/examples/Branched/make_all.py", "docs/examples/Branched/pump-probe/pp.py",
                          "docs/examples/Branched/pump-probe/run.json", "docs/examples/_figures.py", "python/ultrafast_engine-1-py3-none-any.whl", "tests/a.py"]
    rec = inst.read_record(dest)
    assert sorted(rec["components"]) == ["examples.Branched", "examples.shared", "python", "tests"]
    run_install(rel, dest, ["cli"])                                                        # a second --include adds
    assert (dest / "bin" / "ultrafast").read_text() == "exe linux-x64"
    assert (dest / "bin" / "ultrafast").stat().st_mode & 0o111                             # executable bit survives
    inst.uninstall(dest, ["tests"], use_pip=False, log=lambda *_: None)
    assert not (dest / "tests").exists() and (dest / "bin" / "ultrafast").exists()
    inst.uninstall(dest, None, use_pip=False, log=lambda *_: None)
    assert list(dest.iterdir()) == []                                                      # everything it wrote is gone, the record too


def test_a_part_of_a_group_travels_with_the_group_helpers(tmp_path):
    rel, dest = make_release(tmp_path), tmp_path / "dest"
    run_install(rel, dest, ["examples.Branched.pump-probe"])
    assert tree(dest) == ["docs/examples/Branched/make_all.py", "docs/examples/Branched/pump-probe/pp.py", "docs/examples/Branched/pump-probe/run.json",
                          "docs/examples/_figures.py", "python/ultrafast_engine-1-py3-none-any.whl"]
    rec = inst.read_record(dest)
    assert rec["components"]["examples.Branched"]["only"] == ["docs/examples/Branched/pump-probe"]
    run_install(rel, dest, ["examples.Branched.cavity"])
    assert (dest / "docs/examples/Branched/cavity/run.json").exists()
    inst.uninstall(dest, ["examples.Branched.pump-probe"], use_pip=False, log=lambda *_: None)
    assert not (dest / "docs/examples/Branched/pump-probe").exists() and (dest / "docs/examples/Branched/cavity/run.json").exists()


def test_uninstall_leaves_a_changed_file_and_warns(tmp_path):
    rel, dest, msgs = make_release(tmp_path), tmp_path / "dest", []
    run_install(rel, dest, ["tests", "python"])
    (dest / "tests" / "a.py").write_text("my edit")
    (dest / "tests" / "mine.txt").write_text("not installed by the tool")
    inst.uninstall(dest, None, use_pip=False, log=msgs.append)
    assert (dest / "tests" / "a.py").read_text() == "my edit" and (dest / "tests" / "mine.txt").exists()
    assert any("changed after installation" in m for m in msgs)
    assert not (dest / "python").exists()


def test_existing_file_not_ours_is_not_overwritten(tmp_path):
    rel, dest = make_release(tmp_path), tmp_path / "dest"
    (dest / "tests").mkdir(parents=True)
    (dest / "tests" / "a.py").write_text("user file")
    run_install(rel, dest, ["tests"])
    assert (dest / "tests" / "a.py").read_text() == "user file"
    run_install(rel, dest, ["tests"], force=True)
    assert (dest / "tests" / "a.py").read_text() == "a"


def test_hash_mismatch_refuses_and_installs_nothing(tmp_path):
    rel, dest = make_release(tmp_path), tmp_path / "dest"
    asset = rel / "tests-1.0.0.tar.gz"
    asset.write_bytes(asset.read_bytes()[:-8] + b"tampered")
    with pytest.raises(inst.InstallError, match="SHA-256 mismatch"):
        run_install(rel, dest, ["python", "tests"])
    assert not dest.exists() or tree(dest) == []


def test_upgrade_replaces_files_and_drops_stale_ones(tmp_path):
    rel1 = make_release(tmp_path, "1.0.0")
    dest = tmp_path / "dest"
    run_install(rel1, dest, ["tests"])
    rel2 = make_release(tmp_path, "1.1.0", content_tag="v2")
    # version 1.1.0 of tests has a different file name
    (tmp_path / "src-1.1.0" / "tests" / "a.py").rename(tmp_path / "src-1.1.0" / "tests" / "b.py")
    m = json.loads((rel2 / "manifest.json").read_text())
    info = rc.write_asset([("tests/b.py", tmp_path / "src-1.1.0" / "tests" / "b.py")], rel2 / "tests-1.1.0.tar.gz")
    m["components"]["tests"]["assets"] = [dict(platform="any", **info)]
    (rel2 / "manifest.json").write_text(json.dumps(m))
    run_install(rel2, dest, ["tests"])
    assert tree(dest) == ["python/ultrafast_engine-1-py3-none-any.whl", "tests/b.py"]
    assert inst.read_record(dest)["components"]["tests"]["version"] == "1.1.0"


def test_zos_api_in_an_archive_is_refused_on_install(tmp_path):
    rel, dest = make_release(tmp_path), tmp_path / "dest"
    evil = tmp_path / "x"; evil.write_text("x")
    # a hand-made archive (the writer would refuse): the installer must still refuse
    bad = rel / "tests-1.0.0.tar.gz"
    with tarfile.open(bad, "w:gz") as tf:
        tf.add(evil, "tests/ZOSAPI.dll")
    import hashlib
    m = json.loads((rel / "manifest.json").read_text())
    m["components"]["tests"]["assets"][0].update(sha256=hashlib.sha256(bad.read_bytes()).hexdigest(), size=bad.stat().st_size)
    (rel / "manifest.json").write_text(json.dumps(m))
    with pytest.raises(inst.InstallError, match="ZOS-API"):
        run_install(rel, dest, ["python", "tests"])
    assert not (dest / "tests").exists()


def test_unsafe_archive_paths_are_refused(tmp_path):
    with pytest.raises(inst.InstallError, match="unsafe"):
        inst._safe_member("../outside.txt")
    with pytest.raises(inst.InstallError, match="unsafe"):
        inst._safe_member("/etc/passwd")


def test_pip_command_for_a_wheel_component(tmp_path):
    rel, dest, lines = make_release(tmp_path), tmp_path / "dest", []
    inst.install(str(rel), ["python"], None, dest, "linux-x64", dry_run=True, python="py", log=lines.append)
    cmd = [l for l in lines if "-m pip" in l][0]
    assert "py -m pip install --find-links" in cmd and str(dest / "python") in cmd and "ultrafast-engine[hosted]" in cmd
    assert not dest.exists()                                                                # a dry run writes nothing


# ---------------------------------------------------------------------------------------------------- the ZOS-API gate
def test_asset_writer_refuses_zosapi_files(tmp_path):
    f = tmp_path / "ZOSAPI_NetHelper.dll"; f.write_text("x")
    with pytest.raises(SystemExit, match="ZOS-API"):
        rc.write_asset([("zemax/ZOSAPI_NetHelper.dll", f)], tmp_path / "a.tar.gz")
    ok = tmp_path / "Stubs.cs"; ok.write_text("x")
    rc.write_asset([("plugins/zemax/tools/ZosApiStubs/Stubs.cs", ok)], tmp_path / "ok.tar.gz")  # our own stub sources are allowed
    assert rc.forbidden("a/b/zosapi.DLL") and rc.forbidden("ZOSAPI_Interfaces.xml") and not rc.forbidden("ZosApiStubs.csproj")


def test_release_gate_finds_a_planted_zosapi_file(tmp_path):
    rel = make_release(tmp_path)
    assert rc.check_archives([rel]) == []
    assert rc.main(["check-zosapi", str(rel)]) == 0
    planted = tmp_path / "evil.tar.gz"
    with tarfile.open(planted, "w:gz") as tf:
        info = tarfile.TarInfo("bin/plugins/zemax/ZOSAPI.dll"); info.size = 1
        tf.addfile(info, io.BytesIO(b"x"))
    whl = tmp_path / "w.whl"
    with zipfile.ZipFile(whl, "w") as z:
        z.writestr("ultrafast/bin/ZOSAPI_Interfaces.dll", "x")
    shutil.copy(planted, rel / "evil.tar.gz")
    assert any("ZOSAPI.dll" in x for x in rc.check_archives([rel]))
    assert rc.main(["check-zosapi", str(rel)]) == 1
    assert any("ZOSAPI_Interfaces" in x for x in rc.check_archives([whl]))


def test_build_release_with_a_planted_zosapi_in_a_wheel_is_caught(tmp_path):
    inner = io.BytesIO()
    with zipfile.ZipFile(inner, "w") as z:
        z.writestr("ultrafast/bin/hosted/ZOSAPI.dll", "x")
    wheel = tmp_path / "x.whl"; wheel.write_bytes(inner.getvalue())
    info = rc.write_asset([("python/x.whl", wheel)], tmp_path / "python.tar.gz")      # a wheel is not inspected by the writer ...
    assert info["size"] > 0
    assert rc.check_archives([tmp_path / "python.tar.gz"])                             # ... the gate looks inside it


# ---------------------------------------------------------------------------------------------------- manifest / packager
def test_merge_of_job_manifests(tmp_path):
    a = json.loads((make_release(tmp_path / "a") / "manifest.json").read_text())
    b = json.loads(json.dumps(a))
    b["components"]["cli"]["assets"] = [dict(platform="win-x64", file="cli-1.0.0-win-x64.tar.gz", size=1, sha256="0" * 64)]
    m = rc.merge([a, b])
    assert [x["platform"] for x in m["components"]["cli"]["assets"]] == ["linux-x64", "osx-arm64", "win-x64"]
    b["components"]["cli"]["assets"] = [dict(platform="linux-x64", file="x", size=1, sha256="1" * 64)]
    with pytest.raises(SystemExit, match="built twice"):
        rc.merge([a, b])
    b["version"] = "9"
    with pytest.raises(SystemExit, match="version mismatch"):
        rc.merge([a, b])


def test_packager_components_option_and_old_flags_as_aliases():
    a = pkg.parse(["--components", "python", "examples.Branched", "--cli", "--docs"])
    assert a.release_mode and a.components == ["python", "examples.Branched", "cli", "docs"]
    assert not pkg.parse(["--cli"]).release_mode
    with pytest.raises(SystemExit):
        pkg.parse(["--components"])


def test_packager_writes_assets_for_source_components(tmp_path):
    out = tmp_path / "out"
    assert pkg.main(["--components", "schemas", "examples.Branched", "--out", str(out), "--platform", "linux-x64"]) == 0
    rel = next(out.glob("release-*"))
    m = json.loads((rel / "manifest.json").read_text())
    assert {"schemas", "examples.Branched", "examples.shared"} <= set(m["components"])
    assert (rel / "install.py").exists()
    dest = tmp_path / "dest"
    inst.install(str(rel), ["schemas"], None, dest, "linux-x64", use_pip=False, log=lambda *_: None)
    assert (dest / "schemas" / "ufs.schema.json").exists()
    inst.uninstall(dest, None, use_pip=False, log=lambda *_: None)
    assert list(dest.iterdir()) == []


# ---------------------------------------------------------------------------------------------------- git sparse checkout
@pytest.mark.skipif(shutil.which("git") is None, reason="git not installed")
def test_git_sparse_checkout_and_uninstall_in_a_temporary_repository(tmp_path):
    repo = tmp_path / "repo"
    for rel, text in FILES.items():
        p = repo / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text)
    (repo / "big").mkdir(); (repo / "big" / "x.bin").write_text("not wanted")

    def git(*a):
        subprocess.run(["git", "-C", str(repo), "-c", "user.email=t@t", "-c", "user.name=t", *a], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    subprocess.run(["git", "init", "-q", str(repo)], check=True)
    git("add", "-A"); git("commit", "-qm", "x"); git("tag", "v1.0.0")
    c = _defs()
    c["examples.Branched"]["sourcePaths"] = ["docs/examples/Branched"]
    dest = tmp_path / "checkout"
    inst.install_git(c, ["examples.Branched.pump-probe"], None, dest, "1.0.0", repository=str(repo), log=lambda *_: None)
    present = sorted(p.relative_to(dest).as_posix() for p in dest.rglob("*") if p.is_file() and ".git/" not in p.as_posix() and p.name != inst.RECORD)
    assert "docs/examples/Branched/pump-probe/run.json" in present
    assert "docs/examples/_figures.py" in present and "docs/examples/Branched/make_all.py" in present   # cone mode: files of the parent folders
    assert not any(p.startswith(("big/", "tests/", "docs/examples/Fibres/", "docs/examples/Branched/cavity/")) for p in present)
    inst.install_git(c, ["tests"], None, dest, "1.0.0", repository=str(repo), log=lambda *_: None)      # a second --include adds
    assert (dest / "tests" / "a.py").exists()
    with pytest.raises(inst.InstallError, match="built component"):
        inst.install_git(c, ["cli"], None, dest, "1.0.0", repository=str(repo), log=lambda *_: None)
    inst.uninstall(dest, ["tests"], use_pip=False, log=lambda *_: None)
    assert not (dest / "tests").exists() and (dest / "docs/examples/Branched/pump-probe/run.json").exists()
    inst.uninstall(dest, None, use_pip=False, log=lambda *_: None)
    assert not dest.exists()
