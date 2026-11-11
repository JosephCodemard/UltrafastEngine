# Raman self-frequency shift of solitons

Raman scattering pushes fundamental solitons to the red at a rate proportional to T0^-4 (Gordon 1986); the engine reproduces the scaling.

**The story.** A short soliton is so broad in frequency that its blue edge can pump its red edge through Raman scattering. The
soliton slides to the red, and because a shorter soliton is broader, it slides faster: Gordon (Opt. Lett. 11, 662 (1986)) found a
rate `dΩ/dz = -8 T_R |β₂| / (15 T₀⁴)`, so halving the pulse width speeds the shift up sixteen times. The example propagates three
fundamental solitons (T₀ = 40, 50, 65 fs) through 6 m of fibre with the engine's silica Raman response and measures the shift of
the spectral centroid along the fibre.

![hero](hero.png)

![the soliton sliding to the red](animation.gif)

**Layout** (`raman_shift.py`): `fiber` element, β₂ = -20 ps²/km, γ = 1.3 /(W km), `raman: silica` (Blow and Wood, f_R = 0.18),
self-steepening off, 6 m, N = 1 at the input (P₀ = |β₂| / (γ T₀²)). The red shift is the change in the power-weighted mean
angular frequency of the spectrum. T_R in Gordon's formula is computed from the engine's own response: `f_R ∫ t h_R(t) dt`.

```bash
python raman_shift.py
python make_figures.py          # optional: hero.png, animation.gif (about 40 s)
```

```
fundamental solitons N = 1, beta2 = -20 ps^2/km, gamma = 1.3 /(W km), 6 m of silica fibre, Raman response of the engine (T_R = 1.46 fs from the model)
T0 = 40 fs: shift after 6 m -7.9 THz (63 nm), rate -1.319 THz/m; Gordon -8 T_R |beta2| / (15 T0^4) = -0.969 THz/m (ratio 1.36); exact Raman gain -1.344 THz/m (ratio 0.981)
T0 = 50 fs: shift after 6 m -3.0 THz (24 nm), rate -0.503 THz/m; Gordon -8 T_R |beta2| / (15 T0^4) = -0.397 THz/m (ratio 1.27); exact Raman gain -0.499 THz/m (ratio 1.007)
T0 = 65 fs: shift after 6 m -1.0 THz (8 nm), rate -0.162 THz/m; Gordon -8 T_R |beta2| / (15 T0^4) = -0.139 THz/m (ratio 1.16); exact Raman gain -0.160 THz/m (ratio 1.013)
log-log slope of the rate against T0: -4.32 (Gordon: -4)
checks passed
```

**What it shows.** The log-log slope of the shift rate against T₀ is -4.3 (Gordon: -4): the scaling is reproduced. The absolute rate is 16-36 % *above*
Gordon's formula with the model's T_R, and **the cause is Gordon's approximation, not the engine.** Gordon replaces the Raman response by its first moment
(`h_R(t) ≈ T_R δ'(t)`), which is valid only when the soliton spectrum is narrow against the Raman gain, whose peak is at 13 THz. A 40 fs soliton has a spectral width
of about 9 THz, so the gain it sees rises faster than linearly across its spectrum. With the engine's own response `h_R` and the same fixed-width soliton, the rate is
`d⟨Ω⟩/dz = -γ f_R (1/E) ∫ (h_R * P) P' dt` (`exact_rate` in `raman_shift.py`; it reduces to Gordon's formula for a `δ'` response). It gives -1.344, -0.499 and
-0.160 THz/m, and the engine's measured rates are within 2 % of those (0.98, 1.01, 1.01). The ratio to Gordon grows from 1.15 at 65 fs to 1.39 at 40 fs because the
first-moment approximation fails more as the pulse gets shorter. The same fibre in pyNLO (an independent split-step code, 16384 points over 20 ps, no
self-steepening) gives -1.319, -0.504 and -0.162 THz/m over 3-6 m, within 0.3 % of the engine (-1.319, -0.503, -0.162) (`test_raman_soliton_shift_matches_pynlo_and_the_exact_gain_rate`). The peak
power of the soliton falls and the soliton slows down (the group delay changes with the carrier) as it moves red (middle top panel).

**Limits.** Self-steepening, loss and higher dispersion are off; a real fibre has all three. The shift rate is measured over the
last two thirds of the fibre by a straight-line fit.
