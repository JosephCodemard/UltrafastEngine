# Phase 2 design: active media and spatial physics

Status: **agreed plan, revision 3** (2026-10-13). Milestone 2.1 is being implemented; 2.2 and 2.3 follow.
Priorities: **Ti:sapphire, OPCPA, Yb:YAG**; solid-state oscillators.

| milestone | contents |
|---|---|
| **2.1** | foundations (sections 2–3), **spatial models** (radial and 3D, section 4), **χ(2) crystals and OPCPA** (section 5) |
| **2.2** | gain media and amplifiers: Yb:YAG, Ti:sapphire, multipass, regenerative, rep-rate steady state (section 6) |
| **2.3** | solid-state oscillators (Haus and cavity models, Kerr-lens and SESAM mode-locking) and extras (section 7) |

## 1. Principle: one main beam, kept sequential

The system stays a **single sequential path** followed by one **main beam**. Active elements may make that beam
interact with other beams, contain loops, or resolve it in space, but all of that is **encapsulated in the
element**. Seen from outside, every element takes one beam in and hands one beam out.

* **Auxiliary beams** (an OPA pump, an SFG partner) are *specified* on the element. They are not traced from
  upstream.
* The element propagates every field it needs (coupled waves) and then **selects the field that continues as the
  main beam** (`output: "signal" | "idler" | "sh" | ...`). The others are **recorded** as by-products. They can
  be measured by instruments (`"at": "nopa#idler"`) and appear in the energy budget.
