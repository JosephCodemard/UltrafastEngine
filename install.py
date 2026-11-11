#!/usr/bin/env python3
"""Selective installer / uninstaller for UltrafastEngine releases (standard library only).

    python install.py --list                                          # components, sizes, what is installed
    python install.py --include python tests examples.Branched        # download and install only these
    python install.py --include cli plugins.fitting --dest ~/uf       # CLI and one plugin
    python install.py --include examples.Branched.pump-probe          # one example (shared helpers come with it)
    python install.py --include all --exclude media                   # everything that is built for this platform
    python install.py --uninstall --include tests                     # remove part ...
    python install.py --uninstall                                     # ... or all
    python install.py --include docs examples --source git            # source folders by git partial clone

A release is a ``manifest.json`` plus one ``.tar.gz`` per component and platform (written by ``build/package.py --components``).
The installer reads the manifest, resolves the names (a name selects its sub-components: ``examples`` = every ``examples.<Group>``;
dependencies come along; ``--exclude`` removes names), downloads only those assets, checks their SHA-256, unpacks them under ``--dest``
and writes ``<dest>/.ultrafast-install.json`` listing every file it wrote with its hash. Uninstall removes exactly those files
(a file you changed is left in place with a warning), so a second ``--include`` adds to an install and ``--version`` upgrades it.
Python wheels are installed with ``pip install --find-links <dest>/python`` (never from PyPI; their dependencies are).
The ZOS-API (Ansys) is never installed or removed. Guide: docs/releasing.md.

Main functions: ``fetch_manifest``, ``resolve`` (names -> plan), ``install``, ``uninstall``, ``detect_platform``.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import platform as _platform
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request
from pathlib import Path, PurePosixPath

RECORD = ".ultrafast-install.json"
REPOSITORY = "https://github.com/JosephCodemard/UltrafastEngine"
ZOSAPI_SUFFIXES = (".dll", ".xml", ".pdb")


class InstallError(Exception):
    """A problem the user can fix (bad name, hash mismatch, missing asset); printed without a traceback."""


# ---------------------------------------------------------------------------------------------------- platform
def detect_platform() -> str:
    """The release platform name of this computer: win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64, osx-x64."""
    system = _platform.system().lower()
    machine = _platform.machine().lower()
    arch = "arm64" if machine in ("arm64", "aarch64") else "x64" if machine in ("x86_64", "amd64") else "x86" if machine in ("i386", "i686", "x86") else machine
    os_name = {"windows": "win", "linux": "linux", "darwin": "osx"}.get(system, system)
    return "%s-%s" % (os_name, arch)


# ---------------------------------------------------------------------------------------------------- manifest
def base_url_for(version: str | None, repository: str = REPOSITORY) -> str:
    """Where manifest.json and the assets live: the latest release, or the release of tag ``v<version>``."""
    if not version or version == "latest":
        return repository + "/releases/latest/download"
    return "%s/releases/download/v%s" % (repository, version.lstrip("v"))


def _open(url: str):
    if "://" not in url:
        url = Path(url).resolve().as_uri()
    return urllib.request.urlopen(url, timeout=60)


def fetch_manifest(base: str) -> dict:
    """Read ``<base>/manifest.json`` (``base``: https:// or file:// URL, or a folder)."""
    url = base.rstrip("/") + "/manifest.json"
    try:
        with _open(url) as r:
            m = json.loads(r.read().decode("utf-8"))
    except Exception as e:  # noqa: BLE001 -- any failure to fetch is reported the same way
        raise InstallError("cannot read %s: %s" % (url, e))
    if m.get("product") != "UltrafastEngine" or "components" not in m:
        raise InstallError("%s is not an UltrafastEngine release manifest" % url)
    return m


# ---------------------------------------------------------------------------------------------------- resolution
def _children(components: dict, name: str) -> list[str]:
    return [c for c in components if c == name or c.startswith(name + ".")]


def _split_selector(components: dict, sel: str):
    """Match a selector to components: ``(names, only)``. ``only`` is a path prefix for a name deeper than any component
    (``examples.Branched.pump-probe``): the group is installed, but only that part of it (plus the files directly in the group folder)."""
    if sel in ("all", "everything"):
        return [c for c, d in components.items() if d.get("inAll", True) and (d.get("assets") or d.get("sourcePaths") or d.get("kind") == "built")], None
    names = _children(components, sel)
    if names:
        return names, None
    parts = sel.split(".")
    for n in range(len(parts) - 1, 0, -1):
        head = ".".join(parts[:n])
        if head in components and components[head].get("subpath"):
            rest = "/".join(parts[n:])
            return [head], components[head]["subpath"].replace("{rest}", rest)
    known = sorted(components)
    raise InstallError("unknown component '%s'. Known: %s" % (sel, ", ".join(known)))


def expand(components: dict, include: list[str], exclude: list[str] | None = None):
    """Names -> ``(chosen, explicit, required, notes)``: ``chosen`` maps component -> list of only-prefixes (None = whole); ``explicit`` are the
    leaves asked for by their own name; ``required`` came in as dependencies. Works on a manifest and on the packager's definitions."""
    notes: list[str] = []
    chosen: dict[str, list[str] | None] = {}
    explicit: set[str] = set()
    for sel in include:
        names, only = _split_selector(components, sel)
        for n in names:
            if sel == n:
                explicit.add(n)
            if only is None:
                chosen[n] = None
            elif chosen.get(n, []) is not None:
                chosen.setdefault(n, [])
                chosen[n].append(only)  # type: ignore[union-attr]
    excluded: set[str] = set()
    for sel in exclude or []:
        for n in _split_selector(components, sel)[0]:
            excluded.add(n)
            chosen.pop(n, None)
            explicit.discard(n)
    required: set[str] = set()
    queue = list(chosen)
    while queue:
        n = queue.pop()
        for r in components[n].get("requires", []):
            if any(r in components[c].get("provides", []) for c in chosen):       # python-native provides python
                continue
            kids = _children(components, r)
            if not kids:
                raise InstallError("component %s requires %s, which this release does not have" % (n, r))
            for rn in kids:
                if rn not in chosen:
                    chosen[rn] = None
                    required.add(rn)
                    queue.append(rn)
                    if rn in excluded:
                        notes.append("%s is excluded but %s needs it: included anyway" % (rn, n))
                elif chosen[rn] is not None:       # a partial selection needed whole by a dependency
                    chosen[rn] = None
    return chosen, explicit, required, notes


def resolve(components: dict, include: list[str], exclude: list[str] | None = None, platform: str = "linux-x64"):
    """Resolve names to a plan: ``{component: {"only": [prefixes] or None, "asset": asset-dict}}``, plus notes.

    * a name selects itself and every ``name.<child>``; ``all`` selects everything except components with ``"inAll": false`` (media);
    * ``requires`` are added (transitively); ``--exclude`` removes names (and the children), but never a required one;
    * the asset for ``platform`` is used, else the platform-independent one (``any``); a leaf asked for by name without an asset raises,
      one that only came from ``all`` / a parent raises only if a dependency needs it, else it is reported in the notes.
    """
    chosen, explicit, required, notes = expand(components, include, exclude)
    plan: dict[str, dict] = {}
    for n in sorted(chosen):
        d = components[n]
        assets = d.get("assets") or []
        if not assets:
            continue   # a virtual parent
        asset = next((a for a in assets if a["platform"] == platform), None) or next((a for a in assets if a["platform"] == "any"), None)
        if asset is None:
            msg = "%s is not built for %s (available: %s)" % (n, platform, ", ".join(sorted(a["platform"] for a in assets)))
            if n in explicit or n in required:
                raise InstallError(msg)
            notes.append("skipped: " + msg)
            continue
        plan[n] = {"only": chosen[n], "asset": asset}
    for n in plan:
        for c in components[n].get("conflicts", []):
            if c in plan:
                raise InstallError("%s and %s cannot be installed together" % (n, c))
    if not plan:
        raise InstallError("nothing to install for platform %s" % platform)
    return plan, notes


# ---------------------------------------------------------------------------------------------------- record
def read_record(dest: Path) -> dict:
    p = dest / RECORD
    if p.exists():
        return json.loads(p.read_text(encoding="utf-8"))
    return {"schema": 1, "components": {}, "files": {}, "pip": []}


def write_record(dest: Path, rec: dict) -> None:
    p = dest / RECORD
    if not rec["components"]:
        if p.exists():
            p.unlink()
        return
    dest.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(rec, indent=1, sort_keys=True), encoding="utf-8")


