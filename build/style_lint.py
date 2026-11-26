"""Lint the docs prose against docs/style.md (WP B2). The word list, the banned filler and the term list are read from the
tables in that file, never copied here.

    python build/style_lint.py                  # scan docs/**/*.md; exit 1 on any hit not in the baseline
    python build/style_lint.py PATH ...         # only these files or folders (still checked against the baseline)
    python build/style_lint.py --update-baseline  # rewrite build/style_lint_baseline.txt with today's hits
    python build/style_lint.py --no-baseline    # report every hit

Rules (the word before the colon in a report line ``file:line: rule: ...``):

* ``filler``   a word or phrase of the "Banned filler" table;
* ``spelling`` the "not" column of the "British spelling" table (``-ize`` forms are derived from the -ise forms of that row);
* ``term``     the "not" column of the "Term list" table;
* ``units``    a JSON code block whose numeric value sits under a key without a unit suffix (see ``UNIT_SUFFIXES``, ``UNIT_EXCEPTIONS``).

Only prose is checked: fenced code blocks (except the units rule), inline code, HTML tags and comments, link targets and bare URLs are
removed first. A line holding ``<!-- style: ignore -->`` is skipped. Not scanned: ``docs/style.md`` (it quotes the bad forms), the generated
files ``reference/options.md``, ``python-api.md``, ``validation-matrix.md``, ``materials-catalogue.md`` and ``design/notes/``.
Table cells whose "not" entry has a qualifier the linter cannot judge ("(as a noun)", "(the unit)", "(for an element)") are skipped.

The baseline ``build/style_lint_baseline.txt`` holds today's hits, one per line (``path<TAB>rule<TAB>match<TAB>line hash``), so a hit
moves with its line and only a new or changed line fails. Fix a hit and run ``--update-baseline`` to shrink the file.

``scan(paths=None) -> list[Hit]``, ``load_baseline()`` and ``new_hits(hits, baseline)`` are used by ``python/tests/test_style_lint.py``.
"""
from __future__ import annotations

import hashlib
import re
import sys
from collections import Counter
from pathlib import Path
from typing import Dict, List, NamedTuple, Optional, Sequence, Tuple

REPO = Path(__file__).resolve().parents[1]
DOCS = REPO / "docs"
STYLE = DOCS / "style.md"
BASELINE = Path(__file__).resolve().parent / "style_lint_baseline.txt"
GENERATED = {"reference/options.md", "reference/python-api.md", "reference/validation-matrix.md", "reference/materials-catalogue.md"}
SKIP_PREFIXES = ("design/notes/",)
SKIP_FILES = {"style.md"}
IGNORE_MARK = "<!-- style: ignore -->"

# Suffixes that carry a unit (SI, plus the declared exceptions in fs^n) at the end of a JSON key.
UNIT_SUFFIXES = (
    "M", "S", "J", "W", "Hz", "Rad", "Deg", "K", "Pa", "Bar", "V", "A", "C", "T", "Wb", "Fs", "Fs2", "Fs3", "Fs4", "Ps2", "Ps3",
    "M2", "M3", "Mm", "Um", "Nm", "Mj", "Nj", "Uj", "Mw", "Gw", "Tw", "Fs2PerM", "PerM", "Thz", "Ghz", "Mhz", "Db", "Percent",
    "WPerCm2", "PerM2", "M2PerW", "Hz2", "Rad2", "Mrad", "Urad", "DegC", "Pct", "JPerM2", "WPerM2", "Rps", "Bytes", "Mb", "Gb", "Ps", "Ppm", "ppm", "Counts", "Cells", "Levels", "Points", "Slots",
)
# Numeric keys that are legitimately unit-less: counts, ratios, indices, flags, schema versions, seeds, tolerances.
UNIT_EXCEPTIONS = {
    "n", "n2", "n3", "n0", "gamma", "x", "y", "z", "id", "version", "schema", "seed", "count", "samples", "steps", "order", "index", "orders",
    "m2", "mSquared", "m2x", "m2y", "fR", "fr", "fraction", "ratio", "efficiency", "reflectance", "transmittance", "reflectivity",
    "tolerance", "tol", "relTol", "absTol", "rtol", "atol", "threshold", "weight", "scale", "factor", "gain", "power", "polarization",
    "ellipticity", "azimuth", "wave", "size", "nx", "ny", "nt", "nz", "points", "gridPoints", "maxSteps", "nSteps", "numPoints", "bins",
    "pulses", "shots", "iterations", "maxIterations", "population", "generations", "topN", "top", "limit", "width", "height", "dpi",
    "fps", "column", "row", "line", "level", "priority", "mode", "modeIndex", "harmonic", "charge", "rank", "dimension", "dim",
    "levels", "parallel", "device", "threads", "regularization", "smoothness", "modes", "max", "min", "noiseFloor", "outputCoupler", "repeat", "repeats", "count1", "number", "num", "pass", "passes", "bounces", "nBounces", "nPasses", "rep",
}


