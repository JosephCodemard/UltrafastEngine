# Zemax plugin: checklist for the first run on a machine with OpticStudio

Status: **the plugin has never been run against a real OpticStudio since the engine's rewrite.** Everything that can be tested without
OpticStudio is (`ZemaxTests`, with a fake OpticStudio); this list is what only a real installation can confirm. Paste the output of every
step back into the session that works on the plugin.

Requirements: Windows, OpticStudio Professional or Premium (ZOS-API licence), the .NET SDK 10 and the .NET Framework 4.8 developer pack.
Never copy the ZOS-API DLLs into the repository or into a package (Ansys property); the build finds them in your OpticStudio folder.

## 1. Build

```
dotnet build UltrafastEngine.sln -c Release
```

Expect 0 errors. The plugin builds against **your** ZOS-API (default `C:\Program Files\Ansys Zemax OpticStudio 2025 R1`; set the MSBuild property
`ZosApiDir` for another folder) and says so; with the stub warning ("builds against stubs") it did not find your installation.

## 2. Self-test without OpticStudio (any OS)

```
artifacts\plugins\zemax\tools\net48\UltrafastEngine.Zemax.exe selftest
dotnet tests\UltrafastEngine.Tests\bin\Release\net10.0\UltrafastEngine.Tests.dll ZemaxTests
```

Expect all pass.

## 3. Connect and read (read-only; nothing is changed)

Open any sequential file in OpticStudio, then **Programming > Interactive Extension**:

```
UltrafastEngine.Zemax.exe info
```

Paste the output. Check against OpticStudio's own lens data editor:

| what | check |
|---|---|
| lens units | printed "Lens units: ... m" equals your setting (mm 0.001, cm 0.01, m 1, in 0.0254); an unknown unit is an error, not a default |
| environment | temperature (deg C) and pressure (atm) equal System Explorer > Environment |
| surfaces | thickness, semi-diameter, material, comment, coating of five surfaces you choose |
| wavelengths | all wavelengths and weights, the primary wavelength |

## 3b. Connection modes, licence and version checks, shutdown (Z1)

Each mode prints `OpticStudio found at ...` and `Connected to OpticStudio <version>, <mode>`; paste both lines.

