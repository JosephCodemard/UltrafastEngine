# Zemax plugin: the OpticStudio binding

!!! note "Plugin"
    The Zemax integration is the **zemax plugin** (`plugins/zemax`): the sampler, system builder, fake OpticStudio
    and record/replay (cross-platform, `UltrafastEngine.Zemax.Sampling`), the CLI commands `zemax-build`,
    `zemax-replay` and `zemax-demo`, and the OpticStudio program itself in the plugin's `tools/net48` folder
    (`artifacts/plugins/zemax/tools/net48/UltrafastEngine.Zemax.exe` after a build). The core engine does not depend
    on it.

`UltrafastEngine.Zemax.exe` (Windows, .NET Framework 4.8, x64) connects to OpticStudio through ZOS-API, samples
the loaded sequential system by ray tracing and merit-function operands, and writes a **system file**. All
physics then happens in the engine -- on that PC or anywhere else. OpticStudio Professional or Premium is
required (ZOS-API).

## Commands

```
UltrafastEngine.Zemax export <export.json> [-o system.ufs] [--save-sample s.ufsample.json] [--record session.json]
UltrafastEngine.Zemax run <export.json> <run.json> [-o results]       export, then propagate in-process
UltrafastEngine.Zemax info                                             surfaces, materials, coatings, wavelengths
UltrafastEngine.Zemax thermal-lens <results> --element ID --surface N  write a gain element's thermal lens as a Gradient 1 surface
UltrafastEngine.Zemax selftest                                         fake systems, no OpticStudio needed
connection: (default) Interactive Extension -- in OpticStudio: Programming > Interactive Extension, then run
            --standalone --file lens.zos     private OpticStudio instance
            --opticstudio <install folder>   if the installation is not found
```

Cross-platform companions (`ultrafast` CLI): `zemax-build` rebuilds a system file from a saved raw sample with
different export settings (no OpticStudio); `zemax-replay` re-runs the sampler on a recorded session;
`zemax-demo` exports built-in fake systems.

## What is read, and how

