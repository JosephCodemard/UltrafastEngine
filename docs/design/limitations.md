# Known limitations (internal detail)

Internal page ([folder index](README.md)). The user-facing list is [docs/capabilities.md](../capabilities.md); this page adds the cause,
the evidence (test, repro or code location) and whether the limitation is planned work ([roadmap-internal](roadmap-internal.md)) or accepted.

## Physics models

| limitation | cause / evidence | status |
|---|---|---|
| **B-integral `bIntegralRad` is the on-axis temporal peak** (Σ max_t \|Δn\| k0 dz); pulse- and beam-averaged values are reported beside it (`bIntegralPulseAveragedRad`, `bIntegralBeamAveragedRad`, from 2026-11-17) | `Physics/Nonlinear.cs` step statistics | accepted; documented in `docs/reference/nonlinear.md` |
| **1-D beam model has no transverse averaging**: the nonlinear phase uses the peak intensity of one tracked Gaussian; `kerrLens` acts inside one element only | WP-D D2; `Executor.cs` note "the beam leaving the element is not carried to later elements" | the spatial (`transverse: radial`) model is the exact answer and runs through `sameAs` passes (test `SameAsTests.Radial_ThroughSameAsPasses_*`); carrying the lensed beam on is planned (roadmap §2) |
| **FWHM of a multi-peaked pulse** uses the outermost half-maximum crossings | `Results/PulseMetrics.cs` | accepted; `peakCount`, `fwhmEnergyFraction` and a warning were added 2026-11-17; use `rmsWidthS` or `temporalStrehl` for such pulses |
| **Ideal mirrors keep circular light circular**: a mirror without a coating, and `ideal` / `ar` / `gdd` coatings, give r_s and r_p the same phase; only physical stacks, `table` coatings with `phaseRs` / `phaseRp` and bare substrates give retardance; no built-in metals | `Physics/Interfaces.cs:194-239` | accepted (an ideal mirror is a modelling choice); metals planned (roadmap §2) |
| **Polarisers without a material** add no dispersion; there is no Glan element (use `polarizer` with `material` + `thicknessM`, from 2026-11-17) | `Schema/BuiltInElements.cs` | accepted |
| **Lens dispersion needs `thicknessM`**; the lens glass is a uniform slab (no centre-to-edge thickness); in the spatial models the chromatic lens phase carries Bor's propagation-time difference in thin-lens form | `Engine/Compiler.cs` lens branch; `Optics/SurfaceOptics.cs` | accepted; a note warns when a lens has a material but no thickness |
| **Self-steepening** uses τ_shock = 1/ω0 (no A_eff(ω), no user shock time) | `Physics/Nonlinear.cs` | planned (roadmap §2) |
| **No ionisation in χ(2) crystals** | `Crystals/` has no plasma term | planned (roadmap §2) |
| Ionisation: the Drude term is first order in ρ; a density that changes within one optical cycle is resolved in the rate, not in the Drude response; no negative frequencies | `docs/reference/ionization.md` | accepted |
| Tunnelling-electron birth phase: drift energy assumes a uniform birth phase | | planned (needs a reference) |
| SPM reaching zero frequency (one-cycle pulses) is cut and booked as `spectrum at non-positive frequency` | `Executor.cs`; 3 KNOWN component checks | planned (carrier-resolved path) |
| Carrier frame follows the lattice centre; `snapCarrier` changes the Schulte cell by 1.4 % | | planned |
| Domain-resolved QPM: an adaptive step that ends just short of a wall leaves a sliver step; fixed steps (`rk4ip`) ignore walls (warned) | `Crystals/` stepper; fixed 2026-11-17 for the adaptive stepper (steps stretched onto a wall within 10 %) | fixed steps: accepted with warning |
| Cubic Kerr / XPW: 1-D envelope only | | planned |
| Kerr lens `cellMode`: round beams, peak power, a command (not an element) | | planned |
| `multiFrog` with two traces finds the time-reversed twin in most starts | `docs/papers/kida-2014-double-frog/repro_2.py` | planned (investigate) |
| Single-shot cross-correlator: no crystal propagation, scatter, stripe filter, face reflections, fibre array | | planned |

## Numerics and resources

| limitation | cause / evidence | status |
|---|---|---|
| A run keeps the pulse after every element and, by default, every element's transverse profile (`outputs.keepProfiles: all`); fits and studies now force `used` | `Engine/Executor.cs`; fixed for fits 2026-11-17 (the fitting plugin ran out of memory after 2 h because its residual cache held 65 full results) | accepted for single runs; the memory estimate warns ([memory](../reference/memory.md)) |
| GPU memory check compares with free device memory and the number of concurrent device slots; it is an estimate (driver overhead and the FFT plans' work areas are approximated) | `plugins/gpu/src/GpuBackend.cs` | accepted; conservative margin |
| GPU coverage: no spatial χ(2), spatial mixed precision, carrier-resolved propagation or spatial gain on the device; callbacks always run on the CPU | `GpuBackend.WhyNot` | planned (roadmap §3) |
| Mixed precision is refused above 70 dB of instrument dynamic range (FP32 fills the contrast floor below about -85 dB) | FP32 study, `docs/reference/speed.md` | accepted |

## Process and verification

| limitation | status |
|---|---|
| net48 tests never run on the development machines (CI runs them under Mono on Linux) | accepted |
| GPU verified only on one RTX 3060 Ti (CUDA and OpenCL) | accepted until other hardware is available |
| Zemax plugin never run against OpticStudio | accepted (decision P2) |
| `release.yml` has never run; nothing is on PyPI | needs the owner (roadmap §1) |
| No code coverage number | planned (roadmap §7) |
| Built-in constants marked `approximate` / `unverified` in the catalogue (CS2, BaF2, rotational Raman T2, several n2) are listed in every run's `unverifiedInputs` | needs the owner's papers (roadmap §6) |
