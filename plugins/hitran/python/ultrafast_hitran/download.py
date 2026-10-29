"""Download HITRAN line lists with the HITRAN API and store them as CSV for the engine.

The data stay with the user: HITRAN is distributed under its own terms (https://hitran.org), so nothing is bundled. The download
needs the HITRAN API package (``pip install hitran-api``; the module is ``hapi``, or ``hapi2`` in its newer release) and a network
connection. The CSV columns are the HAPI names (``molec_id, local_iso_id, nu, sw, a, gamma_air, gamma_self, elower, n_air,
delta_air``), which the engine's reader accepts; the 160-character ``.par`` format is read as well.
"""
from __future__ import annotations

import csv
import importlib
import os
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence

from .build import MOLECULES

COLUMNS = ["molec_id", "local_iso_id", "nu", "sw", "a", "gamma_air", "gamma_self", "elower", "n_air", "delta_air"]

#: main isotopologues (HITRAN local numbering): H2O 161, 181, 171, 162; CO2 626, 636, 628, 627; O2 66, 68, 67
MAIN_ISOTOPOLOGUES: Dict[str, Sequence[int]] = {"H2O": (1, 2, 3, 4), "CO2": (1, 2, 3, 4), "O2": (1, 2, 3)}


def data_dir(directory: Optional[str] = None) -> Path:
    """The folder with the line files: ``directory``, else ``$ULTRAFAST_HITRAN_DIR``, else ``~/.ultrafast/hitran``."""
    d = directory or os.environ.get("ULTRAFAST_HITRAN_DIR") or str(Path.home() / ".ultrafast" / "hitran")
    p = Path(d).expanduser()
    p.mkdir(parents=True, exist_ok=True)
    return p


def write_csv(path: os.PathLike, rows: Iterable[Sequence[float]]) -> int:
    """Write rows (in :data:`COLUMNS` order) as a CSV line file the engine reads; returns the number of lines."""
    n = 0
    with open(path, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(COLUMNS)
        for r in rows:
            w.writerow(r)
            n += 1
    return n


def write_partition(path: os.PathLike, hapi: Any, molecule: int, isotopologues: Iterable[int], t_min: float = 100.0, t_max: float = 500.0,
                    step: float = 1.0) -> int:
    """Write the total internal partition sums Q(T) (TIPS, from ``hapi.partitionSum``) of the isotopologues as ``iso,T,Q`` rows, so the
    engine scales the line strengths with the real partition function instead of a power law. Returns the number of rows."""
    temps = [t_min + i * step for i in range(int(round((t_max - t_min) / step)) + 1)]
    n = 0
    with open(path, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["iso", "T", "Q"])
        for iso in isotopologues:
            q = hapi.partitionSum(molecule, int(iso), temps)
            for t, v in zip(temps, q):
                w.writerow([int(iso), t, repr(float(v))])
                n += 1
    return n


def _api(api: Any = None) -> Any:
    if api is not None:
        return api
    for name in ("hapi2", "hapi"):
        try:
            return importlib.import_module(name)
        except ImportError:
            continue
    raise ImportError("the HITRAN API is not installed: pip install hitran-api (module hapi / hapi2). "
                      "Or put HITRAN .par / .csv line files into the data folder yourself.")


def download(molecules: Sequence[str] = ("H2O",), numin: float = 6500.0, numax: float = 14000.0, directory: Optional[str] = None, *,
             isotopologues: Optional[Dict[str, Sequence[int]]] = None, api: Any = None) -> Dict[str, str]:
    """Fetch the lines of ``molecules`` (H2O, CO2, O2) between ``numin`` and ``numax`` (cm-1; default 6500-14000, about 700-1540 nm) and
    write ``<directory>/<MOLECULE>.csv``. ``isotopologues`` selects local isotopologue numbers per molecule (default: the main ones).
    Returns {molecule: file}. ``api`` is the HAPI module (default: import hapi2 / hapi)."""
    hapi = _api(api)
    out = Path(data_dir(directory))
    db = out / "hapi_db"
    db.mkdir(exist_ok=True)
    hapi.db_begin(str(db))
    files: Dict[str, str] = {}
    for name in molecules:
        key = name.upper()
        if key not in MOLECULES:
            raise ValueError("supported molecules: " + ", ".join(MOLECULES))
        rows: List[List[float]] = []
        fetched: List[int] = []
        for iso in (isotopologues or {}).get(key, MAIN_ISOTOPOLOGUES[key]):
            table = f"{key}_{iso}_{int(numin)}_{int(numax)}"
            try:
                hapi.fetch(table, MOLECULES[key], int(iso), float(numin), float(numax))
            except Exception as e:      # the server answers 404 when an isotopologue has no line in the range
                print(f"{key} isotopologue {iso}: no lines fetched in {numin:g}-{numax:g} cm-1 ({type(e).__name__}); skipped")
                continue
            fetched.append(int(iso))
            cols = {c: hapi.getColumn(table, c) for c in COLUMNS if c not in ("molec_id", "local_iso_id")}
            for i in range(len(cols["nu"])):
                rows.append([MOLECULES[key], iso] + [float(cols[c][i]) for c in COLUMNS[2:]])
        if not rows:
            raise RuntimeError(f"no {key} lines between {numin:g} and {numax:g} cm-1 for isotopologues {list(isotopologues.get(key, MAIN_ISOTOPOLOGUES[key])) if isotopologues else list(MAIN_ISOTOPOLOGUES[key])}")
        rows.sort(key=lambda r: r[2])
        path = out / f"{key}.csv"
        n = write_csv(path, rows)
        files[key] = str(path)
        if hasattr(hapi, "partitionSum"):
            write_partition(out / f"{key}.partition.csv", hapi, MOLECULES[key], fetched)
        print(f"{key}: {n} lines, {numin:g}-{numax:g} cm-1 -> {path}")
    return files
