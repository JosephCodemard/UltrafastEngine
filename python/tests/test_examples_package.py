"""``build/package.py --examples-wheel``: the examples wheel and its ``ultrafast-examples`` command."""
import importlib.util
import os
import subprocess
import sys
import zipfile
from pathlib import Path

import pytest

from conftest import requires_engine

REPO = Path(__file__).resolve().parents[2]


def _package_module():
    spec = importlib.util.spec_from_file_location("uf_package", REPO / "build" / "package.py")
    mod = importlib.util.module_from_spec(spec)
    sys.modules["uf_package"] = mod
    spec.loader.exec_module(mod)
    return mod


@pytest.fixture(scope="module")
def unpacked(tmp_path_factory):
    out = tmp_path_factory.mktemp("wheel")
    whl = _package_module().build_examples_wheel(out)
    target = out / "site"
    with zipfile.ZipFile(whl) as z:
        z.extractall(target)
    return whl, target


def _cli(site, *args, **env):
    e = dict(os.environ, PYTHONPATH=os.pathsep.join([str(site), str(REPO / "python" / "src")]), **env)
    e.pop("ULTRAFAST_EXAMPLES_DIR", None)
    return subprocess.run([sys.executable, "-m", "ultrafast_examples", *args], capture_output=True, encoding="utf-8", errors="replace", env=e, timeout=300)


def test_wheel_metadata_and_contents(unpacked):
    whl, site = unpacked
    assert whl.name == "ultrafast_examples-2.2.0-py3-none-any.whl"
    meta = (site / "ultrafast_examples-2.2.0.dist-info" / "METADATA").read_text()
    assert "Name: ultrafast-examples" in meta and "Requires-Dist: ultrafast-engine>=2.2.0" in meta
    assert "ultrafast-examples = ultrafast_examples.cli:main" in (site / "ultrafast_examples-2.2.0.dist-info" / "entry_points.txt").read_text()
    data = site / "ultrafast_examples" / "data"
    assert (data / "Computing" / "4f-correlator" / "correlator.py").exists() and (data / "Computing" / "_optics.py").exists()
    # nothing generated or repository-only travels with the wheel
    names = {p.name for p in data.rglob("*")}
    assert not names & {"make_figures.py", "make_all.py", "_figures.py", "_figs.py", "figures", "results"}
    assert not list(data.rglob("ZOSAPI*.dll"))


def test_list_and_copy_bring_the_dependencies_and_shared_files(unpacked, tmp_path):
    _, site = unpacked
    r = _cli(site, "list")
    assert r.returncode == 0 and "Computing" in r.stdout and "4f-correlator" in r.stdout and "shg-lbo" in r.stdout
    r = _cli(site, "copy", "nopcpa-bbo", str(tmp_path))
    assert r.returncode == 0
    assert (tmp_path / "LaserSystems" / "nopcpa-bbo" / "nopcpa.ufs").exists() and (tmp_path / "LaserSystems" / "shg-lbo" / "shg.ufs").exists()
    r = _cli(site, "copy", "Computing", str(tmp_path))
    assert r.returncode == 0 and (tmp_path / "Computing" / "_optics.py").exists() and (tmp_path / "Computing" / "matrix-vector" / "matrix_vector.py").exists()
    assert _cli(site, "copy", "no-such-example", str(tmp_path)).returncode != 0


@requires_engine
def test_run_executes_the_readme_commands_in_a_copy(unpacked, tmp_path):
    _, site = unpacked
    r = _cli(site, "run", "matrix-vector", "--dest", str(tmp_path))
    assert r.returncode == 0, r.stderr[-500:]
    assert "checks passed" in r.stdout
    assert (tmp_path / "Computing" / "matrix-vector" / "results").exists()      # written in the copy, not in the package
