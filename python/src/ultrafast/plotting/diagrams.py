"""Schematic of a simulated system for the plots: elements in order, the beam line coloured by its carrier
wavelength, auxiliary beams coming in, by-products leaving, probes, and numbered instrument locations.

Every figure that shows an instrument refers to these numbers (``①  spectrometer · after compressor``), so a
plot can always be traced back to its place in the system. Used by :mod:`ultrafast.plotting`; needs matplotlib.
"""
from __future__ import annotations

import math
import re
from typing import Dict, List, Optional

import numpy as np

C = 299792458.0
_CIRCLED = "①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰⑱⑲⑳"

#: instrument kinds that read quantities a real device cannot measure directly
VIRTUAL_KINDS = {"wigner", "fieldProbe", "spaceTime", "pulseSummary", "conversion", "phaseMatchingCurve", "delayScan"}

_SHORT = {"spectrometer": "spectrum", "autocorrelator": "AC", "frog": "FROG", "dscan": "d-scan", "scope": "scope",
          "photodiode": "diode", "energyMeter": "energy", "powerMeter": "power", "contrast": "contrast",
          "polarimeter": "Stokes", "pulseSummary": "summary", "compressor": "compressor", "conversion": "conversion",
          "phaseMatchingCurve": "PM curve", "delayScan": "delay scan", "beamProfile": "profile",
          "nearFieldCamera": "near field", "camera": "camera", "farFieldCamera": "far field",
          "wavefrontSensor": "wavefront", "wigner": "Wigner", "fieldProbe": "field", "spaceTime": "I(r,t)"}


def circled(n: int) -> str:
    # the common plot fonts (DejaVu Sans) only have the circled digits 1-10
    return _CIRCLED[n - 1] if 1 <= n <= min(10, len(_CIRCLED)) else f"({n})"


def _el_index(els, at):
    for i, e in enumerate(els):
        if e["id"].lower() == str(at).lower():
            return i
    return None


def instrument_locations(summary: dict) -> List[dict]:
    """Instruments in system order (input first, then by element, end last), numbered from 1.
    Each entry: number, tag (①), name, kind, at, index (-1 input, element index, len for end), where, virtual."""
    els = summary.get("elements", [])
    out = []
    for k, ins in enumerate(summary.get("instruments", [])):
        at = ins.get("at", "end")
        if at == "input":
            idx = -1
        elif at == "end":
            idx = len(els)
        else:
            i = _el_index(els, at.split("#")[0])
            idx = i if i is not None else len(els)
        v = ins.get("values") or {}
        out.append(dict(name=ins["name"], kind=ins["kind"], at=at, index=idx, order=k,
                        virtual=bool(v.get("virtual")) or ins["kind"] in VIRTUAL_KINDS,
                        where=location_text(summary, at)))
    out.sort(key=lambda d: (d["index"], d["order"]))
    for n, d in enumerate(out, 1):
        d["number"], d["tag"] = n, circled(n)
    return out


def location_text(summary: dict, at: str) -> str:
    els = summary.get("elements", [])
    if at == "input":
        return "input"
    if at == "end":
        return f"end (after {els[-1]['id']})" if els else "end"
    base, _, field = str(at).partition("#")
    return f"after {base}" + (f" ({field})" if field else "")


def wavelength_color(lam: float) -> str:
    """Approximate perceived colour of a wavelength; IR fades from deep red to grey-brown, UV to violet-grey."""
    nm = lam * 1e9 if lam < 1e-3 else lam
    if not np.isfinite(nm):
        return "#888888"
    if nm < 380:
        t = min(1.0, (380 - nm) / 180)
        r, g, b = 0.45 * (1 - t) + 0.45 * t, 0.0 * (1 - t) + 0.40 * t, 0.65 * (1 - t) + 0.55 * t
    elif nm <= 780:
        if nm < 440: r, g, b = -(nm - 440) / 60, 0.0, 1.0
        elif nm < 490: r, g, b = 0.0, (nm - 440) / 50, 1.0
        elif nm < 510: r, g, b = 0.0, 1.0, -(nm - 510) / 20
        elif nm < 580: r, g, b = (nm - 510) / 70, 1.0, 0.0
        elif nm < 645: r, g, b = 1.0, -(nm - 645) / 65, 0.0
        else: r, g, b = 1.0, 0.0, 0.0
        f = 0.3 + 0.7 * (nm - 380) / 40 if nm < 420 else (0.3 + 0.7 * (780 - nm) / 80 if nm > 700 else 1.0)
        r, g, b = r * f + 0.12, g * f * 0.85 + 0.05, b * f + 0.05
        r, g, b = min(r, 1), min(g, 1), min(b, 1)
    else:
        t = min(1.0, (nm - 780) / 2500)
        r, g, b = 0.62 * (1 - t) + 0.50 * t, 0.12 * (1 - t) + 0.42 * t, 0.10 * (1 - t) + 0.38 * t
    return "#%02x%02x%02x" % (int(255 * r), int(255 * g), int(255 * b))


