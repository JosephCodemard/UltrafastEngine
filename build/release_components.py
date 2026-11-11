"""Release components: the names ``--components`` (build/package.py) and ``install.py --include`` accept, what each contains, and the
asset writer. Standard library only.

    definitions(repo, plugin_ids)            -> {name: definition}      what exists: description, requires, kind, source paths ...
    collect(repo, name, defs)                -> [(archive path, file)]  the files of a source component (tracked files only)
    write_asset(files, out_file)             -> {file, size, sha256}    a .tar.gz with fixed timestamps, ZOS-API guard
    manifest(version, assets, defs)          -> dict                    what install.py reads (manifest.json)
    merge(manifests)                         -> dict                    the manifests of several build jobs -> one
    forbidden(name)                          -> bool                    True for Ansys ZOS-API assemblies (never packaged)

Component names: ``cli`` ``python`` ``python-native`` ``cpp`` ``csharp`` ``zemax`` (built, per platform or platform-independent),
``plugins.<id>``, ``schemas``, ``docs``, ``examples.<Group>`` + ``examples.shared``, ``media``, ``tests``, ``component-tests``,
``papers``, ``source``. A name selects its children (``examples`` = every group). Files of source components are repository-relative
(``docs/examples/Branched/...``), the same tree as a git sparse checkout. Command line: ``python build/release_components.py merge OUT IN...``
and ``... check-zosapi PATH...``. Guide: docs/releasing.md.
"""
from __future__ import annotations

import fnmatch
import gzip
import hashlib
import io
import json
import subprocess
import sys
import tarfile
from pathlib import Path

MEDIA_EXT = (".png", ".gif", ".svg", ".jpg", ".jpeg", ".mp4", ".webm")
MEDIA_ROOTS = ("docs/examples/", "docs/papers/")
# never packaged: not part of the product (agent memory), caches, licensed paper PDFs, build leftovers
SKIP_PARTS = ("__pycache__", "bin", "obj", "results", ".git")      # only for the no-git fallback walk
SKIP_NAMES = ("CLAUDE.md", "CLAUDE.local.md", ".DS_Store")


def forbidden(name: str) -> bool:
    """Ansys ZOS-API assemblies (``ZOSAPI*.dll`` / ``.xml`` / ``.pdb``, any case). The tracked ``ZosApiStubs`` sources are ours and allowed."""
    n = name.replace("\\", "/").rsplit("/", 1)[-1].lower()
    return n.startswith("zosapi") and n.endswith((".dll", ".xml", ".pdb"))


# ---------------------------------------------------------------------------------------------------- definitions
def example_groups(repo: Path) -> list[str]:
    root = repo / "docs" / "examples"
    return sorted(p.name for p in root.iterdir() if p.is_dir() and not p.name.startswith(("_", ".")) and p.name != "__pycache__")


def definitions(repo: Path, plugin_ids: list[str]) -> dict:
    """Every component: ``kind`` (``built`` or ``source``), ``perPlatform``, ``requires``, ``description``, ``default`` (what ``install.py``
    installs without --include is nothing: ``default`` only marks the usual starting set in ``--list``), ``inAll`` (False: media),
    ``sourcePaths`` (folders in git), ``subpath`` (template for a part of the component), ``pip`` (wheel installation)."""
    d: dict[str, dict] = {
        "cli": dict(kind="built", perPlatform=True, description="the ultrafast command-line program (self-contained)"),
        "python": dict(kind="built", perPlatform=False, description="Python package (hosted engine wheel; needs the .NET 10 runtime)",
                       pip=dict(folder="python", distribution="ultrafast-engine", spec="ultrafast-engine[hosted]")),
        "python-native": dict(kind="built", perPlatform=True, description="Python package with the NativeAOT engine (no plugins)",
                              conflicts=["python"], provides=["python"], inAll=False,
                              pip=dict(folder="python", distribution="ultrafast-engine", spec="ultrafast-engine")),
        "cpp": dict(kind="built", perPlatform=True, description="C API: header, native library, CMake example"),
        "csharp": dict(kind="built", perPlatform=False, description="UltrafastEngine.Core.dll for net10.0 and net48"),
        "zemax": dict(kind="built", perPlatform=True, description="OpticStudio extension (Windows x64; built against your ZOS-API, which is not included)"),
        "schemas": dict(kind="source", description="JSON schemas of .ufs and run.json", sourcePaths=["schemas"]),
        "docs": dict(kind="source", description="documentation (Markdown, no examples / papers / design notes)",
                     sourcePaths=sorted("docs/" + p.name for p in (repo / "docs").iterdir() if p.is_dir() and p.name not in ("examples", "papers", "design", "__pycache__"))),
        "examples.shared": dict(kind="source", description="helpers shared by all examples (_figures.py, make_all.py, README)", sourcePaths=["docs/examples"],
                                gitViaParents=True),
        "media": dict(kind="source", description="figures and animations of examples and papers (png, gif, svg)", inAll=False, sourcePaths=["docs/examples", "docs/papers"]),
        "tests": dict(kind="source", description="test sources and data (C# and Python), no binaries", sourcePaths=["tests/UltrafastEngine.Tests", "python/tests"]),
        "component-tests": dict(kind="source", description="component checks with plots (tests/components)", sourcePaths=["tests/components"]),
        "papers": dict(kind="source", description="paper replications (docs/papers)", sourcePaths=["docs/papers"]),
        "source": dict(kind="source", description="the whole repository (tracked files)", sourcePaths=["."], inAll=False),
    }
    d["tests"]["requires"] = ["python"]
    d["component-tests"]["requires"] = ["python"]
    for g in example_groups(repo):
        d["examples." + g] = dict(kind="source", description="examples: " + g, requires=["examples.shared", "python"],
                                  sourcePaths=["docs/examples/" + g], subpath="docs/examples/%s/{rest}" % g)
    for pid in plugin_ids:
        d["plugins." + pid] = dict(kind="built", perPlatform=False, description="plugin " + pid, requires=["cli"] + (["plugins.fitting"] if pid == "twin" else []))
    d["python"]["default"] = d["cli"]["default"] = True
    for v in d.values():
        v.setdefault("requires", [])
        v.setdefault("default", False)
    return d


