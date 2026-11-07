# Kaur et al. 2024: nonlinear ellipse rotation and XPW in a multi-pass cell

J. Kaur et al., J. Phys. Photonics 6, 015001 (2024), DOI 10.1088/2515-7647/ad078a (open access, CC BY 4.0).

**Readability.** All 14 pages were read as extracted text (the page-image reader needs `pdftoppm`, which is not installed).
No figure was seen, so every value that only appears in a figure is UNVERIFIED and is not checked. Reference numbers are taken
from the running text only (`paper.json`).

## What the paper does

1 mJ, 30 fs, 800 nm Ti:sapphire pulses go through an argon-filled cell (two 1.5 m mirrors, about 3 m apart, 12 passes = 36 m).
Elliptical input light (quarter-wave plate, ellipticity eps 0.25 or 0.18), a second crossed quarter-wave plate and a polariser
turn the intensity-dependent rotation of the ellipse (NER) into a temporal filter: only the strong centre of the pulse is
rotated and transmitted. Reported: internal efficiency (energy after / before the polariser) 58 % at ~350 mbar for eps 0.25;
69 % at 420 mbar for eps 0.18, with 5.8 fs nearly transform-limited pulses after compression and a contrast gain of at least 3
orders. The second half of the paper uses cross-polarised wave generation (XPW) in two 600 um BaF2 plates (65 % internal).

## What we model, and what we cannot

* **NER: modelled.** `run.py`: 1 mJ, 30 fs Gaussian, 12 passes of 3 m argon with 1 % loss per mirror; QWP1 as the ideal input
  ellipse (eps = tan alpha), QWP2 the inverse plate, polariser crossed with the input; efficiency computed from the engine's output
  field. Two readings of the beam: a constant 1.45 mm radius (2.9 mm diameter on the end mirrors, "overall constant beam size")
  and a 450 um waist Gaussian caustic in every pass. Plane wave in 1-D.
* **Analytic reference** (instantaneous Kerr, no dispersion): the ellipse rotates by theta(t) = B(t) sin(2 chi) / 3, B = the
  B-integral of linear light of the same intensity (`docs/reference/nonlinear.md`). Averaged over a Gaussian pulse the transmission
  has a ceiling that depends only on eps: 60.4 % (eps 0.25), 68.2 % (0.18), 71.0 % (0.15).
* **XPW: not modelled.** The engine's chi(3) is isotropic; a linearly polarised pulse never generates the orthogonal wave
  (`repro_2.py`: 8e-32 of the energy in the orthogonal polarisation after 600 um of CaF2 at B = 0.8 rad). BaF2 is not a built-in
  material either (it can be given as a user Sellmeier). Missing capability: the anisotropic cubic-crystal chi(3) tensor.

![NER efficiency](figures/efficiency.png)

*Left: internal NER efficiency against the B-integral of linear light: analytic law (dashed), engine with the 36 m argon cell
(solid), engine short plane-wave cell (crosses); the paper's numbers as black marks. Right: against argon pressure for the two beam
readings, with the paper's two measured points.*

## Comparison

| quantity | paper (location) | ours |
|---|---|---|
| maximum NER efficiency, eps 0.25 | 58 % (p. 4, measured, ~350 mbar) | 57 % at 1.55 bar (constant beam); 62 % at 100 mbar (caustic beam); analytic ceiling 60 % |
| efficiency, eps 0.18, 420 mbar | 69 % (p. 5) | 24 % (constant beam), 34 % (caustic); analytic ceiling 68 %; the engine reaches 61 % only at 3 bar |
| ceiling for eps >= 0.15 | "should not exceed 75 %" (p. 3) | analytic 71 % at eps 0.15 |
| pressure of the maximum, eps 0.25 | ~350 mbar (p. 4) | 1.55 bar (constant) or 0.1 bar (caustic): the paper lies between |
| B at 400 mbar | ~6 rad (p. 3; B not defined) | 5.4 rad (constant beam, engine-reported B of linear light); 19 rad (caustic beam) |
| analytic efficiency at B = 6 rad, eps 0.25 | (58 % claimed) | 32 %; the peak angle at B = 6 rad is 54 deg, not > 90 deg as the paper states (p. 3) |
| transform-limited duration at 420 mbar, eps 0.18 | 5.8 fs (p. 4) | 10.3 fs (constant beam), 3.7 fs (caustic beam) |
| contrast gain with a 5e-3 polariser leak | at least 3 orders (p. 4) | 2.2 orders (peak transmission 0.80 / 5e-3 = 160) |
| overall efficiency | 49 % overall, 69 % internal (p. 5) | the 12 mirrors alone give 0.886; the other losses are not given |
| BaF2 P / Pcr | ~5e3 (p. 7) | 7.7e3 with an assumed n2 of 1.6e-20 m2/W (UNVERIFIED) |
| XPW efficiency 57-65 % | (p. 8, 10) | not reproducible (no anisotropic chi3) |

