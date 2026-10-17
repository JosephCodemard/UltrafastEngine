# UltrafastEngine 2.1.8-beta

Nonlinear ultrafast pulse propagation through open-air optical systems -- stretchers, compressors, multipass
cells, plates, gas cells, mirrors and coatings -- as a Zemax-free C# engine with C, C++, Python and command-line
interfaces, plus an OpticStudio binding that exports your sequential system for it.

```bash
dotnet build UltrafastEngine.sln
dotnet run --project src/UltrafastEngine.Cli -- run docs/examples/03-mpc-argon/mpc.ufs docs/examples/03-mpc-argon/run.json -o results
python python/src/ultrafast/plot.py results/     # figures + report.pdf in results/plots (or: ultrafast-plot results/)
```

```python
import ultrafast as uf
from ultrafast import build as b
res = uf.run(b.system("plate", [b.bulk("plate", "FusedSilica", 5e-3, beam=b.gaussian_beam(100e-6, 2.5e-3))]),
             b.run_spec(b.pulse(1.03e-6, 200e-15, energy=5e-6), instruments=[b.instrument("compressor")]))
print(res.summary["output"]["transformLimitFwhmS"], res.instrument("compressor")["compressedFwhmS"])
```

**Highlights:** exact ray-traced group-delay dispersion (correct through gratings) - absolute indices - thin-film
coatings, Fresnel, clipping, scatter, absorption with an exact loss budget - Jones polarisation with 3D basis
transport - Kerr + Raman + self-steepening with vector coupling - adaptive interaction-picture solver -
carrier-resolved field mode - your own n(t, I), index, filter and beam models as data, expressions or
Python/C/C# functions - provenance of every value - virtual instruments (spectrometer, AC/IAC, FROG, d-scan,
photodiode, contrast, polarimeter, ideal compressor) - χ(2) crystals, gain media, amplifiers and oscillators -
spatial (radial / cartesian) models - instruments that look through a fibre or pinhole in the near or far field,
compare themselves with your recorded data (CSV / NPZ) and calibrate the model with error bars - Gaussian beam
tracking through lenses and off-axis mirrors - plates with ghosts, ND filters, thin-film polarisers, Faraday
rotators, thick waveplates, aberrations - pulse pedestals and a third-order cross-correlator - rotational Raman of
molecular gases, ionisation and plasma, UPPE - the refractiveindex.info database - parameter scans, optimisation
and sensitivity studies - FROG and d-scan retrieval - cross-checked against pyNLO.

| | |
|---|---|
| Documentation | [`docs/`](docs/index.md) (`mkdocs serve`); start with the [tutorials](docs/tutorials/index.md) |
| Examples | [`docs/examples/`](docs/examples) -- 24 worked systems: SPM plate, CPA line, argon MPC, OPCPA, amplifiers, oscillators, every instrument, measured data and calibration, contrast, components, gas ionisation, Zemax workflow, C++ |
| Synthetic data with answers | [`docs/assets/`](docs/assets/README.md) |
| For AI agents | [`AGENTS.md`](AGENTS.md), [`llms.txt`](llms.txt), [`docs/agents.md`](docs/agents.md) |
| Status | [`PROJECT_STATUS.md`](PROJECT_STATUS.md) |
| Roadmap | [`ROADMAP.md`](ROADMAP.md) |
| Changelog | [`CHANGELOG.md`](CHANGELOG.md) |
| Contributing | [`CONTRIBUTING.md`](CONTRIBUTING.md) |

Licence: MIT.
