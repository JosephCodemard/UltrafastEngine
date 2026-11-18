"""Version gate of the release workflow: every place that carries the version must agree, and a tag must match it.

    python build/check_version.py                 # the files agree with each other and the package name is ultrafast-engine
    python build/check_version.py v1.0.0          # ... and the tag is v<version>
    python build/check_version.py v1.0.0a1       # ... or an alpha of that version (wheel version 1.0.0a1, PyPI as a pre-release)
    python build/check_version.py v1.0.0rc1      # ... or a release candidate of that version (wheel version 1.0.0rc1, TestPyPI)
    python build/check_version.py v1.0.0a1 --github-output "$GITHUB_OUTPUT"   # also writes version, wheel_version, is_rc, is_prerelease

The places: ``python/pyproject.toml``, ``python/src/ultrafast/__init__.py``, ``Directory.Build.props``, ``CITATION.cff``, the engine constant in
``RunResult.cs``, the ``version`` and ``engine`` of every ``plugins/*/plugin.json`` and the ``PluginVersion`` of each plugin's C# file.
``collect()`` returns ``{place: version}``; ``check(tag)`` returns ``(base_version, wheel_version, is_rc)`` or raises ``SystemExit`` with the reason.
Exit code 1 on any mismatch (nothing is built or uploaded then).
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path
from typing import Dict, Optional, Tuple

REPO = Path(__file__).resolve().parents[1]
PACKAGE_NAME = "ultrafast-engine"
TAG = re.compile(r"^v(\d+\.\d+\.\d+)((?:a|rc)\d+)?$")


def _grep(path: Path, pattern: str) -> Optional[str]:
    m = re.search(pattern, path.read_text(encoding="utf-8"), re.M)
    return m.group(1) if m else None


def collect(repo: Path = REPO) -> Dict[str, Optional[str]]:
    v: Dict[str, Optional[str]] = {
        "python/pyproject.toml": _grep(repo / "python/pyproject.toml", r'^version\s*=\s*"([^"]+)"'),
        "python/src/ultrafast/__init__.py": _grep(repo / "python/src/ultrafast/__init__.py", r'^__version__\s*=\s*"([^"]+)"'),
        "Directory.Build.props": _grep(repo / "Directory.Build.props", r"<Version>([^<]+)</Version>"),
        "CITATION.cff": _grep(repo / "CITATION.cff", r"^version:\s*(\S+)"),
        "src/UltrafastEngine.Core/Results/RunResult.cs": _grep(repo / "src/UltrafastEngine.Core/Results/RunResult.cs", r'const string Version = "([^"]+)"'),
    }
    for pj in sorted((repo / "plugins").glob("*/plugin.json")):
        d = json.loads(pj.read_text(encoding="utf-8"))
        v["plugins/%s/plugin.json version" % pj.parent.name] = d.get("version")
        v["plugins/%s/plugin.json engine" % pj.parent.name] = d.get("engine")
        for cs in sorted(pj.parent.glob("src/**/*Plugin.cs")):
            got = _grep(cs, r'(?:PluginVersion\s*=|Version\s*=>)\s*"([^"]+)"')
            if got:
                v["plugins/%s/%s" % (pj.parent.name, cs.name)] = got
    return v


def check(tag: Optional[str], repo: Path = REPO) -> Tuple[str, str, bool]:
    versions = collect(repo)
    name = _grep(repo / "python/pyproject.toml", r'^name\s*=\s*"([^"]+)"')
    if name != PACKAGE_NAME:
        raise SystemExit("package name in pyproject.toml is %r, expected %r" % (name, PACKAGE_NAME))
    base = versions["python/pyproject.toml"]
    bad = {k: x for k, x in versions.items() if x != base}
    if bad:
        raise SystemExit("version mismatch (pyproject.toml says %s): %s" % (base, "; ".join("%s = %s" % kv for kv in sorted(bad.items()))))
    if not re.match(r"^\d+\.\d+\.\d+$", base or ""):
        raise SystemExit("the version %r in the files must be X.Y.Z (an alpha or a release candidate is only in the tag: v%s" % (base, "X.Y.Za1 or v" + "X.Y.Zrc1)"))
    if tag is None:
        return base, base, False
    m = TAG.match(tag)
    if not m:
        raise SystemExit("tag %r is not v<X.Y.Z>, v<X.Y.Z>a<N> or v<X.Y.Z>rc<N>" % tag)
    if m.group(1) != base:
        raise SystemExit("tag %s does not match the version %s of the files" % (tag, base))
    return base, base + (m.group(2) or ""), (m.group(2) or "").startswith("rc")


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    out = None
    if "--github-output" in argv:
        i = argv.index("--github-output")
        out = argv[i + 1]
        del argv[i:i + 2]
    tag = argv[0] if argv else None
    if tag and not tag.startswith("v"):          # a branch or a manual run: only the files are checked
        tag = None
    try:
        base, wheel, rc = check(tag)
    except SystemExit as e:
        print("VERSION CHECK FAILED:", e)
        return 1
    print("version %s, wheel version %s, release candidate: %s%s" % (base, wheel, rc, "" if tag else " (no tag given)"))
    if out:
        with open(out, "a", encoding="utf-8") as f:
            f.write("version=%s\nwheel_version=%s\nis_rc=%s\nis_prerelease=%s\n" % (base, wheel, str(rc).lower(), str(wheel != base).lower()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
