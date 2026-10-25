---
description: Full verification before a commit or release - builds, all tests (net10, net48 on Mono if present, Python with pyNLO), examples in strict mode, docs
allowed-tools: Bash, Read, Grep
---
Run the complete verification. Delegate the long runs to the `test-runner` subagent so only the results come back.

1. `dotnet build UltrafastEngine.sln -c Release` (0 errors; the only expected warning is the ZOS-API stub warning).
2. Generated files current: run `/regen` steps and check `git diff --stat` shows no change in the five files.
3. C# tests: `dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll --slow`; on machines with
   Mono also `mono tests/UltrafastEngine.Tests/bin/Release/net48/UltrafastEngine.Tests.exe --slow` (unset ULTRAFAST_STRICT_OPTIONS).
4. Python: `python3 python/tools/build_engine.py`, then in `python/`:
   `PYNLO_SRC=$PWD/third_party/pynlo/src ULTRAFAST_SLOW=1 python3 -m pytest -q tests` (fetch pyNLO once with
   `python3 tools/get_pynlo.py`; skipped pyNLO tests are not a pass).
5. Examples: every `docs/examples/*/run*.json` with `ULTRAFAST_STRICT_OPTIONS=1` and the right system file
   (see docs/CLAUDE.md for the pairings); `python3 docs/examples/27-ner-measured-input/ner.py`.
6. `python3 -m mkdocs build --strict` with 0 warnings.
7. Report a table: step, result, time. Update `PROJECT_STATUS.md` counts if they changed.