* **Loops** (regenerative amplifier round trips, an oscillator cavity) are **composite elements** (2.2/2.3).
* **State** that persists (a gain crystal's inversion) lives in a store keyed by `stateId` (2.2).

### Why sequential first

Branches (splitters, parallel paths, coherent recombination) need four things:

* a graph executor;
* geometry that sequential OpticStudio cannot supply;
* per-branch results;
* sub-cycle phase and timing bookkeeping across paths.

Meanwhile two features cover most use cases:

* **auxiliary beams taken from another run's results** (`"from": "runs/pump_line"`);
* **user elements**: a callback that transforms the main beam.

Auxiliary inputs are *ports* with a pluggable source, so a later branch layer only connects ports.

## 2. Foundations (2.1)

| | item | notes |
|---|---|---|
| F1 | **Per-pulse grids and exact carrier change** | every pulse carries its own grid. After SHG, or when an idler continues, the main beam gets a new carrier and a new grid sized for the rest of the system (re-planned at run time from the actual spectrum). Resampling is exact: windows change by powers of two and carriers sit on grid bins. |
| F2 | **Internal multi-field propagation** | coupled envelopes on one time grid, each with its own carrier, dispersion and polarisation. The adaptive interaction-picture stepper is reused unchanged (the problem is "state vector + diagonal linear operator + nonlinear function"). |
| F3 | **Auxiliary beam ports** | `pulse` (same schema as the run pulse), `from` (a results folder), `callback`; profile (Gaussian, super-Gaussian/flat-top), delay (number or `"auto"`), polarisation role |
| F4 | **By-products** | recorded per element and field (energy, spectrum, pulse); instruments can measure them |
| F5 | **Energy budget with gains** | signed entries (`parametric gain`, `converted to sh`, `to idler`, ...). Transmission may exceed 1. |
| F6 | **User element** | `{"type": "user", "callback": "@name"}`: C#, C or Python receives the spectrum and returns a new one |

## 3. Auxiliary beams in detail

```json
"aux": [ { "role": "pump",
           "pulse": { "centerWavelengthM": 515e-9, "durationFwhmS": 1.5e-12, "energyJ": 2e-3 },
           "beam": { "kind": "superGaussian", "radiusM": 2.0e-3, "order": 6 },
           "delayS": "auto" } ]
```

* `from: "path/to/results"` uses the output pulse of another run (`result.npz` + `summary.json`). An optional
  `"energyJ"` rescales it.
* `delayS` is relative to the main beam's co-moving time origin at the element entrance. `"auto"` places the aux
  pulse at the main pulse's centroid.
* In 1-D mode the aux beam's peak intensity uses its own effective area. In spatial modes its profile is sampled
  on the element's transverse grid.

## 4. Spatial models (2.1)

### 4.1 Levels, chosen per element

| level | field | captures | cost |
|---|---|---|---|
| `none` (default) | 1-D, effective area from the beam model | everything in Phase 1 | 1× |
| `radial` | E(r, t), axisymmetric, quasi-discrete Hankel transform | diffraction, Kerr lensing / self-focusing, space-time focusing, flat-top vs Gaussian overlap, radial gain/conversion saturation, M² of round beams | ~N_r× (32–128) |
| `cartesian` | E(x, y, t), 2-D FFT | everything radial does, plus walk-off, astigmatism, non-collinear lateral drift, asymmetric beams | ~N_x N_y× |

Configured per element: `"transverse": {"model": "radial", "points": 64, "radiusM": 3e-3}`. The rest of the
system can stay 1-D.

### 4.2 Physics

* **Linear operator in (k⊥, ω):** L = i[k_z(ω, k⊥) − k(ω0) − (ω−ω0)/v_ref], with k_z = sqrt(k(ω)² − k⊥²)
  (non-paraxial, evanescent components damped). This includes diffraction, space-time focusing and dispersion
  exactly.
* **Nonlinear operator:** pointwise in space and time (Kerr, Raman, χ(2) coupling, user Δn).
* **Walk-off (cartesian):** an extra phase −ρ k_x for extraordinary fields. Non-collinear drift is the same with
  tan α.

### 4.3 Carrying the transverse field between elements

When a spatial element finishes, the main beam carries a **transverse profile**: E(r, ω) / A(ω), where A is the
collapsed 1-D field. The collapsed field preserves the integrated spectrum, and its phase is the area-weighted
coherent phase. One-dimensional elements apply their (r-independent) transfer functions to A, which is exact for
any r-independent term.

The next spatial element rebuilds E = A × profile. Its starting point depends on what lies in between:

* **Exact transport:** every element in between is transverse-aware: a spatial `gap`, a `lens`, a `mirror` with
  `radiusOfCurvatureM`, or zero-length terms. The profile has then been propagated physically and is used as it is.
* **Rescaled transport:** some element in between has no transverse information (e.g. a 1-D element sampled from
  OpticStudio with only a beam size). The profile's shape is kept, rescaled to this element's beam size, and given
  the beam model's curvature. This is exact for imaging relays; it is noted in the results.
* **No profile:** the field starts from the element's beam model (Gaussian with w and R, or a super-Gaussian).

### 4.4 Diagnostics

Per spatial element:

* second-moment radius at the exit and an M² estimate (from the r and k⊥ moments);
* the true peak fluence and intensity, used for LIDT margins (not just 2E/πw²);
* on-axis versus beam-integrated B-integral.

A `beamProfile` instrument gives fluence(r) and the spatially resolved spectrum.

## 5. χ(2) crystals and OPCPA (2.1)

### 5.1 The `crystal` element

```json
{ "id": "nopa1", "type": "crystal", "crystal": "BBO", "lengthM": 3e-3,
  "cut": { "thetaDeg": 23.8, "phiDeg": 90 },
  "interaction": { "process": "opa", "type": "I", "noncollinearDeg": 2.4 },
  "mainRole": "signal",
  "aux": [ { "role": "pump", "pulse": { "centerWavelengthM": 515e-9, "durationFwhmS": 1.5e-12, "energyJ": 2e-3 },
             "beam": { "kind": "superGaussian", "radiusM": 2.0e-3, "order": 6 }, "delayS": "auto" } ],
  "output": "signal", "record": ["idler", "pump"],
  "transverse": { "model": "radial", "points": 48 } }
```

* **Processes:** `shg`, `sfg`, `dfg`, `opa` (= OPCPA).
* **Phase-matching types:** I (ooe/eeo), II (oee/eoe) and 0 (QPM, eee), with polarisations assigned per field.
  The main beam is projected onto the crystal's eigen-polarisation through the Jones machinery.
* **Coupled-wave equations** with full dispersion of each field (exact GVM/GVD), and the carrier phase mismatch
  Δk0 kept explicitly. Pump depletion, back-conversion, χ(3) SPM/XPM in the crystal and absorption are included.
* **Non-collinear (1-D/radial):**
  * Δk_z(ω) = k_p cos α − k_s − k_i cos β(ω), with the idler angle β from transverse momentum conservation.
  * Signal–pump lateral walk-off is resolved in cartesian mode.
* **QPM:** poling period Λ, or Λ(z) as data, expression or callback (chirped poling).
* **Seeding:** the idler starts from zero or from quantum noise (one photon per mode; optional, gives
  superfluorescence contrast).
* **Idler phase:** follows φ_p − φ_s − π/2 automatically (CEP-stable idler).

### 5.2 Crystal library and calculator

* **Library:** BBO, LBO, BiBO, KDP, DKDP, KTP, KTA, LiNbO₃ (5 % MgO, e-ray) for PPLN, PPKTP, and user-defined
  crystals (Sellmeier and d-tensor in JSON). Each built-in has references.
* **Calculator:** `ultrafast crystal` (CLI) and `ultrafast.crystal.phase_match` (Python) return:
  * phase-matching (and magic non-collinear) angles, or the QPM period;
  * deff and walk-off;
  * acceptance bandwidths (Δλ·L, Δθ·L, ΔT·L), GVM lengths and the small-signal gain spectrum.

### 5.3 Instruments

* `conversion`: energy and photon numbers of every field at a crystal.
* `phaseMatchingCurve`: low-conversion efficiency vs angle, temperature, wavelength or period.
* `delayScan`: re-runs a crystal over a range of pump delays.
* `beamProfile`.

## 6. Gain media and amplifiers (2.2)

```json
{ "id": "disk", "type": "gainMedium", "medium": "Yb:YAG", "lengthM": 215e-6, "dopingAtPercent": 7,
  "temperatureK": 330, "stateId": "disk",
  "pump": { "kind": "cw", "wavelengthM": 969e-9, "powerW": 600, "radiusM": 2.2e-3, "passes": 24 } }
```

* **Model:** cross-sections (temperature-dependent tables, McCumber, overridable), doping, τ_f, saturable pump
  absorption, z-slices. Gain enters the solver as a z-dependent linear operator. Optional Kramers–Kronig
  dispersion. Works with the radial model too: gain guiding and radial saturation.
* **Saturation:**
  * G1: spectral Frantz–Nodvik.
  * G2: time-resolved, for chirped pulses.
  * G3: user callback.
* **Ti:sapphire:** pulsed green pump with a pump-to-seed delay, broadband gain (gain narrowing and red-shift).
* **Yb:YAG:** quasi-three-level, temperature dependence, CW thin-disk pump. A **pulse-train mode** iterates to the
  rep-rate steady state and detects period doubling.
* **Architectures:** multipass amplifiers as unrolled passes sharing `stateId`; regenerative amplifiers as a
  composite element with Pockels gating.

## 7. Oscillators and extras (2.3)

* **Source element** `oscillator` with three models:
  * `parametric`: you give the output pulse;
  * `haus`: master equation;
  * `cavity`: round-trip composite iterated to steady state.
* **Absorbers:** Kerr-lens mode-locking (with the radial model, the real Kerr lens plus a hard or soft aperture)
  and SESAM.
* **Diagnostics:** Q-switched mode-locking, multipulsing, CW breakthrough. Python builder `ultrafast.laser.Oscillator`.
* **Extras:** broadband χ(2) in the carrier-resolved mode, ASE, thermal lens to OpticStudio.

## 8. Validation

| test | reference | milestone |
|---|---|---|
| carrier change and resampling | round trip and SHG → glass vs a single-grid reference | 2.1 |
| Gaussian beam diffraction (radial, cartesian) | analytic w(z), Gouy phase | 2.1 |
| thin Kerr lens / self-focusing collapse distance | analytic Kerr-lens focal length; Marburger formula | 2.1 |
| low-conversion SHG vs angle / Δk = 0 plane-wave SHG | sinc² / Armstrong tanh² | 2.1 |
| OPA small-signal gain | sinh² formula | 2.1 |
| photon bookkeeping | Manley–Rowe | 2.1 |
| cascaded χ(2) | n2,eff ∝ −deff²/Δk | 2.1 |
| walk-off | lateral displacement ρL | 2.1 |
| DFG in PPLN | pyNLO ThreeWaveMixing | 2.1 |
| phase-matching angles | published values | 2.1 |
| Frantz–Nodvik, gain narrowing, rep-rate recovery | analytic | 2.2 |
| soliton area theorem, QML criterion | analytic | 2.3 |

## 9. Known limits

* Paraxial/unidirectional only; no back-reflections.
* No plasma or filamentation at large scale.
* Cartesian runs are expensive on the CPU (a GPU backend is Phase 4).
* No branches or coherent recombination.
* Gain accuracy is bounded by the cross-section data.
