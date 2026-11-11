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
* **gpu** (`gpu/src`): compute back-ends `cuda` / `opencl` (`GpuBackend.cs`), device layer (`GpuDevice.cs`,
  `OpenClDevice.cs`, `CudaDevice.cs`: vendor C APIs through function pointers, loaded at run time), **one** kernel
  source for both runtimes (`Kernels.cs`), device steppers mirroring the CPU (`EnvelopeStepper.cs`: ERK4(3)-IP /
  RK4IP + 1-D operator; `SpatialStepper.cs`), self-test / benchmark (`GpuSelfTest.cs`). Compiled into the NativeAOT
  library (`UF_WITH_GPU`; `ao` likewise with `UF_WITH_AO`, `AoPlugin.RegisterStatic`). A physics change to `EnvelopeProblem` / `SpatialEnvelopeProblem` must be mirrored in the
  kernels (or make `DeviceSpec()` return null for it) and checked by `GpuTests` (PoCL runs them without a GPU:
  docs/plugins/gpu.md). Docs: `docs/plugins/gpu.md`.
* **hitran** (`hitran/src`): the `index` / `attenuation` model kind `"hitran"` (`GasLines.cs`: `HitranAirIndex` -- Voigt lines,
  one complex line shape gives n and alpha; `Faddeeva.cs`; `HitranLines.cs`: `.par` and CSV readers; `HitranPlugin.cs`). Partition sums: `PartitionTable` (`<NAME>.partition.csv`, written by the downloader from `hapi.partitionSum`), else the power law. It implements
  `ISmoothIndex` (the propagation frame ignores the line group delay). No data in the repo: `ultrafast_hitran` (python) downloads
  them with the HITRAN API. Compiled into the NativeAOT library (`UF_WITH_HITRAN`). Docs: `docs/plugins/hitran.md`; tests `HitranTests`.
* **ao** (`ao/src`): `deformableMirror` and `slm` (`Devices.cs`: build a core `Optics.ModulatorElement` directly -- the core's modulator is the
  physics), `spectralShaper` and `aopdf` (`TemporalShapers.cs`: `ShaperTerm : PhaseTerm`, the programmed delay stays in the transfer function;
  frame removes only the carrier phase), `AoMath.cs` (erf for net48, map reader). Python module `ao/python/ultrafast_ao` (builders, device twins,
  `DmBench`, tile builder); the algorithms are NumPy in `python/src/ultrafast/algorithms/{holography,control,sensorless}.py`. The tile array is the core
  `initialProfile` kind `tiles`, not an element. Docs: `docs/plugins/ao.md`; tests `AoDeviceTests`, `python/tests/test_ao_*.py`.
* After changing a plugin's Python: rerun `python3 python/tools/build_engine.py`.
