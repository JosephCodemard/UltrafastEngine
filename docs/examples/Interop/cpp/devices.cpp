// C++ example: list the compute devices (CPU, CUDA, OpenCL), optionally self-test them, then run a system on the
// fastest device that supports each element ("auto" back-end). The GPU back-ends are compiled into the native library.
//
//   Build the engine:  dotnet publish src/UltrafastEngine.Native -c Release -r win-x64   (or linux-x64 / osx-arm64)
//   Build this:        cmake -S docs/examples/cpp -B build -DULTRAFAST_DIR=<publish folder> && cmake --build build
//   Run:               build/devices                       (list)
//                      build/devices --selftest            (compare every device with the CPU)
//                      build/devices docs/examples/25-supercontinuum/fiber.ufs docs/examples/25-supercontinuum/run.json
#include "ultrafast.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

static void check(int rc, const char* what)
{
    if (rc != UF_OK) {
        std::fprintf(stderr, "%s failed (%d): %s\n", what, rc, uf_last_error());
        std::exit(1);
    }
}

int main(int argc, char** argv)
{
    bool selftest = argc > 1 && std::strcmp(argv[1], "--selftest") == 0;
    char* json = nullptr;
    check(uf_devices_json(selftest ? "{\"selftest\": true}" : nullptr, &json), "uf_devices_json");
    std::printf("%s\n", json);   // {"defaultBackend", "fft", "backends": [...], "devices": [...], "selftest": [...]}
    uf_free(json);
    if (argc < 3 || selftest) return 0;

    // every run whose spec has no "compute": {"backend"} now uses the fastest suitable device (else the CPU)
    check(uf_set_default_backend("auto"), "uf_set_default_backend");
    uf_system* sys = nullptr;
    check(uf_system_load_file(argv[1], &sys), "uf_system_load_file");
    uf_run* run = nullptr;
    check(uf_run_create_file(sys, argv[2], &run), "uf_run_create_file");
    uf_result* res = nullptr;
    check(uf_run_execute(run, &res), "uf_run_execute");
    char* summary = nullptr;
    check(uf_result_summary_json(res, &summary), "uf_result_summary_json");
    // the per-element notes in the summary say which device ran each nonlinear element ("device: cuda #0 ...")
    const char* p = std::strstr(summary, "device: ");
    std::printf("%s\n", p ? std::string(p, std::strcspn(p, "\"")).c_str() : "all elements ran on the CPU");
    uf_free(summary);
    uf_release(res); uf_release(run); uf_release(sys);
    return 0;
}
