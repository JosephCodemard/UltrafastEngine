"""Reading a results folder (``summary.json`` + ``result.npz``) written by the CLI: :class:`SavedResult`, :func:`load`."""
from __future__ import annotations

import json
from pathlib import Path
from typing import List, Union

import numpy as np




class SavedResult:
    """A results folder written by the CLI / ``Result.write``: same interface as a live Result
    (``summary``, ``provenance``, ``names()``, ``result[name]``, ``instrument(name)``, ``comparisons``,
    ``comparison(name)``)."""

    def __init__(self, directory: Union[str, Path]):
        self.directory = Path(directory)
        s = self.directory / "summary.json"
        if not s.exists():
            raise FileNotFoundError(f"{s} not found -- is '{directory}' an UltrafastEngine results folder?")
        self.summary = json.loads(s.read_text(encoding="utf-8"))
        npz = self.directory / "result.npz"
        self._npz = np.load(npz) if npz.exists() else None
        if self._npz is None:
            raise FileNotFoundError(f"{npz} not found (enable outputs.npz in the run spec)")
        p = self.directory / "provenance.json"
        self.provenance = json.loads(p.read_text(encoding="utf-8")) if p.exists() else []

    @property
    def name(self) -> str:
        return self.summary.get("run") or self.directory.name

    def names(self) -> List[str]:
        return list(self._npz.files)

    def __contains__(self, k):
        return k in self._npz.files

    def __getitem__(self, k: str) -> np.ndarray:
        return self._npz[k]

    def instrument(self, name: str) -> dict:
        for i in self.summary.get("instruments", []):
            if i["name"] == name:
                d = dict(i["values"])
                for a in i["arrays"]:
                    d[a[len(name) + 1:] if a.startswith(name + "_") else a] = self[a]
                return d
        raise KeyError(name)

    @property
    def comparisons(self):
        """Instruments compared with recorded data (:mod:`ultrafast.measured`)."""
        from .. import measured
        return measured.comparisons(self)

    def comparison(self, instrument: str):
        """The comparison of one instrument with its recorded data (KeyError if it has none)."""
        from .. import measured
        return measured.comparison(self, instrument)


def load(source) -> "SavedResult":
    """A results folder (str/Path) -> :class:`SavedResult`; a live Result / SavedResult is returned as is."""
    if isinstance(source, (str, Path)):
        return SavedResult(source)
    return source


def _has(res, k):
    try:
        return k in res.names()
    except Exception:
        return False


def _label(res) -> str:
    n = getattr(res, "name", None)
    return n if isinstance(n, str) else res.summary.get("run", "run")