class Hit(NamedTuple):
    path: str       # relative to docs/, forward slashes
    line: int
    rule: str       # filler | spelling | term | units
    match: str      # the text that matched
    hint: str       # what to write instead
    text: str       # the full source line (for the baseline hash)

    def key(self) -> Tuple[str, str, str, str]:
        return (self.path, self.rule, self.match.lower() if self.rule != "term" else self.match, _hash(self.text))

    def report(self) -> str:
        return "docs/%s:%d: %s: \"%s\"%s" % (self.path, self.line, self.rule, self.match, (" (" + self.hint + ")") if self.hint else "")


def _hash(text: str) -> str:
    return hashlib.sha1(text.strip().encode("utf-8")).hexdigest()[:8]


# ----------------------------------------------------------------------------------------------------------- the tables
def _section_rows(md: str, heading: str) -> List[List[str]]:
    """The rows (cells, header and separator dropped) of the first table after the heading line that starts with ``heading``."""
    lines = md.splitlines()
    start = next((i for i, l in enumerate(lines) if l.startswith("#") and l.lstrip("# ").strip().startswith(heading)), None)
    if start is None:
        raise ValueError("docs/style.md has no heading '%s'" % heading)
    rows: List[List[str]] = []
    in_table = False
    for l in lines[start + 1:]:
        if l.startswith("|"):
            in_table = True
            cells = [c.strip() for c in l.strip().strip("|").split("|")]
            rows.append(cells)
        elif in_table:
            break
        elif l.startswith("#"):
            break
    return rows[2:]  # header, separator


def _split_items(cell: str) -> List[str]:
    """Split a table cell at commas outside parentheses."""
    out, depth, cur = [], 0, ""
    for ch in cell:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        if ch == "," and depth == 0:
            out.append(cur.strip())
            cur = ""
        else:
            cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out


_QUALIFIER = re.compile(r"^(.*?)\s*\(([^)]*)\)\s*$")


def _item_pattern(item: str) -> Optional[Tuple[str, str]]:
    """(pattern text, flags 'i' or '') for one "not" entry, or None when it cannot be checked mechanically."""
    item = item.replace("*", "").strip()
    if not item or item.startswith("the ") or item.startswith("(delete") or item == "the number" or item.startswith("a "):
        return None
    m = _QUALIFIER.match(item)
    word = item
    if m:
        word, q = m.group(1).strip(), m.group(2).strip()
        quoted = re.search(r'"([^"]+)"', q)
        if quoted:
            word = quoted.group(1)                       # just (as in "just run") -> just run
        elif q not in ("in prose",) and not q.startswith("delete"):
            return None                                  # (as a noun), (the unit), (for an element) ...: not mechanical
    word = word.strip()
    if not word or " (" in word:
        return None
    return (word, "")


