# Candidate papers (research pass of 2026-11-05)

Working notes for the replication programme: which papers were read in full text, their parameters and headline
numbers with locations, and what is missing. Anything marked UNVERIFIED must be read in the paper before it becomes
a check. Chosen papers move into their own folder (see `docs/papers/CLAUDE.md`).


Research pass for the paper replications. Every number below was read in the full text I fetched,
unless it is marked **UNVERIFIED** (abstract only, secondary source, or not retrievable). Numbers are paraphrased with
their location; no text or figures are copied. "Read off Fig. N" = value read from a plot, needs a wide tolerance.

**Access notes (affect what could be verified).** opg.optica.org (Optics Express / OL / JOSA B / Optica) sits behind a
JavaScript bot wall (Radware), so no Optica full text could be fetched by curl or WebFetch, even for gold-OA papers.
HAL (hal.science) works with the default curl user agent (not a browser UA: Anubis challenge). arXiv and Cambridge
(HPLSE) work. Papers whose only OA copy is on Optica are marked "abstract-only" and ranked lower; the owner can read
them in a browser and fill in the gaps.

Difficulty: S = 1-D, analytic-adjacent, minutes; M = 1-D engine run with several elements or a scan; L = spatial /
new physics / long runs.

---

## Topic 1. MPC / NER post-compression

### 1.1 (rank 1) Escoto et al., "Temporal quality of post-compressed pulses at large compression factors"
* JOSA B 39, 1694 (2022), DOI 10.1364/JOSAB.453901 (Crossref; published version not opened: Optica wall). Read: arXiv 2201.05836v1
  (15 Jan 2022), https://arxiv.org/pdf/2201.05836 (full text). Caveat: the preprint, not the published version;
  numbers may have been revised in review.
* Model (Sec. 2.1, Eq. 1): pure SPM step E·exp(iB|E|²) followed by a quadratic spectral phase; no dispersion during
  broadening, no losses; Gaussian input exp(-4 ln2 t²/σ²); time normalised to the input FWHM, GDD to γ/σ².
* Headline numbers:
  - Eq. (2): compression factor C ≈ 0.59 B + 1; error < 5 % up to B = 40 rad for both the FTL and GDD-only cases;
    up to B = 100 rad < 2 % (FTL) and just above 10 % (GDD only) (Sec. 2.1).
  - Spectral broadening factor ≈ 0.88 B (quoted from Nagy et al., Sec. 2.1) - grows faster than C.
  - Energy ratio = energy within t0 ± FWHM of the main peak / total (Eq. 3); a Gaussian gives ~98 % (Sec. 2.2).
  - FTL case: intensity ratio saturates at ~10 % (Fig. 3a, grey dashed line); energy ratio ~70 % at C = 100 and
    ~65 % at C = 1000 (Sec. 3.1). GDD-only case degrades continuously (Fig. 3).
  - Secondary peaks appear at roughly C > 2 (Sec. 4.2, refers to Fig. 3).
  - Input chirp: γ/σ² = ±5 stretches the pulse ~14× (Eq. 4, Sec. 3.3); chirped inputs degrade quality faster (Fig. 6).
  - Cascade (Sec. 4.1, Fig. 10): for C_eff = C1·C2 = 50, energy ratio and peak-power boost are best at C1 = C2; the
    intensity ratio optimum is shifted to C1 > C2; two stages beat one unless C1/C2 > 10. At 80 % transmission per
    stage, two stages win for C > 30 (Fig. 11b).
  - Ten stages: C_eff = 1.31^10 ≈ 13.8 vs one stage C = 13.8, smoother pulse (Fig. 12).
  - Experiment (Sec. 3.4, Fig. 8): 2 m argon MPC, Yb:YAG Innoslab, 730 fs, 1030 nm, THG-FROG; 2 mJ: energy ratio 92 %,
    compression 11, peak-power boost ~10; 3 mJ: 83 %, 17, ~14.
* Inputs: fully specified for the model part (Gaussian, B, GDD only). Missing for the experiment: argon pressure,
  mirror ROC, number of passes, beam size, mirror GDD - **experimental part not replicable as given**.
* Engine features: pure-SPM element (B set directly) or thin Kerr plate with dispersion off, ideal compressor (GDD
  only / FTL), contrast instrument, a scan over B and over GDD, a two-stage chain.
* Difficulty S. Verdict: **highly replicable** (model part); checks: slope 0.59 ± 0.03 for B ≤ 40, energy ratio
  70 ± 3 % at C = 100 (FTL), intensity ratio → 10 %, cascade optimum at log10(C1/C2) ≈ 0 for energy ratio.
  Needs a definition match: "C at FTL" means FWHM_in / FWHM_FTL.

### 1.2 (rank 2) Song et al., "Temporal contrast enhancement by nonlinear elliptical polarization rotation in a multi-pass cell"
* HPLSE 10, e28 (2022), DOI 10.1017/hpl.2022.22, open access CC BY 4.0. Read: Cambridge PDF (full text, 6 pages).
* Headline numbers: total NER efficiency up to 53 % with the plate 18 cm from CM2 (Sec. 3, Fig. 2b); efficiency vs QWP
  angle γ rises then falls over 0-44°, optimum γ = 14° (plate at 14 and 16 cm) and 12° (18 and 20 cm) (Fig. 2a);
  -20 dB bandwidth 94.1 → 131.6 nm and FTL 44.2 → 31 fs as the plate moves away from CM2 (Fig. 3b); 52 % efficiency
  at the strongest broadening; 36 fs after -3200 fs² of chirped mirrors, compression factor 5 (Sec. 3, Fig. 4);
  MPC transmission 95 % (27.5 mW before GL2); M² 1.18 × 1.16 (Fig. 5); contrast 10⁹ measured after a Yb:KGW regen
  seeded by the cleaned pulse vs 10⁷ with the oscillator seed (Fig. 8) - the 10⁹ is a property of the regen
  chain, not of the MPC alone.
