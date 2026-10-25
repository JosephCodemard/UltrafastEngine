# python/ -- the `ultrafast` package

* The engine is reached through the C API: `_native.py` loads `bin/native` (NativeAOT, no plugins) or
  `bin/hosted` (managed, through clr_loader + the .NET 10 runtime, loads plugins). **Rebuild after any C# change:**
  `python3 python/tools/build_engine.py` (also copies the plugins' Python modules).
* `build.py`: builders that return plain dicts typed with the generated TypedDicts (`types.py`, from the schemas:
  `python3 python/tools/gen_types.py`; never edit `types.py`). Keyword-only options, SI units, a docstring that
  says what the engine does with each argument. New builder → `python3 python/tools/gen_api_docs.py`.
* `core.py` (`System`, `run`, `Result` incl. `comparisons`), `measured.py` (typed comparisons), `plotting.py`
  (figures; `FIGURES` list + `register_figure` for plugins), `plugins.py` (loads plugin Python modules).
* Tests: `tests/` with `conftest.requires_engine`; slow ones need `ULTRAFAST_SLOW=1`; pyNLO cross-checks need
  `PYNLO_SRC=$PWD/third_party/pynlo/src` (`python3 tools/get_pynlo.py` fetches the pinned commit).
* Type check: `mypy --follow-imports=silent src/ultrafast/build.py src/ultrafast/types.py`.
* Python ≥ 3.10, NumPy only (matplotlib optional for figures). Must work on arm64 macOS.
