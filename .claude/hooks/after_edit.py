#!/usr/bin/env python3
"""PostToolUse hook (Edit / Write): remind Claude of the follow-up a change needs.

Prints a JSON object with ``additionalContext`` (Claude Code hook protocol); silent for other files.
"""
import fnmatch
import json
import os
import sys

RULES = [
    ("src/UltrafastEngine.Core/Schema/BuiltIn*.cs", "Schema declaration changed: run /regen (5 generated files) before the tests; document the key in docs/reference/."),
    ("src/UltrafastEngine.Core/Schema/Migrations.cs", "Migration changed: keep the old key working and add a test in SchemaTests."),
    ("src/UltrafastEngine.Core/Physics/*", "Propagation physics changed: run the analytic tests (PropagationTests, NerTests) and, if scalar results may change, the pyNLO cross-checks (/physics-change)."),
    ("src/UltrafastEngine.Core/Transverse/*", "Spatial propagation changed: keep it consistent with Physics/Nonlinear.cs; run SpatialTests and NerTests."),
    ("python/src/ultrafast/build.py", "Builder changed: return the TypedDict from ultrafast.types, run python3 python/tools/gen_api_docs.py and mypy on build.py."),
    ("plugins/*/python/*", "Plugin Python changed: run python3 python/tools/build_engine.py before the Python tests."),
    ("src/*.cs", "C# changed: the Python package uses its own engine copy -- run python3 python/tools/build_engine.py before Python tests."),
    ("plugins/*.cs", "Plugin C# changed: rebuild (dotnet build) and run python3 python/tools/build_engine.py before Python tests."),
    ("docs/*.md", "Docs changed: python3 -m mkdocs build --strict must stay at 0 warnings; new pages go into mkdocs.yml and llms.txt."),
]


def main():
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0
    path = (data.get("tool_input") or {}).get("file_path") or ""
    if not path:
        return 0
    root = os.environ.get("CLAUDE_PROJECT_DIR") or data.get("cwd") or os.getcwd()
    rel = os.path.relpath(os.path.abspath(path), os.path.abspath(root)).replace(os.sep, "/")
    for pat, msg in RULES:
        if fnmatch.fnmatch(rel, pat):
            print(json.dumps({"hookSpecificOutput": {"hookEventName": "PostToolUse", "additionalContext": msg}}))
            return 0
    return 0


if __name__ == "__main__":
    sys.exit(main())
