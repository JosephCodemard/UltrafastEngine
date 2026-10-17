# Speed and compute back-ends

## What runs where

* **FFT**: the managed radix-2 FFT (cached twiddle tables, parallel passes for N ≥ 4096) for the time grids (powers
  of two); any other length -- cartesian transverse grids, retrieval grids, your own code -- goes to the mixed-radix
  FFT (factors up to 13) or to Bluestein's chirp-z algorithm (any length, e.g. a prime). `Fft.Provider` can be
  replaced by FFTW, MKL, vDSP, ... ([Extending](extending.md)).
* **Element-wise kernels** (operator products, axpy, exponentials) use 128-bit SIMD on .NET 10: SSE2 on x64, NEON on
  ARM64 (Apple silicon). .NET Framework 4.8 runs the scalar code. `ComplexKernels.Accelerated` reports it;
  `ComplexKernels.ForceScalar = true` switches it off.
* **Nonlinear steps** run on the selected compute back-end.

## Compute back-ends

```jsonc
"compute": { "backend": "cpu", "threads": 8 }      // run spec; threads 0 = all cores
```

A back-end (`IComputeBackend`) receives a whole nonlinear segment -- linear operator, nonlinear operator, length and
stepper settings -- and returns the field at its end, so a GPU back-end can keep the field on the device for all
steps instead of copying it for every FFT. The built-in `cpu` back-end runs the interaction-picture Runge-Kutta
steppers. GPU back-ends (Vulkan / OpenCL, CUDA, Metal) are plugins:

```csharp
public sealed class MyGpuBackend : IComputeBackend
{
    public string Name => "vulkan";
    public string Description => "Vulkan compute, device " + ...;
    public bool Supports(IPropagationProblem p) => p is EnvelopeProblem;     // what it can do on the device
    public Complex[] Propagate(IPropagationProblem p, Complex[] a0, StepperSettings st, StepStatistics stats, Action<double> progress) { ... }
}
// in the plugin: ctx.RegisterBackend(new MyGpuBackend());
```

Problems a back-end does not support fall back to the CPU (per element); an unknown back-end name falls back with a
warning. `ultrafast info` lists the FFT provider; results note the back-end used.

## Studies

Parameter studies freeze the grid, cache the unchanged prefix of the system and run points in parallel batches:
[Parameter studies](studies.md).

## Practical advice

* Let the grid planner size the grid; a 4096-point 1-D run with a few nonlinear elements takes milliseconds to a
  second. Spatial models multiply the cost by the number of transverse points (radial 64-160, cartesian 64² ... 256²).
* For many runs (scans, fits) prefer studies over a Python loop: they reuse the grid and the prefix.
* `"stepper": {"tolerance": 1e-5}` is usually enough; the default 1e-6 is conservative.