def _compile(word: str, flags: int) -> "re.Pattern[str]":
    body = r"\s+".join(re.escape(w) for w in word.split())
    return re.compile(r"(?<![\w\-/.])" + body + r"(?![\w\-])", flags)


def load_rules(style: Path = STYLE) -> List[Tuple[str, "re.Pattern[str]", str]]:
    """(rule, compiled pattern, hint) from the three tables of docs/style.md."""
    md = style.read_text(encoding="utf-8")
    rules: List[Tuple[str, "re.Pattern[str]", str]] = []
    seen = set()

    def add(rule: str, word: str, hint: str, flags: int) -> None:
        if (rule, word) not in seen:
            seen.add((rule, word))
            rules.append((rule, _compile(word, flags), hint))

    for banned, better in (r[:2] for r in _section_rows(md, "Banned filler")):
        for item in _split_items(banned):
            p = _item_pattern(item)
            if p:
                add("filler", p[0], "remove it, or say: " + better, re.I)
    for write, notc in (r[:2] for r in _section_rows(md, "British spelling")):
        hint = "write " + write
        if notc.startswith("the -ize forms"):
            for item in _split_items(write):
                if item.endswith("ise"):
                    stem = item[:-3]
                    rules.append(("spelling", re.compile(r"(?<![\w\-/.])" + re.escape(stem + "iz") + r"\w*", re.I), "write " + stem + "ise"))
            continue
        items = _split_items(notc)
        pats = [_item_pattern(i) for i in items]
        if any(p is None for p in pats):   # one unjudgeable entry (meter (the unit), license (noun)) skips the cell
            continue
        for p in pats:
            add("spelling", p[0], hint, re.I)
    for term, notc in (r[:2] for r in _section_rows(md, "Term list")):
        hint = "write " + term
        for item in _split_items(notc):
            p = _item_pattern(item)
            if p:
                add("term", p[0], hint, 0)
    return rules


# ---------------------------------------------------------------------------------------------------------- the prose
_FENCE = re.compile(r"^\s*(```+|~~~+)\s*([\w+-]*)")
_INLINE = re.compile(r"(`+)(?:.+?)\1")
_TAG = re.compile(r"<[^>\n]*>")
_COMMENT = re.compile(r"<!--.*?-->")
_LINK_TARGET = re.compile(r"\]\([^)]*\)|\]\[[^\]]*\]|^\s*\[[^\]]+\]:\s*\S+")
_URL = re.compile(r"https?://\S+|\b[\w.-]+\.(?:md|json|py|cs|png|ufs|npz|csv|yml|yaml|html|svg|gif)\b")
_JSON_LANGS = {"json", "jsonc", "json5", "ufs"}
_NUM_KEY = re.compile(r'"([A-Za-z_][\w]*)"\s*:\s*(-?\d+\.?\d*(?:[eE][-+]?\d+)?)\s*[,}\]]?\s*(?://.*)?$')


def _prose(line: str) -> str:
    line = _COMMENT.sub(" ", line)
    line = _INLINE.sub(" ", line)
    line = _LINK_TARGET.sub("]", line)
    line = _TAG.sub(" ", line)
    return _URL.sub(" ", line)


def _unit_ok(key: str) -> bool:
    if key in UNIT_EXCEPTIONS or key.lower() in {k.lower() for k in UNIT_EXCEPTIONS}:
        return True
    return any(key.endswith(s) and len(key) > len(s) for s in UNIT_SUFFIXES)


