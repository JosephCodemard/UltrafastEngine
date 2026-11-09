"""Run the figure scripts of this group: python make_all.py [--only NAME] [--no-animations] [--jobs N] [--report FILE]."""
import subprocess
import sys
from pathlib import Path

here = Path(__file__).resolve().parent
sys.exit(subprocess.call([sys.executable, str(here.parent / "make_all.py"), "--group", here.name] + sys.argv[1:]))
