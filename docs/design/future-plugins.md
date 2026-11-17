# Future plugins and larger core updates (not scheduled)

Internal page ([folder index](README.md)). Designs that are worked out but not scheduled. Each section says whether it is a **separate plugin**
or a **core update**. Sizes are focused agent-days (S ≈ 1, M ≈ 2-3, L ≈ 4-6, XL ≥ 7). Decisions referred to are in [decisions.md](decisions.md);
scheduled work is in [roadmap-internal.md](roadmap-internal.md).

| design | class | first tier worth building |
|---|---|---|
| C. Fibres: A_eff(ω), tapers, saturated rate-equation gain, multimode tiers | core update | C1 A_eff(ω) (M) |
| D. Fitting estimators and operators (low-rank, learned, ensemble Kalman, Bayesian optimisation, spatio-temporal operators, backward through gain, autodiff) | plugin `fitting` | D2 Bayesian optimisation (M) |
| E1. Crystal design | separate plugin `crystaldesign` | tiers 1 and 2 |
| E2. Coating / dielectric design | separate plugin `coatingdesign` | tier 1 analysis (M), most useful to multipass cells |
| F. Repetition and Pockels cells | core update | a `pockelsCell` element and a pulse picker |
| Hollow-capillary (Marcatili) gas waveguide | core update | needs C1 |
| Ray-based ASE for bulk crystals and disks, parasitic lasing | separate plugin | builds on `Gain/AseModel.cs` |
| Live-data plugin, ML / PINN surrogate plugins, NuGet package, OpticStudio User Extension | separate plugins / packaging | after 1.0 |

## C. Fibres (core update; one serial worktree: all three touch the steppers and `CompilerFiber.cs`)

**C1 frequency-dependent A_eff (M).** Table or expression for `effectiveAreaM2` / `modeFieldDiameterM`; Lægsgaard
pseudo-field (Opt. Express 15, 16110 (2007)): nonlinear factor (1 + Ω/ω0)·[A_eff(ω0)/A_eff(ω)]^{1/4}. Tests: constant
table equals today's run (1e-12); photon number conserved without Raman (1e-6); a linear A_eff slope shifts τ_shock as
in Agrawal §2.3 and the Gaussian shock distance (Anderson & Lisak, PRA 27, 1393 (1983)), 5 %.

**Multimode, tiers (owner chooses):**

| tier | what | reference | size |
|---|---|---|---|
| T0 | GRIN MMF with the existing `indexProfile`: example + tests | self-imaging period π a/√(2Δ); geometric parametric instability (Krupa et al., PRL 116, 183901 (2016)) | S |
| T1 | few-mode GMMNLSE (Poletti & Horak, JOSA B 25, 1645 (2008)), `multimodeFiber` element, per-mode betas, overlap tensor given or analytic for HG/LG | one mode equals `fiber` (1e-10); HG00/HG10 overlap ratio 1/2 exactly; linear coupling cos² exchange | L |
| T2 | built-in mode solver (step-index LP, finite differences) feeding T1 and C1 | LP01 b(V) (Gloge 1971) | XL |

**C2 tapers (M).** Betas as expressions in z (or a `taper` table). The linear operator is diagonal in ω, so the
interaction picture stays exact with exp(i[B(ω,z2) - B(ω,z1)]), B = ∫β dz. Tests: GDD = ∫β2 dz (1e-9); adiabatic
soliton in exponentially dispersion-decreasing fibre, T(z) = T0 e^{-z/L} (Chernikov & Mamyshev, JOSA B 8, 1633 (1991)), 3 %.

**C3 saturated gain (L, needs C2).** `gain.kind: "rateEquations"`: steady-state two-level inversion from average
signal and pump power (co-pumped initial value, counter-pumped boundary value), g(ω,z) as a z-dependent linear term,
iterated with the propagated spectrum; reuses `GainCrystal`. Yb:silica cross-sections quoted from Paschotta et al.,
IEEE JQE 33, 1049 (1997) (read from the paper, not recalled). Tests: closed-form two-level steady state (Saleh et al.,
IEEE PTL 2, 714 (1990)) to 1e-4; exact photon balance; small-signal limit equals `gainDbPerM`. ASE out of scope here; `Gain/AseModel.cs` (session D) is the shared solver: use a fibre's mode as the bundle (Ω = λ0² / (n² A), p = 2) and its Γ-weighted N2.

## D. Fitting plugin extensions (plugin `fitting`; all inside `plugins/fitting/src`, no packages: everything hand-written)