* Inputs (all Sec. 2): Yb amplifier 181 fs, 1036.8 nm, 9.3 nm FWHM spectrum, 1 kHz; 29 µJ after GL1; MPC mirrors
  ROC 300 mm, Ø 50.8 mm, low-GDD coated 950-1110 nm; spacing 525 mm; eigenmode 2w = 1.023 mm on mirrors, 0.36 mm at
  centre; 5 mm fused-silica plate (AR coated); 13 round trips (0.13 m of FS, 13.5 m of air); total dispersion
  2630 fs² (181 → 185 fs); plate at 14 / 16 / 18 / 20 cm from CM2 → mode diameter 574 → 427 µm (no Kerr lens);
  peak intensity "10¹¹ W/cm² level"; QWP2 at 90° + γ; GL2 extinction > 2·10⁵.
* Missing: n2 (use FS 2.2-2.7·10⁻²⁰ m²/W, document), B-integral, input spectral shape (assume sech² from 181 fs /
  9.3 nm: TBP ≈ 0.47, so not transform-limited; use the measured FWHM with a chirp, or a Gaussian), Raman anisotropy
  (engine default), the exact γ grid.
* Engine features: QWP / MPC (Herriott, tracked Gaussian) / thin Kerr plate / QWP / polariser, vector Kerr + NER,
  B scan via plate position, ideal compressor, efficiency vs angle scan. Fitting showcase: fit n2 (or an
  intensity scale) and input chirp to Fig. 3 bandwidths, then predict Fig. 2a.
* Difficulty M. Verdict: **replicable** (geometry complete; n2 and pulse shape assumed). Checks: γ_opt 12-14° ± 3°,
  peak efficiency ~0.5 (ordering: 18 cm best), bandwidth increases monotonically with distance, ~36 fs after
  -3200 fs² (± 10 %).

### 1.3 (rank 3) Lavenu et al., "Nonlinear pulse compression based on a gas-filled multipass cell"
* Opt. Lett. 43, 2252 (2018), DOI 10.1364/OL.43.002252. Read: HAL hal-01792750 (author version, CC BY-NC 4.0),
  https://iogs.hal.science/hal-01792750 (full text).
* Headline numbers: 160 µJ, 275 fs → 135 µJ, 33 fs (abstract), compression 8.3; cell transmission 93 %, total 85 %;
  -10 dB bandwidth 93 nm, FTL 31 fs (Fig. 2); peak power 3.25 GW, 82 % of the energy in the main lobe, peak-power
  enhancement 6.7 (text by Fig. 2); pulse at the cell output stretched to 550 fs by the gas dispersion; total B from
  their simulation 14.9 rad; spatially averaged B per round trip 0.6 rad (analytic estimate of their Ref. 15);
  M² 1.15 × 1.25 out (Fig. 3); mean spatio-spectral overlap 99.0 / 99.3 % (sim 99.9 %) (Fig. 4).
* Inputs: Yb fibre amplifier 1030 nm, 150 kHz, 24 W, 275 fs, 485 MW peak, M² 1.15 (Fig. 1); input temporal profile
  in Fig. 1b (FROG-measured, not given numerically); mirrors ROC 300 mm, 2 in; 7 bar argon with n2 = 6.5·10⁻²³ m²/W
  and β2 = 110 fs²/m as stated; P_cr = 2 GW; 34 round trips, mirror distance 286.5 mm, total path in gas 20.9 m;
  waist 220 µm (centre), 300 µm (mirrors); mirror |GDD| < 20 fs², R > 99.5 % (910-1180 nm); gas GDD 60 fs² per round
  trip; 5 mm CaF2 windows; compressor total -3100 fs².
* Missing: the input pulse shape (Fig. 1b; assume the 275 fs FWHM with sech² or Gaussian and document), the
  exact sign of the mirror residual GDD (assume 0).
