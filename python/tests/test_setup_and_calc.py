"""Source-build front door (build/setup.py), install.py presets and the pulse calculator from Python.

No .NET build happens here: setup.py runs with --dry-run, install.py against the synthetic release of test_release.py.
"""
import importlib.util
import math
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).parent))
import test_release as tr  # noqa: E402  (fake release helpers)


def _load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


setup = _load("uf_setup", REPO / "build" / "setup.py")
be = setup.be


def test_plugin_selection_adds_required_plugins_and_rejects_unknown_ones():
    assert be.resolve_plugins(["twin"]) == ["fitting", "twin"]            # plugin.json "requires"
    assert be.resolve_plugins(["transform"]) == ["fitting"]               # old name
    assert be.resolve_plugins(["none"]) == []
    assert "zemax" in be.resolve_plugins(None) and "hitran" in be.resolve_plugins(["all"])
    with pytest.raises(SystemExit):
        be.resolve_plugins(["nonsense"])


def test_every_plugin_has_a_project_to_build():
    for pid in be.plugin_ids():
        assert be.plugin_projects(pid), pid


def test_setup_dry_run_shows_only_the_chosen_plugins_and_builds_nothing(capsys):
    assert setup.main(["--cli", "--python", "--plugins", "twin", "--no-pip", "--dry-run"]) == 0
    out = capsys.readouterr().out
    assert "plugins: fitting, twin" in out and "NoPluginBuild=true" in out and "dotnet publish" in out


def test_setup_defaults_to_the_cli(capsys):
    setup.main(["--dry-run"])
    out = capsys.readouterr().out
    assert "UltrafastEngine.Cli.csproj" in out and "pip" not in out


def test_install_preset_expands_to_components(tmp_path, capsys):
    rel, dest = tr.make_release(tmp_path), tmp_path / "dest"
    assert tr.inst.main(["--preset", "cli", "--base-url", str(rel), "--dest", str(dest), "--platform", "linux-x64", "--no-pip"]) == 0
    assert (dest / "bin" / "ultrafast").exists()
    assert "Next:" in capsys.readouterr().out



def test_install_preset_unknown_is_a_usage_error(capsys):
    with pytest.raises(SystemExit):
        tr.inst.main(["--preset", "nonsense", "--list"])


# ---- pulse calculator (needs the built engine, like the other Python tests)
def test_calc_tbp_of_shapes_matches_closed_forms():
    import ultrafast as uf
    assert uf.calc.tbp("gaussian") == pytest.approx(2 * math.log(2) / math.pi, rel=1e-9)
    assert uf.calc.tbp("sech") == pytest.approx(0.31483, abs=1e-5)


def test_calc_analyze_and_limit_round_trip():
    import ultrafast as uf
    lim = uf.calc.limit("gaussian", duration=100e-15, center=800e-9)
    r = uf.calc.analyze(duration=100e-15, bandwidth_m=lim["bandwidthFwhmM"], center=800e-9)
    assert r["ratioToLimit"] == pytest.approx(1.0, abs=1e-9) and r["gddMagnitudeFs2"] == pytest.approx(0.0, abs=1e-6)
