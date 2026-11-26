# Reference audit B (work package C1b)

Pages: `docs/reference/` nonlinear.md, ionization.md, delayed-light.md, gases.md, fibers.md, gain.md, oscillators.md.
Date: 2026-11-25. Method: every factual claim (equations, signs, normalisations, key names, defaults, units, behaviour,
validated numbers, test names) was checked against `Schema/BuiltIn*.cs` (keys, defaults), `Physics/`, `Materials/`,
`Optics/`, `Lasers/`, `Gain/`, `Engine/Compiler*.cs`, `Engine/Executor.cs`, the material catalogue JSON in
`Data/materials/`, and the tests in `tests/UltrafastEngine.Tests` and `python/tests`. Generated files were not read.
Only problems are listed. Severity: wrong (contradicts the code), stale (old numbers, versions, history, dead links),
unclear (ambiguous or inconsistent with itself or with the schema), missing (important behaviour or key not described).
Line numbers are those of the page (`cat -n`). Equation errors are marked **EQUATION**.

Checked and found correct (not listed below): the vector Kerr / Raman terms and the coherent term; circular / linear 2/3 and
(1-fR) 2/3 + fR (1 - b/2); the NER rotation coefficient; Kerr-lens ODE, K, P_c and the 1.06 ratio; P_cr = 3.77 and the 0.3
warning; sech^2 and Gaussian B-integral fractions; chi(2) Delta n = d E / n0 and the 4.5 omega0 grid; Hollenbeck mode-2
widths; Drude index, rho_c, avalanche cross section; solid band gaps and masses of the listed materials; gas catalogue
values (B, delta alpha, spin weights, Ip range, n2); Frantz-Nodvik scale; stress depolarisation formulas; thermal-lens
focal lengths (GainTests.cs:162); duct ABCD matrix; echo delays 61.98 / 92.7 / 123.96 ps; TIS formulas; surface in-mode share;
ripple energy 1 - sqrt(2/pi) s + s^2/2; QML parameter; area theorem 3.53.

