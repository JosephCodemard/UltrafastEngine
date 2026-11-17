"""Golden layouts of the system schematic (ultrafast.plotting.graph_layout): a Mach-Zehnder, a cavity loop, a 46-pass sameAs cell, a skipped layer.

The layouts are computed from hand-written summaries (no engine needed) and compared with fixed numbers, so any change of the algorithm
shows. The figure tests draw the same systems and check the text and the patches. The layout is deterministic: the same summary gives the
same picture.
"""
import copy

import pytest

from ultrafast.plotting import graph_layout as gl

H = gl.LANE_DY


def _summary(elements, links, loops=(), vin=(), vout=()):
    return {"elements": [{"index": i, "id": e[0], "type": e[1]} for i, e in enumerate(elements)],
            "graph": {"topology": "branched", "links": [{"from": a, "to": b} for a, b in links], "loops": [dict(entry=l[0], kind=l[1], members=list(l[2])) for l in loops],
                      "virtualInputs": list(vin), "virtualOutputs": list(vout)}}


MZI = _summary([("bs1", "beamSplitter"), ("armA", "delay"), ("armB", "delay"), ("bs2", "beamSplitter")],
               [("bs1:c", "armA:in"), ("bs1:d", "armB:in"), ("armA:out", "bs2:1"), ("armB:out", "bs2:2"), ("bs2:c", "end")],
               vin=["bs1#b"], vout=["bs2#d"])
CAVITY = _summary([("m1", "beamSplitter"), ("forth", "delay"), ("m2", "beamSplitter"), ("back", "delay")],
                  [("m1:c", "forth:in"), ("forth:out", "m2:2"), ("m2:c", "back:in"), ("m2:d", "end"), ("back:out", "m1:2")],
                  loops=[("m1", "ringDown", ["m1", "forth", "m2", "back"])], vin=[], vout=["m1#d"])


def _node(lay, i):
    return next(n for n in lay["nodes"] if n["id"] == i)


def test_mach_zehnder_layout_is_two_lanes_between_two_splitters():
    lay = gl.layout_graph(MZI)
    pos = {n["id"]: (n["layer"], round(n["x"], 6), round(n["y"], 6)) for n in lay["nodes"]}
    assert pos == {"bs1": (0, 1.0, 0.0), "armA": (1, 2.9, -H / 2), "armB": (1, 2.9, H / 2), "bs2": (2, 4.8, 0.0)}
    kinds = sorted(e["kind"] for e in lay["edges"])
    assert kinds == ["end", "forward", "forward", "forward", "forward", "virtual_in", "virtual_out"]
    assert gl.segments_cross_boxes(lay) == []
    assert lay["loops"] == [] and lay["layers"] == 3


def test_every_forward_route_is_orthogonal_and_ends_on_the_boxes():
    lay = gl.layout_graph(MZI)
    for e in (e for e in lay["edges"] if e["kind"] == "forward"):
        pts = e["points"]
        for (xa, ya), (xb, yb) in zip(pts[:-1], pts[1:]):
            assert abs(xa - xb) < 1e-9 or abs(ya - yb) < 1e-9, e
        src, dst = _node(lay, e["from"]), _node(lay, e["to"])
        assert pts[0][0] == pytest.approx(src["x"] + gl.BOX_W / 2) and pts[-1][0] == pytest.approx(dst["x"] - gl.BOX_W / 2)


def test_cavity_loop_is_a_back_arc_inside_a_loop_region():
    lay = gl.layout_graph(CAVITY)
    assert [(n["id"], n["layer"]) for n in lay["nodes"]] == [("m1", 0), ("forth", 1), ("m2", 2), ("back", 3)]
    assert all(abs(n["y"]) < 1e-9 for n in lay["nodes"])                      # a single row
    back = [e for e in lay["edges"] if e["kind"] == "back"]
    assert len(back) == 1 and (back[0]["from"], back[0]["to"], back[0]["port"]) == ("back", "m1", "out")
    pts = back[0]["points"]
    assert pts[1][1] < -gl.BOX_H / 2 and pts[2][1] == pts[1][1]               # the arc runs below the row
    assert [l["kind"] for l in lay["loops"]] == ["ringDown"] and lay["loops"][0]["members"] == ["m1", "forth", "m2", "back"]
    x0, y0, x1, y1 = lay["loops"][0]["box"]
    assert x0 < _node(lay, "m1")["x"] - gl.BOX_W / 2 and x1 > _node(lay, "back")["x"] + gl.BOX_W / 2
    assert gl.segments_cross_boxes(lay) == []                                 # the output line goes around the 'back' box
    end = next(e for e in lay["edges"] if e["kind"] == "end")
    assert len(end["points"]) == 4 and end["points"][-1][1] > gl.BOX_H / 2


def test_46_pass_same_as_cell_collapses_to_one_box_times_46():
    els = [("pass%d" % i, "delay") for i in range(1, 47)]
    links = [("pass%d:out" % i, "pass%d:in" % (i + 1)) for i in range(1, 46)] + [("pass46:out", "end")]
    lay = gl.layout_graph(_summary(els, links))
    assert len(lay["nodes"]) == 1
    n = lay["nodes"][0]
    assert (n["id"], n["repeat"], n["members"][0], n["members"][-1]) == ("pass1", 46, "pass1", "pass46")
    assert [e["kind"] for e in lay["edges"]] == ["end"]
    assert len(gl.layout_graph(_summary(els, links), group=False)["nodes"]) == 46
    # the cell declared as a loop: still one box, the loop region around it
    lay2 = gl.layout_graph(_summary(els, links, loops=[("pass1", "steadyState", [e[0] for e in els])]))
    assert lay2["loops"][0]["members"] == ["pass1"]


def test_without_links_a_sequential_chain_is_laid_out_in_a_row():
    s = {"elements": [{"index": i, "id": "e%d" % i, "type": t} for i, t in enumerate(["bulk", "gap", "mirror", "gap", "bulk"])], "graph": {"topology": "sequential", "links": []}}
    lay = gl.layout_graph(s, group=False)
    assert [n["layer"] for n in lay["nodes"]] == [0, 1, 2, 3, 4] and {n["y"] for n in lay["nodes"]} == {0.0}


def test_a_link_that_skips_layers_runs_between_the_rows_not_through_boxes():
    s = _summary([("a", "x"), ("b", "x2"), ("c", "x3"), ("d", "x4")],
                 [("a:c", "b:in"), ("a:d", "d:1"), ("b:out", "c:in"), ("c:out", "d:2"), ("d:out", "end")])
    lay = gl.layout_graph(s)
    skip = next(e for e in lay["edges"] if (e["from"], e["to"]) == ("a", "d"))
    assert len(skip["points"]) == 6                                           # out, up to the track, along it, down, in
    assert gl.segments_cross_boxes(lay) == []
    assert _node(lay, "d")["layer"] == 3


def test_two_links_into_one_channel_do_not_overlap():
    lay = gl.layout_graph(MZI)
    verticals = [round(p[0], 6) for e in lay["edges"] if e["kind"] == "forward" and e["from"] == "bs1" for p in e["points"][1:3]]
    assert len(set(verticals)) == 2                                           # the two links out of bs1 use their own channel


def test_layout_is_deterministic_and_does_not_change_its_input():
    before = copy.deepcopy(CAVITY)
    a, b = gl.layout_graph(CAVITY), gl.layout_graph(copy.deepcopy(CAVITY))
    assert a == b and CAVITY == before
    assert gl.element_stem({"type": "gap", "id": "pass12"}) == gl.element_stem({"type": "gap", "id": "pass7"}) == "gap:pass#"
