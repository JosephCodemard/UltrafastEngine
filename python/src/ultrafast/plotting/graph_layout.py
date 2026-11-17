"""Deterministic layout of a branched system's graph (no drawing, no matplotlib): layers, lanes, grouped runs, loop arcs, orthogonal routes.

``layout_graph(summary)`` reads ``summary["elements"]`` and ``summary["graph"]`` (links, loops, virtual ports) and returns a
:class:`GraphLayout` of plain dicts, so the same system always gives the same picture (decision V2) and tests can assert on positions.

    nodes   [{"id", "type", "members": [ids], "repeat", "layer", "x", "y"}]       one per box; a run of identical elements is one node (xN)
    edges   [{"from", "port", "to", "kind", "points": [(x, y), ...], "label_at", ...}]
            kind "forward" (orthogonal polyline around the boxes), "back" (a loop-closing link, drawn as an arc below the row),
            "end" (to the output marker), "virtual_in" / "virtual_out" (an open port with no beamline behind it)
    loops   [{"entry", "kind", "members": [node ids], "box": (x0, y0, x1, y1)}]

Algorithm (all ties broken by file order): collapse runs of linearly connected elements of the same type (and id pattern) into one node;
find loop-closing links by a depth-first search from the first element (links into a node still on the search stack); layer = longest
path over the other links; lane (y) = mean of the predecessors' lanes, pushed apart where boxes would touch; a forward link runs
horizontally out of its source, turns in a channel just after the source layer, travels along a free horizontal track between the rows of
boxes when it skips layers, turns again in a channel before the target layer and enters the target horizontally; channels and tracks are
shared by several links with a small offset each.

Main functions: layout_graph(summary, group=True, min_run=3) -> GraphLayout; element_stem(element) -> str.
"""
from __future__ import annotations

import re
from typing import Dict, List, Optional, Tuple, TypedDict

BOX_W, BOX_H = 0.9, 0.62            # box size in layout units
LAYER_DX = 1.9                       # distance between layers
LANE_DY = 1.3                        # distance between lanes
CHANNEL = 0.2                        # first channel offset from a box edge
CHANNEL_STEP = 0.1                   # spacing between links sharing a channel


class GraphLayout(TypedDict):
    nodes: List[dict]
    edges: List[dict]
    loops: List[dict]
    layers: int
    x_end: float
    x_input: float
    y_range: Tuple[float, float]


def element_stem(e: dict) -> str:
    """Type and id with digit runs replaced by ``#``: the elements of one repeated unit share it (``pass1`` ... ``pass46``)."""
    return str(e.get("type", "")) + ":" + re.sub(r"\d+", "#", str(e.get("id", "")))


def _split(ref: str) -> Tuple[str, str]:
    a, _, b = ref.partition(":")
    return a, b


