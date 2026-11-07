# Kida et al. 2014 -- "double FROG" (SHG-FROG + XFROG analysed together)

**Was the paper readable?** Text yes (4 pages, text layer extracted; the PDF page renderer was unavailable), figures no.
All numbers below come from the text. The retrieved intensities and phases of Figs. 4, 5, 7 and the traces of
Fig. 3 were not seen: UNVERIFIED and not used.

Paper: Y. Kida, Y. Nakano, K. Motoyoshi, T. Imasaka, Opt. Lett. 39, 3006 (2014), doi 10.1364/OL.39.003006 (not open access).

## The idea

Two unknown pulses (an 800 nm reference and a 1200 nm test pulse) are overlapped in one thin BBO crystal. One
scan of the delay between two copies of the double pulse records three signals in one spectrometer: the second
harmonic (SHG) of each pulse (two SHG-FROG traces) and the sum-frequency (SFG) of one with the other (an XFROG trace).
An SHG-FROG trace is the same for a pulse E(t) and its conjugated time reversal E*(-t), so retrieving the reference from
SHG alone gives the right pulse or its twin at random, and the test pulse (retrieved from the XFROG trace using that
reference) is wrong when the twin is used. Analysing the three traces together should remove the ambiguity and make the
retrieval reproducible.

## Setup and what we model

| item | paper | ours |
|---|---|---|
| reference | 800 nm, 35 fs, Ti:sapphire (p. 3007) | engine: Gaussian, 30 fs transform limit + GDD 195 fs^2 -> 35.0 fs (chirp is an assumption) |
| test | 1200 nm OPA, ~58 fs, positive chirp (pp. 3007-3008) | engine: Gaussian 40 fs + GDD 580 fs^2, then 3 mm fused silica -> 57.6 fs (assumption) |
| dichroic mirror | 3 mm fused silica, GDD 25.6 fs^2 (p. 3009) | engine `bulk` FusedSilica 3 mm |
| crystal | BBO 5 um, theta = 33.2 deg, phi = 90 deg | engine phase-matching calculator, type I assumed |
| FROG traces | SHG ref, SHG test, one XFROG (SFG) | synthetic 128 x 128 traces from the engine's fields, additive noise 0.4 % of peak (assumption) |
| algorithm | generalised projections, Fig. 6 | numpy implementation (below) |

**Missing engine capabilities (findings, not faked):** the engine has no XFROG / SFG-FROG instrument, no `xfrog`
retriever and no multi-trace ("double-blind", "double FROG") retrieval; `uf.retrieve` takes one trace. `run.py` therefore builds the
SFG signal E_test(t) E_ref(t - tau) and the algorithm of Fig. 6 in numpy on the engine's complex fields. Those
results are about the algorithm as written here, not the engine. What the engine does supply: the pulses, the
dichroic-substrate dispersion, its SHG-FROG instrument and PCGPA retrieval (ambiguity demonstrated with it) and the
BBO phase-matching calculator.

Algorithm (our reading of Fig. 6, one step of each per iteration): (1) PCGPA step on the reference SHG trace updates
the reference; (2) least-squares step with the XFROG trace updates the test pulse (reference fixed); (3) PCGPA step on
the test SHG trace updates the test pulse; (4) least-squares step with the XFROG trace updates the reference (test
fixed). "Conventional": the reference from its SHG trace alone, then the test pulse from the XFROG with that fixed
reference. "Two traces": steps (1)-(2) only. Starts: random initial guesses (the paper does not give its own).

Run: `python run.py --tier fast|full` (fast: 6 starts, 500 iterations, 12 s; full: 16 starts, 1500 iterations, 80 s).

## Comparison, paper vs ours (full tier, 16 starts)

