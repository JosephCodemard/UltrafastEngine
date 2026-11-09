"""Generate docs/reference/python-api.md from the package source (no import, no engine needed).

    python3 python/tools/gen_api_docs.py            # writes docs/reference/python-api.md
    python3 python/tools/gen_api_docs.py --check    # exit 1 if the committed page is out of date

Documents every public module-level function and class (names without a leading underscore, or listed in
``__all__``) with its signature and the first paragraph of its docstring, plus the public methods of classes.
"""
from __future__ import annotations

import ast
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PKG = ROOT / "python" / "src" / "ultrafast"
OUT = ROOT / "docs" / "reference" / "python-api.md"

MODULES = [
    ("ultrafast", "__init__.py", "Top level: `import ultrafast as uf`."),
    ("ultrafast.core", "core.py", "Running systems, results, commands (studies, retrieval, materials)."),
    ("ultrafast.build", "build.py", "Builders for system files and run specs (`from ultrafast import build as b`)."),
    ("ultrafast.measured", "measured.py", "Measured data: typed comparisons of instruments with recordings (`res.comparisons`), compared arrays."),
    ("ultrafast.materials", "materials.py", "The material library: list, show, import refractiveindex.info pages."),
    ("ultrafast.laser", "laser.py", "Laser and amplifier helpers."),
    ("ultrafast.crystal", "crystal.py", "χ(2) crystal helpers."),
    ("ultrafast.plugins", "plugins.py", "Plugin discovery and loading."),
    ("ultrafast.plot", "plot.py", "Quick plots of results (matplotlib)."),
    ("ultrafast.plotting", "plotting/__init__.py", "Plotting package: `report`, `plot`, `register_figure`, the figures (re-exported from the modules below)."),
    ("ultrafast.plotting.style", "plotting/style.py", "Themes, matplotlib style, SI-prefix formatting."),
    ("ultrafast.plotting.data", "plotting/data.py", "Reading a saved results folder."),
    ("ultrafast.plotting.time_domain", "plotting/time_domain.py", "Temporal panels."),
    ("ultrafast.plotting.spectral", "plotting/spectral.py", "Spectral panels."),
    ("ultrafast.plotting.run_figures", "plotting/run_figures.py", "Whole-run figures: overview, losses, gain, oscillator, comparison."),
    ("ultrafast.plotting.spatial", "plotting/spatial.py", "Image panels."),
    ("ultrafast.plotting.instrument_panels", "plotting/instrument_panels.py", "Instrument panels, probes, measured versus simulated."),
    ("ultrafast.plotting.diagrams", "plotting/diagrams.py", "Layout diagrams of a system (the old `ultrafast.diagram`)."),
    ("ultrafast.plotting.zernike", "plotting/zernike.py", "Zernike decomposition figure."),
    ("ultrafast.plotting.animate", "plotting/animate.py", "Animated GIF (and optional MP4) writer; the figure size budget."),
    ("ultrafast.diagram", "diagram.py", "Deprecated alias of `ultrafast.plotting.diagrams`."),
    ("ultrafast.system", "system/__init__.py", "Sequential and Branched systems, load and save."),
    ("ultrafast.components", "components/__init__.py", "Optical components by kind (CapWords aliases of the builders)."),
    ("ultrafast.sources", "sources.py", "Pulses, beams, measured spectra (CapWords aliases)."),
    ("ultrafast.instruments", "instruments/__init__.py", "One factory per instrument kind."),
    ("ultrafast.algorithms.zernike", "algorithms/zernike.py", "Zernike polynomials (Noll), fit and evaluate, in NumPy."),
    ("ultrafast.results", "results/__init__.py", "Results and curve metrics."),
]


