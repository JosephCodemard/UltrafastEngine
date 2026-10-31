#!/usr/bin/env python3
"""Run the documented examples and check the numbers their READMEs quote (the /example-check job, scripted).

    python3 build/example_check.py                       # every example; exit 1 if a command fails or a quoted line is not reproduced
    python3 build/example_check.py 08 30-iris            # examples whose folder starts with these names
    python3 build/example_check.py --against HEAD~1      # also run the engine of a git ref and diff every number of every summary.json
    python3 build/example_check.py --against path/to/ultrafast.dll --report report.json

For every docs/examples/*/README.md and docs/tutorials/NN-*.md:

1. **Commands.** Every line of the README's ```bash blocks is run as written, in the example's folder (or at the repository root
   when the line names a path under docs/examples/ or python/), with ULTRAFAST_STRICT_OPTIONS=1. ``ultrafast`` is the CLI built in
   src/UltrafastEngine.Cli/bin/Release/net10.0 (build it first), ``python`` is this interpreter. ``ultrafast-plot`` lines, lines
   marked "optional" or "once", and ``cd docs/examples/...`` lines are skipped, as are PYNLO_SRC lines until
   python/tools/get_pynlo.py has fetched pyNLO; the examples listed in MANUAL are not run.
2. **Quoted output.** Every line with numbers in the README's plain ``` blocks (the "expected output"; use ```text for
   other plain text) must reappear in the commands' output: the same text with each number equal to the quoted one within
   the precision it is quoted to (half a unit in the last digit, at least --rtol relative; a value quoted as "x +- s" may
   move by 5 % of s, and s itself by 10 %). "..." or "(...)" in a quoted line stands for any text, and a quote may stop before the end of the line. Timing lines are ignored. Table rows that start with a
   `key` and a number (| `peakPowerOverCritical` | 96.5 | ...) are compared with that key in the summary.json files the
   commands wrote (any element, instrument or section); keys that are not found are listed as unchecked. Numbers in prose
   are not checked -- update them by hand when --against shows that the run changed.
3. **--against** (optional): the same commands with a second engine (a git ref, built in a temporary worktree, or a path to
   ultrafast.dll); every number in every summary.json is compared and changes above --diff-rtol are listed (timing and solver
   statistics excluded). Use it before and after a physics change to see which READMEs may need new numbers.

Results go to <scratch>/example-check (default: a temporary folder); the examples' own folders keep only what the README
commands write there (results-* folders are git-ignored).
"""
import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXAMPLES = os.path.join(ROOT, "docs", "examples")
CLI = os.path.join(ROOT, "src", "UltrafastEngine.Cli", "bin", "Release", "net10.0", "ultrafast.dll")
# examples that need software or files a plain checkout does not have
MANUAL = {"07-zemax-workflow": "needs an OpticStudio export", "cpp": "C++ / CMake build"}
NUM = re.compile(r"(?<![\w.])[-+]?(?:\d+\.\d*|\.\d+|\d+)(?:[eE][-+]?\d+)?(?![\w])")
TIMING = re.compile(r"done in|elapsed|took |\bms\b|seconds|laptop|minutes?\b|~\d", re.I)
SKIP_LINE = re.compile(r"^\s*(ultrafast-plot\b|cd\s+docs/examples)|#.*\b(optional|once)\b", re.I)


def blocks(readme):
    """(language, text) of every fenced block."""
    return re.findall(r"```([\w-]*)[^\n]*\n(.*?)```", readme, re.S)


def commands(readme):
    out = []
    for lang, body in blocks(readme):
        if lang not in ("bash", "sh", "shell"):
            continue
        for line in body.splitlines():
            s = line.strip()
            if not s or s.startswith("#") or SKIP_LINE.search(s):
                continue
            if "PYNLO_SRC" in s and not os.path.isdir(os.path.join(ROOT, "python", "third_party", "pynlo")):
                continue               # needs python/tools/get_pynlo.py
            out.append(s)
    return out


def expected_lines(readme):
    out = []
    for lang, body in blocks(readme):
        if lang:
            continue
        for line in body.splitlines():
            if NUM.search(line) and not TIMING.search(line):
                out.append(line.strip())
    return out


TABLE_ROW = re.compile(r"^\|\s*`([A-Za-z][\w.]*)`\s*\|\s*([-+]?(?:\d+\.\d*|\.\d+|\d+)(?:[eE][-+]?\d+)?)\s*([^\s|]*)")
# units a README may write after a table value; summary.json is in SI
UNITS = {"fs": 1e-15, "ps": 1e-12, "ns": 1e-9, "us": 1e-6, "µs": 1e-6, "ms": 1e-3, "s": 1, "nm": 1e-9, "um": 1e-6, "µm": 1e-6,
         "mm": 1e-3, "m": 1, "nJ": 1e-9, "uJ": 1e-6, "µJ": 1e-6, "mJ": 1e-3, "J": 1, "mW": 1e-3, "W": 1, "kW": 1e3, "MW": 1e6,
         "GW": 1e9, "rad": 1, "K": 1}