def _fmt_len(L: float) -> str:
    if not L or not np.isfinite(L):
        return ""
    if L >= 1:
        return f"{L:.3g} m"
    if L >= 1e-3:
        return f"{L * 1e3:.3g} mm"
    return f"{L * 1e6:.3g} µm"


def _stem(e) -> str:
    return e["type"] + ":" + re.sub(r"\d+", "#", e["id"])


def group_elements(els: List[dict], max_items: int = 22) -> List[dict]:
    """Collapse repeated units (e.g. 20 x [pass, mirror] in a multipass cell) into one item with a repeat count.
    Returns items {"start": i, "unit": L, "repeat": R}; plain elements have unit 1, repeat 1."""
    items, i, n = [], 0, len(els)
    stems = [_stem(e) for e in els]
    collapse = n > max_items
    while i < n:
        best = (1, 1)
        if collapse:
            for L in range(1, 5):
                R = 1
                while i + (R + 1) * L <= n and stems[i + R * L:i + (R + 1) * L] == stems[i:i + L]:
                    R += 1
                if R >= 3 and L * R > best[0] * best[1]:
                    best = (L, R)
        L, R = best
        items.append(dict(start=i, unit=L, repeat=R))
        i += L * R
    return items


def _carrier(summary, idx):
    """Carrier wavelength after element idx (-1 = input)."""
    els = summary["elements"]
    lam = summary["input"].get("centroidWavelengthM")
    for e in els[:idx + 1]:
        if e.get("carrierWavelengthM"):
            lam = e["carrierWavelengthM"]
    return lam


