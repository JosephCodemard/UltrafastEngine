#!/bin/bash
# Regenerates every generated file (the steps of the /regen command) in the worktree it is run from (any worktree may call
# the main worktree's copy) and lists the ones that changed.
cd "$(git rev-parse --show-toplevel)"   # the worktree the command is run from
PY=/Users/josephcodemard/Documents/Projects/UltrafastEngine/UltrafastEngine/.venv/bin/python
[ -x "$PY" ] || PY=python3
dotnet build src/UltrafastEngine.Cli -c Release -f net10.0 2>&1 | grep -E " error |rror\(s\)" | head -5
U="dotnet src/UltrafastEngine.Cli/bin/Release/net10.0/ultrafast.dll"
$U schema system -o schemas/ufs.schema.json && $U schema run -o schemas/run.schema.json
$U reference -o docs/reference/options.md && $U reference materials -o docs/reference/materials-catalogue.md
$PY python/tools/gen_types.py && $PY python/tools/gen_api_docs.py
dotnet build tests/UltrafastEngine.Tests -c Release -f net10.0 2>&1 | grep -E " error |rror\(s\)" | head -5
dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll --matrix docs/reference/validation-matrix.md | tail -1
git status --short schemas docs/reference/options.md docs/reference/materials-catalogue.md docs/reference/validation-matrix.md \
  python/src/ultrafast/types.py docs/reference/python-api.md
rm -f .git/UF_REGEN_NEEDED
