#!/usr/bin/env python3
"""Build UltrafastEngine FROM SOURCE for use on this machine (standard library only).

    python build/setup.py                              # CLI with every plugin  -> artifacts/cli/<rid>/ultrafast
    python build/setup.py --python                     # + Python package (hosted engine) installed editable into the active Python
    python build/setup.py --python --venv .venv        # ... into a new virtual environment
    python build/setup.py --cli --plugins fitting hitran    # CLI with only these plugins (and what they require)
    python build/setup.py --python --python-engine native   # NativeAOT engine inside the package (needs a C toolchain; gpu / hitran / ao compiled in)
    python build/setup.py --cpp                        # C API: artifacts/native/<rid>/ (ultrafast.h is in src/UltrafastEngine.Native/include)
    python build/setup.py --plugin-only fitting        # rebuild one plugin and refresh it in the CLI and Python builds already made
    python build/setup.py --python --check             # build, then smoke-test (CLI runs, `import ultrafast`, plugins listed)
    python build/setup.py --doctor                     # what is installed here (dotnet, python, mono, C toolchain) and what you can build

Wrappers: ./setup.sh (macOS / Linux), setup.bat (Windows). Release packages for other computers: ./build.sh (docs/releasing.md);
installing a release: install.py. Guide: docs/install.md.
Main functions: ``main``, ``build_cli``, ``build_python``, ``build_cpp``, ``refresh_plugin``, ``check``, ``doctor``.
"""
from __future__ import annotations

import argparse
import os
import platform
import shutil
import subprocess
import sys
import venv
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
sys.path.insert(0, str(REPO / "python" / "tools"))
import build_engine as be  # noqa: E402  (python/tools/build_engine.py: plugin discovery and the Python engine build)

CLI_PROJECT = REPO / "src" / "UltrafastEngine.Cli" / "UltrafastEngine.Cli.csproj"
EXE = "ultrafast.exe" if os.name == "nt" else "ultrafast"
DRY = False
STALE: list = []      # older NativeAOT libraries that would shadow a hosted Python build


def run(cmd, **kw):
    print("$ " + " ".join(str(c) for c in cmd))
    if not DRY:
        subprocess.check_call([str(c) for c in cmd], **kw)


def need_dotnet():
    if shutil.which("dotnet") is None:
        sys.exit("The .NET 10 SDK is required but `dotnet` was not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and rerun.")
    v = subprocess.run(["dotnet", "--version"], capture_output=True, text=True).stdout.strip()
    if not v.startswith("10."):
        sys.exit("The .NET 10 SDK is required; `dotnet --version` says '%s'. Install .NET 10 (global.json pins 10.0.x)." % v)


def cli_dir(a) -> Path:
    return Path(a.out).resolve() / "cli" / a.rid if a.out else REPO / "artifacts" / "cli" / a.rid


def build_cli(a, ids):
    """Publish the CLI (framework-dependent, runs with the .NET 10 runtime) and lay out only the chosen plugins next to it."""
    out = cli_dir(a)
    fw = None if a.net48 else "net10.0"
    run(["dotnet", "publish", CLI_PROJECT, "-c", a.configuration, "-r", a.rid, "--self-contained", "false", "-p:NoPluginBuild=true", "-o", out])
    if not DRY:
        be.build_plugins(ids, a.configuration, fw)
        done = be.copy_plugins(ids, out / "plugins")
        print("-> CLI plugins:", ", ".join(done) if done else "(none)")
    print("-> CLI:", out / EXE)
    return out


