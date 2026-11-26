"""The Python tabs of the example READMEs run (WP D1).

Every ``docs/examples/<Group>/<name>/README.md`` is searched for snippets: a ```python fence directly after a line ``<!-- snippet -->``
(``<!-- snippet slow -->``: only with ``ULTRAFAST_SLOW=1``); the parser and the convention are in ``build/readme_snippets.py``.
Each snippet runs as a program in a temporary copy of its example folder (only that folder: a snippet uses the files of its own
example), with

* ``ULTRAFAST_STRICT_OPTIONS=1`` (an unknown key is an error) and ``MPLBACKEND=Agg``;
* **quick fidelity** (docs/reference/fidelity.md): ``"fidelity": "quick"`` is added to every run spec of the copy (``*.json`` with
  ``"format": "ultrafast-run"``) and to every run spec given to ``ultrafast.Run`` as a dict, unless the spec sets ``fidelity``
  itself. Keys a spec writes (``physics.raman: true``, ...) are kept, as the fidelity bundle never overrides them.

The test checks that the snippet exits with 0; the numbers it prints at full fidelity are checked by ``build/example_check.py``
(/example-check) against the output the README quotes.
"""
import importlib
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from conftest import requires_engine

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "build"))
readme_snippets = importlib.import_module("readme_snippets")
EXAMPLES = REPO / "docs" / "examples"
SLOW = os.environ.get("ULTRAFAST_SLOW") == "1"

# runs before the snippet: every dict run spec given to ultrafast.Run gets "fidelity": "quick" unless it has a fidelity
PRELUDE = r'''
import runpy, sys
import ultrafast.core as _core
_init = _core.Run.__init__
def _quick(self, system, spec):
    if isinstance(spec, dict) and "fidelity" not in spec:
        spec = dict(spec, fidelity="quick")
    _init(self, system, spec)
_core.Run.__init__ = _quick
sys.argv = [sys.argv[1]]
runpy.run_path(sys.argv[0], run_name="__main__")
'''


def _collect():
    out = []
    for readme in sorted(EXAMPLES.glob("*/*/README.md")):
        rel = readme.parent.relative_to(EXAMPLES).as_posix()
        for i, s in enumerate(readme_snippets.find_snippets(readme.read_text(encoding="utf-8")), 1):
            marks = [pytest.mark.skipif(not SLOW, reason="slow snippet (set ULTRAFAST_SLOW=1)")] if s.slow else []
            out.append(pytest.param(readme, s, id="%s#%d" % (rel, i), marks=marks))
    return out


def _make_quick(folder: Path):
    """Insert "fidelity": "quick" after the opening brace of every run spec in the copy (JSON with comments: text edit, not a reparse)."""
    for f in folder.rglob("*.json"):
        text = f.read_text(encoding="utf-8")
        if '"ultrafast-run"' in text and '"fidelity"' not in text and "{" in text:
            i = text.index("{") + 1
            f.write_text(text[:i] + ' "fidelity": "quick",' + text[i:], encoding="utf-8")


@requires_engine
@pytest.mark.parametrize("readme,snippet", _collect())
def test_readme_snippet_runs(readme, snippet, tmp_path):
    src = readme.parent
    dst = tmp_path / src.relative_to(REPO)
    shutil.copytree(src, dst, ignore=shutil.ignore_patterns("results", "results-*", "__pycache__", "*.npz"))
    _make_quick(dst)
    script = dst / "_readme_snippet.py"
    script.write_text(snippet.code, encoding="utf-8")
    env = dict(os.environ, ULTRAFAST_STRICT_OPTIONS="1", MPLBACKEND="Agg")
    env["PYTHONPATH"] = os.pathsep.join([str(REPO / "python" / "src")] + ([env["PYTHONPATH"]] if env.get("PYTHONPATH") else []))
    p = subprocess.run([sys.executable, "-c", PRELUDE, str(script)], cwd=dst, env=env, capture_output=True, text=True, timeout=900)
    rel = readme.relative_to(REPO).as_posix()
    assert p.returncode == 0, "%s, snippet at line %d failed:\n%s" % (rel, snippet.line, (p.stdout + p.stderr)[-3000:])


# ------------------------------------------------------------------ the parser (no engine)
def test_parser_finds_marked_blocks_in_tabs_and_skips_unmarked_ones():
    text = "\n".join([
        "=== \"Python\"",
        "",
        "    <!-- snippet -->",
        "    ```python",
        "    import ultrafast as uf",
        "",
        "    print(1)",
        "    ```",
        "",
        "```python",
        "not_run()",
        "```",
        "<!-- snippet slow -->",
        "",
        "```python",
        "print(2)",
        "```",
    ])
    s = readme_snippets.find_snippets(text)
    assert [x.line for x in s] == [4, 15]
    assert s[0].code == "import ultrafast as uf\n\nprint(1)\n" and not s[0].slow
    assert s[1].code == "print(2)\n" and s[1].slow


@pytest.mark.parametrize("text,why", [
    ("<!-- snippet -->\n```bash\nls\n```\n", "must be followed"),
    ("    <!-- snippet -->\n```python\nx\n```\n", "same indentation"),
    ("<!-- snippet fast -->\n```python\nx\n```\n", "unknown snippet option"),
    ("<!-- snippet -->\n```python\nx\n", "not closed"),
])
def test_parser_rejects_a_marker_without_its_block(text, why):
    with pytest.raises(readme_snippets.SnippetError, match=why):
        readme_snippets.find_snippets(text)


def test_quick_fidelity_is_added_to_run_specs_only(tmp_path):
    (tmp_path / "run.json").write_text('// c\n{ "format": "ultrafast-run", "pulse": {} }', encoding="utf-8")
    (tmp_path / "fixed.json").write_text('{ "format": "ultrafast-run", "fidelity": "full" }', encoding="utf-8")
    (tmp_path / "example.json").write_text('{ "title": "x" }', encoding="utf-8")
    _make_quick(tmp_path)
    assert '{ "fidelity": "quick", "format"' in (tmp_path / "run.json").read_text(encoding="utf-8")
    assert '"quick"' not in (tmp_path / "fixed.json").read_text(encoding="utf-8")
    assert '"quick"' not in (tmp_path / "example.json").read_text(encoding="utf-8")