| quantity | paper (location) | ours | verdict |
|---|---|---|---|
| engine SHG-FROG + PCGPA: time direction | two groups, twins (p. 3007) | GDD = +195 and -195 fs^2 across 12 seeds (engine instrument + engine retriever) | agrees |
| conventional (SHG then XFROG): wrong-direction retrievals | random (p. 3007) | 62 % wrong (10 of 16; 37.5 % right) | agrees |
| XFROG G error, right ref vs time-reversed ref | 0.4-0.5 % vs 0.9-1.0 % (pp. 3007-3008) | 0.27 % vs 0.53 % (ratio 2.0; paper ratio 1.8-2.5); absolute values depend on the assumed noise (floor 0.29 %) | agrees (ratio) |
| test pulse FWHM, reversed reference vs right | 34 fs vs 58 fs (p. 3008) | 45-55 fs vs 54-57 fs (one outlier 42 fs); the reversed reference gives the wrong sign of chirp in 6/6 fits | direction agrees (shorter), magnitude does not |
| "SHG(ref) + XFROG only" | no improvement, time-reversed twin appears randomly (p. 3009) | identical to conventional: 37.5 % right, G_x 0.45 % | agrees |
| double FROG: wrong-direction retrievals | none in six (p. 3008) | 3 of 16 stuck in a wrong-direction solution (81 % right) | **not reproduced** |
| double FROG: G errors | constant over retrievals: SHG 0.5 %, XFROG 0.4 % | right-direction runs 0.27 % (all traces); the 3 wrong-direction runs have 0.42 % (XFROG) and 0.63 % (SHG) | G separates the two |
| double FROG: reproducibility of the test FWHM (std over starts) | "no notable difference" (p. 3008) | std 3.1 fs vs 4.3 fs (conventional) over all starts | qualitatively better, not "identical" |
| dichroic substrate GDD (test pulse) | 25.6 fs^2 (p. 3009) | **18.2 fs^2** for 3 mm | **disagrees** (see below) |
| BBO 5 um at 33.2 deg: sinc^2(dk L/2) for SHG 800 / SHG 1200 / SFG | thin crystal chosen for broad phase matching (p. 3007, qualitative) | 0.97 / 0.90 / 0.90 (type I; its exact phase-matching angles 29.2 / 21.2 / 24.3 deg) | consistent |

## Plots

![traces](figures/synthetic-traces.png)

The three synthetic traces (our pulses, noise 0.4 %). They are the analogue of the paper's Fig. 3; the paper's own
trace was not seen.

![retrievals](figures/retrievals.png)

Each dot is one retrieval from a random start. Left: the test-pulse FWHM against the true 57 fs (dashed). Right: the
XFROG G error against the noise floor (dashed: the true pulses). The conventional and two-trace methods fall in two
groups (right and wrong time direction, G 0.3 % and 0.55 %). The double method gives the low group for most starts, and
a group at 0.42 % that is a wrong-direction local minimum.

![dir](figures/time-direction.png)

Fraction of retrievals with both pulses in the right time direction (dotted: 50 %, a coin flip).

## What does not reproduce

1. **Double FROG removing the ambiguity in every retrieval.** Ours removes it in 81 % (13 of 16) in the full tier, 4 of 6 in
   the fast tier; the remaining starts stagnate in a wrong-direction solution. Not an engine issue, and therefore no engine reproduction exists to reduce it to: the algorithm is
   our numpy implementation of Fig. 6 with a 40 fs random-phase start, one projection step per trace, no tuning
   (the paper does not state its start or step details). Facts that support the paper's physical claim: without
   noise the correct solution reaches G = 0 while the stuck solutions stay at G = 0.0032 (so the three traces do
   contain the direction), and with noise the wrong runs show a higher G on all three traces (0.42 / 0.63 / 0.63 %
   against 0.27 %), so the wrong ones are identifiable. Raising iterations from 500 to 1500 did not rescue them.
   Reproducing "six of six" would need the paper's start and step sizes.
2. **GDD of the dichroic substrate: 25.6 fs^2 (paper) vs 18.2 fs^2 (engine).** Debugged first: the engine's value
   equals an independent second derivative of Malitson's fused-silica formula (18.14 fs^2, `repro_1.py`), so the
   engine is right for 3 mm of fused silica at 1200 nm. 25.6 fs^2 would need 4.2 mm, or other glass in the path;
   the paper does not say. This does not change any conclusion (both are tiny compared with the 580 fs^2 chirp).
3. **34 fs vs 58 fs.** The reversed reference gives a test pulse that is shorter (about 49 fs on average, 45-55 fs by
   start) and of the opposite chirp, but not by the 24 fs of the paper. This depends on the real pulses (the paper's
   are not Gaussian: its Figs. 4-5 phases span several radians) and our chirps are assumptions.

## Notes and limits

* Noise is additive, 0.4 % of the peak, chosen before running; absolute G values follow from it, only comparisons
  (ordering, ratios) are tested. `KIDA_NOISE` and `KIDA_ITERS` environment variables exist only for the sensitivity
  statements above (noise 0 and 1 %: right-direction fraction unchanged at 4 of 6 in the fast tier).
* The XFROG delay range is the full circle of the grid, not the single signal at +tau the paper records.
* **API pitfall found on the way** (also in the Kane README): `uf.retrieve("pcgpa", dict)` defaults to `shg-frog`; a PG trace
  given as a dict without `kind="pg-frog"` is retrieved wrongly and silently.
* UNVERIFIED (figures not seen): retrieved pulse shapes and phases, the double-FROG trace itself, the 50 s scan time effect.
