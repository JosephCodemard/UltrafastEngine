# Beam shaping with a phase-only SLM

A phase-only SLM with 256 grey levels turns a Gaussian beam into a smiley, a square, a ring and a spot array (Gerchberg-Saxton holograms), up to a full 1920 x 1080 panel.

**The story.** A spatial light modulator writes only phase, yet the focus of a lens behind it can be any pattern. The
Gerchberg-Saxton algorithm finds the phase: iterate between the SLM plane (a known Gaussian beam, free phase) and the focal plane
(the wanted amplitude, free phase). Here the hologram is quantised to **256 grey levels (8 bit)** *inside* the loop, written to the
`slm` element, and the engine propagates the beam through the lens. A smiley face, a square, a ring and a 3 x 3 spot array
(weighted GS) all come out of the same device.

![hero](hero.png)

![Gerchberg-Saxton converging](animation.gif)

*The far field after 1, 2, 3, ... 60 iterations, with the amplitude error beside it.*

**Layout** (`slm_shaping.py`): Gaussian beam (1.4 mm radius, 800 nm) → `slm` (256 x 256 pixels of 20 µm, 256 linear grey levels) →
lens f = N dx² / λ = 128 mm → camera. With this focal length the engine's focal-plane pixels are exactly the FFT bins of the
hologram, so the design and the engine result share one lattice. The "Fourier prediction" in the output is the orthonormal FFT of
the quantised phase times the |sinc|² envelope of the square pixel.

```bash
python slm_shaping.py
python make_figures.py          # hero.png, quantisation.png, full-panel.png, animation.gif (about 40 s)
```

```
panel 256 x 256 pixels of 20 um, 256 grey levels, lambda 800 nm, f = 128.0 mm (focal pixels = FFT bins), beam radius 1.4 mm
smiley  energy on target pixels 0.94 (design 0.88), engine against the Fourier prediction (inside the window): correlation 0.9842  GS error 0.005 -> 0.001
square  energy on target pixels 0.95 (design 0.89), engine against the Fourier prediction (inside the window): correlation 0.9895  GS error 0.005 -> 0.001
ring    energy on target pixels 0.94 (design 0.88), engine against the Fourier prediction (inside the window): correlation 0.9935  GS error 0.005 -> 0.001
spots   energy on target pixels 0.42 (design 0.43), engine against the Fourier prediction (inside the window): correlation 0.9949  GS error 0.006 -> 0.003
GS error is non-increasing after the first iterations (Fienup 1982)
checks passed
```

**What it shows.** 94-95 % of the light lands on the pixels of the smiley, the square and the ring (the GS design predicts 88-89 %,
the engine's pixel averaging and the absorbing edge move a few percent of the light around). The engine reproduces the Fourier
prediction of the written phase with a correlation of 0.98-0.99. The sharper the target, the more light a GS hologram leaves
elsewhere: the weighted-GS spot array puts 42 % of the light into the nine spots.

Quantisation: `quantisation.png` compares the GS error with an ideal phase, 256 levels and 4 levels, and shows the far field of a
4-level hologram next to the 256-level one through the engine. The S3 measurements found that quantisation inside the loop helps at
2 and 3 levels but not at 4 or 8 on this kind of target.

![quantisation](quantisation.png)

**The full panel.** `python slm_shaping.py --full` designs the smiley for a **1920 x 1080** panel (8 µm pixels, 8 bit) on the 2048 x
2048 grid the engine uses and propagates it (about 15 s on a laptop; the panel's phase goes in as a callback, not as JSON):
91 % of the light on the smiley (the GS design predicts 86 %).

![full panel](full-panel.png)

**Limits.** Patterns stay inside the inner half of the focal window: the periodic FFT of a pixelated hologram repeats the pattern
at the diffraction orders and the engine's absorbing edge removes what the FFT would wrap round. The default SLM birefringence
preset is generic and unverified; this example runs at a single wavelength so it does not enter.
