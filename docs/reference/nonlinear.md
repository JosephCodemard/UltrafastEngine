# Nonlinear physics and solvers

## Envelope model (default)

For each nonlinear element the engine integrates, in the co-moving frame,

dA/dz = L(omega) A + N(A, z),

where L is the element's complete linear operator per unit length (dispersion to all orders and attenuation,
from traced data or the material model) and

N_x = i k0 [ (dn_x + dn_user) A_x + n2 (1 - fR)/3 A_y^2 A_x* / A_eff + n2 fR (h_b * Re(A_x A_y*)) A_y / A_eff ] x (1 + Omega/omega0),
dn_x = n2 [ (1 - fR)(I_x + 2/3 I_y) + fR (h_a * I + h_b * I_x) ],  I = |A|^2 / A_eff(z),

(and the same for y with x and y exchanged), i.e. Kerr self- and cross-phase modulation with the isotropic chi(3)
coherent term, the delayed (Raman) response with its isotropic (h_a) and anisotropic (h_b) parts, self-steepening,
and any user responses dn_user(t, t_lab, z, I, ...). With gamma = n2 omega0 / (c A_eff) this is the generalised NLSE
of Agrawal / Dudley; A_eff(z) follows the beam model, so focusing inside a multipass-cell pass is integrated
exactly. A purely x-polarised pulse in an isotropic medium propagates only Ax (half the cost); for it the split of
the Raman response does not matter (h_a + h_b = h_R).

### Polarisation: circular light and nonlinear ellipse rotation

The Raman response of a glass is a tensor (Hellwarth): R_ijkl(t) = h_a(t) δij δkl + h_b(t) (δik δjl + δil δjk) / 2,
with h_R = h_a + h_b. The isotropic part h_a follows the total intensity; the anisotropic part h_b (the share b of
the response, `anisotropicFraction`) follows the field's orientation and is what makes Raman contribute to
**nonlinear ellipse rotation (NER)** and to the difference between circular and linear light. For an instantaneous
Kerr effect the textbook results hold, and the engine reproduces them (tests `NerTests`):

| quantity | electronic Kerr only | with a slow Raman response (pulse ≫ 100 fs) |
|---|---|---|
| ellipse rotation at the pulse peak, ellipse angle χ (tan χ = ellipticity) | θ = B sin(2χ) / 3 | θ = B sin(2χ) [(1 − fR)/3 + fR b/2] |
| circular / linear nonlinear phase at equal intensity | 2/3 | (1 − fR) 2/3 + fR (1 − b/2) |
| reported B-integral, circular / linear | 2/3 | as the phase |

B is the B-integral of linear light of the same intensity. With fused silica (fR = 0.18, b = 0.25) the Raman part
adds 7 % to the rotation of the electronic part, and circular light gets 3 % less phase than with an isotropic Raman
response. Circular light at 1.5× the energy of linear light has the same spectrum when Raman is off.

