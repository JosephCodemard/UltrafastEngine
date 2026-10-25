#!/usr/bin/env python3
"""Compile and package UltrafastEngine release folders (no source code), for one or more platforms.

    ./build.sh                                             # CLI for this machine, all plugins
    ./build.sh --cli --python --cpp --csharp --plugins fitting zemax --platform win-x64 linux-x64 osx-arm64
    build.bat  --all --plugins none --platform win64 x86
    ./build.sh --list                                      # what can be built here

Each platform gives ``dist/UltrafastEngine-<version>-<platform>/`` (and a .zip / .tar.gz of it):

    bin/        command-line program ``ultrafast`` (self-contained: no .NET needed) + bin/plugins/<id>
    python/     wheel for ``pip install`` (hosted engine: any OS with the .NET 10 runtime; native: this platform only)
    cpp/        C API: include/ultrafast.h, lib/ (NativeAOT library), example/ (CMake)
    csharp/     UltrafastEngine.Core.dll for net10.0 and net48, plugin assemblies, how to reference them
    zemax/      OpticStudio extension (Windows; only when built against your ZOS-API, never containing it)
    schemas/, examples/ (--examples), docs/ (--docs), README.md, LICENSE, CHANGELOG.md, manifest.json

Needs the .NET 10 SDK and Python 3.9+ (which runs this script; nothing else: the wheel is written directly). NativeAOT
(``--cpp``, ``--python-engine native``) must be built on the target OS (Windows: "Desktop development with C++";
Linux / macOS: clang). Cross-platform CLI / C# / hosted-Python packages can be built anywhere; RID-specific
publishing downloads runtime packs from nuget.org on first use.

Documentation: docs/releasing.md.
"""
from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
import platform as _platform
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import zipfile
from base64 import urlsafe_b64encode
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CORE = REPO / "src" / "UltrafastEngine.Core" / "UltrafastEngine.Core.csproj"
CLI = REPO / "src" / "UltrafastEngine.Cli" / "UltrafastEngine.Cli.csproj"
NATIVE = REPO / "src" / "UltrafastEngine.Native" / "UltrafastEngine.Native.csproj"
ZEMAX_EXE = REPO / "plugins" / "zemax" / "src" / "UltrafastEngine.Zemax" / "UltrafastEngine.Zemax.csproj"

# plugin id -> (project that lays out artifacts/plugins/<id>, aliases)
PLUGINS = {
    "fitting": (REPO / "plugins" / "fitting" / "src" / "UltrafastEngine.Plugins.Fitting.csproj", ("transform", "transforms", "fit")),
    "zemax": (REPO / "plugins" / "zemax" / "src" / "UltrafastEngine.Zemax.Sampling" / "UltrafastEngine.Zemax.Sampling.csproj", ("opticstudio",)),
}

RIDS = ["win-x64", "win-x86", "win-arm64", "linux-x64", "linux-arm64", "linux-musl-x64", "osx-x64", "osx-arm64"]
ALIASES = {
    "win64": "win-x64", "windows": "win-x64", "win": "win-x64", "x64": "win-x64",
    "win32": "win-x86", "x86": "win-x86", "winarm": "win-arm64",
    "linux": "linux-x64", "linux64": "linux-x64", "linuxarm": "linux-arm64", "linux-arm": "linux-arm64", "alpine": "linux-musl-x64",
    "mac": "osx-arm64", "macos": "osx-arm64", "osx": "osx-arm64", "mac-arm": "osx-arm64", "mac-intel": "osx-x64", "macos-x64": "osx-x64",
}
# files that never go into a package: Ansys ZOS-API assemblies (property of Ansys), build leftovers
NEVER = ["ZOSAPI*", "*.dbg", "*.dSYM", "CLAUDE.md", "CLAUDE.local.md"]


# ---------------------------------------------------------------------------------------------------- helpers
class Log:
    verbose = False
    dry = False
    warnings: list[str] = []

    @staticmethod
    def step(msg):
        print("==> " + msg, flush=True)

    @staticmethod
    def warn(msg):
        Log.warnings.append(msg)
        print("WARNING: " + msg, flush=True)