def _sha256_file(p: Path) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _safe_member(name: str) -> PurePosixPath:
    p = PurePosixPath(name)
    if p.is_absolute() or ".." in p.parts or not p.parts:
        raise InstallError("unsafe path in archive: %s" % name)
    if p.name.lower().startswith("zosapi") and p.suffix.lower() in ZOSAPI_SUFFIXES:
        raise InstallError("archive contains a ZOS-API file (%s): refusing to install it" % name)
    return p


# ---------------------------------------------------------------------------------------------------- download
def _download(base: str, asset: dict, workdir: Path) -> Path:
    url = base.rstrip("/") + "/" + asset["file"]
    target = workdir / asset["file"]
    h = hashlib.sha256()
    try:
        with _open(url) as r, open(target, "wb") as f:
            for chunk in iter(lambda: r.read(1 << 20), b""):
                h.update(chunk)
                f.write(chunk)
    except Exception as e:  # noqa: BLE001
        raise InstallError("cannot download %s: %s" % (url, e))
    if h.hexdigest() != asset["sha256"]:
        target.unlink()
        raise InstallError("SHA-256 mismatch for %s (expected %s, got %s): nothing was installed from it" % (asset["file"], asset["sha256"], h.hexdigest()))
    return target


def _wanted(rel: PurePosixPath, only: list[str] | None) -> bool:
    if not only:
        return True
    s = rel.as_posix()
    return any(s.startswith(o.rstrip("/") + "/") or s == o.rstrip("/") for o in only) or _is_group_level(rel, only)


