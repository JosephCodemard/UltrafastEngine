# HANDOFF -- WP fix-2 (in progress)

## Findings so far

* The spatial-kerr README numbers (B 1.40 rad, TL 207 fs, R -81 mm, far field 3.70 mrad, exit radius 97.8 um) were
  written at the initial commit 2f77295 and were never re-run. Reproduced there (after adding the untracked
  Results/*.cs from 8dfff5d so it builds): B=1.4046 TL=207.3 R=-81.3 ff=3.705 r=97.8.
* 8dfff5d ("Added GPU support", 2026-10-26) already gives the new numbers (B=1.554 TL=199.1 R=-70.5 ff=3.80 r=97.4);
  so do b656ec1, b06da1a and HEAD. The cause is inside 8dfff5d.
* Cause: 8dfff5d normalises the sampled Raman kernel to unit sum on the time grid (EnvelopeProblem.RamanKernels /
  NormalizedKernel, used by the 1-D, spatial and carrier-resolved paths). Before, the analytic DampedOscillator
  h(t) (tau1 12.2 fs, tau2 32 fs) sampled at the example's dt = 25 fs summed to 0.585, so the Raman share
  (fR = 0.18) was 41 % too weak: effective n2 = 0.925 n2.
* Which is right: the new one. Virial (Vlasov-Petrishchev-Talanov) second-moment law, exact for the paraxial cubic
  NLS: w^2(L) = w0^2 [1 + (1 - P/Pcr) L^2/zR^2], Pcr = lambda^2 / (2 pi n0 n2), energy-weighted P = P0/sqrt2.
  n2 2.6e-20, n0 1.4497: predicted 97.47 um (engine now 97.4; with 0.925 n2 predicted 97.86, old engine 97.8).
  Moment curvature after exit from the free-space radii: -130 mm vs predicted -131 mm.

## Next

* check mpc-argon / ner-measured-input moves (bisect separately; probably different causes).
* test pinning on-axis B (weak power: B0 zR atan(L/zR)/L) and the virial radius / moment focal length.
* temporary worktree /private/tmp/claude-501/uf-bisect (remove with git worktree remove --force).