`anisotropicFraction` defaults to 0.25 for `silica` / `blowWood` and `hollenbeck` (fused silica: the anisotropic share
of the Raman cross-section, Hellwarth; Lin & Agrawal's fb + fc), to fb + fc with the shapes of Lin & Agrawal for
`linAgrawal` (the anisotropic part is fc ha + fb hb), and to 0 for `dampedOscillator`, `expression` and `callback`
responses. Set it explicitly on any delayed-response model (`"raman": {"kind": "silica", "anisotropicFraction":
0.3}`) to study its effect on NER; `0` makes the response isotropic (no Raman contribution to the rotation).

The **B-integral** of an element is the peak over the pulse of the effective nonlinear index change, weighted over
the polarisation components: Δn_eff = Im(Σ A_c* N_c) / (k0 Σ |A_c|²). It contains every term of the χ(3)
polarisation, including the coherent term, so circular light reports 2/3 of the linear value (an earlier version left
the coherent term out of the report and gave 5/6; the propagation itself was correct).

### Raman models

The delayed response h_R(t) is a material property (`"raman"` in a material, or on a `bulk` / `fiber` element), with
the Raman fraction `fR`:

| kind | model | default fR |
|---|---|---|
| `silica` (`blowWood`) | damped oscillator, tau1 = 12.2 fs, tau2 = 32 fs (Blow & Wood 1989); 25 % anisotropic (`anisotropicFraction`) | 0.18 |
| `linAgrawal` | damped oscillator + boson peak, h = (fa + fc) ha + fb hb, tau_b = 96 fs (Lin & Agrawal, Opt. Lett. 31, 3086 (2006)); `fa`, `fb`, `fc`, `taub` adjustable | 0.245 |
| `hollenbeck` | 13 vibrational modes with Lorentzian and Gaussian broadening (Hollenbeck & Cantrell, JOSA B 19, 2886 (2002)); reproduces the measured gain spectrum, peak 13.2 THz | 0.18 |
| `dampedOscillator` | your `tau1`, `tau2` (`anisotropicFraction`, default 0) | `fR` |
| `expression`, `callback` | any h(t) | `fR` |

Molecular gases have their rotational Raman response as an anisotropic tensor response instead
([Gases](gases.md)). Every kernel is sampled on the time grid and normalised there to unit sum, so the delayed
fraction is exactly fR whatever the step (an analytic normalisation is off by a few % when the step is not much smaller
than the 12 fs oscillation; with the grid normalisation the engine and pyNLO agree to 0.05 %).

## Carrier-resolved field model

When a time response declares `carrierResolved: true` (or `physics.fieldMode: "carrier"`), the element is
propagated as the **real field** e(t) = sqrt(2) Re[a(t) e^{-i omega0 t}] on a finer grid reaching
`fieldHarmonic` x omega0:

dE(omega)/dz = L(omega) E + i (omega/c) F[dn_re e] - (|omega|/c) F[dn_im e],
dn = n2 [ (1 - fR)(2/3) e^2 / A_eff + fR h_R * I_env ] + user responses.

The 2/3 makes the cycle-averaged SPM identical to the envelope model; self-steepening and third-harmonic
generation emerge automatically; user models see `t_lab` with optical-cycle resolution and the field `E`.
Energy generated outside the envelope band is reported (`harmonics (out of band)`).

**Broadband χ(2).** The time response `{"kind": "chi2", "deffPmPerV": 2, "periodM": 30e-6}` adds the
second-order polarisation P = 2ε0·d_eff·E² as Δn = d_eff·E/n0 (E the real field in V/m). Second-harmonic,
sum-frequency, difference-frequency generation and optical rectification of any bandwidth then come out of one
field. Phase matching follows from the material's dispersion. `periodM` flips the sign of d_eff every half
period (quasi-phase matching). The model is scalar, so there is no birefringent phase matching; for that use the
`crystal` element. A χ(2) drive is dealiased (no drive above 2/3 of the field band) and the field grid then
reaches at least 4.5 ω0, which keeps the energy exactly conserved at high conversion (tested against the
low-conversion formula and for energy conservation). Validation: `LaserTests.Chi2Response_*`. The field model is scalar
(x); a y component is propagated linearly with a warning. The cross-check test agrees with the envelope model
to 2 % in spectral width for strong SPM in fused silica.

## Steppers

* **`adaptive`** (default): the embedded ERK4(3)-IP of Balac & Mahe (2013). The 4th-order RK4IP solution and an
  embedded 3rd-order one share all stages (first-same-as-last), and their relative difference controls the step
  (`tolerance`, default 1e-6), so steps grow where little happens (mid-pass, weak fields) and shrink near foci.
* **`rk4ip`**: the classic fixed-step RK4IP (Hult 2007), with the step count chosen from the estimated
  B-integral (`maxPhasePerStepRad`) and the beam-size variation.

Both limit the change of the effective area per step (`maxAreaChangePerStep`). The B-integral reported per
element is the integral of k0 max_t(Re dn) dz along the actual steps.

## Validation (see testing.md)

Analytic SPM broadening of a Gaussian (Agrawal eq. 4.1.15) to 1 %, fundamental-soliton shape after one soliton
period to 1 %, adaptive vs fixed steppers to 1e-4, Raman red-shift and self-steepening directions, envelope vs
carrier-resolved models, callback == built-in Kerr to 1e-9, Bessel sidebands of lab-time phase modulation to
1-4 %, and an N ~ 2.4 higher-order soliton against pyNLO to 0.05 % (0.8 % with Raman).