* Engine features: gas (argon density scaling of n and n2, check the engine's n2 vs their 6.5·10⁻²³ at 7 bar), MPC
  with continuous gas nonlinearity and dispersion, ideal / mirror compressor, B tracking. Also a self-consistency
  check of the engine's argon β2 at 7 bar against 110 fs²/m.
* Difficulty M. Verdict: **highly replicable** (all cell numbers given). Checks: output stretched to ~550 fs, -10 dB
  bandwidth ~93 nm, FTL ~31 fs, B ~15 rad, compressed ~33 fs with -3100 fs², energy in the main lobe ~82 %.

### 1.4 (rank 4) Seidel et al., "Ultrafast MHz-rate burst-mode pump-probe laser for the FLASH FEL facility ..."
* Laser Photonics Rev. 16, 2100268 (2022). Read: arXiv 2105.05882 (full text incl. supplement S1-S7).
* Headline numbers (Sec. 2.2, supplement S1.3): three 1 mm AR-coated FS plates, 3 cm apart, in the centre of a
  ~350 mm MPC of two 200 mm-ROC spherical mirrors; 31 round trips; 115 µJ in, ~80 % through the output polariser;
  55 fs Fourier limit; compression factor > 15 to ~60 fs with a double-pass grating compressor; 372 AR surfaces
  (consistent: 3 plates × 2 surfaces × 2 passes × 31); pre-chirped variant: 80 fs FTL at 90 % transmission;
  24 h FTL mean 55.5 fs, σ = 0.4 fs within a 30 dB range (Fig. 2f); dominant side peak ~10 % of the main peak
  (Fig. 2c); 30 % waist-area variation gives ~3.5 fs duration spread (Fig. 2d). NER (Sec. 2.2, Fig. 2e): QWPs at
  entry and exit, post-pulse at 900 fs suppressed > 10×, energy at diagnostics -39 % to 35 µJ, duration ~70 fs.
* Inputs: 1030 nm, ~900 fs after the grating compressor (Sec. 2.1), up to 200 µJ per pulse from the Yb:YAG rods
  (S1.2), oscillator spectrum 4 nm (S1.1, not the amplified spectrum), mode-matched to the cell eigenmode.
* Missing: mirror GDD, beam waist (follows from ROC 200 mm and ~350 mm length: compute), amplified spectrum shape,
  B-integral, waveplate angles for NER (found empirically).
* Engine features: multi-plate MPC, NER, grating compressor, burst/thermal effects (not needed).
* Difficulty M. Verdict: **partially replicable**: the 55 fs FTL from 900 fs and the 900 fs post-pulse are good
  checks; NER numbers depend on unknown angles. Use together with Escoto CLEO 2024 (plan F).

### 1.5 (rank 5, abstract-only) Pfaff et al., "Nonlinear pulse compression of a thin-disk amplifier and contrast enhancement via nonlinear ellipse rotation"
* Opt. Express 30, 10981 (2022), DOI 10.1364/OE.455393, gold OA (CC BY) but only on Optica: **body UNVERIFIED**.
* Abstract (Semantic Scholar / FAU CRIS): 10 mJ, 3 kHz thin-disk amplifier, 840 → 38 fs after a gas MPC; NER contrast
  enhancement > 50× at 56 % optical efficiency (also cited so in Song 2022, ref. 35).
* Cell parameters (gas, pressure, ROC, passes) not read. Verdict: **pending** a browser read of the full text.

## Topic 2. Yb lasers / amplifiers

### 2.1 (rank 1) Zhang et al., "12-mJ 1-kHz cryogenically cooled rod Yb:YAG regenerative amplifier"
* Front. Phys. 12, 1383634 (2024), DOI 10.3389/fphy.2024.1383634, CC BY. Read: Frontiers PDF (full text, brief report).
* Headline numbers: 12 mJ uncompressed, 90 ps, 1030 nm, 1 kHz, 31.7 % optical efficiency at 37.8 W pump (abstract,
  Fig. 1C); output bandwidth 0.3 nm → ~5 ps Gaussian transform limit after "significant gain narrowing" (Sec. 3);
  M² = 1.1 × 1.1 at 10 mJ (Fig. 1D); CW slope 57.7 %, 121.3 W at 220 W pump (Fig. 1B); no saturation seen.
* Inputs (Sec. 2-3): seed 1.1 nJ, 53.6 MHz, 8.4 nm FWHM at 1030 nm; FBG stretcher 34 % efficient, 300 ps/nm,
  4 nm passband → ~1.2 ns; cavity 2.1 m (14 ns round trip), ~33 round trips; Yb:YAG 5 % doped, 10 mm long, Ø 10 mm,
  ~70-100 K; pump 940 nm, 1.7 mm spot; Pockels cell 20 mm BBO; beam Ø ~1.56 mm on the PC; cross section quoted
  2.3·10⁻²⁰ cm² (300 K) → 9.3-10.9·10⁻²⁰ cm² (100 K / 70 K), emission bandwidth ~9 nm → ~1.5 nm (Sec. 1 and 3).
* Missing: cryogenic spectral cross sections (needs a user gain medium), inversion / small-signal gain (derive:
  0.37 nJ in → 12 mJ out is a total gain ≈ 3·10⁷ over ~33 round trips, i.e. ≈ 1.69 per round trip, if the 34 %
  stretcher loss applies to the 1.1 nJ), cavity losses. **Inconsistency**: the conclusion quotes "495 ps" while the
  abstract and Sec. 3 say 90 ps - record, use 90 ps.
* Engine features: regenerative amplifier, user gain medium with a narrow (cryogenic) cross section, spectral
  Frantz-Nodvik, gain narrowing 4 nm → 0.3 nm.
* Difficulty M. Verdict: **replicable for the gain-narrowing check** (a model-level check: with a Lorentzian/Gaussian
  1.5 nm gain line and G ≈ 3·10⁷, does the output reach ~0.3 nm?), not for the efficiency.

### 2.2 (rank 2) He et al., "A Yb:KGW dual-crystal regenerative amplifier"
* HPLSE 8, e35 (2020), DOI 10.1017/hpl.2020.26, CC BY 4.0. Read: Cambridge PDF (full text).
* Headline numbers: seed < 0.2 nJ → 1.5 mJ after 78 round trips, 70 W peak pump (QCW, 1/3 duty, 1 kHz) (Sec. 3);
  compressed 1.2 mJ (80 % compressor), 227 fs (Gaussian fit, Fig. 9b); output 9 nm FWHM at 1032 nm (Fig. 9a);
  same-orientation crystals: only 3 nm and 180 µJ before mirror damage vs > 1 mJ for the orthogonal (Np / Nm)
  arrangement (Sec. 2.1); Q-switched spectrum supports sub-160 fs (Fig. 3b); M² ~1.1.
* Inputs: fibre oscillator 47.1 MHz, 10 mW, 15 nm, centre 1038 nm (Sec. 2, 3); Martinez stretcher to ~250 ps,
  1600 l/mm, 52° incidence (Sec. 2.3); cavity mode Ø 500 µm; two Ng-cut Yb:KGW crystals, one Nm ∥ s, one Np ∥ s;
  fluence ≤ 0.8 J/cm²; thermal lens 600 → 150 mm (Fig. 4).
* Missing: crystal length, doping, the two polarised cross-section curves (literature, e.g. Kuleshov 1997, not in
  paper), losses. Similar data in Song 2022 (Sec. 3): Yb:KGW regen 3 × 3 × 5 mm Ng-cut, 800 µJ, gain-narrowed to
  4.8 nm, 457 fs (sech²).
* Engine features: regenerative amplifier with two gain media / a polarisation-dependent user cross section.
* Difficulty M-L. Verdict: **ordering check only** (orthogonal arrangement broader than parallel; 3 nm vs 9 nm with
  literature cross sections and a documented assumption).

### 2.3 (rank 3) Stark et al., "1 kW, 10 mJ, 120 fs coherently combined fiber CPA laser system"
* Opt. Lett. 46, 969 (2021), DOI 10.1364/OL.417032, OSA OA agreement. Read: arXiv 2103.05614 (full text).
* Headline numbers: 16 coherently combined Yb rod-type amplifiers, 10 mJ, 1 kW, 120 fs (abstract); combining
  efficiency 94 %; AC FWHM 164 fs; compressor transmission 86.5 %; ~90 % energy in the main feature (text by Fig. 3-4).
* Inputs: pre-stretch to ns with CFBGs, 40 nm hard cut at 1035 nm; eight Gaussian notch filters, ~6 nm FWHM, 1.5 dB
  peak, near 1030 nm; main amplifiers 62 µm MFD, 105 cm long; dielectric gratings 1740 l/mm.
* Missing: the FDPS amplitude / phase masks (central to the result), gains per stage, B per amplifier.
* Verdict: **not replicable as a headline**; useful only as a gain-narrowing-with-notch-filters illustration.

**Topic note.** The engine's built-in Yb:YAG is room temperature; no full-text open paper found with a room-temperature
Yb:YAG regen/thin-disk that gives seed spectrum, gain and output spectrum together (Fattahi/Alismail thin-disk papers
are OL/JoVE; not read). Seidel 2022 S1.2 gives a Yb:YAG rod chain: 15 nJ → 5 → 60 → 130 → 200 µJ in four 7 mm rods,
940 nm pump, but no spectra.

## Topic 3. Ti:sapphire

### 3.1 (rank 1) Musheghyan, Lücking, Cheng, Frei & Assion, "0.24 TW ultrabroadband, CEP-stable multipass Ti:Sa amplifier"
* Opt. Lett. 44, 1464 (2019), DOI 10.1364/OL.44.001464. Read: arXiv 1905.02707 (full text).
* Headline numbers: 14-pass butterfly amplifier, 5 mJ before / 3.2 mJ after compression, ≤ 13 fs (12.7 fs FTL, Fig. 2b),
  flat-top spectrum 134 nm FWHM (Fig. 2a), CEP < 150 mrad rms over 75 h; system efficiency 13.8 % (vs 20 % for a
  30 fs system); compressor 64 % (vs 80 %); energies in the last three passes 1, 2.8, 5 mJ; saturation from pass 5-6.
* Inputs: < 7 fs 75 MHz oscillator; stretcher 60 cm SF57 → ~63 ps; 210 mW seed; pump 36.2 W, 1 mm spot; seed
  ~300 µm (passes 1-12) then ~1 mm; Pockels cell after pass 6 (1 kHz); Gaussian-reflectance gain-shaping filters
  in 12 of 14 passes (profile not given); double-pass Dazzler; 1280 l/mm transmission gratings + 4 × -250 fs².
* **Analytic side check (exact inputs given in the introduction):** 60 cm SF57 with GVD 223.58 fs²/mm stretches a
  Gaussian of 38 nm FWHM from 25 fs to 14.9 ps and one of 72 nm from 13 fs to 28.6 ps. Note the engine's SF57
  Sellmeier GVD at 800 nm can be compared with 223.58 fs²/mm.
* Missing: crystal length / doping / temperature, pump energy per pulse (36.2 W at 1 kHz?), filter profiles.
* Difficulty S (stretcher check) / L (amplifier). Verdict: **stretcher numbers replicable exactly; amplifier
  only qualitatively** (gain narrowing without filters → much narrower than 134 nm).

### 3.2 Spence, Kean & Sibbett, OL 16, 42 (1991); Backus et al., RSI 69, 1207 (1998)
* Both paywalled (Optica / AIP); no OA copy found on arXiv or HAL. **UNVERIFIED.** The plan's "60 fs" KLM number is
  from memory. The repo has `docs/examples/19-ti-sapphire-klm-haus` already; a replication needs the cavity
  (prism separation, crystal length, output coupling) from the paper - owner to read in a browser.

## Topic 4. OPA / OPCPA

### 4.1 (rank 1) Bridger, Naranjo-Montoya, Tarasevitch & Bovensiepen, "Towards high power broad-band OPCPA at 3000 nm"
* Opt. Express 27, 31330 (2019), DOI 10.1364/OE.27.031330, OSA OA. Read: arXiv 1908.01879v3 (full text).
* Headline numbers (Sec. 2.2): four stages - (1) NOPA, 3.9 mm type-I BBO, θ = 24.3°, non-collinear angle 2.4°,
  515 nm pump; 25 µJ at 35 GW/cm²; saturation from ~20 GW/cm² (Fig. 2a); operated at 20 GW/cm² (~100 µJ pump,
  0.9 ps) giving ~10 µJ; signal ~35 nm FWHM around 770 nm, supports 30 fs (Fig. 2c). (2) collinear DFG, 0.6 mm
  type-I BBO, θ = 22.5°, beams 0.6 mm FWHM, 400 µJ pump + 6 µJ seed → 20 µJ idler at 1560 nm, 105 nm FWHM
  (43 fs) (Fig. 3). (3) NOPA, 3 mm type-II KTA, θ = 48.6°, angle 4.2°, 15 µJ seed + 1.5 mJ pump (1030 nm) →
  200 µJ, 140 nm FWHM (Fig. 4). (4) collinear, 4 mm type-I LiIO3, θ = 19°, 9 mJ 1030 nm pump, 200 µJ seed → 430 µJ
  idler at 3000 nm, > 490 nm FWHM (supports 35 fs), compressed by sapphire to ~65 fs (90 fs AC, factor 1.4)
  (Fig. 5-6). B < π/4 in all stages with n2 = 8, 17, 20·10⁻¹⁶ cm²/W for BBO, KTA, LiIO3.
* Pump channel (Sec. 2.1): 20 mJ, 1.2 ps after compression; 1 mJ doubled in 2 mm LBO (NCPM at 190 °C) → 700 µJ at
  515 nm. Seed: 6 fs, 800 nm, 2.5 nJ, stretched by 10 cm BK7 to 3 ps.
* Missing: beam sizes in stages 3-4, pump durations at 1030 nm in stages 3-4 (1.2 ps), seed chirp at each stage,
  the delay setting of stage 1.
* Engine features: crystal OPA/NOPA/DFG, phase-matching calculator (angles), noncollinear geometry, pump depletion,
  an LBO NCPM SHG stage. KTA and LiIO3 are not built in (user crystals: Sellmeier + d tensor needed).
* Difficulty S (angles) / M (stages 1-2 energies). Verdict: **highly replicable for the BBO stages and all phase-
  matching angles** (24.3°, 22.5°, 48.6°, 19°, LBO NCPM at 190 °C for 1030 nm); energies within a factor ~1.5.

### 4.2 Dubietis, Jonušauskas & Piskarskas, Opt. Commun. 88, 437 (1992); Cerullo & De Silvestri, RSI 74, 1 (2003)
* Both paywalled (Elsevier / AIP); not on arXiv/HAL. **UNVERIFIED.** The NOPA "magic angle" ≈ 3.7° for BBO pumped
  at 400 nm is a standard result that the engine's phase-matching calculator can compute directly (group-velocity
  matching of signal and idler projection); use it as an engine-internal check, cite the review only after reading.

