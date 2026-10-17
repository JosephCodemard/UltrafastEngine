#!/usr/bin/env python3
"""Fetch the pinned pyNLO commit used by tests/test_pynlo.py into ./third_party/pynlo and print PYNLO_SRC.

    python tools/get_pynlo.py
    export PYNLO_SRC=$(pwd)/third_party/pynlo/src      (Windows: set PYNLO_SRC=...)
"""
import subprocess
from pathlib import Path

REPO = "https://github.com/pyNLO/PyNLO.git"
COMMIT = "789f1457fc097e22cd72fd02019594ab126da84d"   # Python-3 branch, verified with numpy 2.x + shims
dest = Path(__file__).resolve().parents[1] / "third_party" / "pynlo"
if not dest.exists():
    subprocess.check_call(["git", "clone", REPO, str(dest)])
subprocess.check_call(["git", "-C", str(dest), "checkout", COMMIT])
print("PYNLO_SRC=" + str(dest / "src"))