# ---------------------------------------------------------------------------------------------------- source files
def tracked_files(repo: Path) -> list[str]:
    """Repository-relative POSIX paths of the files git tracks (a plain walk, minus build folders, when there is no git)."""
    try:
        out = subprocess.run(["git", "-C", str(repo), "ls-files", "-z"], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, check=True).stdout
        return [p for p in out.decode("utf-8").split("\0") if p and (repo / p).is_file()]
    except (OSError, subprocess.CalledProcessError):
        res = []
        for p in sorted(repo.rglob("*")):
            rel = p.relative_to(repo)
            if p.is_file() and not any(part in SKIP_PARTS for part in rel.parts):
                res.append(rel.as_posix())
        return res


def _is_media(rel: str) -> bool:
    return rel.lower().endswith(MEDIA_EXT) and rel.startswith(MEDIA_ROOTS)


def _skip(rel: str, name: str) -> bool:
    """Tracked files are already clean. Never packaged: ZOS-API assemblies, licensed paper PDFs; the agent notes (CLAUDE.md) except in ``source``."""
    if forbidden(rel) or (rel.lower().endswith(".pdf") and rel.startswith("docs/papers/")):
        return True
    return name != "source" and rel.rsplit("/", 1)[-1] in SKIP_NAMES


def collect(repo: Path, name: str, defs: dict, files: list[str] | None = None) -> list[tuple[str, Path]]:
    """The (archive path, file) pairs of a source component; ``files`` = ``tracked_files(repo)`` (pass it when collecting many)."""
    files = tracked_files(repo) if files is None else files
    roots = defs[name]["sourcePaths"]
    out = []
    for rel in files:
        if _skip(rel, name):
            continue
        media = _is_media(rel)
        if name == "media":
            ok = media
        elif name == "source":
            ok = True
        else:
            if media:
                continue
            if name == "examples.shared":
                parts = rel.split("/")
                ok = rel.startswith("docs/examples/") and len(parts) == 3          # files directly in docs/examples
            elif name == "docs":
                ok = any(rel.startswith(r + "/") for r in roots) or (rel.count("/") == 1 and rel.startswith("docs/")) or rel in ("mkdocs.yml", "llms.txt")
                ok = ok and not rel.endswith(".py")
            else:
                ok = any(rel == r or rel.startswith(r.rstrip("/") + "/") for r in roots)
        if ok:
            out.append((rel, repo / rel))
    return out


# ---------------------------------------------------------------------------------------------------- assets
def write_asset(files: list[tuple[str, Path]], out_file: Path) -> dict:
    """A reproducible ``.tar.gz`` (sorted, fixed mtime, no owner names). Refuses ZOS-API files. Returns file / size / sha256."""
    bad = [a for a, _ in files if forbidden(a)]
    if bad:
        raise SystemExit("refusing to package ZOS-API files (Ansys property): " + ", ".join(bad))
    out_file.parent.mkdir(parents=True, exist_ok=True)
    with open(out_file, "wb") as raw, gzip.GzipFile(fileobj=raw, mode="wb", mtime=0, compresslevel=9) as gz, tarfile.open(fileobj=gz, mode="w", format=tarfile.PAX_FORMAT) as tf:
        for arc, path in sorted(files, key=lambda t: t[0]):
            ti = tarfile.TarInfo(arc)
            data = path.read_bytes()
            ti.size = len(data)
            ti.mtime = 0
            ti.mode = 0o755 if (path.stat().st_mode & 0o111) else 0o644
            ti.uid = ti.gid = 0
            ti.uname = ti.gname = ""
            tf.addfile(ti, io.BytesIO(data))
    h = hashlib.sha256(out_file.read_bytes()).hexdigest()
    return {"file": out_file.name, "size": out_file.stat().st_size, "sha256": h}