def run(cmd, cwd=REPO, quiet=True):
    print("    $ " + " ".join(str(c) for c in cmd), flush=True)
    if Log.dry:
        return ""
    p = subprocess.run([str(c) for c in cmd], cwd=str(cwd), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if Log.verbose or p.returncode != 0:
        print(p.stdout[-6000:] if not Log.verbose else p.stdout)
    if p.returncode != 0:
        hint = ""
        if "NU1301" in p.stdout or "Unable to load the service index" in p.stdout:
            hint = ("\n    (nuget.org is not reachable: RID-specific publishing needs the runtime packs from nuget.org. "
                    "Use --platform portable or this machine's own platform, or build on a machine with internet access.)")
        if "Platform linker not found" in p.stdout or "link.exe" in p.stdout or "clang" in p.stdout.lower() and "not found" in p.stdout.lower():
            hint = "\n    (NativeAOT needs the platform C toolchain: Visual Studio 'Desktop development with C++' on Windows, clang on Linux / macOS.)"
        raise SystemExit("command failed (exit %d): %s%s" % (p.returncode, " ".join(str(c) for c in cmd), hint))
    return p.stdout


def copy_tree(src: Path, dst: Path, symbols=False, exclude=()):
    """Copy src -> dst, leaving out ZOS-API assemblies, symbol files (unless symbols) and the given patterns."""
    pats = list(NEVER) + list(exclude) + ([] if symbols else ["*.pdb"])
    if Log.dry:
        return
    for p in sorted(src.rglob("*")):
        rel = p.relative_to(src)
        if any(fnmatch.fnmatch(part, pat) for part in rel.parts for pat in pats):
            continue
        t = dst / rel
        if p.is_dir():
            t.mkdir(parents=True, exist_ok=True)
        else:
            t.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(p, t)


def version():
    m = re.search(r"<Version>([^<]+)</Version>", (REPO / "Directory.Build.props").read_text())
    return m.group(1) if m else "0.0.0"


def sdk_rid():
    try:
        out = subprocess.run(["dotnet", "--info"], stdout=subprocess.PIPE, text=True).stdout
        m = re.search(r"^\s*RID:\s*(\S+)", out, re.M)
        if m:
            return m.group(1)
    except FileNotFoundError:
        raise SystemExit("the .NET 10 SDK (dotnet) is not on PATH: https://dotnet.microsoft.com/download")
    mach = _platform.machine().lower()
    arch = "arm64" if mach in ("arm64", "aarch64") else ("x86" if mach in ("i386", "i686", "x86") else "x64")
    return ("win-" if os.name == "nt" else "osx-" if sys.platform == "darwin" else "linux-") + arch


def os_of(rid):
    if rid == "portable":
        return "any"
    if rid.startswith("win"):
        return "win"
    if rid.startswith("osx") or rid.startswith("maccatalyst"):
        return "osx"
    return "linux"


def host_os():
    return "win" if os.name == "nt" else "osx" if sys.platform == "darwin" else "linux"


def native_lib_names(rid):
    o = os_of(rid)
    return ["ultrafast.dll", "ultrafast.lib"] if o == "win" else (["libultrafast.dylib"] if o == "osx" else ["libultrafast.so"])


# ---------------------------------------------------------------------------------------------------- build steps
class Builder:
    def __init__(self, a):
        self.a = a
        self.ver = a.version or version()
        self.out = Path(a.out).resolve()
        self.cache = Path(tempfile.mkdtemp(prefix="uf-package-"))
        self._plugins_built = False
        self._core_built = False
        self._hosted_wheel = None
        self._aot = {}

    # plugins -------------------------------------------------------------------------------------
    def build_plugins(self):
        if self._plugins_built or not self.a.plugins:
            return
        Log.step("plugins: " + ", ".join(self.a.plugins))
        for pid in self.a.plugins:
            run(["dotnet", "build", PLUGINS[pid][0], "-c", "Release", "-nologo"])
        self._plugins_built = True

    def copy_plugins(self, dst: Path, with_tools=False):
        for pid in self.a.plugins:
            src = REPO / "artifacts" / "plugins" / pid
            if not Log.dry and not (src / "plugin.json").exists():
                raise SystemExit("plugin %s was not laid out in %s" % (pid, src))
            copy_tree(src, dst / pid, self.a.symbols, exclude=() if with_tools else ("tools",))

    # command-line program --------------------------------------------------------------------------
    def cli(self, rid, root: Path):
        Log.step("CLI for %s" % rid)
        dst = root / "bin"
        tmp = self.cache / ("cli-" + rid)
        cmd = ["dotnet", "publish", CLI, "-c", "Release", "-nologo", "-p:NoPluginBuild=true", "-o", tmp]
        if rid != "portable":
            cmd += ["-r", rid, "--self-contained", "true" if self.a.self_contained else "false"]
            if self.a.single_file:
                cmd += ["-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true"]
        run(cmd)
        copy_tree(tmp, dst, self.a.symbols)
        self.build_plugins()
        self.copy_plugins(dst / "plugins")
        if rid == "portable" and not Log.dry:
            (dst / "ultrafast.cmd").write_text('@echo off\r\ndotnet "%~dp0ultrafast.dll" %*\r\n')
            sh = dst / "ultrafast"
            sh.write_text('#!/bin/sh\nexec dotnet "$(dirname "$0")/ultrafast.dll" "$@"\n')
            sh.chmod(0o755)

    # C# libraries ------------------------------------------------------------------------------------
    def csharp(self, rid, root: Path):
        Log.step("C# libraries")
        if not self._core_built:
            run(["dotnet", "build", CORE, "-c", "Release", "-nologo"])
            self._core_built = True
        dst = root / "csharp"
        for tfm in ("net10.0", "net48"):
            b = REPO / "src" / "UltrafastEngine.Core" / "bin" / "Release" / tfm
            if not Log.dry and not (b / "UltrafastEngine.Core.dll").exists():
                Log.warn("UltrafastEngine.Core for %s was not built (net48 needs Windows, or Mono's reference assemblies on Linux / macOS)" % tfm)
                continue
            for n in ("UltrafastEngine.Core.dll", "UltrafastEngine.Core.xml") + (("UltrafastEngine.Core.pdb",) if self.a.symbols else ()):
                if (b / n).exists() and not Log.dry:
                    (dst / "lib" / tfm).mkdir(parents=True, exist_ok=True)
                    shutil.copy2(b / n, dst / "lib" / tfm / n)
        self.build_plugins()
        self.copy_plugins(dst / "plugins")
        if not Log.dry:
            (dst / "README.md").write_text(CSHARP_README.format(ver=self.ver))

    # NativeAOT library (C API, native Python engine) --------------------------------------------------
    def aot(self, rid):
        if rid in self._aot:
            return self._aot[rid]
        if rid == "portable":
            Log.warn("the NativeAOT library is platform-specific: no C API / native Python in the portable package")
            self._aot[rid] = None
            return None
        if os_of(rid) != host_os():
            Log.warn("the NativeAOT library for %s must be built on %s (this is %s): skipped" % (rid, os_of(rid), host_os()))
            self._aot[rid] = None
            return None
        Log.step("NativeAOT library for %s" % rid)
        tmp = self.cache / ("aot-" + rid)
        run(["dotnet", "publish", NATIVE, "-c", "Release", "-nologo", "-r", rid, "-o", tmp])
        self._aot[rid] = tmp
        return tmp

    def cpp(self, rid, root: Path):
        lib = self.aot(rid)
        if lib is None:
            return
        Log.step("C API for %s" % rid)
        dst = root / "cpp"
        if Log.dry:
            return
        (dst / "lib").mkdir(parents=True, exist_ok=True)
        found = []
        for p in lib.iterdir():
            if p.name in native_lib_names(rid) or p.name in ("ultrafast.so", "ultrafast.dylib"):
                name = p.name if p.name in native_lib_names(rid) else "lib" + p.name
                shutil.copy2(p, dst / "lib" / name)
                found.append(name)
        if not found:
            raise SystemExit("no native library found in " + str(lib))
        copy_tree(REPO / "src" / "UltrafastEngine.Native" / "include", dst / "include")
        ex = dst / "example"
        ex.mkdir(parents=True, exist_ok=True)
        shutil.copy2(REPO / "docs" / "examples" / "cpp" / "run_system.cpp", ex / "run_system.cpp")
        (ex / "CMakeLists.txt").write_text(CPP_CMAKE)
        (dst / "README.md").write_text(CPP_README.format(ver=self.ver, rid=rid, libs=", ".join(found)))

    # Python wheel -------------------------------------------------------------------------------------
    def python(self, rid, root: Path):
        engine = self.a.python_engine
        dst = root / "python"
        if engine == "hosted":
            if self._hosted_wheel is None:
                self._hosted_wheel = self._wheel("hosted", None)
            if self._hosted_wheel and not Log.dry:
                dst.mkdir(parents=True, exist_ok=True)
                shutil.copy2(self._hosted_wheel, dst / self._hosted_wheel.name)
        else:
            if self.a.plugins:
                Log.warn("the native (NativeAOT) Python engine cannot load plugins at run time: %s are left out of the wheel; use --python-engine hosted to ship them" % ", ".join(self.a.plugins))
            w = self._wheel("native", rid)
            if w and not Log.dry:
                dst.mkdir(parents=True, exist_ok=True)
                shutil.copy2(w, dst / w.name)
        if not Log.dry and dst.exists():
            (dst / "README.md").write_text(PY_README.format(ver=self.ver, engine=engine))

    def _wheel(self, engine, rid):
        Log.step("Python wheel (%s engine%s)" % (engine, "" if rid is None else ", " + rid))
        stage = self.cache / ("py-" + engine + ("-" + rid if rid else ""))
        pkg = stage / "ultrafast"
        if not Log.dry:
            shutil.copytree(REPO / "python" / "src" / "ultrafast", pkg, ignore=shutil.ignore_patterns("bin", "__pycache__", "*.pyc"))
        binp = pkg / "bin"
        if engine == "hosted":
            run(["dotnet", "build", NATIVE, "-c", "Release", "-nologo", "-p:UF_NO_AOT=true", "-p:GenerateDocumentationFile=false", "-o", binp / "hosted"])
            if not Log.dry and not self.a.symbols:
                for p in (binp / "hosted").glob("*.pdb"):
                    p.unlink()
            self.build_plugins()
            self.copy_plugins(binp / "hosted" / "plugins")
        else:
            lib = self.aot(rid)
            if lib is None:
                return None
            if not Log.dry:
                (binp / "native").mkdir(parents=True, exist_ok=True)
                for p in lib.iterdir():
                    if p.suffix in (".dll", ".so", ".dylib") and p.stem.replace("lib", "", 1) == "ultrafast":
                        name = p.name if os_of(rid) == "win" or p.name.startswith("lib") else "lib" + p.name
                        shutil.copy2(p, binp / "native" / name)
        if Log.dry:
            return None
        return build_wheel(stage, self.cache / ("wheel-" + engine + ("-" + rid if rid else "")), "any" if engine == "hosted" else wheel_platform(rid))

    # OpticStudio extension ---------------------------------------------------------------------------
    def zemax(self, rid, root: Path):
        if os_of(rid) not in ("win", "any") or (rid != "portable" and not rid.endswith("x64")):
            if self.a.zemax_explicit:
                Log.warn("the OpticStudio extension is a Windows x64 program: not built for %s" % rid)
            return
        zdir = self.a.zos_api_dir or os.environ.get("ZOSAPI_DIR")
        cand = [Path(zdir)] if zdir else [ZEMAX_EXE.parent / "lib", Path(r"C:\Program Files\Ansys Zemax OpticStudio 2025 R1")]
        zos = next((c for c in cand if (c / "ZOSAPI.dll").exists()), None)
        if zos is None:
            if not self.a.zemax_explicit:
                print("    (OpticStudio extension not built: ZOS-API not found; give --zos-api-dir to include it)")
                return
            Log.warn("OpticStudio extension skipped: ZOS-API not found (give --zos-api-dir \"C:\\Program Files\\Ansys Zemax OpticStudio 2025 R1\"). "
                     "The zemax plugin (export / replay commands) is still in the package.")
            return
        Log.step("OpticStudio extension (ZOS-API from %s; the ZOS-API assemblies are not packaged)" % zos)
        run(["dotnet", "build", ZEMAX_EXE, "-c", "Release", "-nologo", "-p:ZosApiDir=%s" % zos, "-p:UseZosApiStubs=false"])
        out = ZEMAX_EXE.parent / "bin" / "Release" / "net48"
        copy_tree(out, root / "zemax", self.a.symbols)
        if not Log.dry:
            (root / "zemax" / "install-zemax.bat").write_text(ZEMAX_BAT)
            (root / "zemax" / "README.md").write_text(ZEMAX_README)

    # common files -------------------------------------------------------------------------------------
    def common(self, rid, root: Path, parts):
        if Log.dry:
            return
        for n in ("LICENSE", "CHANGELOG.md"):
            shutil.copy2(REPO / n, root / n)
        copy_tree(REPO / "schemas", root / "schemas")
        if self.a.examples:
            copy_tree(REPO / "docs" / "examples", root / "examples", exclude=("results", "results-*", "__pycache__"))
        if self.a.docs:
            copy_tree(REPO / "docs", root / "docs", exclude=("examples", "design", "*.py", "__pycache__"))
        (root / "README.md").write_text(release_readme(self.ver, rid, parts, self.a.plugins))

    def manifest(self, rid, root: Path, parts):
        if Log.dry:
            return
        files = {}
        for p in sorted(root.rglob("*")):
            if p.is_file():
                files[p.relative_to(root).as_posix()] = hashlib.sha256(p.read_bytes()).hexdigest()
        (root / "manifest.json").write_text(json.dumps({
            "product": "UltrafastEngine", "version": self.ver, "platform": rid, "components": parts,
            "plugins": self.a.plugins, "selfContained": self.a.self_contained if rid != "portable" else False,
            "pythonEngine": self.a.python_engine if "python" in parts else None,
            "warnings": [w for w in Log.warnings], "sha256": files}, indent=1))

    def archive(self, root: Path, rid):
        if Log.dry or self.a.no_archive:
            return None
        if os_of(rid) in ("win", "any"):
            z = root.parent / (root.name + ".zip")
            with zipfile.ZipFile(z, "w", zipfile.ZIP_DEFLATED) as zf:
                for p in sorted(root.rglob("*")):
                    zf.write(p, Path(root.name) / p.relative_to(root))
            return z
        t = root.parent / (root.name + ".tar.gz")
        with tarfile.open(t, "w:gz") as tf:
            tf.add(root, arcname=root.name)   # keeps the executable bits
        return t

    def package(self, rid):
        root = self.out / ("UltrafastEngine-%s-%s" % (self.ver, rid))
        if root.exists() and not Log.dry:
            shutil.rmtree(root)
        if not Log.dry:
            root.mkdir(parents=True)
        parts = []
        if self.a.cli:
            self.cli(rid, root); parts.append("cli")
        if self.a.csharp:
            self.csharp(rid, root); parts.append("csharp")
        if self.a.cpp:
            self.cpp(rid, root)
            if Log.dry or (root / "cpp").exists():
                parts.append("cpp")
        if self.a.python:
            self.python(rid, root)
            if Log.dry or (root / "python").exists():
                parts.append("python")
        if self.a.zemax:
            self.zemax(rid, root)
            if not Log.dry and (root / "zemax").exists():
                parts.append("zemax-extension")
        self.common(rid, root, parts)
        self.manifest(rid, root, parts)
        arc = self.archive(root, rid)
        return root, arc, parts


# ---------------------------------------------------------------------------------------------------- wheel retag
def wheel_platform(rid):
    return {"win-x64": "win_amd64", "win-x86": "win32", "win-arm64": "win_arm64", "linux-x64": "manylinux2014_x86_64",
            "linux-arm64": "manylinux2014_aarch64", "linux-musl-x64": "musllinux_1_2_x86_64", "osx-x64": "macosx_10_15_x86_64",
            "osx-arm64": "macosx_11_0_arm64"}.get(rid, "linux_x86_64" if "x64" in rid else "linux_" + rid.split("-")[-1])


def _pyproject():
    """name, version, summary, requires-python, licence, dependencies, extras and scripts from python/pyproject.toml."""
    text = (REPO / "python" / "pyproject.toml").read_text(encoding="utf-8")
    try:
        import tomllib  # Python 3.11+
        d = tomllib.loads(text)["project"]
        return d
    except ImportError:   # Python 3.9 / 3.10: the few fields we need, by pattern
        def val(k):
            m = re.search(r"^%s\s*=\s*\"([^\"]*)\"" % re.escape(k), text, re.M)
            return m.group(1) if m else None
        def arr(k, src=text):
            m = re.search(r"^%s\s*=\s*\[([^\]]*)\]" % re.escape(k), src, re.M)
            return re.findall(r"\"([^\"]+)\"", m.group(1)) if m else []
        d = {"name": val("name"), "version": val("version"), "description": val("description"), "requires-python": val("requires-python"),
             "dependencies": arr("dependencies"), "optional-dependencies": {}, "scripts": {}}
        sec = re.search(r"\[project\.optional-dependencies\](.*?)(\n\[|\Z)", text, re.S)
        if sec:
            for k in re.findall(r"^(\w+)\s*=", sec.group(1), re.M):
                d["optional-dependencies"][k] = arr(k, sec.group(1))
        sec = re.search(r"\[project\.scripts\](.*?)(\n\[|\Z)", text, re.S)
        if sec:
            d["scripts"] = dict(re.findall(r"^([\w-]+)\s*=\s*\"([^\"]+)\"", sec.group(1), re.M))
        return d


def build_wheel(stage: Path, out: Path, plat: str) -> Path:
    """Write a wheel of the package folders in ``stage`` (no setuptools needed). ``plat`` "any" for the hosted engine
    (pure .NET assemblies: one wheel for every OS), a platform tag when it carries the NativeAOT library."""
    meta = _pyproject()
    name, ver = meta["name"], meta["version"]
    dist = re.sub(r"[-_.]+", "_", name)
    tag = "py3-none-" + plat
    out.mkdir(parents=True, exist_ok=True)
    whl = out / ("%s-%s-%s.whl" % (dist, ver, tag))
    di = "%s-%s.dist-info" % (dist, ver)
    lines = ["Metadata-Version: 2.1", "Name: " + name, "Version: " + ver]
    if meta.get("description"):
        lines.append("Summary: " + meta["description"])
    if meta.get("requires-python"):
        lines.append("Requires-Python: " + meta["requires-python"])
    lines.append("License: MIT")
    for dep in meta.get("dependencies", []):
        lines.append("Requires-Dist: " + dep)
    for extra, deps in (meta.get("optional-dependencies") or {}).items():
        lines.append("Provides-Extra: " + extra)
        for dep in deps:
            lines.append('Requires-Dist: %s ; extra == "%s"' % (dep, extra))
    readme = REPO / "python" / "README.md"
    body = readme.read_text(encoding="utf-8") if readme.exists() else ""
    files = {}
    for p in sorted(stage.rglob("*")):
        if p.is_file():
            files[p.relative_to(stage).as_posix()] = p.read_bytes()
    files[di + "/METADATA"] = ("\n".join(lines + ["Description-Content-Type: text/markdown", ""]) + "\n" + body).encode()
    files[di + "/WHEEL"] = ("Wheel-Version: 1.0\nGenerator: UltrafastEngine build/package.py\nRoot-Is-Purelib: %s\nTag: %s\n"
                            % ("true" if plat == "any" else "false", tag)).encode()
    scripts = meta.get("scripts") or {}
    if scripts:
        files[di + "/entry_points.txt"] = ("[console_scripts]\n" + "".join("%s = %s\n" % kv for kv in scripts.items())).encode()
    files[di + "/top_level.txt"] = "".join(sorted({k.split("/")[0] + "\n" for k in files if not k.startswith(di)})).encode()
    record = []
    for k, v in files.items():
        h = urlsafe_b64encode(hashlib.sha256(v).digest()).rstrip(b"=").decode()
        record.append("%s,sha256=%s,%d" % (k, h, len(v)))
    record.append(di + "/RECORD,,")
    files[di + "/RECORD"] = ("\n".join(record) + "\n").encode()
    with zipfile.ZipFile(whl, "w", zipfile.ZIP_DEFLATED) as z:
        for k, v in files.items():
            info = zipfile.ZipInfo(k, date_time=(2026, 1, 1, 0, 0, 0))
            info.external_attr = 0o644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, v)
    return whl