def lint_text(rel: str, md: str, rules: Sequence[Tuple[str, "re.Pattern[str]", str]]) -> List[Hit]:
    hits: List[Hit] = []
    fence: Optional[str] = None
    lang = ""
    in_comment = False
    for no, raw in enumerate(md.splitlines(), 1):
        m = _FENCE.match(raw)
        if m and (fence is None or raw.strip().startswith(fence[:3])) and (fence is None or not m.group(2)):
            if fence is None:
                fence, lang = m.group(1), m.group(2).lower()
            else:
                fence, lang = None, ""
            continue
        if IGNORE_MARK in raw:
            continue
        if fence is not None:
            if lang in _JSON_LANGS:
                km = _NUM_KEY.search(raw.strip())
                if km and not _unit_ok(km.group(1)):
                    hits.append(Hit(rel, no, "units", km.group(1), "a numeric key carries its unit: lengthM, energyJ ...", raw))
            continue
        if in_comment:
            if "-->" in raw:
                in_comment = False
            continue
        if "<!--" in raw and "-->" not in raw:
            in_comment = True
            continue
        text = _prose(raw)
        for rule, pat, hint in rules:
            for mm in pat.finditer(text):
                hits.append(Hit(rel, no, rule, mm.group(0), hint, raw))
    return hits


def doc_files(paths: Sequence[str] = ()) -> List[Path]:
    roots = [Path(p).resolve() for p in paths] if paths else [DOCS]
    files: List[Path] = []
    for r in roots:
        files += sorted(r.rglob("*.md")) if r.is_dir() else [r]
    out = []
    for f in files:
        try:
            rel = f.resolve().relative_to(DOCS).as_posix()
        except ValueError:
            rel = f.name
        if rel in GENERATED or rel in SKIP_FILES or rel.startswith(SKIP_PREFIXES):
            continue
        out.append(f)
    return out


def scan(paths: Sequence[str] = (), style: Path = STYLE) -> List[Hit]:
    rules = load_rules(style)
    hits: List[Hit] = []
    for f in doc_files(paths):
        try:
            rel = f.resolve().relative_to(DOCS).as_posix()
        except ValueError:
            rel = f.name
        hits += lint_text(rel, f.read_text(encoding="utf-8"), rules)
    return hits


# ------------------------------------------------------------------------------------------------------------ baseline
def load_baseline(path: Path = BASELINE) -> "Counter[Tuple[str, ...]]":
    c: Counter = Counter()
    if path.is_file():
        for l in path.read_text(encoding="utf-8").splitlines():
            if l.strip() and not l.startswith("#"):
                c[tuple(l.split("\t"))] += 1
    return c


def new_hits(hits: Sequence[Hit], baseline: "Counter[Tuple[str, ...]]") -> List[Hit]:
    left = Counter(baseline)
    out = []
    for h in hits:
        if left[h.key()] > 0:
            left[h.key()] -= 1
        else:
            out.append(h)
    return out


def write_baseline(hits: Sequence[Hit], path: Path = BASELINE) -> None:
    head = ("# Hits of build/style_lint.py that exist today (docs/style.md, WP B2): path, rule, match, hash of the line.\n"
            "# Do not add to this file by hand: fix the prose, then run `python build/style_lint.py --update-baseline`.\n")
    path.write_text(head + "".join("\t".join(k) + "\n" for k in sorted(h.key() for h in hits)), encoding="utf-8")


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    update = "--update-baseline" in args
    nobase = "--no-baseline" in args
    paths = [a for a in args if not a.startswith("--")]
    if update and paths:
        print("--update-baseline scans all of docs/; give no paths", file=sys.stderr)
        return 2
    hits = scan(paths)
    if update:
        write_baseline(hits)
        print("style_lint: wrote %d hits to %s" % (len(hits), BASELINE.relative_to(REPO)))
        return 0
    bad = hits if nobase else new_hits(hits, load_baseline())
    for h in sorted(bad, key=lambda h: (h.path, h.line)):
        print(h.report())
    if bad:
        print("style_lint: %d new hit(s) (%d in the baseline). Fix them, or add `%s` to a line that must keep its wording; see docs/style.md."
              % (len(bad), len(hits) - len(bad), IGNORE_MARK))
        return 1
    print("style_lint: ok (%d hits, all in the baseline)" % len(hits))
    return 0


if __name__ == "__main__":
    sys.exit(main())