def draw_system(res, ax, theme: dict, compact: bool = False):
    """Draw the schematic of ``res`` (live Result or results folder) on ``ax``."""
    from matplotlib.patches import FancyBboxPatch, Rectangle, Ellipse, Polygon, Circle

    s = res.summary
    els = s.get("elements", [])
    items = group_elements(els)
    locs = instrument_locations(s)
    probes = set(s.get("probes", []))
    show_probes = 0 < len(probes) < len(els)

    # slots: input marker at 0, items 1..K, output at K+1
    K = len(items)
    x_of_item = {k: k + 1 for k in range(K)}
    item_of_el = {}
    for k, it in enumerate(items):
        for j in range(it["start"], it["start"] + it["unit"] * it["repeat"]):
            item_of_el[j] = k
    xin, xout = 0.0, K + 1.0
    ax.set_xlim(-0.8, K + 1.8)
    ax.set_ylim(-2.25 if not compact else -1.6, 2.5 if not compact else 1.9)
    ax.set_axis_off()

    # beam line, coloured per segment by the carrier after the element on its left
    seg_x = [xin] + [x_of_item[k] for k in range(K)] + [xout]
    lams = []
    for k in range(len(seg_x) - 1):
        last_el = -1 if k == 0 else items[k - 1]["start"] + items[k - 1]["unit"] * items[k - 1]["repeat"] - 1
        lams.append(_carrier(s, last_el))
    for k, lam in enumerate(lams):
        ax.plot([seg_x[k], seg_x[k + 1]], [0, 0], color=wavelength_color(lam), lw=3.2, solid_capstyle="butt", zorder=1)
        if lam and (k == 0 or abs(lam - lams[k - 1]) > 1e-3 * lam):
            ax.text((seg_x[k] + seg_x[k + 1]) / 2, -0.2, f"{lam * 1e9:.0f} nm", ha="center", va="top", fontsize=6.3,
                    color=wavelength_color(lam), fontweight="bold")

    # input and output markers
    inp = s["input"]
    ax.add_patch(Polygon([[xin - 0.35, -0.22], [xin - 0.35, 0.22], [xin - 0.05, 0]], closed=True, color=theme["text2"], zorder=3))
    ax.text(xin - 0.2, -0.45, "input", ha="center", va="top", fontsize=7.5, color=theme["text"], fontweight="bold")
    ax.text(xin - 0.2, -0.78, _pulse_txt(inp), ha="center", va="top", fontsize=6.3, color=theme["text2"])
    out = s["output"]
    ax.add_patch(Polygon([[xout + 0.05, -0.22], [xout + 0.05, 0.22], [xout + 0.35, 0]], closed=True, color=theme["text2"], zorder=3))
    ax.text(xout + 0.2, -0.45, "output", ha="center", va="top", fontsize=7.5, color=theme["text"], fontweight="bold")
    ax.text(xout + 0.2, -0.78, _pulse_txt(out), ha="center", va="top", fontsize=6.3, color=theme["text2"])

    # elements
    rot = 0 if K <= 9 else 35
    for k, it in enumerate(items):
        x = x_of_item[k]
        unit = els[it["start"]:it["start"] + it["unit"]]
        w = 0.62 / max(1, len(unit))
        for u, e in enumerate(unit):
            xu = x + (u - (len(unit) - 1) / 2) * (w + 0.06)
            _glyph(ax, e, xu, w, theme)
        first = els[it["start"]]
        if it["repeat"] > 1:
            lastid = els[it["start"] + it["unit"] * it["repeat"] - 1]["id"]
            label = f"{first['id']} … {lastid}"
            ax.text(x, 0.55, f"× {it['repeat']}", ha="center", va="bottom", fontsize=8, fontweight="bold", color=theme["text"])
            ax.plot([x - 0.4, x - 0.4, x + 0.4, x + 0.4], [0.42, 0.5, 0.5, 0.42], color=theme["text2"], lw=0.8)
            length = sum(e.get("lengthM") or 0 for e in els[it["start"]:it["start"] + it["unit"] * it["repeat"]])
        else:
            label = " + ".join(e["id"] for e in unit) if len(unit) > 1 else first["id"]
            length = sum(e.get("lengthM") or 0 for e in unit)
        sub = _fmt_len(length)
        extra = [e.get("representation", "") for e in unit if "radial" in (e.get("representation") or "") or "cartesian" in (e.get("representation") or "")]
        if extra:
            sub = (sub + " · " if sub else "") + ("radial" if "radial" in extra[0] else "x-y")
        ax.text(x, -0.42, label, ha="right" if rot else "center", va="top", fontsize=7 if K < 14 else 6, rotation=rot,
                rotation_mode="anchor", color=theme["text"])
        if sub and not compact:
            ax.text(x, -0.42 - (0.32 if not rot else 0.55), sub, ha="center", va="top", fontsize=6, color=theme["text2"])
        # aux beams in, by-products out
        for e in unit if it["repeat"] == 1 else []:
            aux = e.get("aux") or {}
            for j, (field, desc) in enumerate(aux.items()):
                m = re.search(r"@\s*([\d.]+)\s*nm", desc)
                lamA = float(m.group(1)) * 1e-9 if m else None
                src = "saved run" if "from" in desc else ""
                xa = x - 0.12 + 0.24 * j
                ax.annotate("", xy=(xa, 0.3), xytext=(xa - 0.25, 1.05),
                            arrowprops=dict(arrowstyle="-|>", lw=1.6, color=wavelength_color(lamA) if lamA else theme["text2"]))
                ax.text(xa - 0.27, 1.1, f"{field}" + (f" {lamA * 1e9:.0f} nm" if lamA else "") + (f"\n({src})" if src else ""),
                        ha="center", va="bottom", fontsize=6.5, color=theme["text2"])
            for j, (field, ps) in enumerate((e.get("byproducts") or {}).items()):
                xb = x + 0.1 + 0.22 * j
                lamB = ps.get("centroidWavelengthM")
                ax.annotate("", xy=(xb + 0.3, -1.45 if not compact else -1.1), xytext=(xb, -0.3),
                            arrowprops=dict(arrowstyle="-|>", lw=1.3, ls="--", color=wavelength_color(lamB) if lamB else theme["text2"]))
                if not compact:
                    ax.text(xb + 0.32, -1.5, f"{field}\n{_e_txt(ps.get('energyJ'))}", ha="left", va="top", fontsize=6.2, color=theme["text2"])
        if show_probes and any(e["id"] in probes for e in unit) and it["repeat"] == 1:
            ax.plot([x + 0.42], [0], marker="D", ms=4, color=theme["text2"], zorder=4)

    # instruments: numbered tags above the line, stacked per location
    by_slot: Dict[float, List[dict]] = {}
    for d in locs:
        if d["index"] < 0:
            xs = xin + 0.25
        elif d["index"] >= len(els):
            xs = xout - 0.25
        else:
            k = item_of_el.get(d["index"], K - 1)
            xs = x_of_item[k] + 0.46
        by_slot.setdefault(xs, []).append(d)
    step = 0.36 if not compact else 0.3
    top = 0.8 + step * (max((len(v) for v in by_slot.values()), default=0))
    ax.set_ylim(ax.get_ylim()[0], max(ax.get_ylim()[1], top + 0.25))
    for xs, lst in by_slot.items():
        ax.plot([xs, xs], [0.05, 0.62], color=theme["text2"], lw=0.7, zorder=2)
        for j, d in enumerate(lst):
            y = 0.8 + j * step
            ax.plot([xs], [y], marker="s" if d["virtual"] else "o", ms=11, mfc=theme["surface"], mew=1.4,
                    mec=theme["series"][6 if d["virtual"] else 0], zorder=5)
            ax.text(xs, y, str(d["number"]), ha="center", va="center", fontsize=6.5, fontweight="bold", color=theme["text"], zorder=6)
            ax.text(xs + 0.17, y, _SHORT.get(d["kind"], d["kind"]), ha="left", va="center", fontsize=6.3,
                    color=theme["text2"], style="italic" if d["virtual"] else "normal", zorder=6)
    if locs and not compact:
        ax.text(K + 1.8, ax.get_ylim()[1], "○ instrument   □ virtual instrument" + ("   ◆ probe" if show_probes else ""),
                ha="right", va="top", fontsize=6.5, color=theme["text2"])
    return locs