# ---------------------------------------------------------------------------------------------------- texts
def release_readme(ver, rid, parts, plugins):
    s = ["# UltrafastEngine %s -- %s" % (ver, rid), "",
         "Nonlinear ultrafast pulse propagation through optical systems. This folder is a compiled release: no source code.",
         "Contents: " + (", ".join(parts) or "common files only") + "; plugins: " + (", ".join(plugins) or "none") + ".", ""]
    if "cli" in parts:
        exe = "bin\\ultrafast.exe" if rid.startswith("win") else "bin/ultrafast"
        s += ["## Command line (`bin/`)", "", "```", "%s run system.ufs run.json -o results" % exe, "%s plugins list" % exe,
              "%s schema run -o run.schema.json" % exe, "```", "",
              ("The program is self-contained (no .NET installation needed)." if rid != "portable" else
               "Portable build: needs the .NET 10 runtime; run `bin/ultrafast` (macOS / Linux) or `bin\\ultrafast.cmd` (Windows)."),
              "Plugins are in `bin/plugins/`; add more with `ultrafast plugins install <folder or zip>`.", ""]
    if "python" in parts:
        s += ["## Python (`python/`)", "", "`pip install python/ultrafast_engine-*.whl` -- see python/README.md.", ""]
    if "cpp" in parts:
        s += ["## C / C++ (`cpp/`)", "", "`include/ultrafast.h` and the native library in `lib/`; a CMake example in `example/` -- see cpp/README.md.", ""]
    if "csharp" in parts:
        s += ["## C# (`csharp/`)", "", "Reference `lib/net10.0/UltrafastEngine.Core.dll` (or `lib/net48/` for .NET Framework / OpticStudio) -- see csharp/README.md.", ""]
    if "zemax-extension" in parts:
        s += ["## OpticStudio (`zemax/`)", "", "Run `zemax\\install-zemax.bat` once (it copies ZOSAPI_NetHelper.dll from your OpticStudio installation); see zemax/README.md.", ""]
    s += ["`schemas/` holds the JSON schemas for system files and run specs (editor completion). `manifest.json` lists",
          "every file with its SHA-256. Licence: MIT (LICENSE). The Ansys ZOS-API assemblies are not part of this package.", ""]
    return "\n".join(s)


