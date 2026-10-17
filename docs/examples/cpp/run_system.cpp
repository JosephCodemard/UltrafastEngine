// C++ example: load a system file and a run spec, register a C++ time-response model, run, read results.
//
//   Build the engine:  dotnet publish src/UltrafastEngine.Native -c Release -r win-x64   (or linux-x64 / osx-arm64)
//   Build this:        cmake -S docs/examples/cpp -B build -DULTRAFAST_DIR=<publish folder> && cmake --build build
//   Run:               build/run_system docs/examples/01-spm-plate/plate.ufs docs/examples/01-spm-plate/run.json out
//
// In Visual Studio 2022: File > Open > Folder... on docs/examples/cpp (CMake support), set ULTRAFAST_DIR in
// CMakeSettings / CMakePresets, build, and copy ultrafast.dll next to the exe (done automatically below).
#include "ultrafast.h"

#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>

static void check(int rc, const char* what)
{
    if (rc != UF_OK) {
        std::fprintf(stderr, "%s failed (%d): %s\n", what, rc, uf_last_error());
        std::exit(1);
    }
}

// An extra instantaneous Kerr term, Delta n = n2_extra * I -- the C++ counterpart of the Python callbacks.
struct ExtraKerr { double n2; long calls = 0; };

static int extra_kerr(void* user, const uf_response_ctx* ctx, double* dn_re, double* /*dn_im*/)
{
    auto* m = static_cast<ExtraKerr*>(user);
    for (int i = 0; i < ctx->n; ++i) dn_re[i] = m->n2 * ctx->intensity[i];
    ++m->calls;
    return 0;
}

static int progress(void*, const char* id, int32_t index, int32_t count, double fraction)
{
    std::printf("\r[%d/%d] %-20s %5.1f %%", index + 1, count, id, 100.0 * fraction);
    std::fflush(stdout);
    return 0; // non-zero would cancel
}

int main(int argc, char** argv)
{
    if (argc < 3) { std::fprintf(stderr, "usage: run_system <system.ufs> <run.json> [out_dir]\n"); return 1; }
    std::printf("UltrafastEngine %s\n", uf_version());

    uf_system* sys = nullptr;
    check(uf_system_load_file(argv[1], &sys), "uf_system_load_file");

    uf_run* run = nullptr;
    check(uf_run_create_file(sys, argv[2], &run), "uf_run_create_file");

    // registered under a name; referenced from a run-spec override:
    //   {"target": "element:*", "property": "timeResponse", "mode": "add", "model": {"kind": "callback", "name": "extraKerr"}}
    ExtraKerr model{ 0.0 };   // set n2 > 0 and add the override above to make it act
    check(uf_run_set_time_response_callback(run, "extraKerr", extra_kerr, &model, 0), "set callback");
    check(uf_run_set_progress_callback(run, progress, nullptr), "set progress");

    uf_result* res = nullptr;
    check(uf_run_execute(run, &res), "uf_run_execute");
    std::printf("\n");

    // scalar results: JSON
    char* summary = nullptr;
    check(uf_result_summary_json(res, &summary), "summary");
    std::printf("summary.json: %zu bytes\n", std::string(summary).size());
    uf_free(summary);

    // arrays: query the size, then copy into your own buffer
    int32_t ndim = 0; int64_t shape[4] = { 0 }, length = 0;
    check(uf_result_array_info(res, "output_power_w", &ndim, shape, &length), "array info");
    std::vector<double> power(static_cast<size_t>(length));
    check(uf_result_array_copy(res, "output_power_w", power.data(), length), "array copy");
    double peak = 0; for (double p : power) peak = p > peak ? p : peak;
    std::printf("output peak power: %.4g W (%lld samples); model called %ld times\n", peak, static_cast<long long>(length), model.calls);

    if (argc > 3) { check(uf_result_write(res, argv[3]), "write"); std::printf("results written to %s\n", argv[3]); }

    uf_release(res);
    uf_release(run);
    uf_release(sys);
    return 0;
}
