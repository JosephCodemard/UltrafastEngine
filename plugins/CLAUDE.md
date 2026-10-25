# plugins/ -- first-party plugins

A plugin = `plugin.json` + assemblies for net10.0 and net48 + optional `python/` module. Build any project that
imports `build/Plugin.targets` and it is laid out in `artifacts/plugins/<id>/`; hosts copy that folder next to the
engine. Guide: `docs/reference/plugins.md`; extension points in `src/UltrafastEngine.Core/Plugins/Plugins.cs`.

* **fitting** (`fitting/src`): `transform` elements and operators (`TransformOperators.cs`, register more with
  `TransformOperators.Register`), the `fit` run mode (`Fitting.cs`, `Estimators.cs`: LM, CMA-ES, MCMC), backward
  propagation (`Backward.cs`). Recorded-data comparison is **core** (`UltrafastEngine.Measured`); the plugin uses
  `result.Comparisons`. Fit residual vectors must keep their length between evaluations (points outside an
  instrument's range carry residual 0; count points with `Comparison.Points`).
  Python module `fitting/python/ultrafast_fitting` (`fx.measured` / `fx.comparisons` are aliases of the core).
* **zemax** (`zemax/src`): `UltrafastEngine.Zemax.Sampling` (any OS: sampler, system builder, fake OpticStudio,
  record/replay -- this is the plugin) and `UltrafastEngine.Zemax` (Windows net48 x64 OpticStudio extension; builds
  against stubs when no ZOS-API is found). **Never touch, copy or package `ZOSAPI*.dll`** (Ansys property; `lib/` is
  git-ignored and edit-protected).
* After changing a plugin's Python: rerun `python3 python/tools/build_engine.py`.