def _graph_layout(summary: dict):
    """Layers and lanes of a branched system (see :mod:`ultrafast.plotting.graph_layout`), one node per element.
    Returns (ids, edges, layer, lane): ``edges`` are ``(from id, port, to id)`` of the links, ``layer`` the longest path from the input,
    ``lane`` the vertical slot (in units of the lane spacing)."""
    from .graph_layout import LANE_DY, layout_graph
    lay = layout_graph(summary, group=False)
    ids = [n["id"] for n in lay["nodes"]]
    edges = [(e["from"], e["port"], e["to"]) for e in lay["edges"] if e["kind"] in ("forward", "back", "end")]
    return ids, edges, {n["id"]: n["layer"] for n in lay["nodes"]}, {n["id"]: -n["y"] / LANE_DY for n in lay["nodes"]}


def _param_lines(e: dict) -> str:
    """The numbers worth showing under a box with ``params=True``: length, and the element's own few key values."""
    parts = []
    if e.get("lengthM"):
        parts.append(_fmt_len(e["lengthM"]))
    v = e.get("values") or {}
    for key, fmt_ in (("reflectivity", "R={:.3g}"), ("delayS", None), ("roundTrips", "{:.0f} trips"), ("passes", "{:.0f} passes")):
        if isinstance(v.get(key), (int, float)):
            parts.append(_t_txt(v[key]) if fmt_ is None else fmt_.format(v[key]))
    return " · ".join(parts)