def _is_group_level(rel: PurePosixPath, only: list[str]) -> bool:
    """Files that sit directly in the group folder (docs/examples/Computing/_optics.py) travel with every part of the group."""
    parent = PurePosixPath(only[0].rstrip("/")).parent
    return rel.parent == parent


# ---------------------------------------------------------------------------------------------------- install
def _remove_files(dest: Path, rec: dict, paths: list[str], component: str, force: bool, log) -> None:
    for rel in paths:
        entry = rec["files"].get(rel)
        if entry is None:
            continue
        owners = [o for o in entry["components"] if o != component]
        if owners:
            entry["components"] = owners
            continue
        p = dest / rel
        if p.exists():
            if not force and _sha256_file(p) != entry["sha256"]:
                log("WARNING: %s was changed after installation: left in place" % rel)
                rec["files"].pop(rel)
                continue
            p.unlink()
        rec["files"].pop(rel)
        _prune_empty(dest, p.parent)


def _prune_empty(dest: Path, d: Path) -> None:
    dest = dest.resolve()
    d = d.resolve()
    while d != dest and dest in d.parents:
        try:
            d.rmdir()
        except OSError:
            return
        d = d.parent


def _pip(python: str, args: list[str], log, dry: bool) -> None:
    cmd = [python, "-m", "pip"] + args
    log("    $ " + " ".join(cmd))
    if dry:
        return
    p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if p.returncode != 0:
        raise InstallError("pip failed (exit %d):\n%s" % (p.returncode, p.stdout[-3000:]))


