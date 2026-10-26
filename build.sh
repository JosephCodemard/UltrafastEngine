#!/usr/bin/env bash
# Compile and package UltrafastEngine release folders. Options: ./build.sh --help ; guide: docs/releasing.md
#   ./build.sh --cli --python --cpp --csharp --plugins fitting zemax gpu --platform linux-x64 osx-arm64 win-x64
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
py="$(command -v python3 || command -v python || true)"
if [ -z "$py" ]; then echo "build.sh needs Python 3.9+ (python3) and the .NET 10 SDK" >&2; exit 1; fi
exec "$py" "$here/build/package.py" "$@"
