"""Draw branched systems with ultrafast.plotting in every plot style: schematic, parameters, column width, dark surface."""
import sys
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[3] / "python" / "src"))
import ultrafast as uf
from ultrafast import build as b
from ultrafast import plotting as up

LAM, TAU = 1030e-9, 300e-15

# 1. a Mach-Zehnder (two arms, one 300 fs longer) and 2. a ring-down etalon (a loop): the two graph shapes the layout has to draw
mz = b.system("mach-zehnder", b.Branched([
    b.link(b.beam_splitter("bs1"), {"c": "armA", "d": "armB"}),
    b.link(b.delay("armA", delay_s=0.0), "bs2:1"),
    b.link(b.delay("armB", delay_s=TAU), "bs2:2"),
    b.link(b.beam_splitter("bs2"), {"c": "end", "d": "virtual"}),
]))
etalon = b.system("etalon", b.Branched([
    b.link(b.beam_splitter("m1", 0.8), {"c": "forth", "d": "virtual"}, loop=b.loop("ringDown", max_round_trips=200, tolerance=1e-12)),
    b.link(b.gap("forth", 0.01, medium="Vacuum"), "m2:2"),
    b.link(b.beam_splitter("m2", 0.8), {"c": "back", "d": "end"}),
    b.link(b.gap("back", 0.01, medium="Vacuum"), "m1:2"),
]))
pulse = b.run_spec(b.pulse(LAM, 100e-15, energy=1e-9), grid=b.grid(n=4096, window_s=20e-12))
cw = b.run_spec(b.cw(LAM, 1.0))
runs = {"mach-zehnder": uf.run(mz, pulse), "etalon": uf.run(etalon, cw)}

out = HERE / "styles"
out.mkdir(exist_ok=True)

# the same result drawn in each style; the style is chosen with use_style (call, context manager or ULTRAFAST_PLOT_STYLE)
import warnings
styles = []
for style in ("ultrafast", "ultrafast-dark", "paper", "science"):
    with warnings.catch_warnings(record=True) as caught, up.use_style(style):
        warnings.simplefilter("always")
        if up.current_style() != style:                              # "science" falls back to "paper" without SciencePlots
            print("style %s not available (SciencePlots missing): skipped" % style)
            continue
        fig = up.system(runs["mach-zehnder"], params=True)           # params=True: key numbers under each box
        fig.savefig(out / f"mach-zehnder-{style}.png", dpi=up.save_dpi(fig, 110))
        plt.close(fig)
        styles.append(style)
        print("mach-zehnder drawn in style", up.current_style())

# a loop is a dashed arc inside a labelled region; the compact form is meant for a corner of a larger figure
with up.use_style("ultrafast"):
    fig = up.system(runs["etalon"])
    fig.savefig(out / "etalon-ultrafast.png", dpi=110)
    plt.close(fig)
    fig, ax = plt.subplots(figsize=(6, 2.2))
    up.system_diagram(runs["etalon"], ax, compact=True)
    fig.savefig(out / "etalon-compact.png", dpi=110)
    plt.close(fig)

# a paper figure at single-column width (3.4 in): two systems stacked
with up.use_style("paper", column="single"):
    fig, axes = plt.subplots(2, 1, figsize=(3.4, 4.4))
    for ax, name in zip(axes, runs):
        up.system_diagram(runs[name], ax, compact=True)
    fig.savefig(out / "paper-single-column.png", dpi=up.save_dpi(fig))
    plt.close(fig)

# hero: the styles side by side (a figure of images, saved in the default style)
import matplotlib.image as mpimg
fig, axes = plt.subplots(len(styles), 1, figsize=(10, 2.3 * len(styles)), constrained_layout=True)
for ax, style in zip(axes, styles):
    ax.imshow(mpimg.imread(out / f"mach-zehnder-{style}.png"))
    ax.set_axis_off()
    ax.set_title("use_style(%r)" % style, loc="left", fontsize=9)
fig.savefig(HERE / "hero.png", dpi=80)
plt.close(fig)

# the schematic is deterministic: the same system gives the same layout
from ultrafast.plotting.graph_layout import layout_graph
a, c = layout_graph(runs["mach-zehnder"].summary), layout_graph(runs["mach-zehnder"].summary)
print("layout deterministic:", a == c, " nodes:", len(a["nodes"]) if "nodes" in a else "?")
print("styles:", ", ".join(sorted(p.name for p in out.glob("*.png"))))