def draw_graph(res, ax, theme: dict, compact: bool = False, params: bool = False, group: bool = True):
    """Draw a Branched system as a graph on ``ax``: elements as boxes by layer (a run of identical elements as one box with xN), links
    routed orthogonally around the boxes and coloured by the carrier after their source, loop-closing links as arcs below the row
    inside a shaded loop region, virtual ports as open circles, aux beams in and by-products out as arrows, numbered instrument tags
    above the boxes. ``params=True`` adds the key numbers of each element under its box. The layout is deterministic
    (:func:`ultrafast.plotting.graph_layout.layout_graph`). Returns the instrument locations."""
    from matplotlib.patches import FancyBboxPatch, Rectangle
    from matplotlib.path import Path as MPath
    from matplotlib.patches import PathPatch
    from .graph_layout import BOX_H, BOX_W, layout_graph

    s = res.summary
    els = {e["id"]: e for e in s.get("elements", [])}
    lay = layout_graph(s, group=group)
    nodes = {n["id"]: n for n in lay["nodes"]}
    idx = {e["id"]: e["index"] for e in s.get("elements", [])}
    locs = instrument_locations(s)
    y_lo, y_hi = lay["y_range"]
    ax.set_xlim(lay["x_input"] - 0.55, lay["x_end"] + 1.3)
    tags_top = y_hi + 0.5 + 0.34 * max(1, max([sum(1 for d in locs if _loc_node(d, s, nodes) == n) for n in nodes] + [1]))
    ax.set_ylim(y_lo - 0.9, tags_top + 0.35)
    ax.set_axis_off()

    def colour_after(i: str) -> str:
        last = max(idx[m] for m in nodes[i]["members"]) if i in nodes else -1
        return wavelength_color(_carrier(s, last))

    # loop regions first (behind everything)
    for lp in lay["loops"]:
        x0, y0, x1, y1 = lp["box"]
        ax.add_patch(FancyBboxPatch((x0, y0), x1 - x0, y1 - y0, boxstyle="round,pad=0.0,rounding_size=0.15", fc=theme["grid"], ec=theme["ref"],
                                    lw=0.8, ls="--", alpha=0.45, zorder=0))
        ax.text(x0 + 0.08, y1 - 0.05, f"loop: {lp['kind']}" if lp["kind"] else "loop", fontsize=6.5, color=theme["text2"], va="top", style="italic")

    # links
    first = lay["nodes"][0] if lay["nodes"] else None
    for e in lay["edges"]:
        kind = e["kind"]
        pts = e["points"]
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        if kind == "back":
            path = MPath([pts[0], pts[1], pts[2], pts[3]], [MPath.MOVETO, MPath.CURVE4, MPath.CURVE4, MPath.CURVE4])
            ax.add_patch(PathPatch(path, fc="none", ec=colour_after(e["from"]), lw=2.0, ls=(0, (4, 2)), zorder=1))
            ax.annotate("", xy=pts[3], xytext=(pts[3][0], pts[3][1] - 0.25), arrowprops=dict(arrowstyle="-|>", lw=0, color=colour_after(e["from"]), mutation_scale=9), zorder=2)
            if not compact:
                ax.text(e["label_at"][0], e["label_at"][1], f"{e['port']} → {e['to']}" if e["port"] else "back", fontsize=6.5, ha="center", va="top", color=theme["text2"])
            continue
        if kind in ("virtual_in", "virtual_out"):
            ax.plot(xs, ys, color=theme["text2"], lw=1, ls=":", zorder=2)
            far = pts[0] if kind == "virtual_in" else pts[1]
            ax.plot([far[0]], [far[1]], marker="o", mfc=theme["surface"], mec=theme["text2"], ms=5, zorder=3)
            if not compact and e["port"]:
                ax.text(e["label_at"][0], e["label_at"][1], e["port"], fontsize=6, ha="center", color=theme["text2"])
            continue
        col = colour_after(e["from"])
        ax.plot(xs, ys, color=col, lw=2.4, zorder=1, solid_capstyle="round", solid_joinstyle="round")
        if kind == "forward":
            ax.plot([xs[-1] - 0.02], [ys[-1]], marker=">", ms=4.5, color=col, zorder=2)
        if kind == "end":
            ax.plot([xs[-1]], [ys[-1]], marker=">", ms=5, color=col, zorder=2)
            n_out = "output" + ("\n%.3g nJ" % (s.get("graph", {}).get("energyOutJ", 0) * 1e9) if e.get("single") else "")
            ax.text(xs[-1] + 0.1, ys[-1], n_out, fontsize=8, va="center", color=theme["text"])
        multi = sum(1 for o in lay["edges"] if o["from"] == e["from"] and o["kind"] in ("forward", "end", "back")) > 1
        if not compact and e["port"] and (multi or e["port"] not in ("out", "in")):
            ax.text(e["label_at"][0], e["label_at"][1], e["port"], fontsize=6.5, color=theme["text2"], zorder=5)
    if first:
        y0 = first["y"]
        ax.plot([lay["x_input"] + 0.25, first["x"] - BOX_W / 2], [y0, y0], color=wavelength_color(s["input"].get("centroidWavelengthM")), lw=2.4, zorder=1)
        ax.text(lay["x_input"], y0, "input", fontsize=8, ha="center", va="center", color=theme["text"])

    # boxes, group marks, parameters, aux in and by-products out
    for n in lay["nodes"]:
        x, y, i = n["x"], n["y"], n["id"]
        e = els[i]
        ax.add_patch(FancyBboxPatch((x - BOX_W / 2, y - BOX_H / 2), BOX_W, BOX_H, boxstyle="round,pad=0.02,rounding_size=0.08",
                                    fc=theme.get("panel", "white"), ec=theme["text2"], lw=1.1, zorder=3))
        label = i if n["repeat"] == 1 else f"{i} … {n['members'][-1]}"
        ax.text(x, y + 0.11, label, ha="center", va="center", fontsize=8.5 if n["repeat"] == 1 else 7, fontweight="bold", color=theme["text"], zorder=4)
        ax.text(x, y - 0.14, e["type"] if n["repeat"] == 1 else f"{e['type']} × {n['repeat']}", ha="center", va="center", fontsize=7, color=theme["text2"], zorder=4)
        if n["repeat"] > 1:
            ax.text(x, y + BOX_H / 2 + 0.07, f"× {n['repeat']}", ha="center", va="bottom", fontsize=7.5, fontweight="bold", color=theme["text"], zorder=4)
        if not compact:
            sub = _param_lines(e) if params else (_fmt_len(sum(els[m].get("lengthM") or 0 for m in n["members"])) if any(els[m].get("lengthM") for m in n["members"]) else "")
            if sub:
                ax.text(x, y - BOX_H / 2 - 0.1, sub, fontsize=6.5, ha="center", va="top", color=theme["text2"])
            if n["repeat"] == 1:
                for j, (field, desc) in enumerate((e.get("aux") or {}).items()):
                    m = re.search(r"@\s*([\d.]+)\s*nm", desc)
                    lam_a = float(m.group(1)) * 1e-9 if m else None
                    xa = x - 0.2 + 0.4 * j
                    ax.annotate("", xy=(xa, y + BOX_H / 2), xytext=(xa, y + BOX_H / 2 + 0.38),
                                arrowprops=dict(arrowstyle="-|>", lw=1.4, color=wavelength_color(lam_a) if lam_a else theme["text2"]), zorder=2)
                    ax.text(xa, y + BOX_H / 2 + 0.4, field + (f" {lam_a * 1e9:.0f} nm" if lam_a else ""), ha="center", va="bottom", fontsize=6, color=theme["text2"])
                for j, (field, ps) in enumerate((e.get("byproducts") or {}).items()):
                    xb = x - 0.2 + 0.4 * j
                    lam_b = ps.get("centroidWavelengthM")
                    ax.annotate("", xy=(xb, y - BOX_H / 2 - 0.55), xytext=(xb, y - BOX_H / 2),
                                arrowprops=dict(arrowstyle="-|>", lw=1.2, ls="--", color=wavelength_color(lam_b) if lam_b else theme["text2"]), zorder=2)
                    ax.text(xb, y - BOX_H / 2 - 0.58, f"{field} {_e_txt(ps.get('energyJ'))}", ha="center", va="top", fontsize=6, color=theme["text2"])

    # instrument tags above the boxes (input, output and elements)
    slots: Dict[str, List[dict]] = {}
    for d in locs:
        slots.setdefault(_loc_node(d, s, nodes), []).append(d)
    for key, lst in slots.items():
        if key == "<input>":
            xs_, y_top = lay["x_input"] + 0.25, (first["y"] if first else 0.0) + 0.1
        elif key == "<output>":
            xs_, y_top = lay["x_end"] - 0.3, (first["y"] if first else 0.0) + 0.1
        else:
            xs_, y_top = nodes[key]["x"] + BOX_W / 2 - 0.12, nodes[key]["y"] + BOX_H / 2 + 0.12
        for j, d in enumerate(lst):
            yy = y_top + 0.3 + j * 0.34
            ax.plot([xs_, xs_], [y_top, yy - 0.1], color=theme["text2"], lw=0.6, zorder=2)
            ax.plot([xs_], [yy], marker="s" if d["virtual"] else "o", ms=11, mfc=theme["surface"], mew=1.4, mec=theme["series"][6 if d["virtual"] else 0], zorder=5)
            ax.text(xs_, yy, str(d["number"]), ha="center", va="center", fontsize=6.5, fontweight="bold", color=theme["text"], zorder=6)
            ax.text(xs_ + 0.17, yy, _SHORT.get(d["kind"], d["kind"]), ha="left", va="center", fontsize=6.3, color=theme["text2"],
                    style="italic" if d["virtual"] else "normal", zorder=6)
    if locs and not compact:
        ax.text(lay["x_end"] + 1.3, ax.get_ylim()[1], "○ instrument   □ virtual instrument", ha="right", va="top", fontsize=6.5, color=theme["text2"])
    return locs


