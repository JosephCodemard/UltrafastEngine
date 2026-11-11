# Backlog: design that is parked for after the first release

This page was the plan for the work after 2.2.0 (written 2026-11-08). The adaptive-optics and digital-twin work from it is done
([history](history.md)); the plan for the next iteration is [next-steps.md](next-steps.md). What stays here is design that is **not scheduled**:
engineering (GPU coverage, FP32, speed), multimode and tapered fibres, fitting extensions, the crystal- and coating-design plugins, papers still to replicate,
and the repetition / Pockels-cell sketch. Sizes are focused agent-days (S ≈ 1, M ≈ 2-3, L ≈ 4-6, XL ≥ 7). Zemax is out of scope here. Owner decisions 1-11 below
still hold unless next-steps.md says otherwise.

## Decisions (owner)

| # | decision |
|---|---|
| 1 | Push to GitHub: yes. PoCL is added to CI so `GpuTests` run there (not done yet). |
| 2 | No PyPI publishing. The release workflow builds packages and wheels and attaches them to a GitHub release on a version tag. |
| 3 | **Plugins as pip extras**: `pip install "ultrafast[hitran,cuda,fitting]"`, `[opencl]`, `[coatingdesign]`, `[crystaldesign]`, `[all]`. The default Python engine is the NativeAOT library, which can only use plugins compiled into it, so: the native library in the `ultrafast` wheel is built with every non-Zemax plugin compiled in (off until used); each extra installs that plugin's Python module (`ultrafast-hitran`, `ultrafast-fitting`, ...) and its third-party dependencies (`[cuda]`: NVIDIA's NVRTC / cuFFT wheels, not on macOS); the hosted engine loads the plugin assemblies shipped in the same extra wheels. Without PyPI, extras resolve only with `pip install "ultrafast[hitran]" --find-links <release assets or dist/>`. Zemax is never an extra. |
| 4 | (delegated) **FP32 study first; then mixed precision in the existing CUDA / OpenCL back-ends; Metal only if the study shows FP32 is enough for the steps that matter.** Reason: Apple GPUs have no FP64, so Metal is worthless unless FP32 works, and the study is cheap and runs on this Mac. |
| 5 | (open-air focus) Fibre work is reshaped. **In**: frequency-dependent effective area (also needed by hollow capillaries); a **hollow-capillary (Marcatili) gas waveguide** model, the fibre most relevant to open-air post-compression; **rate-equation gain with ASE as a shared gain model**, used by the bulk `gain` / `amplifier` elements as well as fibres, because the ASE pedestal is a contrast question for CPA chains; multimode tier T0 (GRIN example). **Small**: tapers (C2). **Out for now**: GMMNLSE (T1) and a mode solver (T2). |
| 6 | ASE is included in the rate-equation gain (forward and backward ASE, spectrally resolved, as an incoherent power spectrum and as a pedestal in the contrast). |
| 7 | (asked) Recommendation: **keep the estimators in the fitting plugin**; the design plugins declare `"requires": ["fitting"]` in `plugin.json` (a new, small plugin-manager feature: load order and a clear error if missing), and `ultrafast[coatingdesign]` pulls `ultrafast-fitting` in. One home for optimisers, no core API churn; design is fitting with a target specification instead of data. |
| 8 | Escoto 2024 uses the Seidel 2022 cell as a labelled hypothesis. |
| 9 | The owner's separate lab project is not used or modified; only its recorded data files may be **copied into `papers/lab-mpc-ner-contrast/data/`** (the owner's own Pharos MPC recordings, not personal data, not Escoto's system). A separate paper-folder case `papers/lab-mpc-ner-contrast/` is built from scratch from the engine's docs: (1) fit the input and output spectra with `transform` elements; (2) put the fitted transform into the MPC model with the hitran air lines; (3) compare the predicted TOCC contrast with the recordings, then fit what remains. |
| 10 | Build: `pedestal.kind: "measured"` (a contrast trace as the input pedestal; done), an XPW model (anisotropic cubic χ(3); done in 1-D, see the roadmap for spatial and carrier-resolved), the hollow-capillary model (open). |
| 11 | Papers cover a range: MPCs / NER, Yb and Ti:sapphire lasers (oscillators, regenerative and multipass amplifiers), OPA / OPCPA chains, fibres and hollow capillaries, χ(2) conversion, beam propagation / filamentation, contrast cleaning. The goal is each paper's **headline results**; when one cannot be reproduced, its README says why (missing parameter, missing physics, a bug). |

## B. Engineering

| item | plan | Mac-verifiable | size |
|---|---|---|---|
| B1 release workflow | `.github/workflows/release.yml` on `v*` tags: linux-x64/arm64, osx-arm64/x64, win-x64 runners, each `build.sh --all --plugins fitting gpu hitran --platform <rid> --test`; smoke tests (CLI example 01 strict, wheel in a fresh venv runs 00-quickstart, C API example, `ultrafast devices`); a step failing if any archive contains `ZOSAPI*`, plus a test planting a fake DLL; publish with `gh release create` | osx-arm64 build only; YAML needs GitHub | M |
| B2 GPU at install | `ultrafast[cuda]` (nvidia NVRTC/cuFFT wheels, not on Darwin), `ultrafast[opencl]`; a gpu plugin wheel; loader finds the CUDA libs; `ultrafast devices` reports where each back-end came from. NativeAOT can only use compiled-in plugins, so choose hosted-only or per-platform native wheels | packaging + "no device" path | M |
| B3 GPU coverage | order: gas rotational Raman (today it keeps the air gaps of any MPC on the CPU), expression responses compiled to kernels, χ(2), carrier-resolved, spatial gain; each with CPU parity 1e-9 in `GpuTests` | no (needs PoCL in CI or the 3060 Ti) | XL |
| B4 FP32 study | CPU FP32-emulation mode; SPM plate, 50-transit MPC, soliton, SHG, contrast floor vs FP64; accept if energy 1e-6, spectrum L1 1e-4, floor unchanged. Then a mixed-precision mode in CUDA/OpenCL (consumer GPUs run FP64 slowly) before Metal | study yes | M (+M; Metal XL) |
| B5 speed | micro-benchmarks; pooled buffers (25 `new Complex[` in `Nonlinear.cs`); blocked SIMD Hankel; a zero-allocation-per-RHS test (`#if NET`); LightPipes timing benchmark (dev-only tool, tabled in `speed.md`) | yes | M + S |

## C. Fibres (one serial worktree: all three touch the steppers and `CompilerFiber.cs`)

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

## D. Fitting plugin (all inside `plugins/fitting/src`, no packages: everything hand-written)

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

## E. Two new plugins

**Shared architecture (owner decision 8).** Both need estimators that live in the fitting plugin. Recommended: move
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


## F. Repetition and Pockels cells (sketch, was "S6")

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
