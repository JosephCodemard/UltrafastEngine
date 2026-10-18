# Nonlinear physics and solvers

## Envelope model (default)

For each nonlinear element the engine integrates, in the co-moving frame,

dA/dz = L(omega) A + N(A, z),

where L is the element's complete linear operator per unit length (dispersion to all orders and attenuation,
from traced data or the material model) and

N_x = i k0 [ (dn_x + dn_user) A_x + n2 (1 - fR)/3 A_y^2 A_x* / A_eff ] x (1 + Omega/omega0),
dn_x = n2 [ (1 - fR)(I_x + 2/3 I_y) + fR (h_R * I) ],  I = |A|^2 / A_eff(z),

i.e. Kerr self- and cross-phase modulation with the isotropic chi(3) coherent term, delayed (Raman) response,
self-steepening, and any user responses dn_user(t, t_lab, z, I, ...). With gamma = n2 omega0 / (c A_eff) this is
the generalised NLSE of Agrawal / Dudley; A_eff(z) follows the beam model, so focusing inside a multipass-cell
pass is integrated exactly. A purely x-polarised pulse in an isotropic medium propagates only Ax (half the cost).

### Raman models

The delayed response h_R(t) is a material property (`"raman"` in a material, or on a `bulk` / `fiber` element), with
the Raman fraction `fR`:

| kind | model | default fR |
|---|---|---|
| `silica` (`blowWood`) | damped oscillator, tau1 = 12.2 fs, tau2 = 32 fs (Blow & Wood 1989) | 0.18 |
| `linAgrawal` | damped oscillator + boson peak, h = (fa + fc) ha + fb hb, tau_b = 96 fs (Lin & Agrawal, Opt. Lett. 31, 3086 (2006)); `fa`, `fb`, `fc`, `taub` adjustable | 0.245 |
| `hollenbeck` | 13 vibrational modes with Lorentzian and Gaussian broadening (Hollenbeck & Cantrell, JOSA B 19, 2886 (2002)); reproduces the measured gain spectrum, peak 13.2 THz | 0.18 |
| `dampedOscillator` | your `tau1`, `tau2` | `fR` |
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
