# 22 -- Contrast of an amplifier chain

Stretcher → uncoated window (its ghost is a post-pulse) → Kerr medium → compressor. The Kerr medium turns the
ghost into a pre-pulse after compression; a third-order cross-correlator measures both. Walk-through:
[Tutorial 5](../../tutorials/05-contrast.md).

```bash
ultrafast run chain.ufs run.json -o results          # one run, ~1 s
ultrafast study study.json -o study-results          # pre-pulse vs energy, 9 runs, ~5 s
```

What to look at:

* `results/summary.json → instruments → xc`: `prePulseContrastDb` (-30.6 dB at -29.4 ps) and `postPulseContrastDb`
  (-26.6 dB at +29.1 ps); `elements → window → notes`: the ghost's delay and intensity.
* `study-results/study.csv`: the pre-pulse grows as B² (20 dB per decade of energy) above the correlator's own ε²
  artefact (-58 dB).

New features used: plate ghosts, `pulse.pedestal`, the `crossCorrelator` instrument, parameter studies, and the
planner's B estimate after the stretcher.