def _loc_node(d: dict, summary: dict, nodes: dict) -> str:
    """The graph node an instrument location belongs to (``<input>`` / ``<output>`` for the ends)."""
    els = summary.get("elements", [])
    if d["index"] < 0:
        return "<input>"
    if d["index"] >= len(els):
        return "<output>"
    eid = els[d["index"]]["id"]
    for n in nodes.values():
        if eid in n["members"]:
            return n["id"]
    return "<output>"


def _pulse_txt(p):
    e = p.get("energyJ"); f = p.get("fwhmS")
    parts = []
    if e:
        parts.append(_e_txt(e))
    if f and np.isfinite(f):
        parts.append(_t_txt(f))
    return "\n".join(parts)


def _e_txt(e):
    if not e:
        return ""
    for s, p in ((1, "J"), (1e-3, "mJ"), (1e-6, "µJ"), (1e-9, "nJ"), (1e-12, "pJ")):
        if e >= 0.9995 * s:
            return f"{e / s:.3g} {p}"
    return f"{e:.2g} J"


def _t_txt(t):
    for s, p in ((1e-9, "ns"), (1e-12, "ps"), (1e-15, "fs")):
        if t >= 0.9995 * s:
            return f"{t / s:.3g} {p}"
    return f"{t:.2g} s"