def layout_graph(summary: dict, group: bool = True, min_run: int = 3) -> GraphLayout:
    els = summary.get("elements", [])
    order = [e["id"] for e in els]
    by_id = {e["id"]: e for e in els}
    g = summary.get("graph", {}) or {}
    raw = []
    for lk in g.get("links", []):
        a, port = _split(lk["from"])
        if lk["to"] == "end":
            raw.append((a, port, "end", ""))
        else:
            b, bport = _split(lk["to"])
            raw.append((a, port, b, bport))
    if not raw and order:                                   # a sequential system: the chain
        raw = [(order[i], "out", order[i + 1], "in") for i in range(len(order) - 1)] + [(order[-1], "out", "end", "")]

    # ---- collapse runs of identical elements into one node (xN) --------------------------------------------------------------------
    rep_of: Dict[str, str] = {i: i for i in order}          # element id -> id of the node that shows it
    members: Dict[str, List[str]] = {i: [i] for i in order}
    if group:
        outs: Dict[str, list] = {}
        ins: Dict[str, list] = {}
        for a, p, b, bp in raw:
            outs.setdefault(a, []).append(b)
            if b != "end":
                ins.setdefault(b, []).append(a)
        seen = set()
        for i in order:
            if i in seen:
                continue
            run = [i]
            cur = i
            while True:
                o = outs.get(cur, [])
                if len(o) != 1 or o[0] == "end" or o[0] in run or len(ins.get(o[0], [])) != 1:
                    break
                if element_stem(by_id[o[0]]) != element_stem(by_id[i]):
                    break
                run.append(o[0])
                cur = o[0]
            seen.update(run)
            if len(run) >= min_run:
                for m in run:
                    rep_of[m] = run[0]
                members[run[0]] = run
                for m in run[1:]:
                    members.pop(m, None)
    node_ids = [i for i in order if rep_of[i] == i]

    edges_in = []                                           # edges between nodes, in file order; links inside a run vanish
    for a, p, b, bp in raw:
        na, nb = rep_of.get(a, a), (b if b == "end" else rep_of.get(b, b))
        if na == nb:
            continue
        edges_in.append((na, p, nb, bp))

    # ---- back edges by depth-first search from the first node ----------------------------------------------------------------------
    out_edges: Dict[str, List[int]] = {}
    for k, (a, p, b, bp) in enumerate(edges_in):
        out_edges.setdefault(a, []).append(k)
    back = set()
    state: Dict[str, int] = {}                              # 1 on the stack, 2 done
    roots = node_ids[:1] + [i for i in node_ids[1:]]

    def dfs(start: str) -> None:
        stack = [(start, iter(out_edges.get(start, [])))]
        state[start] = 1
        while stack:
            n, it = stack[-1]
            for k in it:
                t = edges_in[k][2]
                if t == "end":
                    continue
                if state.get(t) == 1:
                    back.add(k)
                elif t not in state:
                    state[t] = 1
                    stack.append((t, iter(out_edges.get(t, []))))
                    break
            else:
                state[n] = 2
                stack.pop()

    for r in roots:
        if r not in state:
            dfs(r)

    # ---- layers: longest path over the forward links -------------------------------------------------------------------------------
    layer = {i: 0 for i in node_ids}
    for _ in range(len(node_ids) + 1):
        changed = False
        for k, (a, p, b, bp) in enumerate(edges_in):
            if k in back or b == "end":
                continue
            if layer[b] < layer[a] + 1:
                layer[b] = layer[a] + 1
                changed = True
        if not changed:
            break
    n_layers = (max(layer.values()) + 1) if layer else 1

    # ---- lanes ---------------------------------------------------------------------------------------------------------------------
    preds: Dict[str, List[str]] = {i: [] for i in node_ids}
    for k, (a, p, b, bp) in enumerate(edges_in):
        if k not in back and b != "end":
            preds[b].append(a)
    y: Dict[str, float] = {}
    for L in range(n_layers):
        row = [i for i in node_ids if layer[i] == L]
        want = {i: (sum(y[p] for p in preds[i]) / len(preds[i]) if preds[i] else 0.0) for i in row}
        row.sort(key=lambda i: (want[i], node_ids.index(i)))
        prev = None
        for i in row:
            yy = want[i] if prev is None else max(want[i], prev + LANE_DY)
            y[i] = yy
            prev = yy
        if len(row) > 1:                                    # centre the pushed-apart row on where it wanted to be
            shift = sum(want[i] - y[i] for i in row) / len(row)
            for i in row:
                y[i] += shift
    ys = [y[i] for i in node_ids] or [0.0]

    def xpos(L: int) -> float:
        return 1.0 + L * LAYER_DX

    nodes = [{"id": i, "type": by_id[i].get("type", ""), "members": members[i], "repeat": len(members[i]), "layer": layer[i],
              "x": xpos(layer[i]), "y": y[i]} for i in node_ids]
    node_by = {n["id"]: n for n in nodes}
    x_end = xpos(n_layers - 1) + LAYER_DX * 0.9
    x_in = xpos(0) - LAYER_DX * 0.75

    # ---- exits: spread the ports of one box over its right side -------------------------------------------------------------------
    out_ports: Dict[str, List[str]] = {}
    for a, p, b, bp in edges_in:
        out_ports.setdefault(a, [])
        if p not in out_ports[a]:
            out_ports[a].append(p)
    for v in g.get("virtualOutputs", []):
        i, _, p = v.partition("#")
        if rep_of.get(i, i) in node_by:
            out_ports.setdefault(rep_of.get(i, i), [])
            if p not in out_ports[rep_of.get(i, i)]:
                out_ports[rep_of.get(i, i)].append(p)
    in_ports: Dict[str, List[str]] = {}
    for a, p, b, bp in edges_in:
        if b != "end":
            in_ports.setdefault(b, [])
            if bp not in in_ports[b]:
                in_ports[b].append(bp)
    for v in g.get("virtualInputs", []):
        i, _, p = v.partition("#")
        if rep_of.get(i, i) in node_by:
            in_ports.setdefault(rep_of.get(i, i), [])
            if p not in in_ports[rep_of.get(i, i)]:
                in_ports[rep_of.get(i, i)].append(p)

    def port_y(n: dict, ports: Dict[str, List[str]], port: str) -> float:
        lst = ports.get(n["id"], [port])
        k = lst.index(port) if port in lst else 0
        return n["y"] + (k - (len(lst) - 1) / 2.0) * min(0.2, (BOX_H - 0.14) / max(1, len(lst) - 1 or 1))

    # ---- routes of the forward links -----------------------------------------------------------------------------------------------
    boxes_by_layer: Dict[int, List[float]] = {}
    for n in nodes:
        boxes_by_layer.setdefault(n["layer"], []).append(n["y"])
    chan_count: Dict[Tuple[str, int], int] = {}
    track_count: Dict[float, int] = {}

    def free_track(l_from: int, l_to: int, y_mid: float) -> float:
        """A horizontal track (between two rows of boxes) free of boxes in the layers l_from .. l_to - 1, nearest to ``y_mid``."""
        cands = sorted({round(yy + LANE_DY / 2, 6) for yy in ys} | {round(yy - LANE_DY / 2, 6) for yy in ys}, key=lambda c: (abs(c - y_mid), c))
        yr = None
        for c in cands:
            if all(abs(c - by) > BOX_H / 2 + 0.08 for L in range(l_from, l_to) for by in boxes_by_layer.get(L, [])):
                yr = c
                break
        if yr is None:
            yr = (max(ys) + LANE_DY / 2) if y_mid > 0 else (min(ys) - LANE_DY / 2)
        n_on = track_count.get(round(yr, 3), 0)
        track_count[round(yr, 3)] = n_on + 1
        return yr + 0.07 * n_on
    edges: List[dict] = []
    fwd = [(k, e) for k, e in enumerate(edges_in) if k not in back and e[2] != "end"]
    fwd.sort(key=lambda ke: (node_by[ke[1][0]]["layer"], node_by[ke[1][0]]["y"], node_by[ke[1][2]]["y"], ke[0]))
    for k, (a, p, b, bp) in fwd:
        na, nb = node_by[a], node_by[b]
        x0, y0 = na["x"] + BOX_W / 2, port_y(na, out_ports, p)
        x1, y1 = nb["x"] - BOX_W / 2, port_y(nb, in_ports, bp)
        j1 = chan_count.get(("out", na["layer"]), 0)
        chan_count[("out", na["layer"])] = j1 + 1
        cx = x0 + CHANNEL + CHANNEL_STEP * j1
        if nb["layer"] - na["layer"] == 1:
            cx2 = cx
            pts = [(x0, y0), (cx, y0), (cx, y1), (x1, y1)]
        else:
            j2 = chan_count.get(("in", nb["layer"]), 0)
            chan_count[("in", nb["layer"])] = j2 + 1
            cx2 = x1 - CHANNEL - CHANNEL_STEP * j2
            yr = free_track(na["layer"] + 1, nb["layer"], (y0 + y1) / 2)
            pts = [(x0, y0), (cx, y0), (cx, yr), (cx2, yr), (cx2, y1), (x1, y1)]
        edges.append({"from": a, "port": p, "to": b, "to_port": bp, "kind": "forward", "points": _simplify(pts), "label_at": (x0 + 0.05, y0 + 0.1)})

    # ---- loop-closing links: a U-shaped arc below the row --------------------------------------------------------------------------
    depth = 0
    for k, (a, p, b, bp) in enumerate(edges_in):
        if k not in back:
            continue
        na, nb = node_by[a], node_by[b]
        ylow = min(ys) - BOX_H / 2 - 0.55 - 0.45 * depth
        depth += 1
        edges.append({"from": a, "port": p, "to": b, "to_port": bp, "kind": "back",
                      "points": [(na["x"], na["y"] - BOX_H / 2), (na["x"], ylow), (nb["x"], ylow), (nb["x"], nb["y"] - BOX_H / 2)],
                      "label_at": ((na["x"] + nb["x"]) / 2, ylow - 0.12)})

    # ---- links to the end, virtual ports -------------------------------------------------------------------------------------------
    n_end = sum(1 for e in edges_in if e[2] == "end")
    for k, (a, p, b, bp) in enumerate(edges_in):
        if b != "end":
            continue
        na = node_by[a]
        y0 = port_y(na, out_ports, p)
        x0 = na["x"] + BOX_W / 2
        blocked = any(n["layer"] > na["layer"] and abs(n["y"] - y0) < BOX_H / 2 + 0.1 for n in nodes)
        if not blocked:
            pts = [(x0, y0), (x_end, y0)]
        else:                                               # a box stands between this exit and the output: go around it
            j1 = chan_count.get(("out", na["layer"]), 0)
            chan_count[("out", na["layer"])] = j1 + 1
            cx = x0 + CHANNEL + CHANNEL_STEP * j1
            yr = free_track(na["layer"] + 1, n_layers, y0)
            pts = [(x0, y0), (cx, y0), (cx, yr), (x_end, yr)]
        edges.append({"from": a, "port": p, "to": "end", "to_port": "", "kind": "end", "points": _simplify(pts),
                      "label_at": (x0 + 0.05, y0 + 0.1), "single": n_end == 1})
    for v in g.get("virtualInputs", []):
        i, _, p = v.partition("#")
        n = node_by.get(rep_of.get(i, i))
        if n:
            yy = port_y(n, in_ports, p)
            edges.append({"from": "", "port": p, "to": n["id"], "to_port": p, "kind": "virtual_in",
                          "points": [(n["x"] - BOX_W / 2 - 0.3, yy + 0.08), (n["x"] - BOX_W / 2, yy)], "label_at": (n["x"] - BOX_W / 2 - 0.3, yy + 0.18)})
    for v in g.get("virtualOutputs", []):
        i, _, p = v.partition("#")
        n = node_by.get(rep_of.get(i, i))
        if n:
            yy = port_y(n, out_ports, p)
            edges.append({"from": n["id"], "port": p, "to": "", "to_port": "", "kind": "virtual_out",
                          "points": [(n["x"] + BOX_W / 2, yy), (n["x"] + BOX_W / 2 + 0.3, yy - 0.08)], "label_at": (n["x"] + BOX_W / 2 + 0.3, yy - 0.28)})

    # ---- loop hulls ----------------------------------------------------------------------------------------------------------------
    loops = []
    for lp in g.get("loops", []) or []:
        ids = []
        for m in lp.get("members", []):
            r = rep_of.get(m, m)
            if r in node_by and r not in ids:
                ids.append(r)
        if ids:
            xs_ = [node_by[i]["x"] for i in ids]
            ys_ = [node_by[i]["y"] for i in ids]
            loops.append({"entry": rep_of.get(lp.get("entry"), lp.get("entry")), "kind": lp.get("kind", ""), "members": ids,
                          "box": (min(xs_) - BOX_W / 2 - 0.18, min(ys_) - BOX_H / 2 - 0.2, max(xs_) + BOX_W / 2 + 0.18, max(ys_) + BOX_H / 2 + 0.32)})
    y_lo = min(ys) - BOX_H / 2 - (0.55 + 0.45 * depth if depth else 0.0)
    return {"nodes": nodes, "edges": edges, "loops": loops, "layers": n_layers, "x_end": x_end, "x_input": x_in,
            "y_range": (y_lo, max(ys) + BOX_H / 2)}