def table_values(readme):
    """(key, quoted number, unit) of table rows that start with a backticked key and a number."""
    return [(m.group(1), m.group(2), m.group(3)) for m in (TABLE_ROW.match(l.strip()) for l in readme.splitlines()) if m]


def check_table_value(key, tok, unit, files, rtol):
    """None if the key is in no summary (or the unit is not one we know); else (ok, the closest value, in the README's unit)."""
    if unit and unit not in UNITS:
        return None
    scale = UNITS.get(unit, 1.0)
    last = key.split(".")[-1]
    vals = []
    for f in files:
        vals += [v / scale for k, v in leaves(json.load(open(f))) if k.endswith("/" + last)]
    if not vals:
        return None
    want = float(tok)
    best = min(vals, key=lambda v: abs(v - want))
    return abs(best - want) <= quoted_tolerance(tok, rtol), best


def template(line):
    return " ".join(NUM.sub("#", line).split())


def quoted_tolerance(tok, rtol):
    """Half a unit in the last quoted digit (1.23 -> 0.005, 1.2e-3 -> 0.05e-3), at least rtol relative."""
    mant, _, exp = tok.lower().partition("e")
    dec = len(mant.split(".")[1]) if "." in mant else 0
    unit = 10.0 ** (-dec) * (10.0 ** int(exp) if exp else 1.0)
    return max(0.5 * unit * 1.0001, rtol * abs(float(tok)))


def pattern(line):
    """Regex for a quoted output line: numbers are captured, "..." and "(...)" match anything, and a quote may stop before
    the end of the output line (a trailing "." or ":" is dropped), as READMEs abbreviate long messages."""
    text = " ".join(line.split()).rstrip(".:")
    parts = re.split(r"\(\.\.\.\)|\.\.\.", text)
    rx = []
    for part in parts:
        pieces, pos = [], 0
        for m in NUM.finditer(part):
            pieces.append(re.escape(part[pos:m.start()]).replace("\\ ", "\\s+"))
            pieces.append(r"([-+]?(?:\d+\.\d*|\.\d+|\d+)(?:[eE][-+]?\d+)?)")
            pos = m.end()
        pieces.append(re.escape(part[pos:]).replace("\\ ", "\\s+"))
        rx.append("".join(pieces))
    return re.compile(r"\s*" + ".*?".join(rx))


def is_uncertainty(line, k):
    """True if the k-th number is the uncertainty of the one before it ("x +- s")."""
    nums = list(NUM.finditer(line))
    return k > 0 and k < len(nums) and re.fullmatch(r"\s*\+-\s*", line[nums[k - 1].end():nums[k].start()]) is not None


def sigma_after(line, k):
    """The uncertainty quoted after the k-th number as "x +- s" (or None)."""
    nums = list(NUM.finditer(line))
    if k + 1 < len(nums) and re.fullmatch(r"\s*\+-\s*", line[nums[k].end():nums[k + 1].start()]):
        return abs(float(nums[k + 1].group()))
    return None


def match(line, output_lines, rtol):
    """Best output line matching the quoted one: (ok, worst relative deviation, the line). A number quoted with an
    uncertainty ("x +- s") may differ by 5 % of s (a fit's poorly determined parameters move inside their error bars), and
    s itself by 10 %."""
    rx = pattern(line)
    want = [m.group() for m in NUM.finditer(" ".join(line.split()).rstrip(".:"))]
    best = (False, math.inf, None)
    for cand in output_lines:
        m = rx.match(" ".join(cand.split()))
        if not m:
            continue
        got = m.groups()
        ok, worst = True, 0.0
        for i, (w, g) in enumerate(zip(want, got)):
            a, b = float(w), float(g)
            dev = abs(a - b)
            worst = max(worst, dev / max(abs(a), 1e-300))
            sig = sigma_after(line, i)
            tol = max(quoted_tolerance(w, rtol), 0.05 * sig if sig else 0, 0.1 * abs(a) if is_uncertainty(line, i) else 0)
            if dev > tol:
                ok = False
        if (ok, -worst) > (best[0], -best[1]):
            best = (ok, worst, cand)
    return best


def run_line(line, example_dir, cli, env, timeout):
    """Run one README line with bash; ultrafast / python point at the chosen engine and this interpreter."""
    rooted = "docs/examples/" in line or "docs/tutorials/" in line or re.search(r"(^|\s)python/", line)
    cwd = ROOT if rooted else example_dir
    prelude = 'export ULTRAFAST_CLI="%s"\n' % cli + 'ultrafast() { dotnet "%s" "$@"; }\npython() { "%s" "$@"; }\npython3() { "%s" "$@"; }\n' % (cli, sys.executable, sys.executable)
    t = time.time()
    p = subprocess.run(["bash", "-c", prelude + line], cwd=cwd, env=env, capture_output=True, text=True, timeout=timeout)
    return p.returncode, p.stdout + p.stderr, time.time() - t