## nonlinear.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| N1 | :36 "the Raman part adds 7 % to the rotation of the electronic part" | Raman term fR b/2 = 0.0225 against electronic (1-fR)/3 = 0.2733 is 8.2 % of the electronic part (7.6 % of the total). The 3 % for circular light is right (0.7042 / 0.7267). | formula in NerTests.cs:113-123 (`Rotation_WithAnisotropicRaman_UsesTheAnisotropicShare`) | unclear | fixed (8 % of the electronic rotation, with the numbers 0.0225 / 0.273; the 3 % for circular light kept) |
| N2 | :74 `linAgrawal` "`fa`, `fb`, `fc`, `taub` adjustable" | the key is `taubS` (seconds), not `taub`. | BuiltInInstruments.cs:199 | wrong | fixed (`taubS`; the Raman table lists the `linAgrawal` keys with their defaults) |
| N3 | :107 "`E_perp / E = (B sigma sin(4 beta) / 4)^2 / sqrt(3)`" | the test compares an energy fraction ("XPW fraction"); the expression is quadratic in B, so it cannot be a field-amplitude ratio. `E` is ambiguous (field or energy). | MaterialsCrystalsTests.cs:200-213 (`CubicKerr_Xpw_FollowsSin4BetaSquared_ForTheCutAlong001`) | unclear | fixed ("XPW energy fraction", orthogonal energy over input energy, as the test computes) |
| N4 | :100 heading "Cubic Kerr anisotropy (XPW)" | the schema description points to `nonlinear.md#cubic-anisotropy-xpw`, which is not the heading's anchor (`#cubic-kerr-anisotropy-xpw`). The schema also points to `docs/reference/solvers.md`, which does not exist. | BuiltInOptions.cs:227, :169; `ls docs/reference` | stale | fixed (heading "Cubic anisotropy (XPW)" gives `#cubic-anisotropy-xpw`; an `<a id="cubic-anisotropy">` keeps the anchor of the `Physics/Nonlinear.cs:27` comment; `materials.md` now links the new anchor) and code: the `stepper` description links `solvers.md` (already C4-3 of audit a) |
| N5 | :172-173 "B-integral reported per element is the integral of k0 max_t(Re dn) dz" | contradicts :46-51 (peak over the pulse of the power-weighted effective index, all chi(3) terms, `Math.Abs(Im(...)/(k0 p))`). Two definitions on one page. | Physics/Nonlinear.cs `PeakIndexChange` (~line 208) | unclear | fixed (one definition, in a new "B-integral" section, as `PeakIndexChange` computes it; the steppers sentence removed) |
| N6 | :180 "N ~ 2.4 higher-order soliton against pyNLO to 0.05 % (0.8 % with Raman)" | tests assert 0.3 % (L1) with and without Raman, 0.5 % with gain; `testing.md:115` says "< 0.3 % (gain 0.5 %)". | python/tests/test_pynlo.py:66, :109; docs/testing.md:115 | stale | fixed (0.3 % L1 with and without Raman, 0.5 % with gain, as `test_pynlo.py` asserts) |
| N7 | :89 "engine and pyNLO agree to 0.05 %" (grid-normalised kernel) | the test comment says 3e-4 (0.03 %) at 16384 pyNLO points. | python/tests/test_pynlo.py:30-31 | unclear | fixed (the 0.05 % sentence removed; the pyNLO agreement is quoted once, from the test assertions) |
| N8 | :166-173 Steppers | the key is `stepper.method` ("adaptive" / "rk4ip"), `maxPhasePerStepRad` (default 0.01) is the fixed step control and, for the adaptive stepper, the initial step; `minStepM`, `maxStepM`, `maxSteps`, `steps` are not named. | BuiltInOptions.cs:169-176 | missing | fixed (Keys table and Steppers section name `stepper.method`, `tolerance`, `maxPhasePerStepRad`, `maxAreaChangePerStep`, `minStepM`, `maxStepM`, `maxSteps`, `steps`) |
| N9 | :71-77 Raman table | no row for the `orientational` kind (riseS 0.2 ps, decayS 1.6 ps, default b = 1.5, used by CS2); it appears only in prose at :93-96. | BuiltInInstruments.cs:203-206 | missing | fixed (`orientational` row: riseS 0.2 ps, decayS 1.6 ps, b = 1.5, CS2; a default-b column for every kind) |
| N10 | :60, :188 "Before 2026-11-24 the GPU path reported 0"; "(it used to be read only from override rules)" | change-log history inside a reference page. | page text | stale | fixed for the self-steepening and the 5/6 history (removed); kept, because `docs/style.md` allows one such parenthesis for an earlier wrong result users may have: "Before 2026-11-24 the GPU path reported 0" |
| N11 | :142-147 carrier-resolved model | `physics.fieldHarmonic` default 3 and its meaning are not stated on the page. | BuiltInOptions.cs:124 | missing | fixed (`physics.fieldHarmonic`, default 3, in Keys and the carrier-resolved section) |

Verdict: physics and equations are right; one wrong key name (`taub`), stale validation numbers, two definitions of the B-integral.

