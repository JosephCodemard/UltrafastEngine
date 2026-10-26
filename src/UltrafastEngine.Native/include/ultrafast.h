/*
 * ultrafast.h -- C API of UltrafastEngine 2.0 (nonlinear ultrafast pulse propagation through open-air optical systems)
 *
 * Library: ultrafast.dll (Windows), libultrafast.so (Linux), libultrafast.dylib (macOS), built with
 *   dotnet publish src/UltrafastEngine.Native -c Release -r <win-x64|linux-x64|osx-arm64>
 *
 * Conventions
 *   - Every function returns UF_OK (0) on success or a negative UF_E_* code; uf_last_error() then gives a
 *     human-readable message (thread-local, valid until the next API call on the same thread).
 *   - Handles (uf_system*, uf_run*, uf_result*) are released with uf_release(). Strings returned through
 *     char** out-parameters are UTF-8, NUL-terminated and freed with uf_free().
 *   - All input strings are UTF-8. JSON formats are documented in docs/reference/system-file.md, docs/reference/run-spec.md and
 *     docs/reference/results.md. All physical quantities are SI (m, s, rad/s, W, J).
 *   - Callbacks are vectorised: one call per solver stage with whole arrays. Return 0 on success; a non-zero
 *     return aborts the run with UF_E_CALLBACK. Callbacks are invoked on the thread that called uf_run_execute.
 *   - Thread safety: different handles may be used from different threads concurrently; a single handle
 *     must not be used concurrently.
 */
#ifndef ULTRAFAST_H
#define ULTRAFAST_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#  define UF_API __declspec(dllimport)
#else
#  define UF_API
#endif
#define UF_CALL

/* ---------------------------------------------------------------- status codes */
#define UF_OK              0
#define UF_E_ARGUMENT     -1   /* null pointer / bad argument            */
#define UF_E_PARSE        -2   /* JSON or file-format error              */
#define UF_E_MODEL        -3   /* model / override could not be built    */
#define UF_E_RUN          -4   /* failure during propagation             */
#define UF_E_NOT_FOUND    -5   /* unknown array name, file not found ... */
#define UF_E_CALLBACK     -6   /* a user callback returned non-zero      */
#define UF_E_CANCELLED    -7   /* progress callback asked to stop        */
#define UF_E_BUFFER       -8   /* destination buffer too small           */
#define UF_E_INTERNAL     -99

typedef struct uf_system uf_system;   /* a loaded system file (.ufs)          */
typedef struct uf_run    uf_run;      /* a system + run spec + callbacks      */
typedef struct uf_result uf_result;   /* everything a run produced            */

/* ---------------------------------------------------------------- callbacks */

/* Everything a time-domain response sees (see docs/reference/models.md#time-response). Arrays have length n. */
typedef struct uf_response_ctx {
    int32_t       n;
    const double* t;                 /* co-moving time (s)                                  */
    const double* t_lab;             /* lab time = t + t_arrival (s)                        */
    double        t_arrival;         /* arrival time of the frame origin at this z (s)      */
    double        z;                 /* position inside the element (m)                     */
    double        length;            /* element length (m)                                  */
    const double* intensity;         /* cycle-averaged intensity (W/m^2)                    */
    const double* intensity_x;
    const double* intensity_y;
    const double* instant_intensity; /* = intensity in envelope mode                        */
    const double* field;             /* real field (sqrt(W)), NULL in envelope mode         */
    double        omega0;            /* carrier angular frequency (rad/s)                   */
    double        area;              /* effective area (m^2)                                */
    int32_t       carrier_resolved;  /* 1 in carrier-resolved field mode                    */
    const char*   element_id;
    double        x, y;              /* transverse position of this point (m; 0 in 1-D)     */
    int32_t       spatial;           /* 1 when evaluated at one point of a transverse grid  */
} uf_response_ctx;

/* Delta n(t) = dn_re + i dn_im (dn_im > 0 is absorption); dn_re/dn_im are pre-zeroed. */
typedef int (UF_CALL *uf_time_response_fn)(void* user, const uf_response_ctx* ctx, double* dn_re, double* dn_im);
/* refractive index n(omega) */
typedef int (UF_CALL *uf_index_fn)(void* user, int32_t n, const double* omega, double* n_out);
/* spectral filter: power transmission (pre-filled with 1) and phase (pre-filled with 0) */
typedef int (UF_CALL *uf_filter_fn)(void* user, int32_t n, const double* omega, double* transmission, double* phase);
/* beam radii (1/e^2, m) at positions z (m from the element start) */
typedef int (UF_CALL *uf_beam_fn)(void* user, int32_t n, const double* z, double* radius_x, double* radius_y);
/* position-dependent property f(x, y, z) (m), e.g. "indexProfile": "@name" (docs/reference/spatial-properties.md) */
typedef int (UF_CALL *uf_field_fn)(void* user, int32_t n, const double* x, const double* y, const double* z, double* out);
/* generic y = f(x), e.g. attenuation alpha(omega) or a Raman response h(t) */
typedef int (UF_CALL *uf_array_fn)(void* user, int32_t n, const double* x, double* y);
/* What a user element sees (type "user"): the main beam's spectrum, writable in place. Arrays have length n. */
typedef struct uf_element_ctx {
    int32_t       n;
    const double* omega;             /* absolute angular frequency of each bin (rad/s), ascending */
    const double* t;                 /* co-moving time axis (s)                                   */
    double        omega0;            /* carrier (rad/s)                                           */
    double        dt;                /* time step (s)                                             */
    double*       ax_re;             /* spectral envelope Ax (sqrt(W) per bin), real part -- write the new spectrum here */
    double*       ax_im;
    double*       ay_re;
    double*       ay_im;
    double        arrival;           /* lab arrival time of the frame at the element (s)          */
    const char*   element_id;
    const char*   params_json;       /* the element's "params" object as JSON ("{}" if none)       */
} uf_element_ctx;