def build_python(a, ids):
    fw = None if a.net48 else "net10.0"
    if DRY:
        print("$ build_engine (%s) plugins=%s" % (a.python_engine, ids))
    elif a.python_engine == "native":
        be.build_native(a.rid, a.configuration, ids)
    else:
        be.build_hosted(ids, a.configuration, fw)
    stale = sorted(be.PKG.joinpath("bin", "native").glob("*ultrafast.*")) if a.python_engine == "hosted" else []
    if stale and a.clean_native:
        for f in stale:
            print("removing stale NativeAOT library", f); 
            if not DRY:
                f.unlink()
    elif stale:
        if not DRY:
            STALE.extend(stale)
        print("ERROR: python/src/ultrafast/bin/native holds an older NativeAOT library. `import ultrafast` loads it BEFORE the hosted\n"
              "       engine just built, so Python would run the OLD engine. Rerun with --clean-native to delete it (it is a build output),\n"
              "       or set ULTRAFAST_LIBRARY=%s" % (be.PKG / "bin" / "hosted" / "UltrafastEngine.Native.dll"), file=sys.stderr)
    py = Path(sys.executable)
    if a.venv:
        vdir = Path(a.venv).resolve()
        if not DRY and not vdir.exists():
            print("creating virtual environment", vdir); venv.EnvBuilder(with_pip=True).create(vdir)
        py = vdir / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    if not a.no_pip:
        extras = ["plot"] + (["hosted"] if a.python_engine == "hosted" else []) + (["test"] if a.dev else [])
        run([py, "-m", "pip", "install", "-e", "%s[%s]" % (REPO / "python", ",".join(extras))])
    return py


def build_cpp(a, ids):
    run(["dotnet", "publish", be.NATIVE, "-c", a.configuration, "-r", a.rid, "-o", REPO / "artifacts" / "native" / a.rid]
        + ["-p:UF_WITH_%s=false" % p.upper() for p in ("gpu", "hitran", "ao") if p not in ids])
    print("-> C API library: artifacts/native/%s ; header: src/UltrafastEngine.Native/include/ultrafast.h" % a.rid)


def refresh_plugin(a, name):
    """Rebuild one plugin and copy it into the CLI build and the hosted Python build, if those exist."""
    ids = be.resolve_plugins([name])
    be.build_plugins(ids, a.configuration, None if a.net48 else "net10.0")
    for dst in (cli_dir(a) / "plugins", be.PKG / "bin" / "hosted" / "plugins"):
        if dst.parent.exists():
            for pid in ids:
                s = REPO / "artifacts" / "plugins" / pid
                if s.is_dir():
                    shutil.rmtree(dst / pid, ignore_errors=True)
                    shutil.copytree(s, dst / pid, ignore=shutil.ignore_patterns("tools"))
                    print("-> refreshed", dst / pid)
        else:
            print("(no build at %s: nothing to refresh)" % dst.parent)


def check(a, py):
    ok = True
    exe = cli_dir(a) / EXE
    if exe.exists():
        r = subprocess.run([str(exe), "plugins", "list"], capture_output=True, text=True)
        print("CLI :", "ok" if r.returncode == 0 else "FAILED", (r.stdout.split() or [""])[0:1] and "plugins: " + ", ".join(l.split()[0] for l in r.stdout.splitlines() if l and not l.startswith(" ")))
        ok &= r.returncode == 0
    if a.python:
        # no ULTRAFAST_LIBRARY here: test what `import ultrafast` really loads, so a stale library that shadows the build is caught
        env = {k: v for k, v in os.environ.items() if k != "ULTRAFAST_LIBRARY"}
        code = ("import ultrafast as uf, ultrafast._native as n; lib = n.library(); uf.calc.tbp('sech2'); "
                "print(lib.mode, lib.path, [p.id for p in uf.plugins.installed()])")
        r = subprocess.run([str(py), "-c", code], capture_output=True, text=True, env=env)
        print("Python:", "ok " + r.stdout.strip() if r.returncode == 0 else "FAILED\n" + r.stderr[-600:] + ("\n  -> an older library shadows the build: rerun with --clean-native" if STALE else ""))
        ok &= r.returncode == 0
    return ok