def summaries(example_dir, since):
    """summary.json files written under the example's folder since a time (the README's -o folders)."""
    found = {}
    for dirpath, _, files in os.walk(example_dir):
        if "summary.json" in files:
            f = os.path.join(dirpath, "summary.json")
            if os.path.getmtime(f) >= since:
                found[os.path.relpath(dirpath, example_dir)] = f
    return found


def leaves(o, p=""):
    if isinstance(o, dict):
        for k, v in o.items():
            yield from leaves(v, p + "/" + k)
    elif isinstance(o, list) and len(o) < 64:
        for i, v in enumerate(o):
            yield from leaves(v, "%s[%d]" % (p, i))
    elif isinstance(o, (int, float)) and not isinstance(o, bool):
        yield p, float(o)


IGNORE_KEY = re.compile(r"elapsed|time(s|Seconds)?$|/solver/|seconds|wallclock", re.I)


def diff_summaries(a_path, b_path, rtol):
    a = dict(leaves(json.load(open(a_path))))
    b = dict(leaves(json.load(open(b_path))))
    changed = []
    for k, x in a.items():
        if k not in b or IGNORE_KEY.search(k):
            continue
        y = b[k]
        if x == y or (x != x and y != y):
            continue
        scale = max(abs(x), abs(y))
        if scale < 1e-30:          # round-off around zero (centroids of symmetric beams, ...)
            continue
        rel = abs(x - y) / scale
        if rel > rtol:
            changed.append((rel, k, x, y))
    return sorted(changed, reverse=True), sorted(set(b) - set(a)), sorted(set(a) - set(b))