def install(base: str, include: list[str], exclude: list[str] | None, dest: Path, platform: str | None = None, force: bool = False,
            dry_run: bool = False, use_pip: bool = True, python: str | None = None, log=print, manifest: dict | None = None) -> dict:
    """Install the selected components from the release at ``base`` into ``dest``; returns the new record."""
    platform = platform or detect_platform()
    manifest = manifest or fetch_manifest(base)
    comps = manifest["components"]
    plan, notes = resolve(comps, include, exclude, platform)
    for n in notes:
        log("note: " + n)
    log("UltrafastEngine %s -> %s (%s): %s" % (manifest["version"], dest, platform, ", ".join(plan)))
    if dry_run:
        for n, p in plan.items():
            log("  %-26s %-12s %8.1f MB  %s%s" % (n, p["asset"]["platform"], p["asset"]["size"] / 1e6, p["asset"]["file"], "  only " + ", ".join(p["only"]) if p["only"] else ""))
        pips = [comps[n]["pip"] for n in plan if comps[n].get("pip")]
        for pp in pips:
            _pip(python or sys.executable, ["install", "--find-links", str(dest / pp["folder"]), pp["spec"]], log, True)
        return read_record(dest)
    rec = read_record(dest)
    dest.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="uf-install-") as tmp:
        tmpd = Path(tmp)
        archives = {}
        for n, p in plan.items():      # download and verify everything first: a bad hash leaves the install untouched
            log("  downloading %s (%s, %.1f MB)" % (n, p["asset"]["file"], p["asset"]["size"] / 1e6))
            archives[n] = _download(base, p["asset"], tmpd)
        for n, p in plan.items():
            old = rec["components"].get(n)
            if old and p["only"] is None:   # reinstall / upgrade of the whole component: drop its old, unmodified files first
                _remove_files(dest, rec, [f for f, e in list(rec["files"].items()) if n in e["components"]], n, force, log)
            written = 0
            with tarfile.open(archives[n], "r:gz") as tf:
                for m in tf.getmembers():
                    if not m.isfile():
                        continue
                    rel = _safe_member(m.name)
                    if not _wanted(rel, p["only"]):
                        continue
                    key = rel.as_posix()
                    target = dest / Path(*rel.parts)
                    data = tf.extractfile(m).read()  # type: ignore[union-attr]
                    sha = hashlib.sha256(data).hexdigest()
                    entry = rec["files"].get(key)
                    if target.exists():
                        if entry is None and not force:
                            log("WARNING: %s exists and was not installed by this tool: left in place" % key)
                            continue
                        if entry is not None and _sha256_file(target) not in (entry["sha256"], sha) and not force:
                            log("WARNING: %s was changed after installation: left in place" % key)
                            continue
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(data)
                    if m.mode & 0o111:
                        target.chmod(target.stat().st_mode | 0o111)
                    if entry is None:
                        rec["files"][key] = {"sha256": sha, "components": [n]}
                    else:
                        entry["sha256"] = sha
                        if n not in entry["components"]:
                            entry["components"].append(n)
                    written += 1
            cur = rec["components"].get(n)
            if p["only"] is None or (cur is not None and cur.get("only") is None):
                only_rec = None
            else:
                only_rec = sorted(set((cur or {}).get("only") or []) | set(p["only"]))
            d = comps[n]
            rec["components"][n] = {"version": manifest["version"], "platform": p["asset"]["platform"], "only": only_rec,
                                    "requires": d.get("requires", []), "subpath": d.get("subpath"),
                                    "pipDistribution": (d.get("pip") or {}).get("distribution"), "sourcePaths": d.get("sourcePaths", [])}
            log("  installed %s (%d files)" % (n, written))
    if use_pip:
        for n in plan:
            pp = comps[n].get("pip")
            if pp:
                _pip(python or sys.executable, ["install", "--find-links", str(dest / pp["folder"]), pp["spec"]], log, False)
                if pp["distribution"] not in rec["pip"]:
                    rec["pip"].append(pp["distribution"])
    rec["version"] = manifest["version"]
    rec["source"] = "release"
    write_record(dest, rec)
    return rec


# ---------------------------------------------------------------------------------------------------- uninstall
def uninstall(dest: Path, include: list[str] | None, exclude: list[str] | None = None, force: bool = False, dry_run: bool = False,
              use_pip: bool = True, python: str | None = None, log=print) -> dict:
    """Remove the selected components (all when ``include`` is empty) using the install record; files you changed stay."""
    rec = read_record(dest)
    if not rec["components"]:
        raise InstallError("nothing installed in %s (no %s)" % (dest, RECORD))
    comps = {n: {} for n in rec["components"]}
    if include:
        chosen: dict[str, list[str] | None] = {}
        for sel in include:
            names = _children(comps, sel) if sel not in ("all", "everything") else list(comps)
            if names:
                for n in names:
                    chosen[n] = None
                continue
            # a part of a component (examples.Branched.pump-probe): files below its subpath
            parts = sel.split(".")
            for k in range(len(parts) - 1, 0, -1):
                head = ".".join(parts[:k])
                sub = rec["components"].get(head, {}).get("subpath")
                if sub:
                    chosen.setdefault(head, [])
                    if chosen[head] is not None:
                        chosen[head].append(sub.replace("{rest}", "/".join(parts[k:])))  # type: ignore[union-attr]
                    break
            else:
                raise InstallError("'%s' is not installed in %s (installed: %s)" % (sel, dest, ", ".join(sorted(rec["components"]))))
    else:
        chosen = {n: None for n in rec["components"]}
    for sel in exclude or []:
        for n in _children(comps, sel):
            chosen.pop(n, None)
    if not force:
        for n in rec["components"]:
            if n in chosen:
                continue
            for r in rec["components"][n].get("requires", []):
                if any(c == r or c.startswith(r + ".") for c in chosen if chosen[c] is None):
                    raise InstallError("%s needs %s: remove it too, or use --force" % (n, r))
    log("removing from %s: %s" % (dest, ", ".join(chosen)))
    if rec.get("source") == "git":
        uninstall_git(dest, list(chosen), rec, dry_run, log)
        return rec
    if dry_run:
        for n, only in chosen.items():
            files = [f for f, e in rec["files"].items() if n in e["components"] and (not only or any(f.startswith(o.rstrip("/") + "/") for o in only))]
            log("  %-26s %d files" % (n, len(files)))
        return rec
    for n, only in chosen.items():
        files = [f for f, e in rec["files"].items() if n in e["components"] and (not only or any(f.startswith(o.rstrip("/") + "/") for o in only))]
        _remove_files(dest, rec, files, n, force, log)
        if only is None:
            rec["components"].pop(n)
        log("  removed %s (%d files)" % (n, len(files)))
    if use_pip:
        wanted = {d for d in rec.get("pip", [])}
        still = {rec["components"][n].get("pipDistribution") for n in rec["components"]}
        for d in sorted(wanted - still):
            _pip(python or sys.executable, ["uninstall", "-y", d], log, False)
            rec["pip"].remove(d)
    write_record(dest, rec)
    if not rec["components"]:
        _prune_empty(dest, dest)
    return rec


