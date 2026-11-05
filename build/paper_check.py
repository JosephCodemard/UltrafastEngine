#!/usr/bin/env python3
"""Run the paper replications in docs/papers/ and check their numbers against the published values (docs/papers/CLAUDE.md).

    python3 build/paper_check.py                          # every paper, fast tier; exit 1 if a hard check fails
    python3 build/paper_check.py gordon boyd              # papers whose folder starts with these names
    python3 build/paper_check.py --full                   # the full tier (by hand: longer runs, more points)
    python3 build/paper_check.py --no-run                 # only evaluate the numbers already in <paper>/results/
    python3 build/paper_check.py --report paper-check.json --markdown paper-check.md

For every folder ``<root>/<paper>/`` that holds a ``checks.json``:

1. **Run.** ``python run.py --tier fast`` (or ``--tier full``) in the paper's folder with this interpreter,
   ULTRAFAST_STRICT_OPTIONS=1 and MPLBACKEND=Agg. The script writes ``results/numbers.json``. The harness never passes
   ``--figures``, so the committed ``figures/*.png`` are left alone (run.py writes its figures to ``results/figures/``).
2. **Check.** Every entry of ``checks.json`` -> ``"checks"`` is evaluated against ``results/numbers.json``:

   * ``number``:   ``key`` within ``expected`` +- ``tolerance`` (absolute; ``"relative": true`` makes it a fraction of expected).
   * ``ordering``: ``a`` < ``b`` (two keys; ``margin`` (default 0) is required between them: a + margin < b).
   * ``location``: the x of the maximum of the curve ``key`` (``{"x": [...], "y": [...]}``; ``"min": true`` for the minimum)
     lies within ``expected`` +- ``tolerance``, or inside ``window`` [lo, hi].

   A check runs in the tiers listed in ``tiers`` (default ``["fast", "full"]``). ``"hard": false`` (for example a number
   marked UNVERIFIED in paper.json, or one read off a figure loosely) is reported but does not fail the run.
3. **Report.** A table per paper (check, expected, ours, verdict) on stdout, optionally as JSON (--report) and as Markdown
   (--markdown). Exit status 1 when a run fails or a hard check fails.

``numbers.json`` holds scalars (``{"key": 1.23}``) and curves (``{"key": {"x": [...], "y": [...]}}``); nested objects are
addressed with dotted keys (``"a.b"``).
"""
import argparse
import json
import math
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from example_check import ROOT, leaves  # noqa: E402  (shared with the example checker)

PAPERS = os.path.join(ROOT, "docs", "papers")
KINDS = ("number", "ordering", "location")


def discover(root, only=()):
    """Paper folders (sorted) under root that have a checks.json, filtered by name prefixes."""
    out = []
    for d in sorted(os.listdir(root)):
        p = os.path.join(root, d)
        if os.path.isfile(os.path.join(p, "checks.json")) and (not only or any(d.startswith(o) for o in only)):
            out.append(p)
    return out


def run_paper(folder, tier, timeout):
    """Run the paper's run.py for a tier; (return code, last lines of output, seconds)."""
    env = dict(os.environ, ULTRAFAST_STRICT_OPTIONS="1", MPLBACKEND="Agg")
    t = time.time()
    try:
        p = subprocess.run([sys.executable, "run.py", "--tier", tier], cwd=folder, env=env, capture_output=True, text=True, timeout=timeout)
        rc, out = p.returncode, p.stdout + p.stderr
    except subprocess.TimeoutExpired as e:
        partial = e.stdout.decode(errors="replace") if isinstance(e.stdout, bytes) else (e.stdout or "")
        rc, out = -1, partial + "\ntimeout after %.0f s" % timeout
    return rc, out, time.time() - t


def lookup(numbers, key):
    """numbers[key] with dotted keys for nested objects; KeyError if missing."""
    if key in numbers:
        return numbers[key]
    o = numbers
    for part in key.split("."):
        if not isinstance(o, dict) or part not in o:
            raise KeyError(key)
        o = o[part]
    return o


def _num(v, key):
    if isinstance(v, bool) or not isinstance(v, (int, float)):
        raise ValueError("%s is not a number" % key)
    return float(v)


def evaluate(check, numbers):
    """One check against the numbers: dict(ok, ours, expected, detail). ok is None when the check cannot be evaluated."""
    kind = check.get("kind", "number")
    try:
        if kind == "number":
            ours = _num(lookup(numbers, check["key"]), check["key"])
            exp, tol = float(check["expected"]), float(check["tolerance"])
            if check.get("relative"):
                tol = tol * abs(exp)
            ok = math.isfinite(ours) and abs(ours - exp) <= tol
            return {"ok": ok, "ours": ours, "expected": "%.4g +- %.2g" % (exp, tol)}
        if kind == "ordering":
            a, b = _num(lookup(numbers, check["a"]), check["a"]), _num(lookup(numbers, check["b"]), check["b"])
            margin = float(check.get("margin", 0.0))
            return {"ok": a + margin < b, "ours": "%.4g < %.4g" % (a, b), "expected": "%s < %s" % (check["a"], check["b"]) + (" (margin %.2g)" % margin if margin else "")}
        if kind == "location":
            c = lookup(numbers, check["key"])
            x, y = c["x"], c["y"]
            if len(x) != len(y) or not x:
                raise ValueError("%s: x and y must be non-empty and of the same length" % check["key"])
            pick = min if check.get("min") else max
            i = pick(range(len(y)), key=lambda j: y[j])
            ours = float(x[i])
            if "window" in check:
                lo, hi = map(float, check["window"])
            else:
                exp, tol = float(check["expected"]), float(check["tolerance"])
                lo, hi = exp - tol, exp + tol
            return {"ok": lo <= ours <= hi, "ours": ours, "expected": "%s in [%.4g, %.4g]" % ("argmin" if check.get("min") else "argmax", lo, hi)}
        raise ValueError("unknown kind %r (use %s)" % (kind, ", ".join(KINDS)))
    except (KeyError, ValueError, TypeError) as e:
        return {"ok": None, "ours": None, "expected": check.get("expected"), "detail": "%s: %s" % (type(e).__name__, e)}


