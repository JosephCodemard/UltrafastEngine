"""Check the structure and metadata of the example folders that have an ``example.json`` (WP9). It does not run the examples.

    python build/example_lint.py            # every docs/examples/**/example.json; exit 1 on any problem
    python build/example_lint.py PATH ...   # only these example folders

An example folder is *converted* when it holds ``example.json``; ``docs/examples/_template.md`` is the structure of its ``README.md``.
For each converted folder the linter checks:

* ``example.json`` is JSON with the keys ``title``, ``summary``, ``group``, ``script``, ``plugins`` (list), ``figures`` (list), ``wave`` (int);
  ``group`` is the parent folder; ``script`` and every figure exist in the folder;
* the README starts with ``# <title>`` (the same text), the next paragraph is the ``summary``;
* the README has the bold labels ``**The story.**``, ``**Layout**``, ``**What it shows.**`` and ``**Limits.**`` in this order;
* it has a ``bash`` block that runs the script, and every relative link and image of the README exists;
* with ``"python": true`` in ``example.json`` (optional key, default false; set it when the README gets its Python tab, WP D1): the
  bold label ``**Run it**`` between ``**Layout**`` and ``**What it shows.**``, a content tab ``=== "CLI"`` followed by a tab
  ``=== "Python"``, and in the Python tab at least one snippet (a ```python fence directly after the line ``<!-- snippet -->``;
  ``build/readme_snippets.py``). ``python/tests/test_readme_snippets.py`` runs the snippets.

``lint_folder(folder) -> list[str]`` returns the problems of one folder (used by ``python/tests/test_example_lint.py``).
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path
from typing import List

sys.path.insert(0, str(Path(__file__).resolve().parent))
import readme_snippets  # noqa: E402

REPO = Path(__file__).resolve().parents[1]
EXAMPLES = REPO / "docs" / "examples"
KEYS = {"title": str, "summary": str, "group": str, "script": str, "plugins": list, "figures": list, "wave": int}
OPTIONAL = {"python": bool}
LABELS = ["**The story.**", "**Layout**", "**What it shows.**", "**Limits.**"]
PYTHON_LABELS = ["**The story.**", "**Layout**", "**Run it**", "**What it shows.**", "**Limits.**"]
_LINK = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)\)")


def lint_folder(folder: Path) -> List[str]:
    folder = Path(folder)
    rel = folder.name
    out: List[str] = []
    meta_path = folder / "example.json"
    readme = folder / "README.md"
    if not meta_path.is_file():
        return ["%s: no example.json" % rel]
    try:
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
    except ValueError as e:
        return ["%s: example.json is not valid JSON (%s)" % (rel, e)]
    for k, t in KEYS.items():
        if k not in meta:
            out.append("%s: example.json lacks '%s'" % (rel, k))
        elif not isinstance(meta[k], t) or isinstance(meta[k], bool) and t is int:
            out.append("%s: example.json '%s' must be %s" % (rel, k, t.__name__))
    for k, t in OPTIONAL.items():
        if k in meta and not isinstance(meta[k], t):
            out.append("%s: example.json '%s' must be %s" % (rel, k, t.__name__))
    extra = set(meta) - set(KEYS) - set(OPTIONAL)
    if extra:
        out.append("%s: example.json has unknown keys %s" % (rel, sorted(extra)))
    if out:
        return out
    if meta["group"] != folder.parent.name:
        out.append("%s: group is '%s', the folder is in '%s'" % (rel, meta["group"], folder.parent.name))
    for f in [meta["script"]] + list(meta["figures"]):
        if not (folder / f).is_file():
            out.append("%s: example.json names %s, which does not exist" % (rel, f))
    if not readme.is_file():
        return out + ["%s: no README.md" % rel]
    text = readme.read_text(encoding="utf-8")
    lines = text.splitlines()
    if not lines or lines[0].strip() != "# " + meta["title"]:
        out.append("%s: README must start with '# %s'" % (rel, meta["title"]))
    paras = [p.strip() for p in re.split(r"\n\s*\n", text.split("\n", 1)[1] if "\n" in text else "") if p.strip()]
    if not paras or paras[0] != meta["summary"].strip():
        out.append("%s: the first paragraph of the README must be the summary of example.json" % rel)
    python = meta.get("python") is True
    pos = -1
    for lab in PYTHON_LABELS if python else LABELS:
        i = text.find("\n" + lab)
        if i < 0:
            out.append("%s: README lacks the label %s" % (rel, lab))
        elif i < pos:
            out.append("%s: label %s is out of order (template: %s)" % (rel, lab, ", ".join(PYTHON_LABELS if python else LABELS)))
        else:
            pos = i
    blocks = re.findall(r"```bash\n(.*?)```", text, re.S)
    if not any(meta["script"] in b for b in blocks):
        out.append("%s: no ```bash block that runs %s" % (rel, meta["script"]))
    if python:
        out += _python_tab(rel, text)
    for target in _LINK.findall(text):
        if target.startswith(("http://", "https://", "#", "mailto:")):
            continue
        path = target.split("#", 1)[0]
        if path and not (folder / path).resolve().exists():
            out.append("%s: broken link %s" % (rel, target))
    return out


def _python_tab(rel: str, text: str) -> List[str]:
    """Problems of the "Run it" tabs: a CLI tab, then a Python tab with at least one snippet."""
    try:
        snippets = readme_snippets.find_snippets(text)
    except readme_snippets.SnippetError as e:
        return ["%s: %s" % (rel, e)]
    cli = re.search(r'^=== "CLI"\s*$', text, re.M)
    py = re.search(r'^=== "Python"\s*$', text, re.M)
    if not cli or not py or py.start() < cli.start():
        return ['%s: "python": true needs a tab === "CLI" followed by a tab === "Python" (docs/examples/_template.md)' % rel]
    first_py_line = text.count("\n", 0, py.start()) + 1
    if not any(s.line > first_py_line for s in snippets):
        return ["%s: the Python tab has no snippet (the line <!-- snippet --> directly before its ```python fence)" % rel]
    return []


def find_folders(paths: List[str]) -> List[Path]:
    if paths:
        return [Path(p).resolve() for p in paths]
    return sorted(p.parent for p in EXAMPLES.glob("*/*/example.json"))


def main(argv=None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    folders = find_folders(argv)
    problems: List[str] = []
    for f in folders:
        problems += lint_folder(f)
    for p in problems:
        print("LINT:", p)
    print("%d example folder(s) checked, %d problem(s)" % (len(folders), len(problems)))
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
