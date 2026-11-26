"""WP B2: build/style_lint.py reads the tables of docs/style.md, is green on its baseline and fails on a new violation. No engine."""
import importlib
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "build"))
style_lint = importlib.import_module("style_lint")
RULES = style_lint.load_rules()


def _lint(text):
    return style_lint.lint_text("x.md", text, RULES)


def test_rules_come_from_the_tables():
    kinds = {r for r, _, _ in RULES}
    assert kinds == {"filler", "spelling", "term"}
    assert len(RULES) > 100


def test_docs_are_green_on_the_baseline():
    hits = style_lint.scan()
    assert style_lint.new_hits(hits, style_lint.load_baseline()) == []


def test_a_seeded_violation_fails_the_cli(tmp_path):
    bad = tmp_path / "bad.md"
    bad.write_text("# Page\n\nThe polarizer is very powerful and we use a B integral.\n", encoding="utf-8")
    r = subprocess.run([sys.executable, str(REPO / "build" / "style_lint.py"), str(bad)], capture_output=True, text=True)
    assert r.returncode == 1, r.stdout + r.stderr
    for word in ("polarizer", "very", "powerful", "B integral"):
        assert '"%s"' % word in r.stdout
    assert "bad.md:3:" in r.stdout
    good = tmp_path / "good.md"
    good.write_text("# Page\n\nThe polariser is centred on the beam.\n", encoding="utf-8")
    r = subprocess.run([sys.executable, str(REPO / "build" / "style_lint.py"), str(good)], capture_output=True, text=True)
    assert r.returncode == 0, r.stdout


def test_code_is_not_prose():
    text = "`polarization` and `fiber` are keys.\n\n```json\n{\"polarization\": \"x\"}\n```\n<div align=\"center\">\n[link](polarization.md)\n"
    assert _lint(text) == []
    assert _lint("A polarization effect. <!-- style: ignore -->\n") == []
    assert [h.match for h in _lint("A polarization effect.\n")] == ["polarization"]


def test_qualified_and_kept_forms_are_not_flagged():
    assert _lint("The power meter reads a focused beam of the program.\n") == []
    assert _lint("A co-moving frame and the d-scan.\n") == []


def test_units_rule_for_json_blocks():
    block = "```json\n{\n  \"lengthM\": 1e-3,\n  \"wavelength\": 1030e-9,\n  \"threads\": 4\n}\n```\n"
    assert [h.match for h in _lint(block) if h.rule == "units"] == ["wavelength"]


def test_baseline_moves_with_the_line_and_counts_repeats():
    h = style_lint.Hit("a.md", 3, "term", "run file", "", "the run file")
    moved = h._replace(line=30)
    base = style_lint.Counter([h.key()])
    assert style_lint.new_hits([moved], base) == []
    assert len(style_lint.new_hits([h, moved], base)) == 1
