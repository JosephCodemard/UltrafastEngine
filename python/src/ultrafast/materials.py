"""The material library: built-in materials, gases (with rotational Raman), birefringent crystals and the
refractiveindex.info pages bundled with the engine or imported into the user library (docs/reference/materials.md).

    import ultrafast as uf
    uf.materials.available()["refractiveIndexInfoBundled"]
    uf.materials.show("main/SiO2/Malitson")["table"]          # n, group index, GVD at common wavelengths
    uf.materials.import_page("my_glass.yml", name="MyGlass")  # then {"index": {"kind": "refractiveindex", "page": "MyGlass"}}
"""
from __future__ import annotations

import os
from typing import Any, Dict, Optional

from .core import command

__all__ = ["available", "show", "import_page", "refractiveindex"]


def available() -> Dict[str, Any]:
    """Names of everything the engine knows: built-in materials, gases, birefringent crystals, refractiveindex.info pages."""
    return command({"command": "materials.list"})


def show(name: str) -> Dict[str, Any]:
    """Index model, n2, rotational Raman and a table of n, group index and GVD (fs^2/mm) of a material or page."""
    return command({"command": "materials.show", "name": name})


def import_page(path: "os.PathLike[str] | str", name: Optional[str] = None) -> Dict[str, Any]:
    """Copy a refractiveindex.info YAML page into the user library and return the material definition to use."""
    req: Dict[str, Any] = {"command": "materials.import", "file": os.path.abspath(os.fspath(path))}
    if name:
        req["name"] = name
    return command(req)


def refractiveindex(page: str, pressure_bar: Optional[float] = None, temperature_k: Optional[float] = None) -> Dict[str, Any]:
    """An index model dict for a refractiveindex.info page (bundled, imported, a file, or from ULTRAFAST_RII_PATH)."""
    d: Dict[str, Any] = {"kind": "refractiveindex", "page": page}
    if pressure_bar is not None:
        d["pressureBar"] = pressure_bar
    if temperature_k is not None:
        d["temperatureK"] = temperature_k
    return d