| step | command | expect |
|---|---|---|
| version | `info` (interactive) | the printed version matches Help > About (e.g. 2025 R1 -> `25.1.x`); if it does not, paste both |
| interactive, wrong instance | `info --instance 5` with one OpticStudio | the "Could not attach ... check --instance" message, exit code 4, no crash |
| interactive, not waiting | `info` without clicking Interactive Extension | the "Programming > Interactive Extension" message (confirms `ConnectAsExtension` returns null rather than throwing) |
| interactive, shutdown | `info`, then look at OpticStudio | OpticStudio stays open with your system; Interactive Extension can be started again and `info` works a second time |
| standalone | `info --standalone --file <lens.zos>` | the surfaces of that file; afterwards **no** `OpticStudio.exe` left in the Task Manager |
| standalone, Ctrl+C | `export export.json --standalone --file <lens.zos>`, press Ctrl+C during sampling | "interrupted: closing the standalone OpticStudio instance"; no `OpticStudio.exe` left |
| standalone, bad file | `info --standalone --file nothere.zos` | the "could not load" message; no `OpticStudio.exe` left |
| user extension | copy `artifacts\plugins\zemax\tools\net48\` to `Documents\Zemax\ZOS-API\Extensions\UltrafastEngine\`, start `UltrafastEngine.Zemax.exe` from Programming > User Extensions with the arguments `info --extension` if your version allows arguments | the system's surfaces; OpticStudio stays open (if arguments cannot be passed, say so: Z6 adds the menu integration) |
| wrong folder | `info --opticstudio C:\nothere` | the "found no OpticStudio in 'C:\nothere'" message |
| licence | only if you have a Standard licence or a ZOS-API-less seat | the licence message naming the `LicenseStatus` value |

## 4. Export a system with a known answer

Use a system you can check by hand: a single plate (e.g. 5 mm N-BK7 at 10 deg) and, if you have one, a grating pair (Treacy: group delay 2 L / c ...).

```
UltrafastEngine.Zemax.exe export export.json -o system.ufs --save-sample sample.ufsample.json --record session.json
```

Compare with an independent value and write the numbers down:

| quantity | where to see it in OpticStudio | in the system file |
|---|---|---|
| group delay of the plate | OPL / group index x thickness / c (GRMN? or by hand with the catalogue index) | the `bulk` / `groupDelayS` table |
| angle of incidence | RAID operand | `aoiDeg` of the interface |
| surface normal / tilt (RENA, RENB, RENC) | the operands themselves | direction of the interface |
| beam radius along the gap | GBPS / GBPW operands (the code assumes GBPW has GBPS's parameter layout) | `beam.radiusM` table |

Differences above 1 % in any of these are findings: attach `session.json` (the record of every call) so the session can replay it on any OS
(`ultrafast zemax-replay session.json`).

## 4b. Zernike aberrations (Z2)

Build a test system: a paraxial lens (focal length f = 100 mm, entrance pupil diameter 10 mm, wavelength 1.03 um, one on-axis field).
Set the thickness of the surface before the image to **f + 1 mm**, so the image plane is 1 mm behind the focus. Add this to
`export.json`:

```
"zernike": { "field": 1, "maxTerm": 15, "sampling": 64 }
```

Run `export export.json -o system.ufs --save-sample sample.ufsample.json`, then check:

| check | expect |
|---|---|
| OpticStudio: Analyze > Wavefront > Zernike Standard Coefficients (64x64, wave 1, field 1) | Z4 equals `zernike.nollWaves[3]` in `sample.ufsample.json` to 1e-4 waves (confirms the ZERN layout: Term, Wave, Samp, Field, Type 1 = Standard) |
| the size of Z4 | about \|Z4\| = dz NA² / (4 √3 λ) = 1 mm × 0.05² / (4 √3 × 1.03 µm) ≈ 0.35 waves |
| the sign of Z4 | the code assumes **Z4 > 0** here (positive OPD = wavefront ahead of the reference sphere, so the engine gets −Z4). If OpticStudio reports Z4 < 0, set `"opdSign": 1` and report it: the default must change |
| `radiusM` of `zemax_wavefront` in `system.ufs` | 5e-3 (ENPD / 2 in metres) |
| the log line `Zernike Standard Z1..Z15 ...` | present; no "Zernike Standard Coefficients failed" warning |

Then tilt the lens by 2 deg about x (coordinate breaks): Z5 / Z6 (astigmatism) appear; check them against the analysis window the same way.

## 5. Run

```
UltrafastEngine.Zemax.exe run export.json run.json -o results
```

Expect a run without warnings that the sampler dropped an operand. If an operand is missing in your OpticStudio version the export continues
without that quantity and says so; paste the message.

## 6. Thermal lens back into OpticStudio (writes to the open file; do not save)

```
UltrafastEngine.Zemax.exe thermal-lens results --element rod --surface 5 --dry-run
```

then without `--dry-run`. Check that surface 5 becomes **Gradient 1** with n0 = host index + on-axis dn and Nr2 in lens units, and that a ray trace
shows the expected focal length. Close the file without saving.

## Unverified assumptions (the `TODO(zemax-hw)` comments in `ZosConnection.cs`, `ZosSystemAccess.cs`, `ZosAnalyses.cs` and `ZosConnectionCore.cs`)

* `ZOSAPI_Initializer.GetZemaxDirectory()`, `ZOSAPI_Connection.ConnectToApplication()`, `IZOSAPI_Application.LicenseStatus`, `ZOSMajorVersion` / `ZOSMinorVersion` / `ZOSSPVersion` exist with these names (step 1 fails to compile otherwise: paste the errors).
* `ConnectAsExtension` returns null when no OpticStudio waits; the minimum version 20.1 is right; not calling `CloseApplication` in interactive mode leaves no stale connection (ZOSPy calls it there).

* ZemaxUnits member names (`Millimeters`, `Centimeters`, `Meters`, `Inches`).
* `AddRay(wave, Hx, Hy, Px, Py, OPDMode)` and the column order of `ReadNextResult`.
* `GetGlobalMatrix`: rotation row-major, origin in lens units.
* Environment temperature in deg C and pressure in atm; the fallback to 20 C / 1 atm when the read fails.
* RENA/RENB/RENC return the local surface normal; RAID returns degrees; GBPW has GBPS's parameter layout; coating operand codes (only with `coatingSource: "zemaxOperand"`).
* ZERN parameters [Term, Wave, Samp (1 = 32x32, 2 = 64x64...), Field, Type (1 = Standard), Epsilon, Vertex]; ENPD / EXPD return diameters in lens units; positive OPD = wavefront ahead of the reference sphere (`opdSign` -1) (step 4b).
* Gradient 1: n0 is taken at the primary wavelength, the glass dispersion from the catalogue.
