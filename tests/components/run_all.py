"""Run every component verification script and write tests/components/report/index.html.

What it does: runs tests/components/<family>.py for each family (each in its own process, so one crash does not
stop the others), collects the JSON record each writes to the report folder, and builds one HTML gallery: per family
its figures (the check in the title, the analytic formula under it) and the table of numbers with pass / fail.
Exit code 1 when any check failed or a script crashed.

Usage:
  python tests/components/run_all.py              # figures + report/index.html
  python tests/components/run_all.py --no-plots   # numbers only (CI); the HTML has the tables
  python tests/components/run_all.py linear crystals   # some families only
"""
from __future__ import annotations

import argparse
import html
import json
import math
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
FAMILIES = ["linear", "splitters_masks", "polarisation", "crystals", "instruments"]


def fmt(x) -> str:
    try:
        x = float(x)
    except (TypeError, ValueError):
        return html.escape(str(x))
    if not math.isfinite(x):
        return "–"
    return f"{x:.6g}"


def page(records, wall_s: float) -> str:
    total = sum(len(r["rows"]) for r in records)
    failed = sum(1 for r in records for row in r["rows"] if not row["ok"])
    known = sum(1 for r in records for row in r["rows"] if row.get("status") == "KNOWN")
    out = ["<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'><title>UltrafastEngine component verification</title>",
           "<style>body{font-family:system-ui,sans-serif;max-width:1100px;margin:2em auto;padding:0 1em;color:#222}"
           "h1{font-size:1.6em}h2{margin-top:2em;border-bottom:1px solid #ccc}figure{margin:1.5em 0}"
           "figcaption{font-size:.9em;color:#444}code{background:#f4f4f4;padding:0 .2em}"
           "table{border-collapse:collapse;font-size:.85em;width:100%}td,th{border-bottom:1px solid #eee;padding:2px 6px;text-align:right}"
           "td:first-child,th:first-child{text-align:left}.ok{color:#1a7f37}.FAIL{color:#cf222e;font-weight:bold}.KNOWN{color:#9a6700;font-weight:bold}"
           "img{max-width:100%}nav a{margin-right:1em}</style></head><body>",
           "<h1>UltrafastEngine: every component checked alone</h1>",
           f"<p>{total} checks, <b>{total - failed - known} passed</b>, <b class='KNOWN'>{known} known bugs</b>, "
           f"<b class='FAIL'>{failed} failed</b>; {wall_s:.0f} s. Each figure overlays the engine (solid) on the analytic answer "
           "(dashed black); the lower panel is the residual. How to read it: <code>docs/component-tests.md</code>.</p><nav>"]
    out += [f"<a href='#{r['family']}'>{html.escape(r['family'])}</a>" for r in records]
    out.append("</nav>")
    for r in records:
        nf = sum(1 for row in r["rows"] if not row["ok"])
        out.append(f"<h2 id='{r['family']}'>{html.escape(r['title'])} <small>({len(r['rows']) - nf}/{len(r['rows'])})</small></h2>")
        for n in r.get("notes", []):
            out.append(f"<p><i>{html.escape(n)}</i></p>")
        for fig in r.get("figures", []):
            out.append("<figure>")
            if fig.get("file"):
                out.append(f"<img src='{html.escape(fig['file'])}' alt='{html.escape(fig['title'])}'>")
            out.append(f"<figcaption><b>{html.escape(fig['title'])}</b><br>Analytic: <code>{html.escape(fig['formula'])}</code></figcaption></figure>")
        out.append("<table><tr><th>check</th><th>engine</th><th>analytic</th><th>rel. error</th><th>tolerance</th><th>unit</th><th>result</th><th>formula / note</th></tr>")
        for row in r["rows"]:
            st = row.get("status", "ok" if row["ok"] else "FAIL")
            cls = "ok" if st.startswith("ok") else st
            note = row.get("note") or row.get("formula") or ""
            out.append(f"<tr><td>{html.escape(row['name'])}</td><td>{fmt(row['engine'])}</td><td>{fmt(row['analytic'])}</td>"
                       f"<td>{fmt(row['relError'])}</td><td>{fmt(row['tolerance'])}</td><td>{html.escape(row['unit'])}</td>"
                       f"<td class='{cls}'>{html.escape(st)}</td><td style='text-align:left'>{html.escape(note)}</td></tr>")
        out.append("</table>")
    out.append("</body></html>")
    return "\n".join(out)


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("families", nargs="*", help=f"families to run (default: all of {', '.join(FAMILIES)})")
    p.add_argument("--no-plots", action="store_true", help="numbers only (CI mode)")
    p.add_argument("--out", default=str(HERE / "report"), help="report folder (default tests/components/report)")
    a = p.parse_args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    fams = a.families or FAMILIES
    t0 = time.time()
    records, crashed = [], []
    for fam in fams:
        script = HERE / f"{fam}.py"
        args = [sys.executable, str(script), "--out", str(out)] + (["--no-plots"] if a.no_plots else [])
        rec_path = out / f"{fam}.json"
        if rec_path.exists():
            rec_path.unlink()
        rc = subprocess.call(args)
        if rec_path.exists():
            records.append(json.loads(rec_path.read_text()))
        if rc != 0 and not rec_path.exists():
            crashed.append(fam)
            records.append({"family": fam, "title": f"{fam}: the script crashed (exit {rc})", "rows": [
                {"name": "script ran", "engine": float("nan"), "analytic": float("nan"), "relError": float("nan"), "tolerance": 0,
                 "unit": "", "ok": False, "status": "FAIL", "note": f"exit code {rc}"}], "figures": []})
    (out / "index.html").write_text(page(records, time.time() - t0), encoding="utf-8")
    failed = sum(1 for r in records for row in r["rows"] if not row["ok"])
    total = sum(len(r["rows"]) for r in records)
    known = sum(1 for r in records for row in r["rows"] if row.get("status") == "KNOWN")
    print(f"\n== verification: {total - failed - known}/{total} checks passed, {known} known bugs, {failed} failed"
          + (f", crashed: {', '.join(crashed)}" if crashed else "") + f"; report: {out / 'index.html'}")
    return 1 if failed or crashed else 0


if __name__ == "__main__":
    sys.exit(main())