| item | design | reference test | size |
|---|---|---|---|
| D1 low-rank operator | `lowRank`: A_out = Σ u_k ⟨v_k, A_in⟩, Tikhonov inverse | recover known singular values (1e-8) | M |
| D1 learned operator | small MLP (dense + tanh) → spectral phase / amplitude; weights from NPZ; hand-written Adam and backprop | gradient vs finite differences (1e-6); fits a known map | L |
| D2 ensemble Kalman | ES-MDA (Emerick & Reynolds 2013) | linear-Gaussian posterior equals Kalman within 3/√N | M |
| D2 Bayesian optimisation | GP (Matérn 5/2, Cholesky) + expected improvement | Branin minimum 0.397887 within 1e-2 in ≤ 60 evaluations | M |
| D3 spatio-temporal operators | `pulseFrontTilt`, `spatialChirp`, `angularDispersion` on cartesian profiles and the tracked beam | set value read back by the `spaceTime` instrument | M |
| D4 backward beyond linearisation | exact inverse Frantz-Nodvik through saturated gain; χ(2) by shooting (LM) from the linearised start | forward(backward(y)) = y (1e-6); depleted SHG tanh² inverted analytically | L |
| D5 autodiff | problem-specific adjoints inside the design plugins first (TMM and QPM have cheap analytic gradients), then a reverse-mode layer over the linear terms; adjoint NLSE later | adjoint vs finite differences (1e-7) | L |

The roadmap's condition for autodiff ("if inverse design becomes a goal") is met by the two design plugins below.

## E. Two new plugins (separate plugins)

**Shared architecture (decision E11).** Both need estimators that live in the fitting plugin. Recommended: move
`IEstimator` / `FitProblem` to `UltrafastEngine.Core/Plugins` as public interfaces, so plugins never require plugins.
Both register through `IPlugin.Register`: a CLI command `ultrafast design ...`, a `"design"` run-spec key, elements and
result extensions (`Plugins.cs:105-133`). Each emits plain `.ufs` objects, so a design is re-checked in the core.

### E1. Crystal design (`plugins/crystaldesign`) -- feasibility

**Feasible:** designing *what a crystal does* from known physics. **Not feasible:** predicting χ(2) or n(λ) of a new
material from its structure (that needs DFT / ab-initio codes, research in itself).

| tier | what | references for tests | size |
|---|---|---|---|
| 1 custom crystal | Sellmeier fit from n(λ) data; point-group-allowed d tensors for all 21 non-centrosymmetric classes with Kleinman reduction; Miller's-rule scaling of d with wavelength; phase-matching maps; emits a `"general"` crystal (no core change) | counts of independent coefficients (Boyd §1.5); centrosymmetric and 432 give zero; LiNbO3 3m reproduces the built-in deff (1e-12); Miller scaling of d33 against Shoji et al., JOSA B 14, 2268 (1997) | M |
| 2 QPM domain engineering | chirped, apodised, aperiodic poling, cascaded THG, adiabatic SFG/DFG; Fourier design of H(Δk) = ∫d(z)e^{iΔkz}dz; core adds `qpm.dutyExpr` and `qpm.wallsM` | uniform QPM η = (2/π)² η_PM; duty cycle sin²(πD) (Fejer et al., IEEE JQE 28, 2631 (1992)); chirp acceptance ≈ \|dK/dz\| L; Landau-Zener adiabatic conversion (Suchowski et al., PRA 78, 063821 (2008)); chirped-SHG compression (Arbore 1997) | L |
| 3 1-D lattices / superlattices | isotropic TMM reusing `ThinFilm`; undepleted SHG in multilayers (Bethune 1989); 4x4 Berreman for anisotropic layers only if needed (XL) | Bragg gap (4/π)arcsin((nH-nL)/(nH+nL)); single layer equals Bethune's closed form | L-XL |

Most value for least risk: tiers 1 and 2. Tier 2 is where "our own crystals with real physical responses" becomes real
hardware (custom PPLN / PPKTP masks can be ordered).

### E2. Coating / dielectric design (`plugins/coatingdesign`) -- feasibility: high, most useful to the MPC work

The core already has an Abelès TMM with complex indices, s/p and angle, so this is mostly design and optimisation.

| tier | what | references for tests | size |
|---|---|---|---|
| 1 analysis | R, T, phase, GD and GDD from analytic derivatives, band × angle × s/p sweeps, in-stack \|E(z)\|² for LIDT hot spots; output `stack` JSON | quarter-wave stop band and peak R (Macleod) 1e-12; Poynting flux constant in a lossless stack; Gires-Tournois GD (Kuhl & Heppner 1986); equals core `ThinFilm` (1e-13) | M |
| 2 design | gradual evolution + needle optimisation (Tikhonravov et al., Appl. Opt. 35, 5493 (1996)) with analytic thickness gradients; merit on R/GD/GDD over band, AOI, polarisation; chirped, double-chirped (Szipöcs 1994, Kärtner 1997), GTI mirrors | two-layer V-coat AR thicknesses (Macleod); a DCM reaching a target GDD at 950-1110 nm, re-evaluated in the core | L |
| 3 tolerances | Monte Carlo of thickness / index errors, GDD-ripple percentiles | small-σ variance equals the delta method | M |
| core hook | `CoatingFactory.Register(kind, factory)`, like `TransformOperators.Register` | | S |