## ionization.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| I1 | :47 "`absorption: false` removes the inverse-bremsstrahlung term of the Drude index only"; :50 the ionisation loss "stays local in time"; :52 "With `absorption: false` neither is applied" | with `absorption: false` the ionisation energy loss `dnIm += loss / I / (2 k0)` is also dropped (it sits inside `if (Absorption)`), so ionisation then takes no energy from the pulse. The schema says "Plasma and ionisation losses". | Physics/Ionization.cs:327-334; BuiltInOptions.cs (`absorption`) | wrong | fixed (audit right: `absorption: false` removes the inverse bremsstrahlung and the ionisation loss, `Ionization.cs:327-334`) |
| I2 | :52 "before 2.2.1 it did" | the product is 1.0.0 (alpha 1.0.0a1); version 2.2.1 never existed for a user. | AGENTS.md "What this is" | stale | fixed (removed) |
| I3 | :53-54 "(avalanche) on by default for solids" | for gases the default is `!m.IsGas \|\| spec.Has("collisionTimeS")`: giving `collisionTimeS` on a gas switches the avalanche on. Not stated. | Physics/Ionization.cs:440 | missing | fixed (Keys: avalanche on for solids, and for a gas only when `collisionTimeS` is given) and code: C4-b6 |
| I4 | :22-24 solids list | only fused silica / quartz mass (0.635) is given; all other listed solids use m* = 0.5, N = 2.1e28 m^-3 (generic entry), tau_c 1 fs, tau_rec 150 fs, all `unverified`. | Data/materials/solids/*.json; defaults/SolidIonizationDefaults.json | missing | fixed (a table of the catalogue values); audit partly wrong: the valence densities differ (Sapphire 2.3e28, CaF2 2.4e28, MgF2 / YAG / N-BK7 2.0e28 m^-3) and the generic entry `defaults:solidIonization` uses m* = 0.635, not 0.5 |
| I5 | :56-57 "before 2026-11-17 it was accepted there but not passed ..." | change-log history in a reference page. | CompilerComponents.cs:30 | stale | fixed (history removed; `plate`, `window`, `ndFilter` listed among the elements that take `ionization`) |
| I6 | :5-9 "per element or for the whole run" | the `fiber` element also takes `ionization` (hollow-core gas fibres) and forwards it to the bulk compile; the page never mentions fibres. | BuiltInElements.cs:180; Engine/CompilerFiber.cs (forwarded keys) | missing | fixed (hollow-core `fiber` named in the first paragraph) |

Verdict: the plasma physics is right and well tested; one real omission (`absorption: false` also removes the ionisation loss), version/history leftovers.

## delayed-light.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| D1 | :171-172 "the echo is proportional to `T(lambda)^2 r_b(lambda)`" **EQUATION** | the echo **amplitude** is E_1 = t t' r_b, magnitude T r_b (|t|^2 = T). The **intensity** is T^2 R_b (as :168 says correctly). The page's "T^2 r_b" mixes the two; the cited test shows "ten times stronger" for a ten times larger T, an amplitude ratio. | Optics/MirrorSubstrate.cs; DelayedLightTests.cs:225-246 (`|E1| = T r_b`) | wrong | fixed (amplitude T r_b, intensity T^2 R_b; the test sentence says it checks `|E_1| = T r_b` to 1 %) |
| D2 | :204 "The ripple is a function of the absolute angular frequency and the seed" ; :201 `N(w) = sqrt(2/K) sum cos(w tau_k + phi_k)` | the code evaluates `cos((w - w0) tau_k + phi_k)` with w0 the run carrier, so the realisation depends on the carrier; main and tail grids agree, but two runs with different carriers get different ripples. | Optics/DelayedTerms.cs:30 | unclear | fixed (`N(w)` written with `w - w0`; the realisation depends on the carrier, the same on main and tail grids) |
| D3 | :194-207 ripple keys | `components` (default 64, at least 8) is not described. | Engine/CompilerSubstrate.cs:157; BuiltInElements.cs:41 | missing | fixed (`components`, default 64, at least 8, in Keys and the ripple section; the scatter minimum of 16 too) |
| D4 | :215-218 `H(w) = sqrt(1-f) + sqrt(f) s(w) sum c_k exp(i w tau_k)`; "on average power is conserved" | the code uses `exp(i (w - w0) tau_k)`; with `spectralExponent` n != 0 the mean power is (1-f) + f (w/w0)^n, conserved only at the carrier. | Optics/DelayedTerms.cs:172-173 | unclear | fixed (`exp(i (w - w0) tau_k)`; mean power (1 - f) + f (w/w0)^n, conserved at the carrier and for n = 0) |

Verdict: ledger, window planning, ghost, TIS and echo formulas are right; fix the echo-strength sentence (amplitude T r_b, intensity T^2 R_b).

## gases.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| G1 | :48-51 "Change in 2.1.8 ... Before 2.1.8" | version label and change history of a release that does not exist for users (1.0.0a1). | AGENTS.md | stale | fixed (removed) |
| G2 | :30 "dephasing time T2 ~ 1/p" | T2 = T2(1 bar) / p x sqrt(T / 293.15 K); the per-species T2 at 1 bar (N2 100 ps, O2 80 ps, CO2 50 ps, H2 300 ps) is not given. | Materials/Gases.cs:106; Data/materials/gases/*.json | unclear | fixed (T2 scaling with p and sqrt(T / 293.15 K); T2 at 1 bar in the catalogue table) |
| G3 | :40-41 "N2 at 1 atm approx 2.7e-23 m^2/W, three times the electronic part" | the formula gives about 2.6e-23 (1 bar, 293 K) and 3.5 x the electronic n2; the test asserts only the isolated-molecule limit to 3 % and "> 2 x electronic", not the quoted number. | GasTests.cs:15-33 | unclear | fixed (2.6e-23 m^2/W at 1 bar and 293 K, 3.5 x the electronic n2 at that density; the tests quoted as they assert) |

Verdict: accurate; remove the version note and state T2 scaling and values.

## fibers.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| F1 | :33-39 Raman table | the key `ramanFraction` (default: the model's fR) is not mentioned; for an object `raman` the fR comes from the object, else from `ramanFraction`. | BuiltInElements.cs:175; Engine/CompilerFiber.cs:~150-165 | missing | fixed (`ramanFraction` and the object `fR` precedence) |
| F2 | :50-52 loss and gain tables | outside the tabulated wavelengths the loss is clamped to the end values and the gain spectrum is 0; `lossDbPerKm` wins over `loss` if both are given. | Engine/CompilerFiber.cs (`PerWavelengthOrNumber`, `gainShape`) | missing | fixed (loss held at the end values, gain 0 outside the table, `lossDbPerKm` wins) |
| F3 | :59-61 reported `dispersiveWaveWavelengthM` | only reported when the dispersion comes from Taylor `betas` (not from a `dispersion` curve or a `material`), and only for beta2 < 0 and N > 1. | Engine/CompilerFiber.cs `FiberMetrics` (`betas != null`) | missing | fixed (Taylor coefficients only, beta2 < 0 and N > 1) |
| F4 | :30 "`gamma`, or `n2` with `effectiveAreaM2` / `modeFieldDiameterM`" | `n2` defaults to 2.6e-20 m^2/W; with `gamma` alone A_eff defaults to 1e-11 m^2 (bookkeeping only); `n2` without an area is an error. | BuiltInElements.cs:166-170; CompilerFiber.cs:~120-135 | missing | fixed (n2 default 2.6e-20, area 1e-11 m^2 with gamma alone, n2 without an area is an error) |
| F5 | :22 "beta2(omega) is a natural cubic spline" | spline only for 4 or more table points, otherwise linear interpolation. | CompilerFiber.cs:264 | unclear | fixed (spline for 4 or more points, linear otherwise, held outside) |
| F6 | :3-6 | keys `nonlinear`, `timeResponse`, `replaceKerr`, `transverse`, `ionization` are forwarded to the inner bulk element and are not described. | CompilerFiber.cs (forwarded keys) | missing | fixed (Keys: the keys passed to the inner bulk element) |

Verdict: correct for what it says (units, T0 = FWHM / 1.763, phase matching, pyNLO agreement 0.3-0.5 % per the tests); several keys and edge behaviours undocumented.

## gain.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| A1 | :3-5 "two elements: `gain` ... and `amplifier`" | no section describes the `amplifier` element: `geometry` (multipass / regenerative), `passes` (default 4), `betweenPasses`, `roundTrips` (default "auto"), `passesPerRoundTrip` (2), `maxRoundTrips` (300), `cavity`. The schema links to `reference/gain.md#the-amplifier-element`, an anchor that does not exist. | BuiltInElements.cs:294-300 | missing | fixed (section "The amplifier element"; the anchor `#the-amplifier-element` exists) |
| A2 | :45-52 pump table | `pumpDurationS` is missing; `passes` default is 1 (the table says only "2: ..."); the pump `kind` default is `cw`. | BuiltInElements.cs:317-324 | missing | fixed (pump `passes` default 1, `kind` default cw or pulsed with `energyJ`, `order` default 4) and code: `pumpDurationS` is declared in `pump` but read on the element (C4-b3); `order` default undeclared (C4-b5) |
| A3 | :51 "`passes` 2: the unabsorbed pump is sent back" | reads as if 2 were the default; it is 1 (single pass). | BuiltInElements.cs:323 | unclear | fixed (1 default, 2 sends the pump back) |
| A4 | :65-79 amplification | the keys `spm` (default true), `host`, `dopingProfile`, `temperatureK`, `ghosts` are not described; the SPM step uses the host n2 only (no Raman, no self-steepening). | BuiltInElements.cs:300-315; Gain/GainCrystal.cs:466-500 | missing | fixed (Keys: `spm`, `host`, `dopingProfile`, `temperatureK`, `ghosts`; SPM is instantaneous Kerr only) and code: the element `n2` is read but not declared (C4-b4) |

Verdict: physics (Frantz-Nodvik, rate equations, ASE, thermal lens, stress) matches the code and tests; the amplifier element is undocumented and its doc link is dead.

## oscillators.md

| # | claim (page:line) | what the code does | evidence | severity | resolution |
|---|---|---|---|---|---|
| O1 | :11 `"smallSignalGain": 1.5`; :25 "The gain is a ln power gain per round trip, g(E) = g0 / (1 + E/E_sat)" | `smallSignalGain` is the **linear** power gain G (must be > 1); the code takes g0 = ln G. `smallSignalLnGain` is the alternative key. The page does not say which of G or g0 the key holds. | Lasers/Oscillator.cs:33-35 | unclear | fixed (linear power gain G > 1, g0 = ln G; `smallSignalLnGain`) |
| O2 | :30-33 "`passesPerRoundTrip` (2) ... `relaxation` (0.2)" | both are read from the `gain` object; the schema declares them as element-level keys of `oscillator`. The page and the code agree, the schema does not. | Oscillator.cs:550; BuiltInElements.cs:370 | unclear | fixed (the page says the keys work only inside `gain`) and code: C4-b2 |
| O3 | :14, :51 `roundTrips: {max, tolerance}`, "`checkEvery` (25)" | `max`, `tolerance`, `checkEvery`, `snapshots` are read only inside the `roundTrips` object (a plain number sets the maximum); the schema lists `checkEvery`, `tolerance`, `snapshots` as element-level keys that the code ignores. Defaults (max 5000, tolerance 1e-5) are not on the page. | Oscillator.cs:153-154, :538-540; BuiltInElements.cs:367-370 | unclear | fixed (`roundTrips` object with its defaults; the element-level keys have no effect) and code: C4-b2 |
| O4 | :51-53 "output energy and FWHM change by less than `tolerance` per round trip" | energy: change over the check interval <= tolerance x E x checkEvery; FWHM: <= 10 x tolerance x FWHM x checkEvery + dt (ten times looser); centroid: < 0.05 d omega; three checks in a row. | Oscillator.cs:318-322 | unclear | fixed (the three criteria as `Oscillator.cs:318-322` applies them) |
| O5 | :43-45 `gridPoints` (4096) | must be a power of two >= 64; `outputCoupler` default 0.05 is not stated. | Oscillator.cs:524, :537 | missing | fixed (power of two >= 64; `outputCoupler` default 0.05) |
| O6 | :112 resonator `geometry` | default is `"ring"`; not stated. | Lasers/Resonator.cs:86 | missing | fixed (default `ring`) |

Verdict: formulas (absorbers, area theorem, QML) and diagnostics keys are right; the key placement and the meaning of `smallSignalGain` need clarifying.

## Totals

| page | wrong | stale | unclear | missing |
|---|---|---|---|---|---|
| nonlinear.md | 1 | 3 | 4 | 3 |
| ionization.md | 1 | 2 | 0 | 3 |
| delayed-light.md | 1 | 0 | 2 | 1 |
| gases.md | 0 | 1 | 2 | 0 |
| fibers.md | 0 | 0 | 1 | 5 |
| gain.md | 0 | 0 | 1 | 3 |
| oscillators.md | 0 | 0 | 4 | 2 |

## Code findings for WP C4

Collected by WP C2b. The pages document what the code does today; none of these was changed in code. Proposed fixes:

| id | file:line | finding | proposed fix |
|---|---|---|---|
| C4-b1 | `src/UltrafastEngine.Core/Schema/BuiltInOptions.cs:169` | the `stepper` description links `docs/reference/solvers.md`, which does not exist (same as C4-3 of audit a) | link `reference/nonlinear.md#steppers` |
| C4-b2 | `Schema/BuiltInElements.cs:357-359` vs `Lasers/Oscillator.cs:538-540, :550` | the `oscillator` spec declares `checkEvery`, `tolerance`, `snapshots`, `relaxation`, `passesPerRoundTrip` as element keys; the code reads the first three only inside the `roundTrips` object and the last two only inside the `gain` object, so the declared element keys are accepted and ignored | declare `roundTrips` as a number or an object spec `{max, tolerance, checkEvery, snapshots}` and the two cavity keys under `gain`; or read the element-level keys as well; add a test |
| C4-b3 | `Schema/BuiltInElements.cs:325` vs `Gain/GainElements.cs:801`, `:186` | `pumpDurationS` is declared in the `pump` object ("Pump pulse duration") and never read there; the code reads an element-level `pumpDurationS` (cw pumping time of a single shot, default 8 lifetimes), which `gainCommon` does not declare (an unknown key under `ULTRAFAST_STRICT_OPTIONS=1`) | read it from `pump` with the meaning "cw pumping time before a single shot" and fix the description; keep the element key through a migration |
| C4-b4 | `Gain/GainElements.cs:770` vs `Schema/BuiltInElements.cs:307-322` | the `gain` / `amplifier` element reads `n2` (overrides the host's) but `gainCommon` does not declare it | declare `n2` (m^2/W, default: the host's) in `gainCommon` |
| C4-b5 | `Gain/GainCrystal.cs:39` vs `Schema/BuiltInElements.cs:327` | the pump `order` defaults to 4 in the code; the declaration has no default; also `kind` defaults to `pulsed` when `energyJ` is given (`GainCrystal.cs:35`), the declaration says `cw` | declare `order` default 4 and `kind` default "cw, or pulsed with energyJ" |
| C4-b6 | `Physics/Ionization.cs:440` vs `Schema/BuiltInOptions.cs:141` | for a gas, giving `collisionTimeS` silently switches the avalanche on; the declaration says only "true for solids" | state it in the declaration, or make the gas default `false` regardless of `collisionTimeS` (an explicit `avalanche` key); add a test |
| C4-b7 | `tests/UltrafastEngine.Tests/DelayedLightTests.cs:206` | the test's `Reference` string quotes n_g = 1.4635 for fused silica at 1030 nm; Malitson gives 1.4627 (the test asserts about 1.462 to 1 %, so it passes either way); the string feeds the validation matrix | write n_g = 1.4627 and regenerate the validation matrix |

Physics review (WP C2b, `physics-reviewer` on the seven pages): no physics errors; fixed on the pages: the echo-delay table
(n_g 1.4627 and 61.96 / 92.70 / 123.93 ps instead of 1.4635 and 61.98 / 92.7 / 123.96 ps; also in the `engine-physics` skill),
the `linAgrawal` shape symbols (h_osc, h_boson, not the tensor parts h_a, h_b) and its default b = (fb + fc) / (fa + fb + fc),
the fibre fR fallback for an object `raman`, `loss` < 1, the amplitude-ripple clamp at 0, and ρ_n − ρ_ionised in the ionisation loss.

