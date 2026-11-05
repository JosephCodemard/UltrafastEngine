# Escoto et al. (CLEO 2024): polarisation-based contrast enhancement in a multi-pass cell

E. Escoto et al., "Polarization-Based Contrast Enhancement during Spectral Broadening in a Multi-Pass Cell", CLEO 2024,
SF2O.8 (licensed; only the abstract and values read off its figures were used, nothing is copied).

**Idea.** A Kerr medium (fused-silica plates in a multi-pass cell, dispersion close to zero per round trip) sits between
two quarter-wave plates (QWP1, QWP2) and a polariser. Intense light rotates its polarisation ellipse by nonlinear ellipse
rotation (NER) and passes the polariser; the weak pedestal does not rotate and is blocked. Two numbers are mapped over the
two plate angles in [-50, 50] deg: the **efficiency** (energy through the polariser, normalised to its peak) and the
**contrast improvement at +2 ps** (peak over intensity at +2 ps, output relative to input).

## Set-up used here (hypothesis: the cell is not given)

* Cell assumed to be the Seidel et al. cell (Laser Photon. Rev. 16, 2100268 (2022): three 1 mm fused-silica plates, 31
  round trips, 900 fs, 1030 nm), reduced to **one** 1-D plane-wave fused-silica Kerr element (dispersion taken as
  compensated; silica Raman on). Its energy is set so the B-integral of linear light is **B = 10 rad**, the value of the
  paper's model.
* 900 fs Gaussian at 1030 nm, linear along x; QWP1 at theta1; QWP2 at theta2; ideal polariser crossed to the input.
  The paper's sign convention of theta2 is assumed to be the one that puts the low-efficiency ellipse along
  theta1 = theta2 (engine angle = - paper angle).
* QWP1 and the cell run in the engine (`waveplate`, `bulk`); QWP2 + polariser are applied as Jones matrices to the output
  field (one engine run per theta1, then the whole theta2 axis at once). The matrices agree with the engine's own
  `waveplate` and `polarizer` elements to 3e-3 of the energy (1e-5 at 1 degree steps).
* Input contrast at +2 ps: the Gaussian value (peak / I = 8.8e5). Waveplates and polariser are ideal.

Run: `python run.py --tier fast` (2 degree grid, ~20 s) or `--tier full --figures` (1 degree grid, B = 6, 15, 25 rad,
polariser leak; ~2 min).

## Results

![efficiency and contrast improvement maps](figures/maps.png)

Left: efficiency over (theta2, theta1) with the paper's maxima marked. Right: contrast improvement at +2 ps (log10).

![diagonal](figures/diagonal.png)

Along theta1 = theta2: efficiency (black points: values read off Fig. 2a) and contrast improvement. Dotted: other
assumed B, each normalised to its own peak.

![P1 and P2](figures/p1-p2.png)

Spectra and GDD-compressed pulses at the first (P1) and second (P2) diagonal maximum of our model.

## What reproduces

| feature | paper (read off) | ours |
|---|---|---|
| efficiency exactly 0 at the origin, symmetry (theta1, theta2) -> (-theta1, -theta2) | Fig. 1b | exact |
| dark tilted band along theta1 = theta2 through the origin | Fig. 1b | yes |
| maxima in the (theta2 < 0, theta1 > 0) and (theta2 > 0, theta1 < 0) quadrants | (-30, +35), (+30, -35) deg | broad maxima in the same quadrants, peak at the corner (-45, +45) |
| diagonal efficiency returns to 0 near 45 deg | ~44 deg | 45 deg (linear null of two plates at +-45 deg) |
| contrast improvement is largest on the diagonal and grows towards +-45 deg | Fig. 1c, up to ~8-9 | yes, up to 4.2 (1 degree grid) |
| diagonal is non-monotonic with a first maximum, a minimum and a second maximum | Fig. 2a | one broad maximum only |

## What does not reproduce

* **The diagonal efficiency curve (Fig. 2a).** The paper has a maximum 1 at ~10 deg, a minimum at ~26 deg and a second
  maximum of ~0.62 at ~35 deg. We find a single broad maximum of 0.28 (relative to the map peak) at 19 deg, a minimum at
  32 deg and a weak maximum 0.19 at 28 deg. Changing B moves the first maximum (B = 6: ~21 deg; B = 15: ~14 deg; see
  figure), so the position depends on the assumed B, but the second maximum and the relative height of the diagonal
  to the map peak (~1 in the paper) are not obtained at any B tried. In the engine the diagonal is a thin dark band of
  the linear map; the paper's diagonal reaches the map maximum. This suggests the paper's two plates see the light
  differently from our idealisation (a different plate configuration, e.g. not the same sense of rotation, extra
  retardance of the cell mirrors, or another definition of the angles).
* **P1 modulated, P2 smooth; P2 cleaner (Fig. 2f,g).** Both our spectra are two-lobed SPM spectra of nearly equal width
  (~28 nm; the paper's is ~35 nm wide) and both compress to similar pulses; P2 is not cleaner than P1 here. A single
  lumped Kerr element does not make the multi-plate, multi-pass structure of the real cell (alternating beam size, and a
  spatial NER averaged over the beam), which is the likely source of the paper's smoother P2.
* **Contrast improvement size.** With ideal components our improvement is limited to ~4 orders by the width of the
  null in the 1 degree grid (it is exact at 45 deg, where nothing passes at all). The paper's 8-9 orders need a finer
  null or a different definition (for example the ratio at a fixed output level). A polariser leak of 1e-5 gives
  essentially no change on the diagonal.

Most likely reasons, in order: the assumed cell and B (a lumped plane-wave element instead of three plates and 31 round
trips with a Gaussian beam); the angle convention for QWP2; the contrast metric's definition.

## Files

`run.py` (model and figures), `checks.json`, `paper.json`, `figures/`. Check: `python3 build/paper_check.py escoto-2024`
(4 hard checks: Jones vs engine elements, symmetry, dark origin, maxima quadrants; the figure values are soft checks).
