"""The package structure (S1): every public name of 2.2.0 is still importable, the new packages exist and import cleanly,
the old ``ultrafast.diagram`` path works with a DeprecationWarning, and nothing in the package imports the shim."""
import importlib
import json
import subprocess
import sys
import warnings
from pathlib import Path

import pytest

SNAPSHOT = json.loads((Path(__file__).parent / "data" / "public_surface_2_2_0.json").read_text())
SRC = Path(__file__).resolve().parents[1] / "src"
PLUGIN_PATH = Path(__file__).resolve().parents[2] / "plugins" / "fitting" / "python"


@pytest.fixture(scope="module", autouse=True)
def _fitting_on_path():
    sys.path.insert(0, str(PLUGIN_PATH))
    yield
    sys.path.remove(str(PLUGIN_PATH))


@pytest.mark.parametrize("module", sorted(SNAPSHOT))
def test_every_public_name_of_2_2_0_is_still_there(module):
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", DeprecationWarning)
        mod = importlib.import_module(module)
    missing = [n for n in SNAPSHOT[module] if not hasattr(mod, n)]
    assert not missing, f"{module} lost: {missing}"


def test_old_diagram_path_warns_and_is_the_new_module():
    sys.modules.pop("ultrafast.diagram", None)
    with pytest.warns(DeprecationWarning, match="ultrafast.plotting.diagrams"):
        old = importlib.import_module("ultrafast.diagram")
    from ultrafast.plotting import diagrams
    assert old.draw_system is diagrams.draw_system and old.instrument_locations is diagrams.instrument_locations


def test_new_packages_import_without_warnings_and_without_matplotlib():
    code = ("import sys, ultrafast, ultrafast.plotting, ultrafast.system, ultrafast.components, ultrafast.instruments, ultrafast.sources,"
            " ultrafast.algorithms, ultrafast.results\n"
            "from ultrafast.system import Sequential, Branched\nfrom ultrafast.components import BeamSplitter\n"
            "assert 'matplotlib' not in sys.modules and 'PIL' not in sys.modules, 'matplotlib / Pillow must stay lazy'\nprint('ok')")
    r = subprocess.run([sys.executable, "-W", "error::DeprecationWarning", "-c", code], capture_output=True, text=True,
                       env={**__import__("os").environ, "PYTHONPATH": str(SRC)})
    assert r.returncode == 0 and "ok" in r.stdout, r.stderr[-800:]


def test_aliases_are_the_builders_so_typed_returns_are_kept():
    from ultrafast import build as b
    from ultrafast import components as c, instruments as ins, sources as s, system as sy
    assert c.BeamSplitter is b.beam_splitter and c.Waveplate is b.waveplate and c.Mirror is b.mirror and c.beam_splitter is b.beam_splitter
    assert s.Pulse is b.pulse and s.GaussianBeam is b.gaussian_beam
    assert sy.Sequential is b.Sequential and sy.Branched is b.Branched and sy.link is b.link
    assert ins.Spectrometer(name="o") == b.instrument("spectrometer", "end", "o")
    assert ins.Frog("end", "f", type="pg") == b.instrument("frog", "end", "f", type="pg")


def test_every_builder_is_grouped_or_listed_as_build_only():
    """The decision table: each public builder of ``build`` is exposed by a topic package, or it is deliberately kept in ``build`` only."""
    from ultrafast import build as b
    import ultrafast.components as c, ultrafast.instruments as ins, ultrafast.sources as s, ultrafast.system as sy
    grouped = set(c.__all__) | set(ins.__all__) | set(s.__all__) | set(sy.__all__)
    build_only = {"run_spec", "physics", "grid", "tail_grid", "compute", "sampling", "save", "override", "environment", "air", "transverse",
                  "material", "sellmeier", "gas", "coating_gdd", "coating_quarter_wave", "coherence", "delay_uniform", "delay_exponential",
                  "delay_table", "delayed_scatter"}
    public = [n for n in dir(b) if not n.startswith("_") and callable(getattr(b, n)) and getattr(getattr(b, n), "__module__", "") == b.__name__]
    ungrouped = sorted(n for n in public if n not in grouped and n not in build_only)
    assert not ungrouped, f"decide where these go (topic package or build_only): {ungrouped}"


def test_all_registered_figures_resolve_to_functions():
    from ultrafast import plotting as up
    for name in up.FIGURES:
        if name in up._EXTRA_FIGURES:
            continue
        assert callable(getattr(up, name)) or name in ("system",), name
    for fn in ("temporal", "spectrum", "overview", "instruments", "measurements", "probes", "elements", "report", "plot", "load"):
        assert callable(getattr(up, fn)), f"plotting.{fn} must be the function, not a submodule"


def test_theme_is_shared_by_the_submodules():
    from ultrafast import plotting as up
    from ultrafast.plotting import style
    up.set_theme(dark=True)
    try:
        assert style._theme is up._theme and up._theme["surface"] == style._DARK["surface"]
    finally:
        up.set_theme(dark=False)