/* user element: transform the main beam's spectrum in place */
typedef int (UF_CALL *uf_element_fn)(void* user, uf_element_ctx* ctx);
/* progress: return non-zero to cancel */
typedef int (UF_CALL *uf_progress_fn)(void* user, const char* element_id, int32_t index, int32_t count, double fraction);

/* ---------------------------------------------------------------- library */
UF_API const char* UF_CALL uf_version(void);
UF_API const char* UF_CALL uf_last_error(void);
UF_API void        UF_CALL uf_free(void* p);       /* strings returned by the API */
UF_API void        UF_CALL uf_release(void* h);    /* uf_system*, uf_run*, uf_result* */
/* JSON describing built-in model kinds, materials, crystals and instruments */
UF_API int         UF_CALL uf_capabilities(char** json_out);
/* JSON command: studies, pulse retrieval, material library.
   request: {"command": "study" | "retrieve" | "materials.list" | "materials.show" | "materials.import" | "devices" |
   "compute.default", ...}
   (docs/reference/c-api.md). The reply (free with uf_free) is a JSON object. */
UF_API int         UF_CALL uf_command(const char* request_json, char** result_json_out);
/* compute back-ends and devices (CPU, CUDA, OpenCL; docs/reference/speed.md). options_json may be NULL or
   {"selftest": true, "benchmark": false, "backend": "cuda", "device": 0}: the self-test compares each device with
   the CPU on standard problems. Same as uf_command({"command": "devices", ...}). Free the reply with uf_free. */
UF_API int         UF_CALL uf_devices_json(const char* options_json, char** json_out);
/* back-end of runs whose spec has no "compute": {"backend"}: "cpu", "auto", "cuda", "opencl"; NULL or "" restores the
   default (ULTRAFAST_BACKEND, else "cpu"). Returns UF_E_ARGUMENT for an unknown back-end. */
UF_API int         UF_CALL uf_set_default_backend(const char* name);
/* phase-matching calculator (docs/reference/crystals.md): JSON request in, JSON result out */
UF_API int         UF_CALL uf_crystal_phase_match(const char* request_json, char** result_json_out);

/* ---------------------------------------------------------------- systems */
UF_API int UF_CALL uf_system_load_file(const char* path, uf_system** out);
UF_API int UF_CALL uf_system_load_json(const char* json, uf_system** out);
UF_API int UF_CALL uf_system_to_json(uf_system* s, char** json_out);
UF_API int UF_CALL uf_system_validate(uf_system* s, char** report_json_out);

/* ---------------------------------------------------------------- runs */
UF_API int UF_CALL uf_run_create(uf_system* s, const char* run_json, uf_run** out);
UF_API int UF_CALL uf_run_create_file(uf_system* s, const char* run_path, uf_run** out);
UF_API int UF_CALL uf_run_set_time_response_callback(uf_run* r, const char* name, uf_time_response_fn fn, void* user, int32_t carrier_resolved);
UF_API int UF_CALL uf_run_set_index_callback(uf_run* r, const char* name, uf_index_fn fn, void* user);
UF_API int UF_CALL uf_run_set_filter_callback(uf_run* r, const char* name, uf_filter_fn fn, void* user);
UF_API int UF_CALL uf_run_set_beam_callback(uf_run* r, const char* name, uf_beam_fn fn, void* user);
UF_API int UF_CALL uf_run_set_array_callback(uf_run* r, const char* name, uf_array_fn fn, void* user);
UF_API int UF_CALL uf_run_set_element_callback(uf_run* r, const char* name, uf_element_fn fn, void* user);
UF_API int UF_CALL uf_run_set_field_callback(uf_run* r, const char* name, uf_field_fn fn, void* user);
UF_API int UF_CALL uf_run_set_progress_callback(uf_run* r, uf_progress_fn fn, void* user);
UF_API int UF_CALL uf_run_execute(uf_run* r, uf_result** out);

/* ---------------------------------------------------------------- results */
UF_API int UF_CALL uf_result_summary_json(uf_result* res, char** json_out);
UF_API int UF_CALL uf_result_provenance_json(uf_result* res, char** json_out);
UF_API int UF_CALL uf_result_array_names(uf_result* res, char** json_list_out);
/* ndim <= 4; shape receives ndim entries; length = product of shape */
UF_API int UF_CALL uf_result_array_info(uf_result* res, const char* name, int32_t* ndim, int64_t* shape4, int64_t* length);
UF_API int UF_CALL uf_result_array_copy(uf_result* res, const char* name, double* dest, int64_t capacity);
UF_API int UF_CALL uf_result_write(uf_result* res, const char* directory);

#ifdef __cplusplus
}
#endif
#endif /* ULTRAFAST_H */