CSHARP_README = """# UltrafastEngine {ver} for C#

`lib/net10.0/UltrafastEngine.Core.dll` (.NET 10) and `lib/net48/UltrafastEngine.Core.dll` (.NET Framework 4.8,
e.g. inside an OpticStudio extension). No NuGet dependencies.

```xml
<ItemGroup>
  <Reference Include="UltrafastEngine.Core"><HintPath>path/to/csharp/lib/net10.0/UltrafastEngine.Core.dll</HintPath></Reference>
</ItemGroup>
```

```csharp
var sys = UltrafastEngine.Systems.SystemDescription.Load("system.ufs");
var run = UltrafastEngine.Specs.RunSpec.Load("run.json");
var res = UltrafastEngine.Simulation.Run(sys, run);
res.WriteAll("results");
```

Plugins: put the folders of `plugins/` into a `plugins/` folder next to your executable, or into
`~/.ultrafast/plugins`, or point `ULTRAFAST_PLUGIN_PATH` at this `plugins/` folder.
"""

CPP_README = """# UltrafastEngine {ver} C API ({rid})

* `include/ultrafast.h` -- the API (systems, runs, callbacks, results).
* `lib/` -- {libs}: the NativeAOT library. It needs no .NET runtime. Plugins cannot be loaded by it.
* `example/` -- `run_system.cpp` and a CMake project:

```bash
cmake -S example -B build -DULTRAFAST_DIR="$PWD"
cmake --build build --config Release
build/run_system system.ufs run.json results        # (Windows: build\\Release\\run_system.exe)
```

Keep the library next to your program (or on the library path).
"""

