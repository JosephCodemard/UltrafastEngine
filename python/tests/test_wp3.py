"""WP-3: one version everywhere, the release workflow's gates, the example lint and the public roadmap. No engine, no network."""
import importlib
import json
import re
import shutil
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "build"))
check_version = importlib.import_module("check_version")
example_lint = importlib.import_module("example_lint")
sync_roadmap = importlib.import_module("sync_roadmap")
WORKFLOW = REPO / ".github" / "workflows" / "release.yml"


# ------------------------------------------------------------------- one version
def test_every_place_carries_the_same_version():
    versions = check_version.collect()
    assert len(versions) >= 20 and set(versions.values()) == {"1.0.0"}, versions


@pytest.mark.parametrize("tag,wheel,rc", [(None, "1.0.0", False), ("v1.0.0", "1.0.0", False), ("v1.0.0rc1", "1.0.0rc1", True), ("v1.0.0rc12", "1.0.0rc12", True)])
def test_tags_accepted(tag, wheel, rc):
    assert check_version.check(tag) == ("1.0.0", wheel, rc)


@pytest.mark.parametrize("tag", ["v1.0.1", "v2.2.0", "1.0.0", "v1.0.0-beta", "v1.0", "v1.0.0.post1", "v1.0.0rc"])
def test_tags_refused(tag):
    with pytest.raises(SystemExit):
        check_version.check(tag)


def test_mismatch_in_any_place_is_found(tmp_path):
    for rel in ("python/pyproject.toml", "python/src/ultrafast/__init__.py", "Directory.Build.props", "CITATION.cff", "src/UltrafastEngine.Core/Results/RunResult.cs",
                "plugins/twin/plugin.json", "plugins/twin/src/TwinPlugin.cs", "plugins/gpu/plugin.json", "plugins/gpu/src/GpuPlugin.cs"):
        (tmp_path / rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy(REPO / rel, tmp_path / rel)
    assert check_version.check(None, tmp_path)[0] == "1.0.0"
    f = tmp_path / "plugins/twin/plugin.json"
    f.write_text(f.read_text(encoding="utf-8").replace('"engine": "1.0.0"', '"engine": "2.2.0"'), encoding="utf-8")
    with pytest.raises(SystemExit, match="plugins/twin/plugin.json engine"):
        check_version.check(None, tmp_path)


def test_package_name_is_ultrafast_engine():
    assert re.search(r'^name = "ultrafast-engine"', (REPO / "python" / "pyproject.toml").read_text(encoding="utf-8"), re.M)


def test_wheel_version_override(monkeypatch):
    package = importlib.import_module("package")
    monkeypatch.delenv("UF_WHEEL_VERSION", raising=False)
    assert package._pyproject()["version"] == "1.0.0"
    monkeypatch.setenv("UF_WHEEL_VERSION", "1.0.0rc2")
    assert package._pyproject()["version"] == "1.0.0rc2"
    for bad in ("1.0.1", "2.0.0rc1", "1.0.0-rc1", "1.0.0.dev1"):
        monkeypatch.setenv("UF_WHEEL_VERSION", bad)
        with pytest.raises(SystemExit):
            package._pyproject()


# --------------------------------------------------------------- release workflow
def _workflow():
    yaml = pytest.importorskip("yaml")
    return yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))


def test_workflow_gates_and_trusted_publishing():
    wf = _workflow()
    jobs = wf["jobs"]
    assert {"version", "light-tests", "managed", "native", "publish", "testpypi", "pypi"} <= set(jobs)
    for name in ("managed", "native", "publish"):
        assert "version" in (jobs[name]["needs"] if isinstance(jobs[name]["needs"], list) else [jobs[name]["needs"]])
    assert "light-tests" in jobs["publish"]["needs"]
    for name, env, rc in (("testpypi", "testpypi", "true"), ("pypi", "pypi", "false")):
        j = jobs[name]
        assert j["environment"]["name"] == env, "the upload job must sit behind a protected environment"
        assert j["permissions"] == {"id-token": "write"}
        assert "publish" in j["needs"] and "refs/tags/v" in j["if"] and "is_rc == '%s'" % rc in j["if"]
        assert any(str(st.get("uses", "")).startswith("pypa/gh-action-pypi-publish@") for st in j["steps"])
    assert "test.pypi.org" in json.dumps(jobs["testpypi"]) and "test.pypi.org" not in json.dumps(jobs["pypi"]).replace("https://pypi.org", "")