# ---------------------------------------------------------------------------------------------------- git (source selection)
def _git(dest: Path | None, *args: str) -> None:
    cmd = ["git"] + (["-C", str(dest)] if dest else []) + list(args)
    r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if r.returncode != 0:
        raise InstallError("git failed: %s\n%s" % (" ".join(cmd), r.stdout[-2000:]))


def install_git(components: dict, include: list[str], exclude: list[str] | None, dest: Path, version: str, repository: str = REPOSITORY,
                dry_run: bool = False, log=print) -> None:
    """Source folders only, by git partial clone: ``git clone --filter=blob:none --sparse --branch v<version>`` then a cone-mode
    ``sparse-checkout`` of the folders of the selected components (the tag pins the version). Cone mode also brings the files that sit
    directly in the parent folders, which is how the shared example helpers travel with a single example. Built components (cli,
    wheels, cpp ...) are not in git: use the release for them. Figures cannot be separated from their folders here."""
    if shutil.which("git") is None:
        raise InstallError("--source git needs git on the PATH")
    chosen: dict[str, list[str] | None] = {}
    for sel in include:
        names, only = _split_selector(components, sel)
        for n in names:
            if not components[n].get("sourcePaths"):
                if sel == n:
                    raise InstallError("%s is a built component, not a folder of the repository: use --source release" % n)
                continue
            if only is None:
                chosen[n] = None
            elif chosen.get(n, []) is not None:
                chosen.setdefault(n, []).append(only)  # type: ignore[union-attr]
    for sel in exclude or []:
        for n in _split_selector(components, sel)[0]:
            chosen.pop(n, None)
    for n in list(chosen):                                  # dependencies that are folders (examples.shared)
        for r in components[n].get("requires", []):
            for rn in _children(components, r):
                if components[rn].get("sourcePaths") and not components[rn].get("gitViaParents") and rn not in chosen:
                    chosen[rn] = None
    if not chosen:
        raise InstallError("nothing to check out")
    whole = any("." not in n and components[n].get("sourcePaths") == ["."] for n in chosen)
    paths = sorted({sp for n, only in chosen.items() for sp in (only if only else components[n]["sourcePaths"])})
    log("git %s of v%s into %s: %s" % ("clone" if whole else "sparse checkout", version.lstrip("v"), dest, ", ".join(paths)))
    if dry_run:
        return
    if (dest / ".git").exists():
        if not whole:
            _git(dest, "sparse-checkout", "add", *paths)
    else:
        dest.parent.mkdir(parents=True, exist_ok=True)
        args = ["clone", "--filter=blob:none"] + ([] if whole else ["--sparse"]) + ["--branch", "v" + version.lstrip("v"), repository, str(dest)]
        _git(None, *args)
        if not whole:
            _git(dest, "sparse-checkout", "set", *paths)
    rec = read_record(dest)
    for n, only in chosen.items():
        d = components[n]
        rec["components"][n] = {"version": version.lstrip("v"), "platform": "any", "only": only, "requires": d.get("requires", []),
                                "subpath": d.get("subpath"), "pipDistribution": None, "sourcePaths": only or d["sourcePaths"]}
    rec["source"] = "git"
    write_record(dest, rec)


