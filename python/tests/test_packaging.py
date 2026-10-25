"""The release packager (build/package.py): option parsing, platform aliases and the wheel writer."""
import hashlib
import importlib.util
import zipfile
from base64 import urlsafe_b64encode
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("package", REPO / "build" / "package.py")
pkg = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pkg)


def test_aliases_and_plugin_selection():
    a = pkg.parse(["--cli", "--python", "--plugins", "transform", "--platform", "win64", "x86", "mac", "linux"])
    assert a.rids == ["win-x64", "win-x86", "osx-arm64", "linux-x64"]
    assert a.plugins == ["fitting"] and a.cli and a.python and not a.cpp
    assert pkg.parse(["--plugins", "none"]).plugins == []
    assert pkg.parse([]).plugins == ["fitting", "zemax"] and pkg.parse([]).cli
    b = pkg.parse(["--all"])
    assert b.cli and b.python and b.cpp and b.csharp
    with pytest.raises(SystemExit):
        pkg.parse(["--plugins", "nosuch"])


def test_os_of_and_native_names():
    assert pkg.os_of("win-x86") == "win" and pkg.os_of("osx-arm64") == "osx" and pkg.os_of("ubuntu.24.04-x64") == "linux"
    assert pkg.native_lib_names("osx-arm64") == ["libultrafast.dylib"]
    assert pkg.wheel_platform("win-x64") == "win_amd64" and pkg.wheel_platform("osx-arm64") == "macosx_11_0_arm64"


def test_never_packages_zosapi(tmp_path):
    src = tmp_path / "src"; (src / "sub").mkdir(parents=True)
    for n in ("ZOSAPI.dll", "ZOSAPI_NetHelper.dll", "sub/ZOSAPI_Interfaces.dll", "UltrafastEngine.Zemax.exe", "a.pdb", "CLAUDE.md"):
        (src / n).write_text("x")
    pkg.copy_tree(src, tmp_path / "dst")
    names = sorted(p.name for p in (tmp_path / "dst").rglob("*") if p.is_file())
    assert names == ["UltrafastEngine.Zemax.exe"]


def test_wheel_is_valid(tmp_path):
    stage = tmp_path / "stage" / "ultrafast"; stage.mkdir(parents=True)
    (stage / "__init__.py").write_text("x = 1\n")
    whl = pkg.build_wheel(tmp_path / "stage", tmp_path / "out", "macosx_11_0_arm64")
    assert whl.name.startswith("ultrafast_engine-") and whl.name.endswith("-py3-none-macosx_11_0_arm64.whl")
    with zipfile.ZipFile(whl) as z:
        names = z.namelist()
        di = next(n.split("/")[0] for n in names if n.endswith("/RECORD"))
        meta = z.read(di + "/METADATA").decode()
        assert "Name: ultrafast-engine" in meta and "Requires-Dist: numpy" in meta and 'extra == "hosted"' in meta
        assert "Root-Is-Purelib: false" in z.read(di + "/WHEEL").decode()
        assert "ultrafast-plot = ultrafast.plot:main" in z.read(di + "/entry_points.txt").decode()
        for line in z.read(di + "/RECORD").decode().splitlines():
            path, h, size = line.split(",")
            if h:
                data = z.read(path)
                assert h == "sha256=" + urlsafe_b64encode(hashlib.sha256(data).digest()).rstrip(b"=").decode() and int(size) == len(data)


def test_dry_run_prints_the_plan(capsys):
    assert pkg.main(["--all", "--plugins", "fitting", "--platform", "host", "--dry-run"]) == 0
    out = capsys.readouterr().out
    assert "dotnet publish" in out and "UltrafastEngine.Cli.csproj" in out
