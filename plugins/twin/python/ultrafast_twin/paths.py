"""Fit paths in Python: the same path language as ``fit.parameters[].path`` of the fitting plugin.

``pulse/...`` and ``run/...`` address the run spec, ``system/...`` and ``element:ID/...`` the system file, ``instrument:NAME/...``
an instrument of the run, ``material:NAME/prop`` a material override. Segments step into objects (by key) and lists (by index).
"""
from __future__ import annotations

import copy
from typing import Any, Dict, Tuple


class PathError(ValueError):
    pass


def _root(system: Dict[str, Any], run: Dict[str, Any], path: str) -> Tuple[Any, str]:
    p = path.strip()
    low = p.lower()
    if low.startswith("pulse/"):
        return run.setdefault("pulse", {}), p[6:]
    if low.startswith("run/"):
        return run, p[4:]
    if low.startswith("system/"):
        return system, p[7:]
    if low.startswith("element:"):
        name, _, rest = p[8:].partition("/")
        for el in system.get("elements", []):
            if str(el.get("id", "")).lower() == name.lower():
                return el, rest
        raise PathError(f"path {path}: no element '{name}'")
    if low.startswith("instrument:"):
        name, _, rest = p[11:].partition("/")
        ins = run.get("instruments", [])
        for i in ins:
            if str(i.get("name", "")).lower() == name.lower():
                return i, rest
        for i in ins:
            if "name" not in i and str(i.get("kind", "")).lower() == name.lower():
                return i, rest
        raise PathError(f"path {path}: no instrument named '{name}'")
    raise PathError(f"path {path}: must start with pulse/, run/, system/, element:ID/, instrument:NAME/ or material:NAME/")


def get_value(system: Dict[str, Any], run: Dict[str, Any], path: str) -> Any:
    cur, rest = _root(system, run, path)
    for part in rest.split("/"):
        cur = cur[int(part)] if isinstance(cur, list) else cur[part]
    if isinstance(cur, dict) and "value" in cur:      # a fit object
        return cur["value"]
    return cur


def set_value(system: Dict[str, Any], run: Dict[str, Any], path: str, value: Any) -> None:
    """Write ``value`` at ``path`` (in place); a number replaces the ``value`` of a fit object there."""
    if path.lower().startswith("material:"):
        target, _, prop = path.partition("/")
        run.setdefault("overrides", []).append({"target": target, "property": prop, "value": value})
        return
    cur, rest = _root(system, run, path)
    parts = rest.split("/")
    for part in parts[:-1]:
        if isinstance(cur, list):
            cur = cur[int(part)]
        else:
            cur = cur.setdefault(part, {})
    last = parts[-1]
    if isinstance(cur, list):
        i = int(last)
        old = cur[i]
        if isinstance(old, dict) and "value" in old and isinstance(value, (int, float)):
            old["value"] = value
        else:
            cur[i] = value
        return
    old = cur.get(last)
    if isinstance(old, dict) and "value" in old and isinstance(value, (int, float)):
        old["value"] = value
    else:
        cur[last] = value


def apply_set(system: Dict[str, Any], run: Dict[str, Any], assignments: Dict[str, Any]) -> None:
    for path, value in assignments.items():
        set_value(system, run, path, copy.deepcopy(value))