def check_paper(folder, tier, numbers):
    """Evaluate every check of a paper that belongs to the tier; a list of result rows."""
    spec = json.load(open(os.path.join(folder, "checks.json"), encoding="utf-8"))
    rows = []
    for c in spec.get("checks", []):
        if tier not in c.get("tiers", ["fast", "full"]):
            continue
        r = evaluate(c, numbers)
        hard = c.get("hard", True)
        verdict = "pass" if r["ok"] else ("ERROR" if r["ok"] is None else ("FAIL" if hard else "soft-fail"))
        rows.append(dict(r, id=c.get("id", c.get("key", "?")), kind=c.get("kind", "number"), hard=hard, verdict=verdict,
                         source=c.get("source", ""), description=c.get("description", "")))
    return rows


def fmt(v):
    if isinstance(v, float):
        return "%.4g" % v
    return "" if v is None else str(v)


def table(rows):
    """A Markdown table of check rows."""
    lines = ["| check | kind | expected | ours | verdict |", "|---|---|---|---|---|"]
    for r in rows:
        lines.append("| %s | %s | %s | %s | %s |" % (r["id"], r["kind"] + ("" if r["hard"] else " (soft)"), fmt(r["expected"]), fmt(r["ours"]), r["verdict"]))
    return "\n".join(lines)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("only", nargs="*", help="paper folder name prefixes (default: all)")
    ap.add_argument("--full", action="store_true", help="run the full tier instead of the fast one")
    ap.add_argument("--no-run", action="store_true", help="do not run run.py; evaluate the existing results/numbers.json")
    ap.add_argument("--root", default=PAPERS, help="folder of the paper folders (default: docs/papers/)")
    ap.add_argument("--timeout", type=float, default=1800, help="seconds per paper run (default 1800)")
    ap.add_argument("--report", help="write a JSON report here")
    ap.add_argument("--markdown", help="write the tables as Markdown here")
    args = ap.parse_args(argv)
    tier = "full" if args.full else "fast"
    folders = discover(args.root, args.only)
    if not folders:
        print("no paper folders with a checks.json under %s" % args.root)
        return 1
    report, md, failures = [], [], 0
    for folder in folders:
        name = os.path.basename(folder)
        entry = {"paper": name, "tier": tier}
        if not args.no_run:
            rc, out, dt = run_paper(folder, tier, args.timeout)
            entry.update(rc=rc, seconds=round(dt, 1))
            if rc != 0:
                failures += 1
                entry["error"] = out.strip().splitlines()[-12:]
                print("FAIL %-40s run.py --tier %s exited %d after %.0f s:\n     %s" % (name, tier, rc, dt, "\n     ".join(entry["error"])))
                report.append(entry)
                continue
        npath = os.path.join(folder, "results", "numbers.json")
        if not os.path.isfile(npath):
            failures += 1
            entry["error"] = ["no results/numbers.json"]
            print("FAIL %-40s no results/numbers.json" % name)
            report.append(entry)
            continue
        numbers = json.load(open(npath, encoding="utf-8"))
        rows = check_paper(folder, tier, numbers)
        entry["checks"] = rows
        bad = [r for r in rows if r["verdict"] in ("FAIL", "ERROR")]
        soft = [r for r in rows if r["verdict"] == "soft-fail"]
        failures += bool(bad)
        print("%-4s %-40s %d checks, %d failed, %d soft-failed%s" % ("ok" if not bad else "FAIL", name, len(rows), len(bad), len(soft),
                                                                     " (%.0f s)" % entry["seconds"] if "seconds" in entry else ""))
        for r in rows:
            print("     %-9s %-46s expected %-28s ours %s%s" % (r["verdict"], r["id"], fmt(r["expected"]), fmt(r["ours"]), ("  [" + r["detail"] + "]") if r.get("detail") else ""))
        if any(r.get("detail") for r in bad):
            print("     keys in numbers.json: %s" % ", ".join(sorted({k.strip("/").split("/")[0] for k, _ in leaves(numbers)})))
        md.append("### %s (%s tier)\n\n%s\n" % (name, tier, table(rows)))
        report.append(entry)
    if args.report:
        json.dump(report, open(args.report, "w"), indent=1)
    if args.markdown:
        open(args.markdown, "w", encoding="utf-8").write("\n".join(md))
    print("%d paper(s) with problems" % failures)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