def test_workflow_stores_no_secret_and_publishes_no_sdist():
    text = WORKFLOW.read_text(encoding="utf-8")
    code = "\n".join(l for l in text.splitlines() if not l.lstrip().startswith("#"))
    assert "secrets." not in code.replace("secrets.GITHUB_TOKEN", "") and "password" not in code.lower() and "twine upload" not in code
    assert "sdist" not in code.lower() and "twine check --strict" in code and "check-zosapi wheels" in code
    assert "UF_WHEEL_VERSION" in code and "build/check_version.py" in code


def test_workflow_runs_only_on_tags_and_by_hand():
    on = _workflow().get(True) or _workflow().get("on")
    assert on["push"] == {"tags": ["v*"]} and "workflow_dispatch" in on


# ----------------------------------------------------------------- example lint
def test_wave_one_examples_pass_the_lint():
    folders = example_lint.find_folders([])
    assert len(folders) == 14
    assert {f.parent.name for f in folders} == {"AdaptiveOptics", "Branched", "DigitalTwin", "Fibres", "GettingStarted"}
    for f in folders:
        assert example_lint.lint_folder(f) == []


def _copy(tmp_path):
    src = REPO / "docs" / "examples" / "Fibres" / "spm-compression"
    dst = tmp_path / "Fibres" / "spm-compression"
    shutil.copytree(src, dst)
    _stub(tmp_path)
    return dst


def _stub(root):
    """The README of spm-compression links to another example: a copy of it two levels up keeps that link valid."""
    other = root / "LaserSystems" / "stretcher-compressor"
    other.mkdir(parents=True, exist_ok=True)
    shutil.copy(REPO / "docs" / "examples" / "LaserSystems" / "stretcher-compressor" / "README.md", other / "README.md")


def test_lint_finds_each_kind_of_problem(tmp_path):
    ok = _copy(tmp_path)
    assert example_lint.lint_folder(ok) == []
    readme = (ok / "README.md").read_text(encoding="utf-8")
    meta = json.loads((ok / "example.json").read_text(encoding="utf-8"))

    def broken(readme_text=None, meta_edit=None):
        root = tmp_path / ("b%d" % len(list(tmp_path.iterdir())))
        d = root / "Fibres" / "x"
        shutil.copytree(ok, d)
        _stub(root)
        if readme_text is not None:
            (d / "README.md").write_text(readme_text, encoding="utf-8")
        if meta_edit:
            m = dict(meta); meta_edit(m)
            (d / "example.json").write_text(json.dumps(m), encoding="utf-8")
        return example_lint.lint_folder(d)

    assert any("**Limits.**" in p for p in broken(readme.replace("**Limits.**", "Limits:")))
    swapped = readme.replace("**Layout**", "**TMP**").replace("**What it shows.**", "**Layout**").replace("**TMP**", "**What it shows.**")
    assert any("out of order" in p for p in broken(swapped))
    assert any("must start with" in p for p in broken("# Another title\n" + readme.split("\n", 1)[1]))
    assert any("summary" in p for p in broken(readme.replace(meta["summary"], "Something else.")))
    assert any("bash" in p for p in broken(readme.replace("```bash", "```sh")))
    assert any("broken link" in p for p in broken(readme + "\n[x](nowhere.md)\n"))
    assert any("lacks 'wave'" in p for p in broken(meta_edit=lambda m: m.pop("wave")))
    assert any("does not exist" in p for p in broken(meta_edit=lambda m: m.update(script="missing.py")))
    assert any("group is" in p for p in broken(meta_edit=lambda m: m.update(group="Other")))
    assert any("unknown keys" in p for p in broken(meta_edit=lambda m: m.update(extra=1)))
    assert any("must be int" in p for p in broken(meta_edit=lambda m: m.update(wave="1")))


def test_template_exists_and_is_excluded_from_the_site():
    tpl = (REPO / "docs" / "examples" / "_template.md").read_text(encoding="utf-8")
    for label in example_lint.LABELS:
        assert label in tpl
    assert "examples/_template.md" in (REPO / "mkdocs.yml").read_text(encoding="utf-8")


# --------------------------------------------------------------- public roadmap
def test_public_roadmap_has_three_blocks_and_the_root_copy_is_in_sync():
    text = (REPO / "docs" / "roadmap.md").read_text(encoding="utf-8")
    assert [l for l in text.splitlines() if l.startswith("## ")] == ["## In 1.0.0", "## Next", "## Later"]
    assert sync_roadmap.main(["--check"]) == 0
    assert "](docs/capabilities.md)" in (REPO / "ROADMAP.md").read_text(encoding="utf-8")
