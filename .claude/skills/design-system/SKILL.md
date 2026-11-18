---
name: design-system
description: Design a new laser, amplifier, compressor, beamline or nonlinear stage in UltrafastEngine from a stated goal - choose the architecture, size it with the calculators, build the system file, check it in the cheapest faithful model, then confirm it in full. Use when asked to design, size or propose a system (not to model an existing lab setup, and not to tune an existing one).
---
# Designing a system from a goal

Tools: the calculators (`docs/reference/calculators.md`, `docs/reference/pulse-calculator.md`: pulse limits, beam and cavity
matching, B-integral and critical power, phase matching), the element list (`docs/reference/components.md`, `fibers.md`,
`crystals.md`, `gain.md`), studies (`docs/reference/studies.md`), fidelity (`docs/reference/fidelity.md`) and the plotting
backend (`docs/reference/plotting.md`).

1. **Write the goal as numbers**: output energy, duration, wavelength, repetition rate, beam size, and the limits that must hold
   (B-integral, fluence against the damage threshold `lidt`, intensity in the medium, efficiency). Ask for what is missing; do not guess.
2. **Size on paper first**, with the calculators: the Fourier limit of the bandwidth, the beam waist and lens or mirror spacing
   (`calc.beam`), the crystal angle and acceptance (`calc.crystal`), the B-integral and critical power (`calc.nonlinear`).
   Write the numbers down: they are the check for the simulation later.
3. **Build the smallest system that has the physics** (one stage, 1-D, no spatial model) as a `.ufs` file with the builders
   (`ultrafast.build`) or JSON. Name every element; SI units with the unit in the key. Add the instruments that read the goal
   (pulse width, spectrum, energy, B-integral).
4. **Run it in `fidelity: quick`**, compare with the paper numbers of step 2, and say where they differ and why. A disagreement
   is a finding: do not tune a parameter to hide it.
5. **Add one effect at a time** (Raman, self-steepening, gain, the spatial model, higher orders), each time checking that the
   result changes by the amount the effect should change it. Stop when the next effect changes the goal numbers by less than the tolerance.
6. **Scan the free parameters** (`docs/reference/studies.md`): length, angle, chirp, energy. Report the sensitivity, and the
   margin to every limit of step 1. Use `uf.validate` and read every warning (`explain_warning` on the MCP server).
7. **Confirm in full** (`fidelity: full`) at the chosen point and at the edges of the tolerance range.
8. **Deliver**: the `.ufs` and `run.json`, a schematic (`up.system(result, params=True)`), the figure of merit against the
   goal, the margins, the assumptions (every value that is a guess, with its source), and what was not modelled.

Do not present a design whose limits were not checked, or one found at the boundary of a scan without saying so.
