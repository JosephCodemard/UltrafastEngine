# Liu 2013 -- pulse cleaner by nonlinear ellipse rotation in liquid CS2

**PDF readable?** Yes (3 pages). Read through the text layer (PyMuPDF; the Read tool's PDF mode needs `pdftoppm`, which is not
installed) and rendered images; Eq. (1) and "n2 = (3 +- 0.6)e-19 m2/W" were confirmed on rendered crops. Figure values are read off
plots. Nothing is copied from the paper.

## What the paper does
Linear light -> quarter-wave plate (QWP1, angle alpha) -> CS2 cell -> QWP2 (axis perpendicular) -> crossed polarizer. Intense light
rotates its polarisation ellipse and passes; weak prepulses and ASE do not. Theory (Eq. 1, Gaussian 100 fs, 800 nm): transmissivity
above 60 % at alpha about 34 deg; contrast improved by four orders for polariser extinction ratio 1e4 (Eq. 2). Experiment (808 nm,
70 fs, 2 mm CS2, ~320 GW/cm2): 30 % transmission, three orders of contrast, 60 fs cleaned pulse, optimum alpha 32.5 deg.

## Engine set-up (`run.py`, `paper.json`)
Engine elements `waveplate` - `bulk` - `waveplate` - `polarizer`. CS2 is **not a built-in material** (finding): user material with the
Kedenburg 2012 index formula (external; not from the paper) and the paper's n2 = 3e-19 m2/W as an instantaneous Kerr effect, no
delayed response. 1-D plane wave. B is the B-integral of linear light at the same peak intensity; the engine's reported B follows the
pulse inside the cell, so it is calibrated on a 10 um cell and scaled (checked against a numpy split-step to 1e-3).

## The angle and phase convention of Eq. (1)
Jones calculus for this chain gives T = cos^2(2a) sin^2(theta), theta = (B/3) sin(2a) (the engine's NER law), a measured from the fast
axis; the engine matches it to 0.005. The paper's Eq. (1) is `sin^2(2a) sin^2(phi cos(2a)/6)`. The two coincide with
**a_paper = 45 deg - a** and **phi = 2B** (derived, not fitted; engine vs Eq. (1) then agrees to 0.002 everywhere on the (alpha, B)
map). With the paper's literal definitions (a from the fast axis, phi = B) they differ by up to 0.68. The paper does not define phi_SPM
for elliptical light, so this is a convention issue of the paper, not an engine disagreement.

## Results
![transmissivity](figures/transmissivity.png)
Left: transmissivity against phase (engine with phi = 2B vs Eq. 1). Right: against alpha at phi = 8 pi.

![contrast](figures/contrast.png)
Contrast (pedestal / peak; smaller is better): the paper's Eq. (2) curves and the engine at C_in = 1e-5.

![experiment](figures/experiment.png)
Experiment-like case (808 nm, 70 fs, 2 mm CS2): transmissivity against intensity, and cleaned-pulse width.

| quantity | paper | ours |
|---|---|---|
| Eq. (1) max transmissivity, alpha = 34 deg (Gaussian) | > 60 %, Fig. 2a ~0.66 at ~9.5 pi | 0.667 at 9.5 pi (evaluated) |
| engine max transmissivity (all alpha, B <= 6 pi) | -- | 0.709, same as Eq. (1) over its range |
| contrast, ideal polariser | ~1e15 from 1e5 (Fig. 3a) | 10 orders gained (1.6e-15 from 1e-5) |
| contrast gain, ER 1e4 | about 4 orders (Fig. 3b) | engine 3.9; Eq. (2) 3.7 |
| optimum alpha, experiment case (paper convention) | 32.5 deg (Fig. 4a); 34 deg theory | 32.5 deg (full tier, 1.5 deg grid); 31 deg (fast tier) |
| optimum intensity | ~320 GW/cm2 (Fig. 4b) | 393 (327-491 for n2 3.6-2.4e-19); literal angle: 164 |
| transmissivity at optimum | 30 % measured (optics losses) | 0.66 (not comparable) |
| cleaned width, no dispersion | ~60 fs from 70 | 0.89 x input at the transmission optimum |
| cleaned width, with the cell's GDD (436 fs2) | ~60 fs | **158 fs at 320 GW/cm2** |
| spectrum broadened, spatial profile preserved (Fig. 6) | qualitative | not compared |
| 3 orders contrast in the experiment | Fig. 5a | not comparable (scatter, ER, plate accuracy) |

## What does not reproduce
* **Cleaned pulse width.** With CS2's own dispersion the engine's cleaned pulse is 158 fs (input 70 fs), not ~60 fs. Debugging: the
  engine agrees with an independent numpy split-step to 1e-3 (peak) and exactly (width); with the cell shortened to 10 um at the same
  B the width is 70 fs and the narrowing 0.89 matches the paper's trend. So it is the 1-D physics with 436 fs2 of GDD (from the
  Kedenburg formula, not cross-checked) and B = 15 rad, which Eq. (1) neglects. Open: the real focused beam, the quartz cell, and the
  paper's pulse chirp are not modelled. Not an engine bug.
* **Intensity optimum** is 23 % above 320 GW/cm2, inside the paper's n2 uncertainty (+-20 %), and depends on the unstated beam
  definition. The 30 % measured transmission includes optics losses and is not compared.
* **Delayed response of CS2 (70 fs / orientational)**: not run. `repro_1.py` shows (a) `anisotropicFraction` on `expression` delayed
  responses is silently ignored (docs say it applies to any delayed response), (b) values above 1 are rejected, but a purely
  orientational response needs 1.5. So a physical CS2 response cannot be built with the current schema.
* The experiment has P/Pcr ~ 4e5 (6 mJ, 70 fs); self-focusing is outside a 1-D model. The implied beam area at 320 GW/cm2 is 0.25 cm2.

## Run
`python run.py --tier fast` (~12 s) or `--tier full --figures`; `python3 build/paper_check.py liu`: 12 checks, 11 pass, 1 soft-fail.