CPP_CMAKE = """# Example program for the UltrafastEngine C API (release package layout: ../include, ../lib).
cmake_minimum_required(VERSION 3.16)
project(ultrafast_cpp_example CXX)
set(CMAKE_CXX_STANDARD 17)
set(ULTRAFAST_DIR "${CMAKE_CURRENT_SOURCE_DIR}/.." CACHE PATH "UltrafastEngine cpp/ folder (include/, lib/)")
add_executable(run_system run_system.cpp)
target_include_directories(run_system PRIVATE "${ULTRAFAST_DIR}/include")
if(WIN32)
  target_link_libraries(run_system PRIVATE "${ULTRAFAST_DIR}/lib/ultrafast.lib")
  add_custom_command(TARGET run_system POST_BUILD COMMAND ${CMAKE_COMMAND} -E copy_if_different "${ULTRAFAST_DIR}/lib/ultrafast.dll" $<TARGET_FILE_DIR:run_system>)
else()
  find_library(ULTRAFAST_LIB NAMES ultrafast PATHS "${ULTRAFAST_DIR}/lib" NO_DEFAULT_PATH REQUIRED)
  target_link_libraries(run_system PRIVATE "${ULTRAFAST_LIB}")
  set_target_properties(run_system PROPERTIES BUILD_RPATH "${ULTRAFAST_DIR}/lib" INSTALL_RPATH "$ORIGIN")
endif()
"""

