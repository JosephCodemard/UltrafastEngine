#!/usr/bin/env python3
"""PreToolUse hook (Edit / Write): refuse edits that must never happen in this repository.

* ZOS-API assemblies (Ansys property): never written, copied or modified.
* Generated files: edit their source and regenerate (/regen) instead.
* Build output and third-party code.

Exit code 2 blocks the tool call and shows the message to Claude (Claude Code hook protocol).
"""
import fnmatch
import json
import os
import sys

BLOCK = [
    ("*ZOSAPI*.dll", "ZOS-API assemblies are Ansys property: never create, modify or copy them."),
    ("plugins/zemax/src/UltrafastEngine.Zemax/lib/*", "This folder holds the owner's ZOS-API assemblies (Ansys property): do not touch it."),
    ("schemas/*.schema.json", "Generated file: change src/UltrafastEngine.Core/Schema/BuiltIn*.cs and run /regen."),
    ("docs/reference/options.md", "Generated file: change src/UltrafastEngine.Core/Schema/BuiltIn*.cs and run /regen."),
    ("python/src/ultrafast/types.py", "Generated file: change the schema declarations and run /regen (python3 python/tools/gen_types.py)."),
    ("docs/reference/python-api.md", "Generated file: change the Python docstrings and run python3 python/tools/gen_api_docs.py."),
    ("*/bin/*", "Build output: change the source instead."),
    ("*/obj/*", "Build output: change the source instead."),
    ("artifacts/*", "Build output (plugin layout): change the plugin sources and rebuild."),
    ("dist/*", "Release output of build.sh: rebuild instead of editing."),
    ("python/third_party/*", "Third-party checkout (pyNLO): do not modify."),
]


def main():
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0
    ti = data.get("tool_input") or {}
    path = ti.get("file_path") or ti.get("notebook_path") or ""
    if not path:
        return 0
    root = os.environ.get("CLAUDE_PROJECT_DIR") or data.get("cwd") or os.getcwd()
    rel = os.path.relpath(os.path.abspath(path), os.path.abspath(root)).replace(os.sep, "/")
    if rel.startswith(".."):
        rel = path.replace(os.sep, "/")
    for pat, why in BLOCK:
        if fnmatch.fnmatch(rel, pat) or fnmatch.fnmatch(os.path.basename(rel), pat):
            print("Blocked edit of %s: %s" % (rel, why), file=sys.stderr)
            return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