def _glyph(ax, e, x, w, theme):
    """One element's symbol, centred at (x, 0)."""
    from matplotlib.patches import FancyBboxPatch, Rectangle, Ellipse, Polygon
    t = e["type"].lower()
    edge, fill = theme["text2"], theme["grid"]
    h = 0.5
    if t in ("bulk",):
        spatial = any(k in (e.get("representation") or "") for k in ("radial", "cartesian"))
        ax.add_patch(Rectangle((x - w / 2, -h / 2), w, h, facecolor="#d7e7f7" if not spatial else "#cfe9de", edgecolor=edge, lw=0.8, zorder=3))
    elif t == "crystal":
        ax.add_patch(Rectangle((x - w / 2, -h / 2), w, h, facecolor="#f3e2c6", edgecolor=edge, lw=0.9, hatch="///", zorder=3))
        ax.text(x, 0, "χ²", ha="center", va="center", fontsize=7, fontweight="bold", color=theme["text"], zorder=4)
    elif t == "gain":
        spatial = any(k in (e.get("representation") or "") for k in ("radial", "cartesian"))
        ax.add_patch(Rectangle((x - w / 2, -h / 2), w, h, facecolor="#f6d2d8" if not spatial else "#f1c6e3", edgecolor=edge, lw=0.9, zorder=3))
        ax.text(x, 0, "gain", ha="center", va="center", fontsize=6.5, fontweight="bold", color=theme["text"], zorder=4)
        if (e.get("values") or {}).get("absorbedPumpEnergyJ"):
            ax.annotate("", xy=(x, h / 2), xytext=(x, h / 2 + 0.28), arrowprops=dict(arrowstyle="-|>", color="#3a9a4a", lw=1.2), zorder=4)
    elif t == "amplifier":
        ax.add_patch(FancyBboxPatch((x - w / 2, -h / 2), w, h, boxstyle="round,pad=0.02,rounding_size=0.1", facecolor="#f6d2d8", edgecolor=edge, lw=1.0, zorder=3))
        v = e.get("values") or {}
        n = v.get("roundTrips") or v.get("passes")
        lab = ("regen" if v.get("roundTrips") else "amp") + (f"\n×{int(n)}" if n else "")
        ax.text(x, 0, lab, ha="center", va="center", fontsize=6, fontweight="bold", color=theme["text"], zorder=4, linespacing=0.9)
        if v.get("absorbedPumpEnergyJ"):
            ax.annotate("", xy=(x, h / 2), xytext=(x, h / 2 + 0.28), arrowprops=dict(arrowstyle="-|>", color="#3a9a4a", lw=1.2), zorder=4)
    elif t == "oscillator":
        from matplotlib.patches import FancyBboxPatch as _FB
        ax.add_patch(_FB((x - w / 2, -h / 2), w, h, boxstyle="round,pad=0.02,rounding_size=0.12", facecolor="#fde7b5", edgecolor=edge, lw=1.0, zorder=3))
        ax.text(x, 0.06, "laser", ha="center", va="center", fontsize=6.5, fontweight="bold", color=theme["text"], zorder=4)
        ax.text(x, -0.13, (e.get("values") or {}).get("model", "") or "", ha="center", va="center", fontsize=5, color=theme["text2"], zorder=4)
    elif t in ("saturableabsorber", "sesam"):
        ax.plot([x, x], [-0.3, 0.3], color="#5b3a8c", lw=3, solid_capstyle="round", zorder=3)
        ax.text(x, 0.36, "SA", ha="center", va="bottom", fontsize=5.5, color=theme["text2"])
    elif t == "gap":
        ax.plot([x - w / 2, x + w / 2], [0.12, 0.12], color=edge, lw=0.6, ls=":", zorder=3)
        ax.plot([x - w / 2, x + w / 2], [-0.12, -0.12], color=edge, lw=0.6, ls=":", zorder=3)
    elif t in ("mirror",):
        ax.plot([x - 0.12, x + 0.12], [-0.28, 0.28], color=edge, lw=3, solid_capstyle="round", zorder=3)
    elif t in ("interface",):
        ax.plot([x, x], [-0.3, 0.3], color=edge, lw=1.4, zorder=3)
    elif t in ("grating", "gratingpair"):
        ax.add_patch(Rectangle((x - w / 2, -0.3), w * 0.25, 0.6, facecolor=fill, edgecolor=edge, lw=0.8, zorder=3))
        for yy in np.linspace(-0.27, 0.27, 7):
            ax.plot([x - w / 2 + w * 0.25, x - w / 2 + w * 0.35], [yy, yy], color=edge, lw=0.6, zorder=3)
        if t == "gratingpair":
            ax.add_patch(Rectangle((x + w / 2 - w * 0.25, -0.3), w * 0.25, 0.6, facecolor=fill, edgecolor=edge, lw=0.8, zorder=3))
    elif t == "chirpedmirror":
        zz = np.linspace(-0.28, 0.28, 9)
        ax.plot(x + 0.06 * np.where(np.arange(9) % 2 == 0, 1, -1), zz, color=edge, lw=1.4, zorder=3)
    elif t == "lens":
        ax.add_patch(Ellipse((x, 0), 0.14, 0.62, facecolor="#d7e7f7", edgecolor=edge, lw=0.9, zorder=3))
    elif t == "aperture":
        ax.plot([x, x], [0.12, 0.35], color=edge, lw=2.5, zorder=3); ax.plot([x, x], [-0.35, -0.12], color=edge, lw=2.5, zorder=3)
    elif t == "probe":
        ax.plot([x], [0], marker="D", ms=5, color=theme["text2"], zorder=4)
    elif t == "user":
        ax.add_patch(FancyBboxPatch((x - w / 2, -h / 2), w, h, boxstyle="round,pad=0.02,rounding_size=0.08", facecolor="#ece4f7", edgecolor=edge, lw=0.8, zorder=3))
        ax.text(x, 0, "f(ω)", ha="center", va="center", fontsize=6.5, color=theme["text"], zorder=4)
    elif t in ("waveplate", "polarizer", "rotator"):
        ax.add_patch(Rectangle((x - 0.05, -0.3), 0.1, 0.6, facecolor="#e9e3cf", edgecolor=edge, lw=0.8, zorder=3))
        ax.text(x, 0.36, {"waveplate": "λ-plate", "polarizer": "pol", "rotator": "rot"}[t], ha="center", va="bottom", fontsize=5.5, color=theme["text2"])
    else:  # dispersion, filter, custom, ...
        ax.add_patch(Rectangle((x - w / 2, -h / 2), w, h, facecolor=fill, edgecolor=edge, lw=0.8, zorder=3))
        ax.text(x, 0, t[:6], ha="center", va="center", fontsize=5.5, color=theme["text2"], zorder=4)


