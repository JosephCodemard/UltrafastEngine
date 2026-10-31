# C API (C, C++ and everything else)

Header: `src/UltrafastEngine.Native/include/ultrafast.h`. Library: `ultrafast.dll` / `libultrafast.so` /
`libultrafast.dylib`, built with NativeAOT -- a self-contained native library, no .NET installation needed by
the caller:

```bash
dotnet publish src/UltrafastEngine.Native -c Release -r win-x64      # linux-x64, linux-arm64, osx-x64, osx-arm64
```
(NativeAOT needs the C++ build tools: Visual Studio "Desktop development with C++" on Windows, clang on
Linux/macOS. `-p:UF_NO_AOT=true` builds the managed, hosted variant instead.)

## Life cycle

```c
uf_system* sys;  uf_system_load_file("stretcher.ufs", &sys);          // or uf_system_load_json(json, &sys)
uf_run* run;     uf_run_create_file(sys, "run.json", &run);           // or uf_run_create(sys, json, &run)
uf_run_set_time_response_callback(run, "plasma", my_plasma, &state, 0);
uf_run_set_progress_callback(run, my_progress, NULL);
uf_result* res;  uf_run_execute(run, &res);
char* json;      uf_result_summary_json(res, &json);  ...; uf_free(json);
int32_t nd; int64_t shape[4], n;  uf_result_array_info(res, "output_spectrum", &nd, shape, &n);
double* buf = malloc(n * sizeof(double));  uf_result_array_copy(res, "output_spectrum", buf, n);
uf_result_write(res, "out");
uf_release(res); uf_release(run); uf_release(sys);
```

* Every function returns `UF_OK` (0) or a negative `UF_E_*` code; `uf_last_error()` gives the message
  (thread-local). Codes: `ARGUMENT -1`, `PARSE -2`, `MODEL -3`, `RUN -4`, `NOT_FOUND -5`, `CALLBACK -6`,
  `CANCELLED -7`, `BUFFER -8`.
* Strings returned through `char**` are UTF-8 and freed with `uf_free`; handles with `uf_release`.
* Different handles may be used from different threads; one handle must not be used concurrently.
* `uf_capabilities` returns the built-in model kinds, materials, element types and instruments (JSON).

## All functions

| group | functions |
|---|---|
| library | `uf_version()`, `uf_last_error()`, `uf_free(p)`, `uf_release(h)`, `uf_capabilities(&json)`, `uf_command(request, &json)`, `uf_devices_json(options, &json)`, `uf_set_default_backend(name)`, `uf_crystal_phase_match(request, &json)` |
| system | `uf_system_load_file(path, &sys)`, `uf_system_load_json(json, &sys)`, `uf_system_to_json(sys, &json)`, `uf_system_validate(sys, &report)` |
| run | `uf_run_create(sys, json, &run)`, `uf_run_create_file(sys, path, &run)`, the `uf_run_set_*_callback` functions below, `uf_run_execute(run, &res)` |
| result | `uf_result_summary_json(res, &json)`, `uf_result_provenance_json(res, &json)`, `uf_result_array_names(res, &json)`, `uf_result_array_info(res, name, &ndim, shape4, &length)`, `uf_result_array_copy(res, name, dest, capacity)`, `uf_result_write(res, directory)` |

`uf_version` and `uf_last_error` return strings owned by the library: do not free them. A Branched system is the same file with `"topology": "branched"`, so the C API has no calls of its own for it: `uf_system_load_*` takes either form, and `uf_result_summary_json` gains `graph` ([Branched systems](branches.md)).

## Callbacks

All callbacks are vectorised (whole arrays per call) and return 0 for success (non-zero aborts the run with
`UF_E_CALLBACK`). They are registered under a name and referenced from JSON as `{"kind": "callback", "name": ...}`
or `"@name"`.

The header names these types `uf_time_response_fn`, `uf_index_fn`, `uf_filter_fn`, `uf_beam_fn`, `uf_array_fn`, `uf_element_fn`, `uf_field_fn` and `uf_progress_fn`.