def _simplify(pts: List[Tuple[float, float]]) -> List[Tuple[float, float]]:
    """Drop repeated points and the middle point of a straight run."""
    out: List[Tuple[float, float]] = []
    for p in pts:
        if out and abs(p[0] - out[-1][0]) < 1e-9 and abs(p[1] - out[-1][1]) < 1e-9:
            continue
        out.append(p)
    i = 1
    while i < len(out) - 1:
        a, b, c = out[i - 1], out[i], out[i + 1]
        if (abs(a[0] - b[0]) < 1e-9 and abs(b[0] - c[0]) < 1e-9) or (abs(a[1] - b[1]) < 1e-9 and abs(b[1] - c[1]) < 1e-9):
            out.pop(i)
        else:
            i += 1
    return out


def segments_cross_boxes(layout: GraphLayout) -> List[Tuple[str, str]]:
    """(edge, node) pairs where an orthogonal forward route passes through a box other than its own ends: empty for a good layout."""
    bad = []
    for e in layout["edges"]:
        if e["kind"] != "forward":
            continue
        for (xa, ya), (xb, yb) in zip(e["points"][:-1], e["points"][1:]):
            for n in layout["nodes"]:
                if n["id"] in (e["from"], e["to"]):
                    continue
                if min(xa, xb) < n["x"] + BOX_W / 2 and max(xa, xb) > n["x"] - BOX_W / 2 and min(ya, yb) < n["y"] + BOX_H / 2 and max(ya, yb) > n["y"] - BOX_H / 2:
                    bad.append((f"{e['from']}:{e['port']}->{e['to']}", n["id"]))
    return bad
