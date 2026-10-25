---
description: Regenerate the five generated files (schemas, options.md, Python types and API docs) and check them
allowed-tools: Bash(dotnet build:*), Bash(dotnet src/UltrafastEngine.Cli/bin/*), Bash(python3 python/tools/*), Bash(git diff:*), Bash(mypy:*)
---
Regenerate everything derived from the schema declarations and the Python docstrings:

```bash
dotnet build src/UltrafastEngine.Cli -c Release -f net10.0 2>&1 | grep -E "rror\(s\)"
U="dotnet src/UltrafastEngine.Cli/bin/Release/net10.0/ultrafast.dll"
$U schema system -o schemas/ufs.schema.json
$U schema run -o schemas/run.schema.json
$U reference -o docs/reference/options.md
python3 python/tools/gen_types.py
python3 python/tools/gen_api_docs.py
(cd python && mypy --follow-imports=silent src/ultrafast/build.py src/ultrafast/types.py)
```

Then `git diff --stat` on those five files and summarise what changed in one or two lines (new keys, renamed
types). Do not read the generated files in full; grep them for the keys you changed if you need to confirm.
