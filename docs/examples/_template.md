# Title of the example (same text as "title" in example.json)

One paragraph that says what the example shows, with its main number (this paragraph is "summary" in example.json, word for word).

**The story.** What physical situation is modelled and why it matters, in plain English. Say which published result or analytic formula the run
is checked against.

![hero](hero.png)

![what the animation shows](animation.gif)

**Layout** (`script_name.py`): the system and run parameters that matter (elements, lengths, energies, wavelengths, physics switches), in SI units
with the unit in the name. Say how long the script takes.

**Run it** in the example folder. The two tabs run the same simulation and print the same key numbers.

=== "CLI"

    ```bash
    ultrafast run system.ufs run.json -o results     # about N s
    ```

    ```
    the CLI's lines with the key numbers, as printed (the numbers quoted here must match a fresh run: /example-check)
    ```

=== "Python"

    <!-- snippet -->
    ```python
    import ultrafast as uf
    from ultrafast import build as b

    system = uf.System.load("system.ufs")          # the example's own system file (or build it: b.system(...))
    spec = b.run_spec(                             # run.json, written with the builders: typed dicts, so the editor completes every key
        b.pulse(1.03e-6, 250e-15, energy=4e-6),
        instruments=[b.instrument("compressor", name="ideal_compressor")],
    )
    res = uf.run(system, spec)                     # uf.run("system.ufs", "run.json") runs the files as they are
    res.write("results")
    out, tot = res.summary["output"], res.summary["totals"]
    print(f"  energy {out['energyJ']:.4G} J (T = {100 * tot['energyTransmission']:.2f} %), FWHM {out['fwhmS'] * 1e15:.4G} fs "
          f"(TL {out['transformLimitFwhmS'] * 1e15:.4G} fs), peak {out['peakPowerW']:.4G} W, B = {tot['bIntegralRad']:.3G} rad")
    ```

    ```
    the same lines, as printed
    ```

The full script (scans, checks, figures):

```bash
python script_name.py           # about N minutes
python make_figures.py          # optional: figures
```

```
the output of the script, as printed (the numbers quoted here must match a fresh run: /example-check)
checks passed
```

**What it shows.** Each number of the output explained against its reference (formula, paper, other example), with the agreement.

**Limits.** What the model leaves out and what would change if it were included. Link to the related examples and reference pages.

<!--
Files in the folder: README.md (this structure), example.json, the script, make_figures.py (optional), hero.png, animation.gif (optional).
example.json: {"title", "summary", "group" (the parent folder), "script" (the file the example runs: a .py, or the run spec of a CLI
example), "plugins" (list), "figures" (list of files), "wave" (int), "python" (true once the README has the Python tab)}.
"Run it": a CLI tab and a Python tab (pymdownx.tabbed). CLI tab: `ultrafast run <system>.ufs <run>.json -o results` when the example
has (or can be given) a system file and a run spec; a Python-only example runs its script there (`python script_name.py`) and drops
the separate "full script" block. Python tab: the marker line shown above (the HTML comment "snippet") directly before the ```python fence, at the same
indentation ("snippet slow" if it takes more than about a minute at quick fidelity; build/readme_snippets.py). The snippet is a complete program run in the example
folder: `import ultrafast as uf`, the builders of `ultrafast.build` for what you write (not raw dicts), `uf.System.load` for the
example's own .ufs; it prints the CLI's key numbers in the CLI's format (G4: `:.4G`, B-integral G3: `:.3G`, transmission `:.2f} %`).
python/tests/test_readme_snippets.py runs every snippet at quick fidelity with ULTRAFAST_STRICT_OPTIONS=1; build/example_check.py runs
them at full fidelity and checks the quoted output. A README without a CLI equivalent keeps the two tabs: CLI = the script command.
Check: python build/example_lint.py. The linter checks structure and metadata only; it does not run the example.
-->
