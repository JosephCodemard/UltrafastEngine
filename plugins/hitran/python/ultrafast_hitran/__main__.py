"""``python -m ultrafast_hitran download H2O [CO2 O2] [--numin 6500] [--numax 14000] [--dir DIR]``"""
from __future__ import annotations

import argparse

from .download import download


def main() -> None:
    p = argparse.ArgumentParser(prog="python -m ultrafast_hitran", description="Download HITRAN line lists for the hitran plugin.")
    sub = p.add_subparsers(dest="cmd", required=True)
    d = sub.add_parser("download", help="fetch line lists with the HITRAN API (pip install hitran-api)")
    d.add_argument("molecules", nargs="*", default=["H2O"], help="H2O, CO2, O2 (default H2O)")
    d.add_argument("--numin", type=float, default=6500.0, help="lowest wavenumber, cm-1 (default 6500 = 1538 nm)")
    d.add_argument("--numax", type=float, default=14000.0, help="highest wavenumber, cm-1 (default 14000 = 714 nm)")
    d.add_argument("--dir", default=None, help="data folder (default $ULTRAFAST_HITRAN_DIR or ~/.ultrafast/hitran)")
    a = p.parse_args()
    download(a.molecules, a.numin, a.numax, a.dir)


if __name__ == "__main__":
    main()
