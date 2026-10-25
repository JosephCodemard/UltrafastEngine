#!/bin/sh
# Runs a hook script with whichever Python is installed (python3 on macOS / Linux, python on Windows).
py="$(command -v python3 || command -v python)"
[ -z "$py" ] && { echo "hooks need Python 3 on PATH" >&2; exit 0; }
exec "$py" "$@"
