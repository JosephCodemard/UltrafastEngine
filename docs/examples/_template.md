# Title of the example (same text as "title" in example.json)

One paragraph that says what the example shows, with its main number (this paragraph is "summary" in example.json, word for word).

**The story.** What physical situation is modelled and why it matters, in plain English. Say which published result or analytic formula the run
is checked against.

![hero](hero.png)

![what the animation shows](animation.gif)

**Layout** (`script_name.py`): the system and run parameters that matter (elements, lengths, energies, wavelengths, physics switches), in SI units
with the unit in the name. Say how long the script takes.

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
example.json: {"title", "summary", "group" (the parent folder), "script", "plugins" (list), "figures" (list of files), "wave" (int)}.
Check: python build/example_lint.py. The linter checks structure and metadata only; it does not run the example.
-->