def uninstall_git(dest: Path, chosen: list[str], rec: dict, dry_run: bool, log) -> None:
    """Remove components of a git checkout by shrinking the sparse set (git deletes the files and keeps ones with local changes)."""
    remaining = sorted({sp for n, d in rec["components"].items() if n not in chosen for sp in d["sourcePaths"]})
    log("git sparse-checkout set %s" % (", ".join(remaining) or "(nothing)"))
    if dry_run:
        return
    if remaining:
        _git(dest, "sparse-checkout", "set", *remaining)
        for n in chosen:
            rec["components"].pop(n)
        write_record(dest, rec)
        return
    st = [l for l in subprocess.run(["git", "-C", str(dest), "status", "--porcelain"], stdout=subprocess.PIPE, text=True).stdout.splitlines()
          if not l.endswith(RECORD)]
    if st:
        log("WARNING: the checkout has local changes: left in place (%s)" % dest)
        return
    shutil.rmtree(dest, ignore_errors=True)


# ---------------------------------------------------------------------------------------------------- list
def list_components(manifest: dict, dest: Path, platform: str, out=print) -> None:
    rec = read_record(dest)
    out("UltrafastEngine %s: components for %s (installed in %s: %s)" % (manifest["version"], platform, dest, ", ".join(sorted(rec["components"])) or "nothing"))
    out("%-30s %-11s %9s  %-9s %s" % ("component", "platform", "size", "installed", "requires / description"))
    for n, d in sorted(manifest["components"].items()):
        assets = d.get("assets") or []
        if not assets:
            continue
        a = next((a for a in assets if a["platform"] == platform), None) or next((a for a in assets if a["platform"] == "any"), None)
        plat = a["platform"] if a else "(none)"
        size = "%.1f MB" % (a["size"] / 1e6) if a else "-"
        inst = rec["components"].get(n, {}).get("version", "")
        extra = ("[needs %s] " % ", ".join(d["requires"]) if d.get("requires") else "") + d.get("description", "") + ("" if d.get("inAll", True) else " (not in 'all')")
        out("%-30s %-11s %9s  %-9s %s" % (n, plat, size, inst, extra))


# ---------------------------------------------------------------------------------------------------- command line
def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="install.py", description="Install or remove parts of an UltrafastEngine release (docs/releasing.md).",
                                 formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__.split("\n\n")[0])
    ap.add_argument("--include", nargs="*", default=None, metavar="NAME", help="components: python tests examples.Branched ... 'all' = everything built for this platform except media")
    ap.add_argument("--exclude", nargs="*", default=None, metavar="NAME", help="components to leave out")
    ap.add_argument("--list", action="store_true", help="show the components, their sizes and what is installed")
    ap.add_argument("--uninstall", action="store_true", help="remove the --include components (all when none are given)")
    ap.add_argument("--version", help="release version (default: latest)")
    ap.add_argument("--latest", action="store_true", help="the latest release (default)")
    ap.add_argument("--platform", default="auto", help="auto, or win-x64 win-arm64 linux-x64 linux-arm64 osx-arm64 osx-x64")
    ap.add_argument("--dest", default=None, help="install folder (default: ./UltrafastEngine)")
    ap.add_argument("--source", choices=["release", "git"], default="release", help="release assets (default) or a git partial clone of source folders")
    ap.add_argument("--base-url", help="folder or URL holding manifest.json and the assets (default: the GitHub release)")
    ap.add_argument("--repository", default=REPOSITORY, help="GitHub repository URL for --source git and the default base URL")
    ap.add_argument("--python", help="Python used for pip (default: the one running this script)")
    ap.add_argument("--no-pip", action="store_true", help="unpack the wheels but do not run pip")
    ap.add_argument("--force", action="store_true", help="overwrite / remove files even if they were changed or not installed by this tool")
    ap.add_argument("--dry-run", action="store_true", help="show what would happen")
    a = ap.parse_args(argv)
    dest = Path(a.dest or "UltrafastEngine").expanduser().resolve()
    plat = detect_platform() if a.platform == "auto" else a.platform
    try:
        if a.uninstall:
            uninstall(dest, a.include, a.exclude, a.force, a.dry_run, not a.no_pip, a.python)
            return 0
        base = a.base_url or base_url_for(None if a.latest else a.version, a.repository)
        manifest = fetch_manifest(base)
        if a.list:
            list_components(manifest, dest, plat)
            return 0
        if not a.include:
            raise InstallError("say what to install: --include NAME ... (see --list; 'all' for everything)")
        if a.source == "git":
            install_git(manifest["components"], a.include, a.exclude, dest, manifest["version"], a.repository, a.dry_run)
        else:
            install(base, a.include, a.exclude, dest, plat, a.force, a.dry_run, not a.no_pip, a.python, manifest=manifest)
    except InstallError as e:
        print("error: %s" % e, file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