def doctor():
    def ver(cmd):
        try:
            return subprocess.run(cmd, capture_output=True, text=True, timeout=20).stdout.strip().splitlines()[0]
        except Exception:  # noqa: BLE001
            return None
    rows = [("platform (release rid)", be.default_rid() + ", " + platform.platform()),
            (".NET SDK (need 10.x)", ver(["dotnet", "--version"])),
            ("Python (need 3.10+)", sys.version.split()[0]),
            ("C compiler (NativeAOT, --cpp)", next((c for c in ("clang", "gcc", "cl") if shutil.which(c)), None)),
            ("Mono (net48 on macOS/Linux, optional)", shutil.which("mono"))]
    for k, v in rows:
        print("%-42s %s" % (k, v or "NOT FOUND"))
    print("plugins:", ", ".join(be.plugin_ids()))
    print("CLI build :", "yes" if (REPO / "artifacts" / "cli" / be.default_rid() / EXE).exists() else "no")
    print("Python bin:", "yes" if (be.PKG / "bin").is_dir() and any((be.PKG / "bin").iterdir()) else "no")


def main(argv=None):
    global DRY
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--cli", action="store_true", help="the `ultrafast` command line (default when nothing else is chosen)")
    ap.add_argument("--python", action="store_true", help="the Python package: build its engine and pip install it (editable)")
    ap.add_argument("--cpp", action="store_true", help="the C API library (NativeAOT)")
    ap.add_argument("--plugins", nargs="*", default=None, metavar="ID", help="plugins to include: %s | all | none (default: all)" % " ".join(be.plugin_ids()))
    ap.add_argument("--plugin-only", metavar="ID", help="rebuild one plugin and refresh it in existing CLI / Python builds")
    ap.add_argument("--python-engine", choices=["hosted", "native"], default="hosted", help="hosted: .NET runtime, all plugins, any OS (default); native: NativeAOT, no .NET at run time")
    ap.add_argument("--venv", metavar="DIR", help="create / use this virtual environment for the Python package")
    ap.add_argument("--no-pip", action="store_true", help="build the Python engine but do not run pip")
    ap.add_argument("--clean-native", action="store_true", help="delete an older NativeAOT library in python/src/ultrafast/bin/native that would shadow the hosted build")
    ap.add_argument("--dev", action="store_true", help="also install the test extra (pytest)")
    ap.add_argument("--rid", default=be.default_rid(), help="runtime identifier (default: this machine)")
    ap.add_argument("--configuration", default="Release")
    ap.add_argument("--out", help="folder for the CLI build (default: artifacts/)")
    ap.add_argument("--net48", action="store_true", help="also build the plugins for .NET Framework 4.8 (needs Mono on macOS / Linux)")
    ap.add_argument("--check", action="store_true", help="smoke-test what was built")
    ap.add_argument("--doctor", action="store_true", help="show the tools found on this machine and exit")
    ap.add_argument("--dry-run", action="store_true", help="print the commands only")
    a = ap.parse_args(argv)
    DRY = a.dry_run
    STALE.clear()
    if a.doctor:
        doctor(); return 0
    if not DRY:
        need_dotnet()
    ids = be.resolve_plugins(a.plugins)
    if a.plugin_only:
        refresh_plugin(a, a.plugin_only); return 0
    if not (a.cli or a.python or a.cpp):
        a.cli = True
    print("plugins:", ", ".join(ids) or "(none)")
    py = None
    if a.cli:
        build_cli(a, ids)
    if a.python:
        py = build_python(a, ids)
    if a.cpp:
        build_cpp(a, ids)
    if a.check and not DRY and not check(a, py):
        return 1
    if STALE:
        print("\nThe build finished, but Python will not use it until the older NativeAOT library is removed: rerun with --clean-native.", file=sys.stderr)
        return 1
    print("\nDone. Next: %s" % ("ultrafast run docs/examples/GettingStarted/spm-plate/plate.ufs docs/examples/GettingStarted/spm-plate/run.json -o results   (CLI: %s)" % (cli_dir(a) / EXE) if a.cli else "python docs/examples/GettingStarted/quickstart/quickstart.py"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
