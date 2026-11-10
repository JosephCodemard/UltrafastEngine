"""A twin project on disk and the fit that turns it into a :class:`~ultrafast_twin.twin.Twin`.

Layout::

    my-lab/
      twin.json        the project: system file, base run, instrument, conditions (variables, set, data, heldOut), parameters, fit options
      system.ufs       the believed description of the lab (fittable numbers are listed in twin.json["parameters"] or marked in transforms)
      data/            one recorded file per condition
      fits/<name>/     fit.json, fitted-system.ufs, fitted-run.json, fitted-conditions.json (written by ``fit``); fits/latest.txt names the newest
      reports/         validation.json / validation.md, design.json, track.json
"""
from __future__ import annotations

import copy
import json
import time
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional

PROJECT_FILE = "twin.json"


class ProjectError(ValueError):
    pass


class TwinProject:
    def __init__(self, folder):
        self.folder = Path(folder).resolve()
        f = self.folder / PROJECT_FILE
        if not f.is_file():
            raise ProjectError(f"{self.folder} is not a twin project (no {PROJECT_FILE}); create one with `ultrafast twin init`")
        self.spec: Dict[str, Any] = json.loads(f.read_text(encoding="utf-8"))
        if self.spec.get("format") != "ultrafast-twin":
            raise ProjectError(f"{f}: \"format\" must be \"ultrafast-twin\"")

    # ------------------------------------------------------------------ files
    @property
    def name(self) -> str:
        return self.spec.get("name", self.folder.name)

    @property
    def instrument(self) -> str:
        return self.spec["instrument"]

    @property
    def sigma(self) -> float:
        return float(self.spec.get("sigma", 0.01))

    def resolve(self, rel: str) -> Path:
        """A path inside the project (refuses anything that leaves the folder)."""
        p = (self.folder / rel).resolve()
        if self.folder != p and self.folder not in p.parents:
            raise ProjectError(f"path {rel} is outside the project folder")
        return p

    def system_dict(self) -> Dict[str, Any]:
        return json.loads(self.resolve(self.spec["system"]).read_text(encoding="utf-8"))

    def conditions(self) -> List[Dict[str, Any]]:
        return self.spec["conditions"]

    def condition(self, name: str) -> Dict[str, Any]:
        for c in self.conditions():
            if c["name"] == name:
                return c
        raise ProjectError(f"no condition named {name}")

    def data_path(self, cond: Dict[str, Any]) -> str:
        return str(self.resolve(cond["data"]))

    # ------------------------------------------------------------------ the fit
    def fit_run_spec(self) -> Dict[str, Any]:
        """The run spec of the multi-condition fit: base run + instrument data + the fit block built from twin.json."""
        run = copy.deepcopy(self.spec["run"])
        ins = self.instrument
        first = next(c for c in self.conditions() if not c.get("heldOut"))
        for i in run.get("instruments", []):
            if i.get("name") == ins:
                i["measured"] = {"file": self.data_path(first), "sigma": self.sigma}
                break
        else:
            raise ProjectError(f"the base run has no instrument named {ins}")
        key = f"instrument:{ins}/measured/file"
        conds = []
        for c in self.conditions():
            entry = {"name": c["name"], "heldOut": bool(c.get("heldOut", False)), "variables": c.get("variables", {}),
                     "set": {**c.get("set", {}), key: self.data_path(c)}}
            if "weight" in c:
                entry["weight"] = c["weight"]
            conds.append(entry)
        fit = dict(self.spec.get("fit", {}))
        fit["conditions"] = conds
        if self.spec.get("parameters"):
            fit["parameters"] = copy.deepcopy(self.spec["parameters"])
        if self.spec.get("surfaces"):
            fit["surfaces"] = copy.deepcopy(self.spec["surfaces"])
        run["fit"] = fit
        return run

    def fit(self, name: Optional[str] = None, progress: Optional[Callable] = None):
        """Run the fit, write ``fits/<name>/`` and return the :class:`~ultrafast_twin.twin.Twin`."""
        import ultrafast as uf
        from .twin import Twin
        res = uf.run(self.system_dict(), self.fit_run_spec(), progress=progress)
        name = name or time.strftime("fit-%Y%m%d-%H%M%S")
        out = self.folder / "fits" / name
        res.write(out)
        (self.folder / "fits" / "latest.txt").write_text(name, encoding="utf-8")
        return Twin(self, out)

    def twin(self, fit: str = "latest"):
        from .twin import Twin
        if fit == "latest":
            f = self.folder / "fits" / "latest.txt"
            if not f.is_file():
                raise ProjectError("no fit yet: run `ultrafast twin fit`")
            fit = f.read_text(encoding="utf-8").strip()
        return Twin(self, self.folder / "fits" / fit)


def init(folder, template: str = "synthetic-lab", force: bool = False) -> TwinProject:
    """Create a project folder. Templates: ``synthetic-lab`` (a hidden truth the twin recovers) and ``synthetic-lab-saturating``
    (the energy law bends outside the training range: the validation report must flag it)."""
    from . import synthetic
    folder = Path(folder)
    if (folder / PROJECT_FILE).exists() and not force:
        raise ProjectError(f"{folder / PROJECT_FILE} exists (use --force to overwrite)")
    if template not in ("synthetic-lab", "synthetic-lab-saturating"):
        raise ProjectError(f"unknown template {template}; choose synthetic-lab or synthetic-lab-saturating")
    spec = synthetic.build_lab(folder, saturating=template.endswith("saturating"))
    (folder / PROJECT_FILE).write_text(json.dumps(spec, indent=2), encoding="utf-8")
    for sub in ("fits", "reports"):
        (folder / sub).mkdir(exist_ok=True)
    return TwinProject(folder)
