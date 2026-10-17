#!/usr/bin/env python3
"""Build the engine library for the Python package.

    python tools/build_engine.py            # hosted (managed) build -> src/ultrafast/bin/hosted   (needs .NET 10 SDK)
    python tools/build_engine.py --aot      # NativeAOT library     -> src/ultrafast/bin/native   (needs .NET 10 SDK + C toolchain)
    python tools/build_engine.py --aot --rid osx-arm64

The hosted build needs `pip install clr_loader` and the .NET 10 runtime at run time; the NativeAOT build needs
nothing at run time (it is what wheels ship).
"""
import argparse
import platform
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PKG = HERE.parent / "src" / "ultrafast"
REPO = HERE.parent.parent
NATIVE = REPO / "src" / "UltrafastEngine.Native" / "UltrafastEngine.Native.csproj"


def default_rid():
    m = platform.machine().lower()
    arch = "arm64" if m in ("arm64", "aarch64") else "x64"
    if sys.platform.startswith("win"):
        return "win-" + arch
    if sys.platform == "darwin":
        return "osx-" + arch
    return "linux-" + arch


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--aot", action="store_true", help="build the NativeAOT library")
    ap.add_argument("--rid", default=default_rid())
    ap.add_argument("--configuration", default="Release")
    a = ap.parse_args()
    if a.aot:
        out = PKG / "bin" / "native"
        cmd = ["dotnet", "publish", str(NATIVE), "-c", a.configuration, "-r", a.rid, "-o", str(REPO / "artifacts" / "native" / a.rid)]
        print(" ".join(cmd)); subprocess.check_call(cmd)
        out.mkdir(parents=True, exist_ok=True)
        for f in (REPO / "artifacts" / "native" / a.rid).iterdir():
            if f.suffix in (".dll", ".so", ".dylib") and f.stem.lstrip("lib") == "ultrafast":
                shutil.copy2(f, out / f.name); print("->", out / f.name)
    else:
        out = PKG / "bin" / "hosted"
        cmd = ["dotnet", "build", str(NATIVE), "-c", a.configuration, "-o", str(out), "-p:UF_NO_AOT=true"]
        print(" ".join(cmd)); subprocess.check_call(cmd)
        # first-party plugins (fitting, zemax): build them and copy their installable folders next to the engine
        for proj in (REPO / "plugins").glob("*/src/*.csproj"):
            subprocess.check_call(["dotnet", "build", str(proj), "-c", a.configuration], stdout=subprocess.DEVNULL)
        for proj in (REPO / "plugins").glob("*/src/*/*.Sampling.csproj"):
            subprocess.check_call(["dotnet", "build", str(proj), "-c", a.configuration], stdout=subprocess.DEVNULL)
        src = REPO / "artifacts" / "plugins"
        if src.is_dir():
            dst = out / "plugins"
            if dst.exists():
                shutil.rmtree(dst)
            shutil.copytree(src, dst, ignore=shutil.ignore_patterns("tools"))
            print("-> plugins:", ", ".join(sorted(p.name for p in dst.iterdir())))
        print("->", out)


if __name__ == "__main__":
    main()