# ----------------------------------------------------------------------------------------------- figures
from .data import load
from .style import _Style, _figure, _plt, _theme


def system_diagram(res, ax=None, compact: bool = False, params: bool = False):
    """Schematic of the system on ``ax``: elements, beam line coloured by carrier wavelength, aux beams in,
    by-products out, probes and numbered instrument locations (the numbers used in every instrument title). A branched system is drawn as
    a graph (``params=True`` adds the key numbers of each element)."""
    _, plt = _plt()
    res = load(res)
    with _Style():
        ax = ax or plt.gca()
        if res.summary.get("graph", {}).get("topology") == "branched" and res.summary.get("graph", {}).get("links"):
            draw_graph(res, ax, _theme, compact=compact, params=params)
        else:
            draw_system(res, ax, _theme, compact=compact)
        if compact:
            ax.set_title("System")
    return ax


def system(res, params: bool = False):
    """Stand-alone system schematic with a numbered list of the instruments and where they sit (``params=True``: key numbers of each
    element under its box in a branched system)."""
    res = load(res)
    locs = instrument_locations(res.summary)
    n = len(locs)
    rows = max(1, (n + 2) // 3)
    height = 4.2 + 0.25 * rows
    if res.summary.get("graph", {}).get("topology") == "branched" and res.summary.get("graph", {}).get("links"):
        from .graph_layout import layout_graph
        y0, y1 = layout_graph(res.summary)["y_range"]
        height = max(height, 2.6 + 0.62 * (y1 - y0) + 0.25 * rows)
    fig = _figure(15, height)
    with _Style():
        gs = fig.add_gridspec(2, 1, height_ratios=[3.4, 0.35 + 0.25 * rows])
        system_diagram(res, fig.add_subplot(gs[0]), params=params)
        ax = fig.add_subplot(gs[1]); ax.set_axis_off()
        cols = 3
        for k, d in enumerate(locs):
            c, r = k % cols, k // cols
            txt = f"{d['tag']}  {d['name'].replace('_', ' ')}  ({d['kind']}{', virtual' if d['virtual'] else ''}) · {d['where']}"
            ax.text(c / cols, 1 - (r + 0.5) / rows, txt, transform=ax.transAxes,
                    fontsize=7.5, va="center", color=_theme["text2"] if d["virtual"] else _theme["text"])
        if not locs:
            ax.text(0, 0.5, "no instruments in this run", transform=ax.transAxes, fontsize=8, color=_theme["text2"])
        fig.suptitle(f"System · {res.summary.get('system', '')}", x=0.01, ha="left", fontsize=12,
                     fontweight="bold", color=_theme["text"])
    return fig

