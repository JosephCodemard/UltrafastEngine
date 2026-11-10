"""``python -m ultrafast_twin`` / ``ultrafast twin``: init, fit, validate, predict, design, track, check, serve."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import List, Optional

from . import design as _design
from .project import ProjectError, TwinProject, init


def _kv(items: Optional[List[str]], numeric_only: bool = False) -> dict:
    out = {}
    for it in items or []:
        k, _, v = it.partition("=")
        if not _:
            raise SystemExit(f"expected NAME=VALUE, got {it!r}")
        try:
            out[k] = float(v)
        except ValueError:
            if numeric_only:
                raise SystemExit(f"{k}: a number is expected, got {v!r}")
            out[k] = v
    return out


def _print_validation(r) -> None:
    print(f"verdict: {r['verdict']}  (training chi^2/point {r['trainingReducedChi2']:.3g})")
    for c in r["conditions"]:
        print(f"  {c['name']:<14} {'held out' if c['heldOut'] else 'training':<9} chi^2/point {c['reducedChi2']:8.3g}   inside 2-sigma band {100 * c['coverage2sigma']:5.1f} %   {c['status']}")
    for f in r["flags"]:
        print("  FLAG:", f)


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(prog="ultrafast twin", description=__doc__)
    sub = ap.add_subparsers(dest="cmd", required=True)
    a = sub.add_parser("init", help="create a project (synthetic lab template)"); a.add_argument("folder"); a.add_argument("--template", default="synthetic-lab"); a.add_argument("--force", action="store_true")
    a = sub.add_parser("fit", help="fit all training conditions"); a.add_argument("folder"); a.add_argument("--name")
    a = sub.add_parser("validate", help="held-out validation with credible bands"); a.add_argument("folder"); a.add_argument("--samples", type=int, default=12); a.add_argument("--json", action="store_true")
    a = sub.add_parser("predict", help="predict a condition with a credible band"); a.add_argument("folder"); a.add_argument("--var", action="append"); a.add_argument("--set", action="append"); a.add_argument("--samples", type=int, default=12)
    a = sub.add_parser("design", help="rank candidate measurements by expected information gain"); a.add_argument("folder")
    g = a.add_mutually_exclusive_group(required=True)
    g.add_argument("--placement", action="store_true", help="candidates: the instrument at each element"); g.add_argument("--variable", help="candidates: values of this condition variable")
    a.add_argument("--values", help="comma-separated values of --variable"); a.add_argument("--set-key", help="path that also receives the value, e.g. pulse/energyJ"); a.add_argument("--picks", type=int, default=1)
    a = sub.add_parser("track", help="assimilate recorded files with an ensemble Kalman filter"); a.add_argument("folder"); a.add_argument("--data", action="append", required=True)
    a.add_argument("--var", action="append"); a.add_argument("--set", action="append"); a.add_argument("--process", action="append", help="NAME=SIGMA: expected drift per measurement"); a.add_argument("--members", type=int, default=16)
    a = sub.add_parser("check", help="check goals.json, or run the benchmark"); a.add_argument("folder", nargs="?"); a.add_argument("--goals"); a.add_argument("--benchmark", action="store_true")
    a = sub.add_parser("serve", help="MCP server on stdio for agents (workspace-scoped)"); a.add_argument("folder"); 
    args = ap.parse_args(argv)
    try:
        if args.cmd == "init":
            p = init(args.folder, args.template, args.force)
            print(f"created {p.folder} ({len(p.conditions())} conditions); next: ultrafast twin fit {args.folder}")
        elif args.cmd == "fit":
            tw = TwinProject(args.folder).fit(args.name)
            f = tw.fit
            print(f"fit {tw.dir.name}: chi^2 {f['chi2Initial']:.4g} -> {f['chi2Final']:.4g}, {f['evaluations']} runs, converged {f['converged']}")
            for p in f["parameters"]:
                print(f"  {p['name']:<28} {p['value']:<14.6g} +- {p['sigma'] if p['sigma'] is not None else float('nan'):.3g}")
        elif args.cmd == "validate":
            r = TwinProject(args.folder).twin().validate(args.samples)
            print(json.dumps(r, indent=2) if args.json else "", end="")
            if not args.json:
                _print_validation(r)
            return 0 if r["verdict"] in ("ok", "marginal") else 1
        elif args.cmd == "predict":
            tw = TwinProject(args.folder).twin()
            pr = tw.predict(_kv(args.var, True), _kv(args.set), n_samples=args.samples)
            for k, (m, s) in pr["scalars"].items():
                print(f"  {k:<14} {m:.6g} +- {s:.2g}")
        elif args.cmd == "design":
            tw = TwinProject(args.folder).twin()
            if args.placement:
                cands = _design.placement_candidates(tw)
            else:
                cands = _design.energy_candidates(tw, args.variable, [float(v) for v in args.values.split(",")], args.set_key)
            r = tw.design(cands, picks=args.picks)
            for x in r["ranking"]:
                print(f"  {x['name']:<28} expected information gain {x['gain']:.3f}")
            print("chosen:", ", ".join(c["name"] for c in r["chosen"]))
            (tw.project.folder / "reports").mkdir(exist_ok=True)
            (tw.project.folder / "reports" / "design.json").write_text(json.dumps(r, indent=2), encoding="utf-8")
        elif args.cmd == "track":
            tw = TwinProject(args.folder).twin()
            tr = tw.tracker(members=args.members, process_sigma=_kv(args.process, True))
            for d in args.data:
                s = tr.update(str(Path(d).resolve()), _kv(args.var, True), _kv(args.set))
                print(f"  {Path(d).name}: " + ", ".join(f"{n}={v:.6g}" for n, v in s["mean"].items()) + (f"   DRIFTED: {', '.join(s['drifted'])}" if s["drifted"] else ""))
            (tw.project.folder / "reports").mkdir(exist_ok=True)
            tr.write(tw.project.folder / "reports" / "track.json")
        elif args.cmd == "check":
            from . import goals
            if args.benchmark:
                r = goals.benchmark()
                for t in r["tasks"]:
                    print(f"  {'PASS' if t['passed'] else 'FAIL'}  {t['task']}: {t['expected']}  -> {t['answer']}")
            else:
                proj = TwinProject(args.folder)
                r = goals.check(proj.twin(), goals.load_goals(args.goals or proj.folder / "goals.json"))
                for c in r["checks"]:
                    print(f"  {'PASS' if c['passed'] else 'FAIL'}  {c['id']}: {c['detail']}")
            return 0 if r["passed"] else 1
        elif args.cmd == "serve":
            print("ultrafast twin serve: the MCP server is not implemented yet (docs/design/notes/s4-twin.md, section 5)", file=sys.stderr)
            return 2
    except (ProjectError, KeyError, ValueError) as e:
        print(f"ultrafast twin: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
