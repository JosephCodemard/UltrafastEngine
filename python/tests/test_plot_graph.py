"""The system figure draws a Branched system as a graph: one box per element, in layers, with the arms in separate lanes."""
import pytest

matplotlib = pytest.importorskip("matplotlib")
matplotlib.use("Agg")

import ultrafast as uf
from ultrafast import build as b
from ultrafast import diagram, plotting as up


@pytest.fixture(scope="module")
def mach_zehnder():
    system = b.system("mach-zehnder", b.Branched([
        b.link(b.beam_splitter("bs1"), {"c": "armA", "d": "armB"}),
        b.link(b.delay("armA", delay_s=0.0), "bs2:1"),
        b.link(b.delay("armB", delay_s=300e-15), "bs2:2"),
        b.link(b.beam_splitter("bs2"), {"c": "end", "d": "virtual"})]))
    return uf.run(system, b.run_spec(b.pulse(1030e-9, 100e-15, energy=1e-9), grid=b.grid(n=4096, window_s=20e-12)))


def test_layout_puts_the_arms_in_one_layer_and_two_lanes(mach_zehnder):
    ids, edges, layer, lane = diagram._graph_layout(mach_zehnder.summary)
    assert [layer[i] for i in ("bs1", "armA", "bs2")] == [0, 1, 2]
    assert layer["armA"] == layer["armB"] and lane["armA"] != lane["armB"]


def test_system_figure_is_drawn_for_a_branched_system(mach_zehnder):
    fig = up.system(mach_zehnder)
    texts = [t.get_text() for t in fig.axes[0].texts]
    assert {"bs1", "bs2", "armA", "armB"} <= set(texts)
    assert any("output" in t for t in texts)


def test_sequential_system_keeps_the_beam_line():
    system = b.system("plate", [b.bulk("plate", "FusedSilica", 1e-3)])
    res = uf.run(system, b.run_spec(b.pulse(1030e-9, 100e-15, energy=1e-9)))
    assert up.system(res) is not None
