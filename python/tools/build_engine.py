#!/usr/bin/env python3
"""Build the engine library for the Python package. (Wrapper used by build/setup.py: `python build/setup.py --python`.)

    python tools/build_engine.py                      # hosted (managed) build -> src/ultrafast/bin/hosted   (needs .NET 10 SDK)
    python tools/build_engine.py --plugins fitting    # ... with only these plugins (and what they require); `none` = core only
    python tools/build_engine.py --aot                # NativeAOT library     -> src/ultrafast/bin/native   (needs .NET 10 SDK + C toolchain)
    python tools/build_engine.py --aot --rid osx-arm64

The hosted build needs `pip install clr_loader` and the .NET 10 runtime at run time; the NativeAOT build needs
nothing at run time (it is what wheels ship). Plugins: default all. With --aot the gpu / hitran / ao plugins are
compiled in (the NativeAOT library cannot load plugin folders), the others are not available there.
"""
import argparse
import json
import platform
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PKG = HERE.parent / "src" / "ultrafast"
REPO = HERE.parent.parent
NATIVE = REPO / "src" / "UltrafastEngine.Native" / "UltrafastEngine.Native.csproj"
PLUGINS = REPO / "plugins"


def default_rid():
    m = platform.machine().lower()
    arch = "arm64" if m in ("arm64", "aarch64") else "x64"
    if sys.platform.startswith("win"):
        return "win-" + arch
    if sys.platform == "darwin":
        return "osx-" + arch
    return "linux-" + arch


def plugin_ids():
    """Ids of the plugins in plugins/ (folders with a plugin.json)."""
    return sorted(p.parent.name for p in PLUGINS.glob("*/plugin.json"))


def resolve_plugins(names):
    """Names -> ids with the plugins they require (plugin.json "requires"). None / ['all'] = every plugin; ['none'] = no plugin."""
    known = plugin_ids()
    if names is None or "all" in names:
        return known
    want = [n for n in names if n != "none"]
    if any(n.lower() == "transform" for n in want):          # old name of the fitting plugin
        want = ["fitting" if n.lower() == "transform" else n for n in want]
    out = []
    def add(n):
        if n not in known:
            sys.exit("unknown plugin '%s' (available: %s)" % (n, ", ".join(known)))
        for r in json.loads((PLUGINS / n / "plugin.json").read_text(encoding="utf-8")).get("requires", []):
            add(r)
        if n not in out:
            out.append(n)
    for n in want:
        add(n)
    return out


def plugin_projects(pid):
    """The projects that lay out artifacts/plugins/<pid>: the plugin assembly, and for zemax the OpticStudio-independent sampling library."""
    src = PLUGINS / pid / "src"
    return sorted(src.glob("*.csproj")) + sorted(src.glob("*/*.Sampling.csproj"))


def build_plugins(ids, configuration="Release", framework=None, quiet=True):
    """Build the plugins `ids` (artifacts/plugins/<id>/ is laid out by each build). framework: e.g. net10.0 to skip net48."""
    for pid in ids:
        for proj in plugin_projects(pid):
            cmd = ["dotnet", "build", str(proj), "-c", configuration]
            if framework:
                cmd += ["-f", framework]
            print(" ".join(cmd))
            subprocess.check_call(cmd, stdout=subprocess.DEVNULL if quiet else None)


def copy_plugins(ids, dst):
    """Copy artifacts/plugins/<id> for the chosen ids to dst (replacing dst); returns the ids copied."""
    src = REPO / "artifacts" / "plugins"
    if dst.exists():
        shutil.rmtree(dst)
    done = []
    for pid in ids:
        if (src / pid).is_dir():
            shutil.copytree(src / pid, dst / pid, ignore=shutil.ignore_patterns("tools"))
            done.append(pid)
    return done


def build_hosted(plugins=None, configuration="Release", framework=None):
    out = PKG / "bin" / "hosted"
    ids = resolve_plugins(plugins)
    cmd = ["dotnet", "build", str(NATIVE), "-c", configuration, "-o", str(out), "-p:UF_NO_AOT=true"]
    if any((PKG / "bin" / "native").glob("*ultrafast.*")):
        print("WARNING: bin/native holds a NativeAOT library, which loads before this hosted build and has no plugins. It is not\n"
              "         rebuilt here, so Python may run an old engine. Delete src/ultrafast/bin/native, rerun with --aot, or set\n"
              "         ULTRAFAST_LIBRARY=" + str(out / "UltrafastEngine.Native.dll"), file=sys.stderr)
    print(" ".join(cmd)); subprocess.check_call(cmd)
    build_plugins(ids, configuration, framework)
    done = copy_plugins(ids, out / "plugins")
    print("-> plugins:", ", ".join(done) if done else "(none)")
    print("->", out)
    return out


def build_native(rid, configuration="Release", plugins=None):
    out = PKG / "bin" / "native"
    ids = resolve_plugins(plugins)
    cmd = ["dotnet", "publish", str(NATIVE), "-c", configuration, "-r", rid, "-o", str(REPO / "artifacts" / "native" / rid)]
    for pid in ("gpu", "hitran", "ao"):                       # compiled into the NativeAOT library
        if pid not in ids:
            cmd.append("-p:UF_WITH_%s=false" % pid.upper())
    print(" ".join(cmd)); subprocess.check_call(cmd)
    out.mkdir(parents=True, exist_ok=True)
    for f in (REPO / "artifacts" / "native" / rid).iterdir():
        if f.suffix in (".dll", ".so", ".dylib") and f.stem.lstrip("lib") == "ultrafast":
            shutil.copy2(f, out / f.name); print("->", out / f.name)
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--aot", action="store_true", help="build the NativeAOT library")
    ap.add_argument("--rid", default=default_rid())
    ap.add_argument("--configuration", default="Release")
    ap.add_argument("--plugins", nargs="*", default=None, metavar="ID", help="plugins to include (default: all; none = core only): " + ", ".join(plugin_ids()))
    ap.add_argument("--framework", default=None, help="build plugins for this target framework only (e.g. net10.0; default: all, net48 needs Mono on macOS/Linux)")
    a = ap.parse_args()
    if a.aot:
        build_native(a.rid, a.configuration, a.plugins)
    else:
        build_hosted(a.plugins, a.configuration, a.framework)


if __name__ == "__main__":
    main()
