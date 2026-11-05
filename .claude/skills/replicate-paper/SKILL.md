---
name: replicate-paper
description: Reproduce the headline results of a published ultrafast-optics paper with UltrafastEngine - extract parameters, build the system, run it, compare with the paper's numbers and explain any disagreement. Use when asked to replicate, check or reproduce a paper, figure or published number.
---
# Replicating a paper

Examples to copy: `docs/papers/*/` (each has `paper.json`, `run.py`, `checks.json`, README). Harness: `python3 build/paper_check.py`.
Conventions and what the engine can simulate: `docs/capabilities.md`, `docs/concepts.md`.

1. **Read the paper, list the inputs.** Wavelength, duration and shape, energy or power, beam size, every length and
   material, dispersion, pressure, mirror GDD. Write each with its location (section / figure / table) in `paper.json`.
   What is missing gets a stated, reasonable assumption; say so in the README. Never copy the paper's text, figures or PDF.
2. **List the headline results**: the numbers the paper claims (compression factor, efficiency, shift, contrast ...).
   A number you only recalled, or could not read in the full text, is UNVERIFIED and is never a hard check.
3. **Build the smallest system that contains the physics** (`run-simulations` skill). Start 1-D; add the spatial model
   only if the paper's result depends on it. Check units and the pulse-duration convention (FWHM of intensity vs T0).
4. **Check the model before comparing**: energy budget closes, B-integral as expected, grid resolves the pulse and
   the broadened spectrum (`ultrafast validate`, `summary.json` warnings).
5. **Compare**: an analytic reference computed in `run.py` is better than a number read off a figure. Put checks in
   `checks.json` (`number`, `ordering`, `location`) with tolerances that reflect the source.
6. **If it disagrees**: first make sure the setup matches the paper; then try the debug-setup skill (grid, switches,
   polarisation, units); only then suspect the engine and reduce it to a minimal case (`/bug-report`). Record in the
   README what agrees, what does not and the most likely reason.
7. Plots: our result with the paper's formula or numbers on the same axes, labelled with units; commentary in the
   README for a reader who has not seen the paper.
