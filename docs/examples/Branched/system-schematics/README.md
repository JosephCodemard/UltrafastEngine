# Drawing branched systems: schematics and plot styles

ultrafast.plotting draws a Mach-Zehnder and a resonator with a loop as deterministic schematics in four plot styles: default, dark, paper and science.

**The story.** A branched system is a graph: beam splitters send light into arms, arms rejoin, a loop feeds a mirror back on itself. A line of boxes
cannot show that, so `up.system(result)` lays the graph out in layers, routes every link in orthogonal channels, draws loops as a dashed arc inside a
labelled region, and writes the energy that leaves at each port. The layout is code, not a hand drawing, so the same system always gives the same picture
(decision V2). This example runs two small systems whose answers are known (the [interferometers](../interferometers/README.md) example checks their
physics) and draws them in every style.

![the Mach-Zehnder in each style](hero.png)

**Layout** (`schematics.py`): a Mach-Zehnder (`bs1`, arms `armA` / `armB` with 0 and 300 fs delay, `bs2`) run with a 100 fs, 1 nJ pulse at 1030 nm, and a
ring-down etalon (two 80 % mirrors 10 mm apart joined by a loop) run with a cw source. About 5 s.

```bash
python schematics.py            # writes figures/*.png and hero.png
```

```
mach-zehnder drawn in style ultrafast
mach-zehnder drawn in style ultrafast-dark
mach-zehnder drawn in style paper
style science not available (SciencePlots missing): skipped
layout deterministic: True  nodes: 4
```

With SciencePlots installed (`pip install ultrafast-engine[paper]`) the fourth line becomes `mach-zehnder drawn in style science`.

**What it shows.**

* **Choosing a style.** `up.use_style("paper")` stays in force; `with up.use_style("ultrafast-dark"):` holds only inside the block;
  `ULTRAFAST_PLOT_STYLE=paper` and `ultrafast-plot --style paper` do the same from the environment and the command line. `up.current_style()` says what is in force.
  `science` without SciencePlots warns and gives `paper`; the script checks `current_style()` and skips the file.
* **A loop** is a dashed arc under the row, from the element that closes the loop to the one it feeds, inside a shaded region labelled with the loop kind.

  ![the etalon with its loop](figures/etalon-ultrafast.png)

* **Compact schematics** (`up.system_diagram(result, ax, compact=True)`) draw into an axes of your own figure, for a corner of a larger plot.
  In the `paper` styles with `column="single"` the figure is 3.4 in wide and saved at 600 dpi:

  ![two systems at single-column width](figures/paper-single-column.png)

* **Determinism.** `layout_graph(summary)` returns the same positions every time (the last output line); the tests keep golden layouts.

**Limits.** The schematic shows the topology and the energy at each port, not the geometry of the beams. A sequential system keeps the beam line with glyphs
per element. See [plotting](../../../reference/plotting.md) for every function, the palette checks and the animation and 3-D spectrogram figures.
