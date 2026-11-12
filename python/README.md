# ultrafast-engine (Python)

Python bindings for **UltrafastEngine** -- nonlinear ultrafast pulse propagation through complete optical
systems (stretchers, compressors, multipass cells, plates, gas cells), with optional OpticStudio export.

```bash
./setup.sh --python --venv .venv --check            # from a repository checkout: builds the engine, installs this package (editable)
python install.py --preset python                   # or: from a release, without compiling (needs the .NET 10 runtime)
python install.py --preset python-native            # or: the NativeAOT engine, no .NET needed
```

The package is a thin layer over the engine's native library, so `pip install` alone is not enough: the engine is built (or
installed from a release) first. Details, plugin choice and troubleshooting: [Install and build](../docs/install.md). Nothing is on PyPI yet.

```python
import ultrafast as uf
from ultrafast import build as b
sysd = b.system("plate", [b.bulk("plate", "FusedSilica", 5e-3, beam=b.gaussian_beam(100e-6, 2.5e-3))])
res = uf.run(sysd, b.run_spec(b.pulse(1.03e-6, 200e-15, energy=20e-6)))
print(res.summary["output"])
```

Full documentation: [`docs/reference/python.md`](../docs/reference/python.md); calculators: `ultrafast.calc` (time-bandwidth product), `ultrafast.crystal` (phase matching).

<p align="center">
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../assets/logo/ultrafast-logo-long-dark.svg">
  <img alt="UltrafastEngine" src="../assets/logo/ultrafast-logo-long.svg" height="44">
</picture>
</p>