def _sig(fn: ast.FunctionDef | ast.AsyncFunctionDef, drop_self: bool = False) -> str:
    a = fn.args
    parts: list[str] = []
    pos = list(a.posonlyargs) + list(a.args)
    defaults = [None] * (len(pos) - len(a.defaults)) + list(a.defaults)
    for arg, d in zip(pos, defaults):
        if drop_self and arg is pos[0] and arg.arg in ("self", "cls"):
            continue
        s = arg.arg
        if arg.annotation is not None:
            s += ": " + ast.unparse(arg.annotation)
        if d is not None:
            s += " = " + ast.unparse(d)
        parts.append(s)
    if a.vararg:
        parts.append("*" + a.vararg.arg)
    elif a.kwonlyargs:
        parts.append("*")
    for arg, d in zip(a.kwonlyargs, a.kw_defaults):
        s = arg.arg
        if arg.annotation is not None:
            s += ": " + ast.unparse(arg.annotation)
        if d is not None:
            s += " = " + ast.unparse(d)
        parts.append(s)
    if a.kwarg:
        parts.append("**" + a.kwarg.arg)
    ret = " -> " + ast.unparse(fn.returns) if fn.returns is not None else ""
    return f"{fn.name}({', '.join(parts)}){ret}"


def _doc(node) -> str:
    d = ast.get_docstring(node) or ""
    para = d.strip().split("\n\n")[0].strip()
    return " ".join(line.strip() for line in para.splitlines())


def _public(name: str, all_names: set[str] | None) -> bool:
    if all_names is not None:
        return name in all_names
    return not name.startswith("_")


def _all(tree: ast.Module) -> set[str] | None:
    for node in tree.body:
        if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "__all__" for t in node.targets):
            try:
                return set(ast.literal_eval(node.value))
            except Exception:
                return None
    return None


def module_section(modname: str, path: Path, blurb: str) -> list[str]:
    tree = ast.parse(path.read_text(encoding="utf-8"))
    names = _all(tree)
    out = [f"## `{modname}`", "", blurb, ""]
    mdoc = _doc(tree)
    if mdoc:
        out += [mdoc, ""]
    if modname == "ultrafast" and names:
        out += ["Exported names: " + ", ".join(f"`{n}`" for n in sorted(names)), ""]
        names = None  # functions defined here: document all public ones
    items = 0
    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and _public(node.name, names):
            out += [f"### `{_sig(node)}`", ""]
            d = _doc(node)
            if d:
                out += [d, ""]
            items += 1
        elif isinstance(node, ast.ClassDef) and _public(node.name, names):
            bases = ", ".join(ast.unparse(b) for b in node.bases)
            out += [f"### class `{node.name}" + (f"({bases})" if bases else "") + "`", ""]
            d = _doc(node)
            if d:
                out += [d, ""]
            for m in node.body:
                if isinstance(m, (ast.FunctionDef, ast.AsyncFunctionDef)) and (not m.name.startswith("_") or m.name == "__init__"):
                    md = _doc(m)
                    out.append(f"* `{_sig(m, drop_self=True)}`" + (f" -- {md}" if md else ""))
            out.append("")
            items += 1
    if items == 0:
        out += ["(No public functions; see the exported names above.)", ""]
    return out


def generate() -> str:
    lines = [
        "# Python API (generated)",
        "",
        "<!-- generated by python/tools/gen_api_docs.py from the package source; do not edit by hand -->",
        "",
        "Signatures and the first paragraph of every public docstring. The narrative guide is [Python](python.md);",
        "every element and option the builders accept is in the [option reference](options.md). Editors get the same",
        "information through the type hints and the TypedDicts in `ultrafast.types` (autocompletion of system and run",
        "dictionaries).",
        "",
    ]
    for modname, fname, blurb in MODULES:
        p = PKG / fname
        if p.exists():
            lines += module_section(modname, p, blurb)
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    text = generate()
    if "--check" in sys.argv:
        cur = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if cur != text:
            print(f"{OUT} is out of date: run python3 python/tools/gen_api_docs.py")
            return 1
        return 0
    OUT.write_text(text, encoding="utf-8")
    print(f"wrote {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
