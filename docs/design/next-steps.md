# Next steps

Open items after the November 2026 iteration (what was built: [history](history.md)). Longer-term design that is not scheduled: [backlog](backlog.md).
Rules of `CLAUDE.md` apply: a physics change needs a published or analytic reference in a test; a disagreement is a finding; tags and releases only when the owner asks.

## 0. Install and release (done in the install overhaul; open parts)

- Done: `docs/install.md`, `build/setup.py`, `install.py --preset`. Open: run `release.yml` once on GitHub (needs a tag, the owner's call) and check the "download `install.py` then `--preset full`" path against a real release; decide on a PyPI upload (forbidden until the owner says so; the wheel and its extras are ready); `pip install "ultrafast[plugins]"` extras only make sense once the wheel carries the plugin folders.
- Pulse calculator ideas not done: other TBP shapes (Lorentzian, asymmetric exponential), a GDD sign from a phase measurement, a `tbp` instrument in `run.json`.

## 1. WP9: documentation overhaul (large; in waves)

Every example README and landing page follows one structure instead of "the story":

1. **Question** (one sentence: what is tested, why it matters).
2. **Setup:** the engine's schematic and a parameter table (SI units in the keys); the exact command.
3. **What is checked:** reference (analytic, published or a second code) and tolerance, stated before the result.
4. **Results:** hero figure, then a heat map or animation (every example gets one), the table of numbers, the quoted output.
5. **Extend it:** three or four concrete changes (keys to turn, what to expect, which example to look at next) with a Python or `.ufs` fragment.
6. **Limits and provenance:** what the model leaves out, open findings, unverified inputs (from the catalogue).

Mechanics: `docs/examples/_template.md`; `build/example_lint.py` (required sections, hero, animation or heat map, quoted-output block, `# optional:` on figure scripts, no wall time in quoted output) run in CI beside
`example_check.py`; a one-file `example.json` per example (title, group, question, references, figures) feeding the gallery and group indexes.
Waves: (1) the 12 examples written in S5; (2) `Wavefronts`, `LaserSystems` (several results per chain: wavefront, contrast, FROG trace, beam maps), `Nonlinear`; (3) `Diffraction`, `Branched`
(with the cavity figure), `Diagnostics`, `Components`, `GettingStarted`, `DigitalTwin/transforms`; (4) landing pages (`docs/index`, `examples/README`, `concepts`), the plugin pages' "what is checked" sections.
Reference pages stay reference pages. Missing AO / twin examples (component gallery, complex-field shaping, spline against network) are written directly in the new structure.

**Cavity figure** (part of WP9): `Branched/cavity-and-coherence` has no figure. Draw the Fox-Li iteration (mode converging round trip by round trip, radius against the ABCD eigenmode) and the Gaussian-Schell coherent modes and width against `w(z)`.

## 2. Roadmap: needs the owner's input or a larger piece of work

| item | why not now |
|---|---|
| **Verify approximate / unverified material values against their papers**, starting with CS2 (n2, 90/10 electronic-orientational split, 1.6 / 0.2 ps constants) and BaF2 (n2, sigma = -1.2); rotational Raman dephasing `T2At1Bar` of N2, O2, CO2, H2; the other n2 values | needs the owner's PDFs; the CREOL scans could not be rendered. Then re-run `liu-2013`, `shimizu-1967`, `kaur-2024` |
| LBO (Kato 1994) and the Jundt / Gayer LiNbO3 e-ray forms | no database page to compare with; needs the papers |
| Yb:YAG / Nd:YAG Fuchtbauer-Ladenburg consistency: do the catalogue's cross-section fits integrate to the radiative rate? | a one-hour check; the ASE source term depends on sigma_e through these fits (all `approximate`) |
| Birth phase of tunnelling electrons: drift-energy loss assumes a uniform birth phase; in carrier-resolved mode with PPT / ADK electrons are born near the crests, so the model books less loss (comparison test uses MPI) | needs a reference calculation (U_p at birth phase, Corkum 1993) and a change in how the energy is booked; not quick |
| Ray-based ASE for bulk crystals and disks, parasitic lasing, fibre use (backlog C3 builds on `AseModel`) | separate package |
| Stress: slice-by-slice retardation, the common-mode radial / tangential lenses on the field, vector gain for a y input | needs Koechner's formulas read from the source (not written from memory) and a reference for each; a package of its own |
| MCP server (T6): `ultrafast twin serve` exits with 2 | design below |
| GPU parity of the modulator and tile source; `release.yml` has never run on GitHub (Linux / Windows NativeAOT jobs, wheel in a fresh venv; macOS NativeAOT is built on the owner's Mac); its smoke test installs `examples.Branched` but does not run it | needs a GPU / a tag, which only the owner creates |
| Live-data plugin, ML / PINN plugins, PyPI wheels and a NuGet package (PyPI publishing is forbidden by the owner), OpticStudio User Extension | after the first release |

## 3. Small items that are ready to do

* `asePedestalEnergyFraction` is reported but not fed into the next stage's `pulse.pedestal`.
* A lens under-sampling warning (phase step per cell above pi at the beam edge) would have caught the tiled-array case.
* Python has no typed accessor for `unverifiedInputs`.
* The examples still pass `gain=` to SPGD explicitly; they could use the new default.

## 4. MCP server: the agreed design (not built)

Hand-written JSON-RPC 2.0 over newline-delimited stdio; no `mcp` dependency. Check the current MCP specification for the `protocolVersion` string and message shapes before writing.
Refusals are results with `isError: true`; unknown methods give -32601; notifications get no reply.

* **Eight tools:** `describe`, `validate`, `run`, `compare_to_measured`, `scan`, `fit`, `explain_warning`, `plot`; workspace-scoped, read-only with respect to lab hardware.
* **Worker:** engine work runs in a persistent subprocess with its own stdout. On overrun it is killed and respawned. A pre-flight estimate (runs x grid points) refuses up front. Memory: `DOTNET_GCHeapHardLimit`
  plus caps on N and nx*ny*spectralSamples; not `RLIMIT_AS`. The worker starts with `ULTRAFAST_HOME` set to an empty private folder so user plugins are not loaded.
* **Refuse by default:** strings starting with `@`, every callback / python / expression / user kind (list from `Schema/BuiltIn*.cs`); `set` narrowed to roots `pulse/`, `element:`, `instrument:` and never
  `type`, `kind`, `measured`, `file`; values are finite numbers or identifier-pattern strings; file-valued strings must resolve inside the workspace; names match `^[A-Za-z0-9_-]{1,40}$`; unknown arguments rejected.
* **Tests:** `tools/list` returns eight tools; refusals for an expression, an `@name`, `../x.csv`, an absolute path, a symlink out of the workspace, and a call over its time budget (the next call must still work).