For MPC work: dispersive mirrors (a GDD per bounce) and a mirror's reflectance edge become physical stacks,
Kramers-Kronig-consistent by construction, instead of an ideal `gdd` coating.


## Papers still to do, by topic

| topic | candidates (first pass) |
|---|---|
| MPC / NER | Escoto 2024 (CLEO, NER heatmaps); Escoto 2022 (JOSA B, compression quality); Song 2022 (HPLSE, NER in a solid-state MPC); Pfaff 2022 (OE, thin-disk MPC + NER); Lavenu 2018 / Kaumanns 2018 (gas MPCs) |
| Yb lasers | Seidel 2022 (LPR, Yb chain + MPC); a Yb:KGW / Yb:YAG regenerative or thin-disk amplifier paper with gain narrowing and output energy |
| Ti:sapphire | Spence, Kean & Sibbett 1991 (KLM oscillator, 60 fs); Backus et al. 1998 (RSI, CPA design numbers); a regenerative amplifier with gain narrowing |
| OPA / OPCPA | Dubietis, Jonušauskas & Piskarskas 1992 (first OPCPA); Cerullo & De Silvestri 2003 (NOPA bandwidth); an OPCPA chain paper with stage gains and bandwidths |
| fibres / capillaries | Dudley, Genty & Coen 2006 (supercontinuum); Gordon 1986 (self-frequency shift); Nisoli 1997 (hollow fibre, 5 fs); Travers 2019 (soliton self-compression) |
| χ(2) | Boyd & Kleinman 1968 (focused SHG); Bache, Moses & Wise 2007 (cascaded quadratic compression) |
| beams / filaments | Bespalov & Talanov 1966; Marburger 1975 / Couairon & Mysyrowicz 2007 |
| contrast | Jullien 2005 (XPW, needs item 10); ASE pedestal in a CPA chain (with decision 6) |
| fitting showcases | lab-mpc-ner-contrast (decision 9); Song 2022 spectra; Hollenbeck & Cantrell 2002 Raman response |


## F. Repetition and Pockels cells (core update; sketch)

*What the engine does today.* Most of it is single shot. Gain and amplifier elements with a `repRateHz` (on the pulse, or on the element, which then acts as an ideal
pulse picker) iterate the pump-amplify cycle to a steady state with a fast energy model, then propagate the last pulse with the full model; period doubling is detected
and warned about ([gain](../reference/gain.md)). A regenerative amplifier is `geometry: "regenerative"` with a number of round trips. `grid.tail` carries delayed light.
*Not there:* a **switch model** (a Pockels cell, a pulse picker with finite contrast, a switching window with rise time, the leakage of a regen's cavity switch into
neighbouring pulses). Time between pulses is only the repetition period of the gain model.

*Why it matters.* The Pockels cell decides which pulse goes where (pulse picking, regen switch-in and switch-out, cavity dumping), and its imperfections are the
contrast problems the engine already cares about: finite extinction (1e-3 to 1e-2 leakage), finite rise and fall time, voltage-dependent retardance, and in a regen the
round-trip number that sets energy and pre-pulse level. Repetition also decides thermal lensing (average power), what a DM or SLM sees (averages, not single shots) and
tiled-array phase noise (a rate question).

*Design sketch.* (1) A `pockelsCell` element: a voltage-controlled waveplate pair with a polariser, crystal (KD*P, BBO, RTP, LN) with V_pi, length, extinction ratio and a
voltage waveform V(t) (rise, flat top, fall, delay), built from the existing Jones elements. (2) A pulse-train layer: N pulses at the repetition period, each with
transmission T(V(t_k)), the full propagation for the pulse that matters, a macro-time table of energy and pre- and post-pulse leakage per pulse. (3) A regen switch with
switch-in and switch-out in round trips and the cell's edge times, so that the pre-pulse and the cavity-dump contrast come out of the model. (4) Examples: a pulse-picker
contrast and a regen with switching. Open for the owner: which switch first (picker, regen, cavity dump), and whether switch contrast is something to fit with the twin.


## Rules for work on these items

One working branch per item (`dev/...`), merged to `main` after `/verify` and a green CI. Shared files (changelog, status, roadmap pair, validation matrix) are updated by whoever merges. Hot files kept sequential: `Nonlinear.cs` (speed work, then waveguides, then GPU coverage), `CrystalElement.cs` (crystal design tier 2), `BuiltInElements.cs` (regenerate after each merge), `Gain/` (rate equations only after the waveguide work).
