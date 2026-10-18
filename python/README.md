# ultrafast-engine (Python)

Python bindings for **UltrafastEngine** -- nonlinear ultrafast pulse propagation through complete optical
systems (stretchers, compressors, multipass cells, plates, gas cells), with optional OpticStudio export.

```bash
pip install -e python[hosted,plot]       # from a repository checkout
python python/tools/build_engine.py      # builds the engine (hosted) into the package
```

```python
import ultrafast as uf
from ultrafast import build as b
sysd = b.system("plate", [b.bulk("plate", "FusedSilica", 5e-3, beam=b.gaussian_beam(100e-6, 2.5e-3))])
res = uf.run(sysd, b.run_spec(b.pulse(1.03e-6, 200e-15, energy=20e-6)))
print(res.summary["output"])
```

Full documentation: `docs/python.md` in the repository.