PY_README = """# UltrafastEngine {ver} for Python ({engine} engine)

```bash
pip install ultrafast_engine-*.whl            # add [plot] for figures: pip install "ultrafast_engine-...whl[plot]"
python -c "import ultrafast as uf; print(uf.version())"
```

* **hosted** wheel (any OS): needs the .NET 10 runtime (https://dotnet.microsoft.com/download) and
  `pip install clr_loader` (`pip install "ultrafast_engine-...whl[hosted]"`). Loads plugins (bin/hosted/plugins).
* **native** wheel (this platform only): the NativeAOT engine, nothing else needed; plugins cannot be loaded.

Docs: docs/reference/python.md (with --docs in the package, or in the repository).
"""

ZEMAX_README = """# UltrafastEngine OpticStudio extension

`UltrafastEngine.Zemax.exe` connects to OpticStudio through ZOS-API (Interactive Extension or standalone) and
exports the loaded system to a .ufs file.

1. Run `install-zemax.bat` once: it copies `ZOSAPI_NetHelper.dll` from your OpticStudio installation next to the
   program. (The ZOS-API assemblies are Ansys property and are not distributed with UltrafastEngine.)
2. In OpticStudio: Programming > Interactive Extension, then run `UltrafastEngine.Zemax.exe export ...`
   (`UltrafastEngine.Zemax.exe help`).
"""