| quantity | source |
|---|---|
| surfaces | LDE rows: material, comment, surface type, coating name, thickness, semi-diameter, ignore flag |
| sample wavelengths | written into the Wavelength Data Editor in batches (OpticStudio's limit), restored afterwards -- also on error |
| chief-ray geometry | batch ray trace (error/vignetting codes, OPL) + global positions/directions from **RAGX/RAGY/RAGZ, RAGA/RAGB/RAGC** (cross-checked against `GetGlobalMatrix`) |
| angle of incidence | **RAID** per surface and wavelength |
| surface normals | **RENA/RENB/RENC** (rotated to global coordinates) |
| index of each medium | `ILensDataEditor.GetIndex` (relative to air) |
| Gaussian beam | **GBPS** (radius) and **GBPW** (waist) at every surface via temporary merit-function rows (removed again) |
| coatings (optional) | merit operand (`coatingSource: "zemaxOperand"`, see below) or rules |
| environment | System > Environment temperature and pressure |

Operands are evaluated with `GetOperandValue` -- your merit function is not modified (except the temporary
GBPS/GBPW rows, appended and removed in one go).

## From samples to a system file

* **Group delay, not phase.** tau(omega) = sum n_g(omega) L(omega) / c over the gaps, with L the geometric
  length along that wavelength's own chief ray and n_g from a polynomial fit of OpticStudio's index. This is
  exact through diffraction gratings (Fermat's principle with the grating phase), where a phase-path model
  (omega x OPL / c) gets even the sign of the GDD wrong; both are reported in `source.dispersionSummary`.
* **Media.** Coordinate breaks and MIRROR rows inherit the medium before them (OpticStudio semantics).
  `media` rules (by material, comment or surface type) say which media are nonlinear and what the engine should
  know about them (gas pressure, n2, Raman...).
* **Elements.** Consecutive linear gaps become one `dispersion` element (with `airPathM` for the absolute-index
  correction); each pass through a nonlinear medium becomes a `bulk` element with its traced group delay and a
  sampled Gaussian beam profile w(z) reconstructed from GBPS at both ends and GBPW (waist position solved
  analytically; reported per pass); every surface that does something -- index step, mirror, grating, coating
  rule, aperture that clips -- becomes an `interface` with per-wavelength AOI, ray vectors (`kIn`, `kOut`,
  `normal`) for exact polarisation basis tracking, semi-diameter and beam radius (clipping, fluence).
  Wavelengths vignetted on the chief ray are blocked at the vignetting surface.
* **Materials.** Non-air media get OpticStudio's own index at the sampled wavelengths (`table`, relative to
  air) and, when the glass is a known built-in (F_SILICA, N-BK7...), the library's n2 and Raman via `base`.

## `export.json`

See `plugins/zemax/src/UltrafastEngine.Zemax/examples/export.json` (fully commented) and `docs/examples/07-zemax-workflow`.

| key | |
|---|---|
| `pulse` | optional run-spec pulse: sets the carrier and the auto sampling range |
| `sampling.mode` | `auto` (pulse spectrum above `thresholdDb`, widened by `margin`), `explicit` (`minWavelengthM`, `maxWavelengthM`), `zemax` (the WDE as is) |
| `sampling.count`, `batchSize`, `geometry` (`operands` / `batchRayTrace`), `carrierWavelengthM` | |
| `beam` | `waistRadiusM`, `waistToSurface1M`, `m2` -- the input beam for GBPS/GBPW. OpticStudio treats the waist as the embedded Gaussian waist and scales by M |
| `media[]` | `matchMaterial` / `matchComment` / `matchType`, `material` (name in the system file), `nonlinear`, `definition` (material JSON), `beam` (fallback) |
| `coatings[]` | `matchComment` / `matchCoating` / `matchMaterial` / `matchType`, `coating` (coating JSON), `lidt`, `roughnessRmsM` |
| `coatingSource` | `rules` (default) or `zemaxOperand` |
| `coatingOperand` | `type` (default `CODA`), `codes` {Rs, Rp, Ts, Tp, PhaseRs...}, `dataArgument`, `phaseInDegrees` -- **verify the data codes against your OpticStudio's operand help before use**; a sanity check (R + T <= 1) disables it if the values look wrong |
| `apertures`, `vectors`, `mergeLinear`, `vignetting` | switches (all true by default) |

## Debugging without OpticStudio

* `--save-sample` writes the raw sampled data; `ultrafast zemax-build sample.json export.json` rebuilds the
  system file anywhere (e.g. on a Mac) -- iterate on media/coating rules without re-tracing.
* `--record session.json` records every ZOS-API call; `ultrafast zemax-replay session.json export.json` replays
  the sampler bit for bit.
* `plugins/zemax/tools/ZosApiStubs` lets the binding compile without OpticStudio (`-p:UseZosApiStubs=true`; automatic when
  ZOS-API is not found, with a warning).

## Building

The project compiles against the ZOS-API of your OpticStudio installation (default
`C:\Program Files\Ansys Zemax OpticStudio 2025 R1`; pass `-p:ZosApiDir="..."` for another one). The assemblies are
Ansys property and are not part of this repository. You may copy `ZOSAPI.dll`, `ZOSAPI_Interfaces.dll` and
`ZOSAPI_NetHelper.dll` from your installation into `plugins/zemax/src/UltrafastEngine.Zemax/lib` (git-ignored) to
build elsewhere.
ZOSAPI and ZOSAPI_Interfaces are resolved from the installation at run time by ZOSAPI_NetHelper (not copied).

## Known assumptions to confirm on your installation

All operand names and interface members used by the binding exist in the ZOS-API assemblies (checked at compile
time against OpticStudio 2025 R1). What remains to confirm is run-time behaviour:

* GBPW shares GBPS's parameter layout [Surf, Wave, UseX, Waist, S1toW, M2] (confirmed for GBPS).
* RENA/RENB/RENC return the local surface normal; RAID returns degrees. If an operand is missing the export
  continues without that quantity and says so.
* The coating operand codes (only with `coatingSource: "zemaxOperand"`).

## Thermal lens back to OpticStudio

`thermal-lens <results folder> --element rod --surface 5 [--dry-run]` reads the gain element's
`thermalGrinNr2PerM2`, `thermalDnOnAxis` and `hostIndex` from `summary.json`. It converts surface 5 (the crystal,
whose thickness is the crystal length) into a **Gradient 1** surface, n(r) = n0 + Nr2·r², with
n0 = n_host + Δn(0) and Nr2 converted to lens units (z-averaged; ΔT step = thickness/50). It uses the typed
ZOS-API `ISurfaceGradient1` (DeltaT, n0, Nr2, Nr1), checked against the supplied assemblies. **To confirm:** a
Gradient 1 surface takes n0 at the primary wavelength and the glass dispersion from the catalogue. The change is
not saved; save the lens file yourself.

## Active elements

The binding exports passive optics (surfaces, gaps, gratings, coatings, beam sizes). Nonlinear crystals, lenses
acting on transverse profiles and user elements are not recognised from OpticStudio yet: add them to the exported
system file -- e.g. replace an exported crystal slab `bulk`
element by a `crystal` element with the same `id` and length. Automatic mapping of crystal surfaces is on the
[roadmap](../roadmap.md).
