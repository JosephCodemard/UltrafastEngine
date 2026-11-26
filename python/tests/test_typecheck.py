"""mypy gate: the typed front door is clean, and a seeded type error is caught with the same config."""
import subprocess
import sys
import textwrap
from pathlib import Path

import pytest

pytest.importorskip("mypy", reason="mypy not installed (pip install mypy)")

PYTHON_DIR = Path(__file__).resolve().parents[1]


def _mypy(*args, cwd=PYTHON_DIR):
    return subprocess.run([sys.executable, "-m", "mypy", *args], cwd=cwd, capture_output=True, text=True, timeout=600)


def test_front_door_is_clean():
    r = _mypy()
    assert r.returncode == 0, r.stdout + r.stderr


def test_seeded_type_error_fails(tmp_path):
    bad = tmp_path / "bad.py"
    bad.write_text(textwrap.dedent('''
        from ultrafast.calc import tbp

        def f() -> int:
            return "not an int"
    '''))
    r = _mypy("--config-file", str(PYTHON_DIR / "pyproject.toml"), str(bad))
    assert r.returncode != 0 and "return-value" in r.stdout, r.stdout + r.stderr
