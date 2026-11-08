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

The tabulated parameters of the `hollenbeck` model are those of Table 1 of the paper as commonly reproduced (the same
numbers are used by other open-source pulse-propagation codes). In that table the Lorentzian width of every mode is
one third of its Gaussian width except mode 2 (100 cm^-1: Gaussian 110.42, Lorentzian 38.81 where one third would be
36.81). The engine could not check this entry against the original paper, so it uses the value as printed; the
difference is small (2 cm^-1 on one of thirteen modes). The default `fR` of 0.18 is the
commonly used value for silica; set `fR` yourself to use another.

Molecular gases have their rotational Raman response as an anisotropic tensor response instead
([Gases](gases.md)). Every kernel is sampled on the time grid and normalised there to unit sum, so the delayed
fraction is exactly fR whatever the step (an analytic normalisation is off by a few % when the step is not much smaller
than the 12 fs oscillation; with the grid normalisation the engine and pyNLO agree to 0.05 %). On a grid whose step is
longer than 0.5 fs the response is evaluated on a finer sub-grid and band-limited to the grid before it is sampled, so
its spectrum is the response's own H(ω) at every frequency of the grid and the Raman shift does not depend on the step.

`anisotropicFraction` (b, between 0 and 1.5) works on every delayed-response kind (`silica`, `linAgrawal`, `hollenbeck`, `dampedOscillator`, `orientational`,
`expression`, `callback`): `h_b = b h`, `h_a = (1 - b) h`. b = 0 is isotropic (circular and linear light see the same delayed index), b = 0.25 is fused silica,
b = 1.5 is a purely orientational response (`h_a = -h_b/3`; circular light sees a quarter of the linear index change). Values outside 0 - 1.5 are errors.
An explicit `anisotropicFraction` on `linAgrawal` (even 0) replaces its own fa/fb/fc split. The built-in liquid CS2 uses the `orientational` kind.

### Cubic Kerr anisotropy (XPW)

For a cubic (m3m) crystal chi(3)_ijkl = chi1122 (d_ij d_kl + d_ik d_jl + d_il d_jk) + Delta d_ijkl; sigma = Delta / chi1111 = (chi1111 - 3 chi1122) / chi1111
(`kerrAnisotropy` on the material or the element; BaF2 -1.2, UNVERIFIED). The electronic Kerr index change becomes
`n2 [ (1 - sigma) (isotropic Maker-Terhune) + sigma sum_a u_a |u_a.A|^2 (u_a.A) ]` with `u_a` the transverse components of the cubic axes [100], [010], [001]:
`n2` is the value for light polarised along [100]. Orientation on the element: `kerrCutAxis` ([h, k, l], default [0, 0, 1]) and `kerrAzimuthDeg` (angle of the lab
x axis from the in-plane reference axis: the projection of [100], or of [010] for a [100] cut; the crystal [100] then lies at minus that angle in the lab frame).
A linearly polarised pulse then makes the orthogonal polarisation (cross-polarised wave, XPW): the vector propagation is used automatically. In the [001] cut with the
input at angle beta from [100] the Delta term drives `-(sigma/4) sin(4 beta)` into the orthogonal polarisation, so at low conversion
`E_perp / E = (B sigma sin(4 beta) / 4)^2 / sqrt(3)` for a Gaussian pulse (B = k0 n2 I0 L): zero at 0 and 45 degrees, maximal at **22.5 degrees**. The SPM index goes
as `n2 [1 - (sigma/2) sin^2(2 beta)]`. In the [011] cut (lab x = [100], y = [01-1]) the drive is `sin b cos b (sin^2 b / 2 - cos^2 b)` (zero at 54.7 degrees).
Only the electronic share `1 - fR` is anisotropic. Implemented for the 1-D envelope model (scalar-free vector solver, CPU); a `transverse` model or carrier-resolved
propagation with sigma != 0 is an error, never silently ignored. Tests: `MaterialsCrystalsTests`.

### Critical power

The reported ratio of peak power to critical power (`criticalPowerRatio` in the element table, `peakPowerOverCritical` for
spatial models) uses P_cr = 3.77 lambda^2 / (8 pi n0 n2) (Marburger 1975, Gaussian beam; lambda is the carrier wavelength
in vacuum, n0 the linear index at the carrier). Other sources quote 3.72 for the collapse threshold (Fibich and Gaeta,
Opt. Lett. 25, 335 (2000)); the two differ by 1.3 %. The engine uses 3.77 everywhere, so the ratio is comparable between
elements and with the textbook value. A ratio above 0.3 raises a warning.

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


## Limits that the engine reports

* **Spectrum reaching zero frequency.** The envelope model has no field at ω <= 0. A pulse of a few cycles (10 fs at 2 µm is 1.5) broadened by SPM (B = 3) puts part of its spectrum there; the engine drops that part, books it as `spectrum at non-positive frequency` (not `numerical`) and warns. Use longer pulses or less nonlinearity; the result is outside the model's range.
* **Delayed responses longer than the window.** The index change of a delayed response (Raman, orientational) lasts its memory (the time after which 1e-4 of its weight remains: 9.2 τ for exp(−t/τ)) after the pulse. The auto grid adds it to the window; a fixed window that is too short is warned about ("Grid looks too small").
* **Phase-mismatched χ(2) crystals.** The integrator keeps the mismatch phase Δk·dz below 0.5 rad per step, so the generated field of a thick, strongly mismatched crystal (Δk L = 370 rad) follows sin²(Δk L / 2).
* **Element-level `selfSteepening`** on fibres and bulk elements is honoured (it used to be read only from override rules).
