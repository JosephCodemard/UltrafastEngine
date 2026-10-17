"""Plugins from Python: which are installed, and loading their Python parts.

Plugins live in folders holding a ``plugin.json`` (docs/reference/plugins.md). The engine (C#) loads their
assemblies itself; this module finds the same folders and imports each plugin's optional Python module (named in
``plugin.json`` -> ``"python": {"module": ...}``, from the plugin's ``python/`` folder), which can add builders and
figures. Search order (first wins for a given id):

1. ``ULTRAFAST_PLUGIN_PATH`` (folders separated by ``os.pathsep``)
2. ``plugins/`` next to the hosted engine of this package (``ultrafast/bin/hosted/plugins``)
3. ``artifacts/plugins`` of the source checkout this package lives in (development)
4. the user folder: ``$ULTRAFAST_HOME/plugins`` or ``~/.ultrafast/plugins``

Set ``ULTRAFAST_NO_PLUGINS=1`` to skip them.

>>> import ultrafast as uf
>>> [p.id for p in uf.plugins.installed()]
['fitting', 'zemax']
>>> fitting = uf.plugins.module("fitting")       # the plugin's Python module (or None)
"""
from __future__ import annotations

import importlib
import json
import os
import sys
import warnings
from dataclasses import dataclass, field
from pathlib import Path
from types import ModuleType
from typing import Dict, List, Optional

__all__ = ["PluginInfo", "search_paths", "installed", "load_python_plugins", "module", "loaded"]

_PKG = Path(__file__).resolve().parent


@dataclass
class PluginInfo:
    """An installed plugin folder (from its ``plugin.json``)."""
    id: str
    name: str
    version: str
    description: str
    folder: Path
    python_module: Optional[str] = None
    commands: List[str] = field(default_factory=list)


def search_paths() -> List[Path]:
    """Folders searched for plugins, in order."""
    dirs: List[Path] = []
    env = os.environ.get("ULTRAFAST_PLUGIN_PATH")
    if env:
        dirs += [Path(p) for p in env.split(os.pathsep) if p]
    dirs.append(_PKG / "bin" / "hosted" / "plugins")
    repo = _PKG.parents[2] if len(_PKG.parents) > 2 else None
    if repo is not None and (repo / "UltrafastEngine.sln").exists():
        dirs.append(repo / "artifacts" / "plugins")
    home = os.environ.get("ULTRAFAST_HOME") or str(Path.home() / ".ultrafast")
    dirs.append(Path(home) / "plugins")
    seen, out = set(), []
    for d in dirs:
        k = str(d.resolve()) if d.exists() else str(d)
        if k not in seen:
            seen.add(k)
            out.append(d)
    return out


def installed() -> List[PluginInfo]:
    """Every plugin folder found in :func:`search_paths` (first occurrence of each id)."""
    if os.environ.get("ULTRAFAST_NO_PLUGINS") == "1":
        return []
    found: Dict[str, PluginInfo] = {}
    for d in search_paths():
        if not d.is_dir():
            continue
        for sub in sorted(d.iterdir()):
            mf = sub / "plugin.json"
            if not mf.is_file():
                continue
            try:
                m = json.loads(mf.read_text())
            except (OSError, ValueError) as e:
                warnings.warn(f"plugin {sub}: bad plugin.json ({e})")
                continue
            pid = m.get("id")
            if not pid or pid in found:
                continue
            found[pid] = PluginInfo(pid, m.get("name", pid), m.get("version", "0"), m.get("description", ""), sub,
                                    (m.get("python") or {}).get("module"), list(m.get("commands") or []))
    return list(found.values())


_loaded: Dict[str, ModuleType] = {}
_done = False


def load_python_plugins() -> Dict[str, ModuleType]:
    """Import the Python module of every installed plugin (once) and call its ``register(host)``, where ``host``
    offers ``register_figure``. Returns {plugin id: module}. Errors are warnings, never fatal."""
    global _done
    if _done:
        return dict(_loaded)
    _done = True
    for p in installed():
        if not p.python_module:
            continue
        pydir = p.folder / "python"
        if not pydir.is_dir():
            continue
        if str(pydir) not in sys.path:
            sys.path.insert(0, str(pydir))
        try:
            mod = importlib.import_module(p.python_module)
            if hasattr(mod, "register"):
                mod.register(_Host())
            _loaded[p.id] = mod
        except Exception as e:  # noqa: BLE001 -- a broken plugin must not break the engine
            warnings.warn(f"plugin '{p.id}': Python module {p.python_module} not loaded ({e})")
    return dict(_loaded)


def module(plugin_id: str) -> Optional[ModuleType]:
    """The Python module of an installed plugin (loading plugins if needed), or None."""
    return load_python_plugins().get(plugin_id)


def loaded() -> List[str]:
    """Ids of plugins whose Python module is loaded."""
    return sorted(load_python_plugins())


class _Host:
    """What a plugin's ``register(host)`` may use."""

    @staticmethod
    def register_figure(name, maker, after=None):
        from .plotting import register_figure
        register_figure(name, maker, after)