## What does not reproduce

* **The pressure (and B) at which the efficiency peaks.** The magnitude of the maximum agrees with the analytic ceiling for
  all three ellipticities (58 / 69 / <= 75 % against 60 / 68 / 71 %), so the polarisation chain and the ellipticity convention
  are consistent with the paper. The B needed to reach it is not: for eps = 0.25 the textbook law needs a peak angle of 90 deg, i.e.
  B = 10 rad of linear light, while the paper says the maximum occurs at about 6 rad. At B = 6 rad the same law gives 32 %.
  Debugging: the polarisation chain gives zero light for linear input (`linearLeak`); the engine agrees with the analytic
  efficiency to 2 % absolute in a short plane-wave cell (B up to 14 rad); with dispersion and self-steepening off the
  conclusion is unchanged; and `repro_1.py` shows the engine's one-pass B equals the closed form 8 pi n2 P / lambda^2 atan(d / 2 zR)
  to 0.1 % and its ellipse rotation equals B sin(2 chi)/3 to 0.4 %. The engine therefore does what the textbook law says;
  the gap is in how the paper's B, ellipticity or beam is defined. We tried the other reading of eps (the ratio of the minor and
  major INTENSITIES, tan^2 chi) in exploration: it puts the maximum at B ~ 6-7 rad, as the paper says, but caps the efficiency at
  28-42 %, far below 58-69 %, so neither reading reproduces all the numbers. This was not tuned further.
* **Beam geometry.** The stated cell is not self-consistent: the largest eigenmode waist for 1.5 m mirrors at 800 nm is 437 um
  (confocal spacing), so a 450 um waist cannot belong to a quasi-concentric cell, and a 2.9 mm beam on the mirrors would need a
  waist of about 270 um in the linear cell. The paper's mode is a Kerr-lens-matched one it does not specify. The two readings
  bracket the measured 350 mbar by a factor 4 on each side (1.55 bar and 0.1 bar). A radial / Kerr-matched run is left to do.
* **Compressed pulse duration.** 5.8 fs needs a spectrum of order 150 nm: the constant-beam reading (B = 5.4 rad) gives 10 fs,
  the caustic reading 3.7 fs. Again the geometry decides.
* **Contrast.** With the quoted leak of 5e-3 (read as a power leak) the improvement is limited to ~160 (2.2 orders), less than
  the reported >= 3 orders. The reading of "5e-3" (power or field, polariser quality of the rotated beam) is not stated.

## What is left

XPW (needs the anisotropic cubic chi3 tensor and BaF2 data); a radial Kerr-matched cell for the NER efficiency against pressure;
the measured spectrum as input; Fig. 3 spectra (not read); Fig. 7 caustics; the output compressor and spectral phase; the polariser
as a physical thin-film stack instead of an ideal one.

## Files and run

`run.py` (tiers), `repro_1.py` (engine vs closed form, one pass and ellipse rotation), `repro_2.py` (no XPW in the engine),
`checks.json`, `paper.json`.

```bash
python3 build/paper_check.py kaur                                    # fast tier, ~15 s
cd docs/papers/kaur-2024-mpc-ner-xpw && python run.py --tier full --figures
```