| registration | signature | used as |
|---|---|---|
| `uf_run_set_time_response_callback(run, name, fn, user, carrier_resolved)` | `int fn(void* user, const uf_response_ctx* ctx, double* dn_re, double* dn_im)` | `timeResponse` |
| `uf_run_set_index_callback` | `int fn(void* user, int32_t n, const double* omega, double* n_out)` | material `index` |
| `uf_run_set_filter_callback` | `int fn(void* user, int32_t n, const double* omega, double* T, double* phase)` | `filter` |
| `uf_run_set_beam_callback` | `int fn(void* user, int32_t n, const double* z, double* wx, double* wy)` | `beam` |
| `uf_run_set_array_callback` | `int fn(void* user, int32_t n, const double* x, double* y)` | `raman` (h(t)); as `name#index` for `attenuation` |
| `uf_run_set_element_callback(run, name, fn, user)` | `int fn(void* user, uf_element_ctx* ctx)` -- modify `ctx->ax_re/ax_im/ay_re/ay_im` in place | `user` element (`"callback": "@name"`) |
| `uf_run_set_field_callback(run, name, fn, user)` | `int fn(void* user, int32_t n, const double* x, const double* y, const double* z, double* value)` | position-dependent properties (`"@name"`, [spatial-properties.md](spatial-properties.md)) |
| `uf_run_set_progress_callback` | `int fn(void* user, const char* element_id, int32_t i, int32_t count, double fraction)` | return non-zero to cancel |

`uf_response_ctx` carries `n`, `t`, `t_lab`, `t_arrival`, `z`, `length`, `intensity`, `intensity_x`,
`intensity_y`, `instant_intensity`, `field` (NULL in envelope mode), `omega0`, `area`, `carrier_resolved`,
`element_id`, and (at the end of the struct) `x`, `y` (m) and `spatial` (1 on spatial grids). Output arrays arrive zeroed.

`uf_element_ctx` carries `n`, `omega` (absolute, ascending), `t`, `omega0`, `dt`, the writable spectra
`ax_re`, `ax_im`, `ay_re`, `ay_im`, `arrival`, `element_id`, `params_json`.

The phase-matching calculator is a plain function: `uf_crystal_phase_match(const char* request_json, char** result_json)`
(free the result with `uf_free`); request and result as in [Crystals](crystals.md#phase-matching-calculator).

Studies, retrieval and the material library go through one JSON entry point,
`uf_command(const char* request_json, char** result_json)` (free the result with `uf_free`; relative paths are
relative to the current directory):

```c
char* out;
uf_command("{\"command\": \"retrieve\", \"method\": \"pcgpa\", \"trace\": \"results/result.npz\", \"output\": \"retrieved\"}", &out);
uf_command("{\"command\": \"study\", \"system\": \"chain.ufs\", \"run\": \"run.json\", \"mode\": \"scan\","
           " \"parameters\": [{\"path\": \"pulse/energyJ\", \"min\": 1e-4, \"max\": 1e-2, \"scale\": \"log\", \"points\": 5}],"
           " \"outputs\": [\"record:rod/BIntegralRad\"], \"output\": \"study-results\"}", &out);
uf_command("{\"command\": \"materials.show\", \"name\": \"main/SiO2/Malitson\"}", &out);
```

Commands: `study` ([Parameter studies](studies.md)), `retrieve` ([Pulse retrieval](retrieval.md)),
`materials.list`, `materials.show`, `materials.import` ([Materials](materials.md#refractiveindexinfo-pages)),
`devices` and `compute.default` (below).

## Compute devices (GPU)

```c
char* json;
uf_devices_json(NULL, &json);                       /* {"defaultBackend", "fft", "backends": [...], "devices": [...]} */
uf_free(json);
uf_devices_json("{\"selftest\": true, \"benchmark\": false, \"backend\": \"cuda\", \"device\": 0}", &json);
uf_set_default_backend("auto");                     /* runs without "compute": {"backend"} use the GPU when it helps */
```

`uf_devices_json` lists the CPU and every CUDA / OpenCL device; with `"selftest": true` each device is compared with
the CPU on standard problems (`"selftest": [{"backend", "device", "passed", "checks": [...]}]`), with
`"benchmark": true` it is also timed. `uf_set_default_backend` sets the back-end of runs whose spec names none
(`"cpu"`, `"auto"`, `"cuda"`, `"opencl"`; NULL restores `ULTRAFAST_BACKEND` or `"cpu"`); an unknown name returns
`UF_E_ARGUMENT`. The library has the [gpu plugin](../plugins/gpu.md) compiled in (unless it was packaged without it);
`docs/examples/cpp/devices.cpp` is a complete program.

A complete C++ program is in `docs/examples/cpp` (CMake; opens directly in Visual Studio).

## Other languages

Anything with a C FFI works: MATLAB (`loadlibrary('ultrafast', 'ultrafast.h')`), Julia (`ccall`), Fortran
(`bind(C)`), LabVIEW (Call Library Function), Rust (`bindgen`). The Python package is a thin `ctypes` layer
over this same API.
