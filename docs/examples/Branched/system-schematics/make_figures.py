"""Run this example and write its figures (the script writes figures/ and hero.png itself)."""
import runpy
from pathlib import Path

if __name__ == "__main__":
    runpy.run_path(str(Path(__file__).resolve().parent / "schematics.py"), run_name="__main__")
