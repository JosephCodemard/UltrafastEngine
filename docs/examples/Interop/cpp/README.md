# C++ example

`devices.cpp` lists the compute devices (CPU, CUDA, OpenCL), self-tests them (`--selftest`) and runs a system on
the fastest suitable one (`uf_devices_json`, `uf_set_default_backend("auto")`; docs/plugins/gpu.md).

`run_system.cpp` uses the C API (`src/UltrafastEngine.Native/include/ultrafast.h`): load a system and run spec,
register a C++ time-response callback and a progress callback, run, read the summary JSON and an array, write
the results. The same API is usable from C, Fortran (ISO_C_BINDING), MATLAB (`loadlibrary`), LabVIEW, Julia...

Build the engine as a native library first (needs the .NET 10 SDK and, on Windows, the VS "Desktop development
with C++" workload):

```bash
dotnet publish src/UltrafastEngine.Native -c Release -r win-x64 -o artifacts/native/win-x64
cmake -S docs/examples/cpp -B build/cpp -DULTRAFAST_DIR=artifacts/native/win-x64
cmake --build build/cpp --config Release
build/cpp/Release/run_system docs/examples/01-spm-plate/plate.ufs docs/examples/01-spm-plate/run.json out
```
