---
description: Build and run the C# tests for a filter (class or method substring), then report failures only
argument-hint: "[filter ...]  e.g. NerTests Measured  (empty = full suite with --slow)"
allowed-tools: Bash(dotnet build:*), Bash(dotnet tests/UltrafastEngine.Tests/bin/*), Read, Grep
---
Run the C# tests for: $ARGUMENTS

1. `dotnet build tests/UltrafastEngine.Tests -c Release -f net10.0 2>&1 | grep -E " error |rror\(s\)" | sort -u`
2. If the filter is empty: `dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll --slow`,
   otherwise pass the filter words as arguments. Pipe through `grep -v "^   at" | grep -E "FAIL|passed"` first; only
   when something fails, show that test's message and the first stack frames in our code.
3. For each failure: read the test (and the code it exercises) and say whether the test or the code is wrong.
   Do not loosen a physics tolerance without a stated physical or numerical reason.