ZEMAX_BAT = r"""@echo off
rem Copies ZOSAPI_NetHelper.dll from your OpticStudio installation next to UltrafastEngine.Zemax.exe.
rem Usage: install-zemax.bat ["C:\Program Files\Ansys Zemax OpticStudio 2025 R1"]
setlocal
set "ZOS=%~1"
if "%ZOS%"=="" for /d %%D in ("C:\Program Files\Ansys Zemax OpticStudio*" "C:\Program Files\Zemax OpticStudio*") do set "ZOS=%%~D"
if not exist "%ZOS%\ZOS-API\Libraries\ZOSAPI_NetHelper.dll" (
  echo OpticStudio not found. Give its folder: install-zemax.bat "C:\Program Files\Ansys Zemax OpticStudio 2025 R1"
  exit /b 1
)
copy /y "%ZOS%\ZOS-API\Libraries\ZOSAPI_NetHelper.dll" "%~dp0" >nul && echo Copied ZOSAPI_NetHelper.dll from "%ZOS%".
"""


# ---------------------------------------------------------------------------------------------------- main
def parse(argv):
    ap = argparse.ArgumentParser(prog="build", description="Compile and package UltrafastEngine release folders (docs/releasing.md).",
                                 formatter_class=argparse.RawDescriptionHelpFormatter,
                                 epilog="platforms: %s, portable, host (this machine)\naliases: %s" % (", ".join(RIDS), ", ".join("%s=%s" % kv for kv in ALIASES.items())))
    ap.add_argument("--cli", action="store_true", help="command-line program (default when no component is given)")
    ap.add_argument("--python", action="store_true", help="Python wheel")
    ap.add_argument("--cpp", action="store_true", help="C API: header + NativeAOT library + CMake example")
    ap.add_argument("--csharp", action="store_true", help="UltrafastEngine.Core.dll (net10.0, net48) + plugin assemblies")
    ap.add_argument("--zemax", action="store_true", help="OpticStudio extension (Windows; needs your ZOS-API; on by default with the zemax plugin and --all)")
    ap.add_argument("--all", action="store_true", help="every component")
    ap.add_argument("--plugins", nargs="*", default=None, metavar="ID", help="plugins to include: fitting (alias transform), zemax, all, none (default: all)")
    ap.add_argument("--platform", "--platforms", nargs="*", default=None, dest="platforms", metavar="RID", help="target platforms (default: this machine)")
    ap.add_argument("--framework-dependent", action="store_true", help="CLI needs an installed .NET 10 runtime (smaller; default: self-contained)")
    ap.add_argument("--single-file", action="store_true", help="CLI as one executable file")
    ap.add_argument("--python-engine", choices=["hosted", "native"], default="hosted", help="hosted: any OS, .NET runtime, plugins (default); native: NativeAOT, this platform")
    ap.add_argument("--examples", action="store_true", help="include docs/examples")
    ap.add_argument("--docs", action="store_true", help="include the documentation (Markdown)")
    ap.add_argument("--symbols", action="store_true", help="keep .pdb debug symbols")
    ap.add_argument("--zos-api-dir", help="OpticStudio folder with ZOSAPI.dll (for the extension; never packaged)")
    ap.add_argument("--version", help="version label (default: from Directory.Build.props)")
    ap.add_argument("--out", default=str(REPO / "dist"), help="output folder (default: dist/)")
    ap.add_argument("--no-archive", action="store_true", help="do not zip / tar the folders")
    ap.add_argument("--test", action="store_true", help="run the C# tests before packaging")
    ap.add_argument("--list", action="store_true", help="show platforms, plugins and what this machine can build")
    ap.add_argument("--dry-run", action="store_true", help="print the commands only")
    ap.add_argument("-v", "--verbose", action="store_true")
    a = ap.parse_args(argv)
    a.self_contained = not a.framework_dependent
    if a.all:
        a.cli = a.python = a.cpp = a.csharp = a.zemax = True
    if not (a.cli or a.python or a.cpp or a.csharp or a.zemax):
        a.cli = True
    # plugins
    names = ["all"] if a.plugins is None else (a.plugins or ["none"])
    sel = []
    for n in names:
        n = n.lower().strip(",")
        if n == "all":
            sel = list(PLUGINS)
        elif n == "none":
            sel = []
        else:
            pid = next((k for k, (_, al) in PLUGINS.items() if n == k or n in al), None)
            if pid is None:
                ap.error("unknown plugin '%s' (known: %s)" % (n, ", ".join(PLUGINS)))
            if pid not in sel:
                sel.append(pid)
    a.plugins = sel
    # the OpticStudio extension comes with the zemax plugin for Windows targets when your ZOS-API is found
    a.zemax_explicit = a.zemax and not a.all
    if "zemax" in sel:
        a.zemax = True
    # platforms
    host = sdk_rid()
    rids = []
    for p in (a.platforms or ["host"]):
        p = p.lower().strip(",")
        r = host if p in ("host", "native", "this") else ALIASES.get(p, p)
        if r not in RIDS and r != "portable" and r != host:
            if not re.match(r"^[a-z0-9.]+-(x64|x86|arm64|arm)$", r):
                ap.error("unknown platform '%s' (known: %s, portable, host)" % (p, ", ".join(RIDS)))
            Log.warn("platform '%s' is not a standard RID; passing it to dotnet as is" % r)
        if r not in rids:
            rids.append(r)
    a.rids, a.host = rids, host
    return a


