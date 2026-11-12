#!/usr/bin/env bash
# Build UltrafastEngine from source for this machine. Options: ./setup.sh --help ; guide: docs/install.md
#   ./setup.sh --cli --python --plugins fitting hitran --check
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
py="$(command -v python3 || command -v python || true)"
if [ -z "$py" ]; then echo "setup.sh needs Python 3.10+ (python3) and the .NET 10 SDK" >&2; exit 1; fi
exec "$py" "$here/build/setup.py" "$@"