def asset_name(component: str, version: str, platform: str) -> str:
    return "%s-%s%s.tar.gz" % (component, version, "" if platform == "any" else "-" + platform)


def check_archives(paths: list[Path]) -> list[str]:
    """Names of ZOS-API files found in the given archives (.tar.gz, .zip, .whl) or folders: the release gate."""
    import zipfile
    found = []
    expanded = []
    for p in paths:
        expanded.append(p)
        if p.is_dir():          # a folder: its loose files and every archive in it
            expanded += [f for f in sorted(p.rglob("*")) if f.is_file() and (f.name.endswith((".tar.gz", ".whl", ".zip")))]
    for p in expanded:
        if p.is_dir():
            found += [str(f) for f in p.rglob("*") if f.is_file() and forbidden(f.name)]
        elif p.suffix in (".zip", ".whl"):
            found += ["%s:%s" % (p.name, n) for n in zipfile.ZipFile(p).namelist() if forbidden(n)]
        else:
            with tarfile.open(p, "r:*") as tf:
                found += ["%s:%s" % (p.name, n) for n in tf.getnames() if forbidden(n)]
                for m in tf.getmembers():       # wheels inside archives
                    if m.name.endswith(".whl") and m.isfile():
                        try:
                            names = zipfile.ZipFile(io.BytesIO(tf.extractfile(m).read())).namelist()  # type: ignore[union-attr]
                        except zipfile.BadZipFile:
                            raise SystemExit("%s:%s is not a valid wheel" % (p.name, m.name))
                        found += ["%s:%s:%s" % (p.name, m.name, n) for n in names if forbidden(n)]
    return found


# ---------------------------------------------------------------------------------------------------- manifest
def manifest(version: str, assets: dict[str, list[dict]], defs: dict, repository: str = "https://github.com/JosephCodemard/UltrafastEngine") -> dict:
    """``assets``: component -> [{platform, file, size, sha256}]. Components without an asset but with children are listed as virtual
    parents (no files); the installer expands them. ``defs`` entries are copied without the build-only keys."""
    comps = {}
    for name, d in defs.items():
        entry = {k: d[k] for k in ("description", "requires", "default", "conflicts", "pip", "subpath", "sourcePaths", "gitViaParents", "provides") if d.get(k)}
        entry["default"] = bool(d.get("default"))
        if d.get("inAll") is False:
            entry["inAll"] = False
        entry["requires"] = list(d.get("requires", []))
        entry["assets"] = sorted(assets.get(name, []), key=lambda a: a["platform"])
        if entry["assets"] or d.get("sourcePaths"):
            comps[name] = entry
    return {"schema": 1, "product": "UltrafastEngine", "version": version, "tag": "v" + version, "repository": repository, "components": comps}


def merge(manifests: list[dict]) -> dict:
    """Combine the manifests of several build jobs (each built other components / platforms) into one; the versions must agree."""
    if not manifests:
        raise SystemExit("nothing to merge")
    out = json.loads(json.dumps(manifests[0]))
    for m in manifests[1:]:
        if m["version"] != out["version"]:
            raise SystemExit("version mismatch: %s and %s" % (out["version"], m["version"]))
        for n, c in m["components"].items():
            t = out["components"].setdefault(n, {**c, "assets": []})
            have = {a["platform"] for a in t["assets"]}
            for a in c["assets"]:
                if a["platform"] in have:
                    old = next(x for x in t["assets"] if x["platform"] == a["platform"])
                    if old["sha256"] != a["sha256"]:
                        raise SystemExit("%s for %s was built twice with different content" % (n, a["platform"]))
                    continue
                t["assets"].append(a)
            t["assets"].sort(key=lambda a: a["platform"])
    return out


def main(argv=None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    if argv[:1] == ["merge"] and len(argv) >= 3:
        m = merge([json.loads(Path(p).read_text(encoding="utf-8")) for p in argv[2:]])
        Path(argv[1]).write_text(json.dumps(m, indent=1), encoding="utf-8")
        print("merged %d manifests -> %s (%d components)" % (len(argv) - 2, argv[1], len(m["components"])))
        return 0
    if argv[:1] == ["check-zosapi"] and len(argv) >= 2:
        paths = []
        for a in argv[1:]:
            p = Path(a)
            paths.append(p)
        found = check_archives(paths)
        for f in found:
            print("ZOS-API file in release: " + f)
        print("ZOS-API check: %s" % ("FAILED" if found else "clean"))
        return 1 if found else 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main())