## Topic 5. Fibres / hollow capillaries

### 5.1 (rank 1) Travers, Grigorova, Brahms & Belli, "High-energy pulse self-compression and ultraviolet generation through soliton dynamics in hollow capillary fibres"
* Nat. Photon. 13, 547 (2019), DOI 10.1038/s41566-019-0416-4. Read: arXiv 1811.05877v2 (full text incl. Methods).
* Analytic content: Eq. 1, HE_nm GVD β2 ≈ λ³/(4πc²)·(ρ_r ∂²χe/∂λ² − u_nm²/(2π²a²)); Eq. 2, 1/e power loss length
  L_loss ≈ 4π²a³/(3 u_nm² λ²), quoted as ~1 cm for HE11, a = 15 µm, 800 nm (I recomputed 1.2 cm: consistent);
  Eq. 3, L_fiss ∝ a²τ_fw/√I0 (Lfiss ≈ L_D/N).
* Headline numbers: Fig. 2 simulations, 10 fs, N = 2.6, 800 nm, helium with λ_zd = 380 nm: (a) a = 15 µm, 27.8 bar,
  0.006 mJ; (b) a = 125 µm, 400 mbar, 0.4 mJ; (c) a = 350 µm, 51 mbar, 3 mJ; RDW at ~130 nm with 0.6 / 25 / 300 µJ.
  Experiment (Fig. 4): 3 m HCF, a = 125 µm, 1 bar He (λ_zd = 470 nm), 26 fs, 400 µJ → N = 6.8, L_fiss = 4.2 m,
  self-compression 26 → 9 fs; with 10 fs input at 0.4 bar (λ_zd = 380 nm): 239 µJ (N = 2) → 2.7 fs (TL 2.5 fs),
  279 µJ → 2.3 fs (2.1), 337 µJ → 1.2 fs (1.1 fs). VUV/DUV RDW 110-400 nm, up to 116 µJ (abstract).