def main(argv=None):
    a = parse(sys.argv[1:] if argv is None else argv)
    Log.verbose, Log.dry = a.verbose, a.dry_run
    if a.list:
        print("UltrafastEngine %s packaging -- this machine: %s (%s)" % (version(), a.host, host_os()))
        print("platforms: " + ", ".join(RIDS) + ", portable (framework-dependent, any OS), host")
        print("plugins:   " + ", ".join("%s (aliases: %s)" % (k, ", ".join(al)) for k, (_, al) in PLUGINS.items()))
        print("here:      CLI / C# / hosted Python for every platform (RID publishing needs nuget.org);")
        print("           C API and native Python only for %s targets (NativeAOT does not cross-compile between OSes)." % host_os())
        return 0
    b = Builder(a)
    print("UltrafastEngine %s -> %s" % (b.ver, b.out))
    print("components: %s | plugins: %s | platforms: %s" % (
        ", ".join(k for k in ("cli", "python", "cpp", "csharp", "zemax") if getattr(a, k)), ", ".join(a.plugins) or "none", ", ".join(a.rids)))
    if a.test:
        Log.step("tests")
        run(["dotnet", "build", REPO / "tests" / "UltrafastEngine.Tests", "-c", "Release", "-f", "net10.0", "-nologo"])
        run(["dotnet", REPO / "tests" / "UltrafastEngine.Tests" / "bin" / "Release" / "net10.0" / "UltrafastEngine.Tests.dll"])
    results = []
    try:
        for rid in a.rids:
            results.append((rid,) + b.package(rid))
    finally:
        shutil.rmtree(b.cache, ignore_errors=True)
    print()
    for rid, root, arc, parts in results:
        size = sum(p.stat().st_size for p in root.rglob("*") if p.is_file()) / 1e6 if root.exists() else 0
        print("%-16s %s (%.0f MB: %s)%s" % (rid, root, size, ", ".join(parts), "\n%16s %s" % ("", arc) if arc else ""))
    if Log.warnings:
        print("\n%d warning(s):" % len(Log.warnings))
        for w in Log.warnings:
            print("  - " + w)
    return 0


if __name__ == "__main__":
    sys.exit(main())
