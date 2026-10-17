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
