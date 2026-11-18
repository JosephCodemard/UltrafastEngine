---
name: optimise-setup
description: Find better settings for a simulated setup (compressor, waveplate angles, lengths, pressures, input chirp, energy) with parameter studies and the fitting plugin's estimators, against a stated goal. Use when asked to optimise, tune, scan or find the best operating point of a system.
---
# Optimising a setup

Tools: studies (`docs/reference/studies.md`: scans and grids, the study restarts after unchanged elements, so scanning a
late parameter is cheap), instruments (`docs/reference/instruments.md`), the fitting plugin (LM, CMA-ES, ensemble MCMC).

1. **State the goal as a number**: peak power, duration, contrast at +2 ps, efficiency, bandwidth. Name the
   constraints (damage threshold `lidt`, minimum throughput, maximum B-integral, no wavefront distortion).
2. **Start with a coarse scan** of the one or two parameters that matter; plot it. A scan shows whether the optimum is
   interior, whether the landscape has several maxima, and which parameters do nothing.
3. **Refine with an estimator** (`fit` with the goal as a target, or CMA-ES for multimodal landscapes); keep the
   evaluation count small by running the cheapest faithful model (1-D before spatial, fewer points before the final run).
4. **Check the answer in the full model** and at the edges: tolerance to ±1 step of each parameter (alignment, angle
   resolution of a waveplate mount). A sharp optimum is a warning, not a result.
5. **Report**: the setting, the figure of merit, the sensitivity, what limits it, and the runner-up settings.
   Do not report an optimum found at the boundary of the scanned range without saying so.
