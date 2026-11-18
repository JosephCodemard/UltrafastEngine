"""Run this example and write its figure (the script writes hero.png itself)."""
import runpy
from pathlib import Path

if __name__ == "__main__":
    runpy.run_path(str(Path(__file__).resolve().parent / "calculators.py"), run_name="__main__")
