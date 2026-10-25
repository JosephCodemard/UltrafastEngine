---
description: Build release packages with build.sh (no source code) and check them
argument-hint: "[build.sh options]  e.g. --all --plugins fitting zemax --platform osx-arm64 win-x64"
allowed-tools: Bash(./build.sh:*), Bash(dotnet:*), Bash(python3:*), Read
---
Package a release: ./build.sh $ARGUMENTS

1. Run `/verify` first unless it ran on this commit.
2. `./build.sh --list` (what this machine can build), then `./build.sh $ARGUMENTS`. Read docs/releasing.md for the
   options. NativeAOT parts (`--cpp`, `--python-engine native`) only build for this OS.
3. Smoke-test what was built: `dist/*/bin/ultrafast plugins list` and a run of docs/examples/01-spm-plate; install
   the wheel in a fresh venv and `import ultrafast; ultrafast.version()`; build `cpp/example` with CMake.
4. Confirm no `ZOSAPI*.dll` is in `dist/` (`find dist -iname "ZOSAPI*"` must print nothing).
5. Report the archives with sizes and the warnings the packager printed.