def baseline_cli(spec, scratch):
    if spec.endswith(".dll") and os.path.exists(spec):
        return os.path.abspath(spec)
    wt = os.path.join(scratch, "baseline-src")
    if os.path.exists(wt):
        subprocess.run(["git", "worktree", "remove", "--force", wt], cwd=ROOT, capture_output=True)
    subprocess.run(["git", "worktree", "add", "--detach", wt, spec], cwd=ROOT, check=True, capture_output=True)
    subprocess.run(["dotnet", "build", os.path.join(wt, "src", "UltrafastEngine.Cli"), "-c", "Release", "-f", "net10.0"],
                   check=True, capture_output=True)
    cli = os.path.join(wt, "src", "UltrafastEngine.Cli", "bin", "Release", "net10.0", "ultrafast.dll")
    # stop now rather than run every example against an engine that cannot start
    p = subprocess.run(["dotnet", cli, "schema", "run"], capture_output=True, text=True)
    if p.returncode != 0:
        sys.exit("the %s engine does not start (exit %d), try --scratch in another folder:\n%s" % (spec, p.returncode, (p.stdout + p.stderr)[-400:]))
    return cli


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("only", nargs="*", help="example folder name prefixes (default: all)")
    ap.add_argument("--rtol", type=float, default=1e-3, help="minimum relative tolerance of a quoted number (default 1e-3)")
    ap.add_argument("--against", help="git ref or path to ultrafast.dll to diff every summary.json against")
    ap.add_argument("--diff-rtol", type=float, default=1e-4, help="relative change reported by --against (default 1e-4)")
    ap.add_argument("--timeout", type=float, default=1800, help="seconds per command (default 1800)")
    ap.add_argument("--scratch", help="folder for the --against runs and worktree (default: a temporary folder)")
    ap.add_argument("--report", help="write a JSON report here")
    args = ap.parse_args()
    if not os.path.exists(CLI):
        sys.exit("build the CLI first: dotnet build src/UltrafastEngine.Cli -c Release -f net10.0")
    scratch = args.scratch or tempfile.mkdtemp(prefix="example-check-")
    os.makedirs(scratch, exist_ok=True)
    other = baseline_cli(args.against, scratch) if args.against else None
    env = dict(os.environ, ULTRAFAST_STRICT_OPTIONS="1")
    # (name, folder the commands run in, the Markdown page): the examples, then the tutorials (in the folder their "Files:"
    # line names, else docs/tutorials/files)
    pages = [(d, os.path.join(EXAMPLES, d), os.path.join(EXAMPLES, d, "README.md"))
             for d in sorted(os.listdir(EXAMPLES)) if os.path.isfile(os.path.join(EXAMPLES, d, "README.md"))]
    tut = os.path.join(ROOT, "docs", "tutorials")
    for f in sorted(os.listdir(tut)):
        if not re.match(r"\d\d-.*\.md$", f):
            continue
        m = re.search(r"Files: `docs/examples/([\w.-]+)/", open(os.path.join(tut, f), encoding="utf-8").read())
        pages.append(("tutorials/" + f[:-3], os.path.join(EXAMPLES, m.group(1)) if m else os.path.join(tut, "files"), os.path.join(tut, f)))
    if args.only:
        pages = [p for p in pages if any(p[0].startswith(o) or p[0].startswith("tutorials/" + o) for o in args.only)]
    report, failures = [], 0
    for name, d, page in pages:
        if name in MANUAL:
            print("-- %-30s skipped (%s)" % (name, MANUAL[name]))
            report.append({"example": name, "skipped": MANUAL[name]})
            continue
        readme = open(page, encoding="utf-8").read()
        cmds = commands(readme)
        entry = {"example": name, "commands": [], "quoted": [], "diffs": []}
        output, ok, t0 = [], True, time.time()
        for line in cmds:
            rc, out, dt = run_line(line, d, CLI, env, args.timeout)
            entry["commands"].append({"line": line, "rc": rc, "seconds": round(dt, 1)})
            output += out.splitlines()
            if rc != 0:
                ok = False
                print("FAIL %-30s %s\n     %s" % (name, line, "\n     ".join(out.strip().splitlines()[-6:])))
        new_summaries = summaries(d, t0)
        bad, unchecked = [], []
        for q in expected_lines(readme):
            good, worst, got = match(q, output, args.rtol)
            entry["quoted"].append({"line": q, "ok": good, "got": got})
            if not good:
                bad.append((q, got))
        for key, tok, unit in table_values(readme):
            r = check_table_value(key, tok, unit, list(new_summaries.values()), args.rtol)
            if r is None:
                unchecked.append(key)
                continue
            entry["quoted"].append({"line": "%s = %s" % (key, tok), "ok": r[0], "got": "%.6g" % r[1]})
            if not r[0]:
                bad.append(("| `%s` | %s" % (key, tok), "summary.json: %.6g" % r[1]))
        entry["unchecked"] = unchecked
        if other and ok:
            snap = os.path.join(scratch, "new", name)
            for rel, f in new_summaries.items():
                os.makedirs(os.path.join(snap, rel), exist_ok=True)
                shutil.copy(f, os.path.join(snap, rel, "summary.json"))
            t1 = time.time()
            for line in cmds:
                rc, out, _ = run_line(line, d, other, env, args.timeout)
                if rc != 0:
                    entry.setdefault("baselineFailed", []).append({"line": line, "rc": rc, "tail": out.strip().splitlines()[-1:]})
            for rel, f in summaries(d, t1).items():
                mine = os.path.join(snap, rel, "summary.json")
                if not os.path.exists(mine):
                    continue
                changed, added, removed = diff_summaries(f, mine, args.diff_rtol)
                entry["diffs"].append({"output": rel, "changed": [{"key": k, "against": x, "now": y, "rel": r} for r, k, x, y in changed],
                                       "added": added, "removed": removed})
            if new_summaries and not entry["diffs"]:
                entry["notCompared"] = True
            # leave the example folders with this engine's results
            for line in cmds:
                run_line(line, d, CLI, env, args.timeout)
        status = "ok" if ok and not bad else "FAIL" if not ok else "DIFF"
        failures += status != "ok"
        print("%-4s %-30s %d commands, %d quoted values, %d not reproduced%s (%.0f s)" % (status, name, len(cmds),
              len(entry["quoted"]), len(bad), ", %d table keys not in summary.json" % len(unchecked) if unchecked else "", time.time() - t0))
        for q, got in bad:
            print("       README: %s\n       output: %s" % (q, got or "(no line with this text)"))
        for bf in entry.get("baselineFailed", []):
            print("       vs %s: exit %d on: %s\n         %s" % (args.against, bf["rc"], bf["line"], " ".join(bf["tail"])[:160]))
        if entry.get("notCompared"):
            print("       vs %s: NOT COMPARED, no summary.json from that engine" % args.against)
        for dd in entry["diffs"]:
            if dd["changed"] or dd["added"] or dd["removed"]:
                print("       vs %s, %s: %d numbers changed, %d keys added, %d removed" % (args.against, dd["output"], len(dd["changed"]), len(dd["added"]), len(dd["removed"])))
                for c in dd["changed"][:8]:
                    print("         %.2e %s: %.6g -> %.6g" % (c["rel"], c["key"], c["against"], c["now"]))
        report.append(entry)
    if args.report:
        json.dump(report, open(args.report, "w"), indent=1)
    if other and not args.against.endswith(".dll"):
        subprocess.run(["git", "worktree", "remove", "--force", os.path.join(scratch, "baseline-src")], cwd=ROOT, capture_output=True)
    print("%d example(s) with problems" % failures)
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