* Inputs (Methods A): Legend Elite Duo 26 fs, 800 nm, 1 kHz; first stage 1.6 m, a = 225 µm, He 2.2 bar → ~10 fs;
  second stage 3 m stretched HCF, a = 125 µm, coupling 60-70 %, linear transmission 65 % at 800 nm; output via 2 mm
  silica or 1 mm MgF2 window. Model (Methods D): full-field multimode UPPE with Marcatili modes, ionisation.
* Missing: the measured input pulse shape (sech/Gaussian assumption), helium χe (engine's gas model).
* Engine features: needs the **Marcatili capillary mode model** (gap, plan F); 1-D fibre with β(ω) from Eq. 1 and
  loss from Eq. 2 is a fair first step; plasma for the highest energies.
* Difficulty S (Eqs. 1-3, λ_zd vs pressure) / L (1.2 fs). Verdict: **Eqs. and the 26 → 9 fs / 10 → 2.7 fs points
  replicable in 1-D once the capillary model exists.**

### 5.2 (rank 2) Marcatili & Schmeltzer, "Hollow metallic and dielectric waveguides for long distance optical transmission and lasers"
* Bell Syst. Tech. J. 43, 1783 (1964). Read: archive.org scan, OCR text (bstj43-4-1783), freely available;
  equations badly OCR'd - **read Eq. (22) and Table I from the scanned PDF before writing checks**.
* Headline numbers (abstract and Sec. IV): EH11 in glass ν = 1.50, λ = 1 µm, a = 1 mm → 1.85 dB/km; doubled for an
  axis curvature radius R ≈ 10 km; attenuation ∝ λ²/a³ and ∝ u_nm²; EH_nm loss minimum at ν = √3 = 1.73 (TM0m at
  √2); EH11 is the lowest-loss mode for ν < 2.02 (TE01 above). Check I ran by hand: field attenuation
  α = (u/2π)²·λ²/a³·(ν²+1)/(2√(ν²−1)) with u = 2.405 gives 2α·4.343 = 1.85 dB/km - consistent with the abstract
  and with Travers' Eq. 2 (the factor 3 ≈ (ν²+1)/√(ν²−1) at ν ≈ 1.45).
* Difficulty S. Verdict: **highly replicable** (unit tests for the future capillary model).

### 5.3 (rank 3) Dudley, Genty & Coen, RMP 78, 1135 (2006), Fig. 3 benchmark
* Paywalled (APS); HAL record hal-00268071 has no file. **Original UNVERIFIED.** Open secondary I read:
  gnlse-python `examples/test_Dudley.py` (WUST-FOG, GitHub; paper arXiv 2110.00298): 835 nm, 15 cm, γ = 0.11 /(W m),
  10 kW, 50 fs sech, Blow-Wood Raman, self-steepening, β2..β10 = -11.830e-3, 8.1038e-5, -9.5205e-8, 2.0737e-10,
  -5.3943e-13, 1.3486e-15, -2.5495e-18, 3.0524e-21, -1.7140e-24 (ps^n/m). These match the repo's
  `docs/examples/25-supercontinuum/pcf.ufs` (ps^n/km) exactly. Whether "50 fs" is the FWHM or T0 must be checked in
  the original (gnlse's SechEnvelope argument).
* Already an engine example; a `docs/papers/` entry would add location checks (fission length, Raman soliton and
  dispersive-wave wavelengths read off Fig. 3). Difficulty M. Verdict: replicable, but numbers only from the figure.

### 5.4 Gordon, OL 11, 662 (1986); Nisoli et al., OL 22, 522 (1997)
* Both Optica, paywalled; no OA copy. **UNVERIFIED** - including the plan's "250 fs at 1.5 µm shifts its own width in
  ~100 m" and "5 fs at 70 µJ". Gordon's T0⁻⁴ law can be tested in the engine without the paper (published scaling;
  cite after reading). Nisoli needs the capillary model.

## Topic 6. χ(2)

### 6.1 (rank 1) Aliasger et al., "Comparative study of second harmonic generation at 1030 nm in BiBO and LBO crystals using a 100 W-class picosecond laser"
* Opt. Laser Technol. 204, 116775 (2026), DOI 10.1016/j.optlastec.2026.116775 (OA status of the journal version not
  checked). Read: arXiv 2605.18527 (full text).
* Headline numbers: 32 W SH at 515 nm from 57 W for both crystals, 56 % (abstract, Fig. 4); LBO saturates, BiBO still
  rising; BiBO internal angular acceptance 5.1 mrad measured vs 5.14 mrad simulated (FWHM, 7 W), narrowing to
  2.69 mrad at 57 W (Fig. 5, text).
* Inputs (Sec. 2-4, Table 1): Yb:YAG thin disk, 1.3 ps (AC 1.77 ps, Gaussian), 91 kHz, 57 W (~626 µJ), spectrum
  1.5 nm FWHM at 1029.9 nm, side peaks in the AC; beam 3.3 × 2.7 mm (second-moment diameters) at the crystal, M²
  1.1 × 1.5; peak intensity ~13 GW/cm²; LBO 6 mm, θ = 90°, φ = 13.5°, type I (XY), d_eff 0.828 pm/V; BiBO 1.5 mm,
  θ = 166.6°, φ = 90°, type I (YZ), d_eff 3.4 pm/V. Table 1 also gives walk-off (8.22 / 30.39 mrad), GVM
  (0.052 / 0.19 ps/mm), acceptances (LBO 10.1 nm, 10.5 K, 5.8 mrad; BiBO 10.9 nm, 15.8 K, 5.6 mrad), group indices.
  The paper's own simulation (mlSNLO 2D-mix-SP): 7 W, 1.3 ps, 2 × 1.6 mm FWHM.
* Missing: pulse shape (side peaks reduce efficiency, Sec. 4.2 says), temperature tuning; BiBO has no built-in d
  tensor in the engine (use d_eff).
* Engine features: crystal SHG with walk-off, GVM, depletion; phase-matching calculator (angles, acceptances).
* Difficulty S (Table 1 values, angles) / M (efficiency curve). Verdict: **highly replicable**: Table 1 is a ready
  set of calculator checks; 56 % at 13 GW/cm² is a depletion check (spatial model for the elliptical beam).

### 6.2 (rank 2) Bache, Moses & Wise, "Scaling laws for soliton pulse compression by cascaded quadratic nonlinearities"
* JOSA B 24, 2752 (2007), DOI 10.1364/JOSAB.24.002752. Read: arXiv 0706.1507v2 (full text).
* Headline numbers: N_eff² = N_SHG² − N_Kerr² (Eq. 10) must exceed 1; fits (Sec. 3.B): z_opt/z0 =
  0.44/N_eff + 2.56/N_eff³ − 0.002 (Eq. 18) and f_c = 4.7 (N_eff − 0.86) (Eq. 19) [the fractions are garbled in the
  text extraction; the inverse powers follow the paper's remark on the N⁻² term and Eq. 16 - **confirm in the PDF**];
  Fig. 5(a): Δk = 50 mm⁻¹, 200 fs FWHM, 59 GW/cm², N_eff = 8 → 6.0 fs, f_c = 33, Q_c = 0.26, 2.3 % SH; Fig. 5(b):
  Δk = 55 mm⁻¹, 2000 fs, 26.7 GW/cm², N_eff = 50 → caption 7.6 fs vs text 8.3 fs (**inconsistent in the paper**),
  f_c = 264, Q_c = 0.06, z_opt = 342 mm; shortest 4.7 fs (1.3 cycles) (Sec. 4); τ_R,SHG = 2|d12|/|Δk| of 1-5 fs.
* Inputs (App. C): BBO, λ1 = 1064 nm, type I oo→e, d_eff ≈ 2.22 pm/V, n_Kerr = 3.65 ± 0.6·10⁻²⁰ m²/W, sech input,
  Sellmeier dispersion; plane wave (beams > ~100 µm).
* Engine features: crystal SHG far from phase matching with Kerr (cascaded n2), phase-mismatch scan.
* Difficulty M. Verdict: **replicable**: Fig. 5(a) point and the f_c law; also the cascaded n2 ∝ −d_eff²/Δk.

### 6.3 Boyd & Kleinman, JAP 39, 3597 (1968)
* AIP, paywalled; **UNVERIFIED** (h_max = 1.068 at ξ = 2.84 in the plan is from memory: do not use until read).

## Topic 7. Beams / filaments

### 7.1 (rank 1) Mysyrowicz, Couairon & Keller, "Self-compression of optical laser pulses by filamentation"
* New J. Phys. 10, 025023 (2008), DOI 10.1088/1367-2630/10/2/025023, open access. Read: HAL hal-00455018.
* Analytic content (Sec. 2): P_cr ≡ 3.72 λ0²/(8π n0 n2); argon 1 atm, n2 = 3·10⁻¹⁹ cm²/W at 800 nm → P_cr = 3.4 GW;
  Marburger collapse distance L_c = 0.367 L_DF / √([(P/P_cr)^½ − 0.852]² − 0.0219), L_DF = π n0 w0²/λ0 (cited to
  Marburger 1975). **Finding:** the repo test `SmallScaleTests.cs:132` uses 3.77 λ²/(8π n0 n2), this paper 3.72
  (both appear in the literature; the reason for each is not verified here) - document which one the engine uses
  and why, and use the paper's constant in the check.
* Simulation case (Fig. 1, Table 1): 0.9 mJ, 800 nm, 0.8 atm argon; w0 = 2.7 mm, f = 56 cm, t_p = 25 fs (FWHM 30 fs,
  TL) - the caption says 40 fs (**inconsistent**); n2 = 3·10⁻¹⁹ p cm²/W, P_cr = 3.4/p GW, MPI K = 11,
  σ_K = 5·10⁻¹⁴⁰ s⁻¹cm²²W⁻¹¹, β_K = 3.4·10⁻¹³⁸ p cm¹⁹W⁻¹⁰, τ_c = 190/p fs, σ_IB = 7.8·10⁻²⁴ m²; densities read
  as 2.5·10¹⁹ p cm⁻³ (neutral) and 1.8·10²¹ cm⁻³ (critical) - the extracted exponents carry a spurious minus sign.
  Results: 35 cm plateau at 7·10¹³ W/cm², electron density slightly above 10¹⁷ cm⁻³, minimum duration 5 fs at the
  filament end; experiments 5.7 fs (0.38 mJ), 5.1 fs (0.18 mJ), 4.9 fs (0.12 mJ) (Sec. 3).
* Engine features: radial spatial model with Kerr, MPI/PPT, Drude plasma, dispersion; the Marburger formula is an
  S check (collapse distance vs P/P_cr with ionisation off).
* Difficulty S (Marburger) / L (filament). Verdict: **Marburger replicable now; filament plateau qualitatively.**
  (Couairon & Mysyrowicz Phys. Rep. 441, 47 (2007): HAL record hal-00454778 has no file - UNVERIFIED.)

### 7.2 Bespalov & Talanov, JETP Lett. 3, 307 (1966)
* Not retrievable (paywalled / Russian archive). **UNVERIFIED**; the engine already tests the growth law
  (`SmallScaleTests.cs:86`). No new paper entry needed beyond citing a textbook after reading it.

## Topic 8. Contrast (XPW, NER, ASE)

### 8.1 (rank 1) Ramirez et al., "Compact, simple and robust cross polarized wave generation source of few-cycle, high-contrast pulses for seeding petawatt-class laser systems"
* JOSA B 30, 2607 (2013), DOI 10.1364/JOSAB.30.002607. Read: HAL hal-00904513 (author version, full text).
* Model (Sec. 2, Eqs. 1-2, Table 1): coupled SPM/XPM/XPW equations for A (fundamental) and B (XPW), holographic
  [011] cut; **σ = (χxxxx − 3χxxyy)/χxxxx = −1.2 for BaF2, χxxxx = 1.59·10⁻²² m²/V², β = 115.5°** (Table 1 note);
  γ0 = 6πχxxxx/(8nλ), D = 1 + (3σ/16) cos4β − 7σ/16, γ1..γ5 in Table 1.
* Headline numbers: single 2.5 mm BaF2, 25 fs, 1.15 mJ on the crystal, 2 mm beam, ~1.5·10¹² W/cm² →
  30 % efficiency, 8.4 fs FTL, 9.6 fs compressed (experiment); simulation 8.8 fs FTL at 32 % (Sec. 2); XPW FTL shorter
  than the fundamental up to 0.5·10¹² W/cm², SPM dominates above; spectral shape changes at 0.5, 1.15, 1.5·10¹²
  (Fig. 2); input: super-Gaussian spectrum 60 nm FWHM. Dual-crystal (Sec. 3): 1 mm + 2.5 mm, 5.8 % in stage 1,
  12.6 / 13.4 fs FTL (Fig. 4).
* Missing: the input spectral phase ("chosen to match"), exact super-Gaussian order.
* Engine features: needs an **anisotropic χ(3) (XPW) model** (gap, plan F); then crystal Kerr with tensor, polariser.
* Difficulty M (after the model). Verdict: **the reference for the XPW model** - fully specified equations.

### 8.2 (rank 2) Ramirez et al., "Efficient cross polarized wave generation for compact, energy-scalable, ultrashort laser sources"
* Opt. Express 19, 93 (2011), DOI 10.1364/OE.19.000093. Read: HAL hal-00838480 (full text).
* Headline numbers (Sec. 3): 1.5 mJ, 25 fs → 315 µJ XPW, 32 % internal, 19.3 % global; saturation ~30 % above 1.3 mJ
  (Fig. 2a); > 20 % within ± 300 fs² and ± 2000 fs³ of the optimum; 25 → 9.6 fs (8.4 fs FTL, 148 nm), ~300 µJ;
  3.3 mJ, 30 fs → 650 µJ, 33 %, 15.5 fs; contrast 10⁸ → 3.11·10¹⁰ estimated (TFP extinction); theoretical single-crystal
  maxima 37 % (z-cut) and 45 % (holographic cut) quoted from refs. 2-3.
* Inputs: 20 cm, 250 µm-diameter FS hollow waveguide filter (85 % coupling), 1.5 m focusing mirror; 2.5 mm [011]
  BaF2 at 25-41 cm after the waveguide; vacuum; Glan (> 10³) or TFP (~10²).
* Missing: intensity on the crystal (given in 8.1 for the same setup: ~1.5·10¹² W/cm², 2 mm beam).
* Verdict: **replicable together with 8.1** (efficiency vs energy curve, GDD tolerance).

### 8.3 Jullien et al., OL 30, 920 (2005)
* Optica, paywalled; no HAL file found. **UNVERIFIED.** 8.1/8.2 supersede it as references.
* ASE pedestal in a CPA chain: Song 2022 Fig. 8 (10⁷ with the oscillator seed vs 10⁹ with the NER-cleaned seed,
  Yb:KGW regen) is the only open number found; no paper with an ASE model and its inputs was read.

## Topic 9. Fitting showcases

### 9.1 Hollenbeck & Cantrell, JOSA B 19, 2886 (2002), Table 1
* Optica, paywalled; **original UNVERIFIED.** Open secondary read: gnlse-python `gnlse/raman_response.py`
  (`raman_holltrell`, GitHub). Its 13-mode table (positions cm⁻¹, amplitudes, Gaussian and Lorentzian FWHM cm⁻¹) is
  **identical** to the repo's `src/UltrafastEngine.Core/Materials/RamanModels.cs:63-66`.
* **Finding to check against the original:** in both copies every Lorentzian FWHM equals Gaussian FWHM / 3
  (to 0.01 cm⁻¹) except mode 2: Gaussian 110.42, Lorentzian 38.81 (110.42/3 = 36.81). Either a typo propagated from a
  common source or the paper's value; read Table 1 before using it as a fit target.
  Also fR: gnlse uses 0.20, the engine 0.18.
* Showcase: fit Blow-Wood (fR, τ1, τ2) to h(t) / Im H(Ω) from this table (LM, CMA-ES, MCMC); report the misfit
  above 15 THz. Difficulty S. Verdict: replicable once the table is confirmed.

### 9.2 Song 2022 (entry 1.2) and Lavenu 2018 (1.3)
* Song: fit n2 (intensity scale) and input chirp to the Fig. 3b bandwidths, predict Fig. 2a efficiency vs γ.
  Lavenu: fit the input pulse (Fig. 1b) to the measured spectrum, predict 33 fs / 82 %.

---

## Summary ranking (what to do first)

| # | paper | topic | size | verdict |
|---|---|---|---|---|
| 1 | Escoto 2022 (arXiv) | MPC | S | high: Eq. 2, Fig. 3, cascade |
| 2 | Marcatili 1964 (BSTJ) + Travers 2019 Eqs. 1-3 | capillary | S | high, after the capillary model |
| 3 | Musheghyan 2019 stretcher numbers | Ti:Sa | S | exact |
| 4 | Aliasger 2026 Table 1 | χ(2) | S | high (calculator) |
| 5 | Bridger 2019 phase-matching angles, BBO stages | OPCPA | S/M | high |
| 6 | Mysyrowicz-Couairon 2008 Marburger | filament | S | high; P_cr constant finding |
| 7 | Lavenu 2018 | gas MPC | M | high |
| 8 | Song 2022 | NER MPC + fitting | M | replicable (n2 assumed) |
| 9 | Bache 2007 | cascaded χ(2) | M | replicable |
| 10 | Ramirez 2013 + 2011 | XPW | M | needs XPW model |
| 11 | Zhang 2024 | Yb regen | M | gain-narrowing check |
| 12 | Seidel 2022 | MPC/NER | M | partial |
| 13 | Travers 2019 dynamics | capillary | L | after model |
| 14 | He 2020 | Yb:KGW regen | M-L | ordering only |
| 15 | Dudley 2006 (via gnlse) | fibre | M | figure-read only |
| 16 | Hollenbeck-Cantrell (via gnlse) | fitting | S | confirm table |
| 17 | Stark 2021, Pfaff 2022 | fibre CPA, NER | - | not replicable / pending |

UNVERIFIED (not retrievable here, owner to read in a browser): Pfaff 2022 body, Spence 1991, Backus 1998,
Dubietis 1992, Cerullo 2003, Gordon 1986, Nisoli 1997, Boyd-Kleinman 1968, Jullien 2005, Bespalov-Talanov 1966,
Couairon-Mysyrowicz 2007 Phys. Rep., Hollenbeck-Cantrell original, Dudley original.
