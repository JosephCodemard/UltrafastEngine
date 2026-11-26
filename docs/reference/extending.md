# Extending the engine

The engine is extended at three levels, from no code to C#. This page shows each with the interface to implement and a minimal example; plugins ([Plugin system](plugins.md)) are the way to ship level 3 to other users.

1. **Data or expressions** in the system file / run spec ([Models](models.md)) -- no code.
2. **Callbacks** in Python, C/C++ or C#: register a function under a name, reference it as `"@name"`.
3. **New model kinds, element types, instruments or FFT back-ends** in C#. Ship them as a **plugin** ([Plugin system](plugins.md)); a program that links the library can also register them in code.

## Keys

Nothing is configured in a file at levels 2 and 3: you register by name in code and reference it from the system file or run spec (`"@name"`, `"kind": "myKind"`).

## Callbacks from C#

```csharp
var reg = ModelRegistry.CreateDefault();
reg.RegisterTimeResponse("plasma", (ctx, dnRe, dnIm) =>
{
    double rho = 0, dt = ctx.T[1] - ctx.T[0];
    for (int i = 0; i < ctx.N; i++) { rho += sigma * Math.Pow(ctx.Intensity[i], 4) * dt; dnRe[i] = -rho / (2 * n0 * rhoC); }
}, carrierResolved: false);
reg.RegisterCallback("myIndex", (IndexCallback)((omega, n) => { for (int i = 0; i < omega.Length; i++) n[i] = 1.45; }));
var result = Simulation.Run(SystemDescription.Load("sys.ufs"), RunSpec.Load("run.json"), reg);
```

Delegates: `IndexCallback`, `TimeResponseCallback`, `FilterCallback`, `BeamCallback`, `ArrayCallback`, `ElementCallback(UserElementContext)` (the `user` element) and `FieldCallback(x, y, z, valueOut)` (position-dependent properties).

## A new model kind

Implement the interface (`IRefractiveIndex`, `IAttenuation`, `ITimeDomainResponse`, `ISpectralFilter`,
`IBeamModel`, `IAperture`, `IDelayedResponse`, `ISpatialField` for category `field`; `ISmoothIndex` and `IDispersiveResponse` are optional extensions) and register a factory for a `kind`:

```csharp
public sealed class ThermalSellmeier : IRefractiveIndex
{
    private readonly double _t;
    public ThermalSellmeier(double temperatureK) { _t = temperatureK; }
    public string Description => "thermal Sellmeier at " + _t + " K";
    public void Evaluate(double[] omega, double[] nOut) { /* fill nOut[i] */ }
}

reg.Register<IRefractiveIndex>("index", "thermalSellmeier", (spec, r) => new ThermalSellmeier(spec.GetRequired("temperatureK")));
// JSON: "index": { "kind": "thermalSellmeier", "temperatureK": 350 }
```

Contracts: evaluate whole arrays; never keep references to the arrays you are given; return NaN outside your
range of validity (the engine detects it); be deterministic.

## A new linear effect

Derive from one of the term base classes in `Physics/LinearTerms.cs` and implement one method:

* `PhaseTerm` -- absolute phase phi(omega) (+ optional amplitude, optional y phase);
* `GroupDelayTerm` -- absolute group delay tau(omega) (+ optional power transmission);
* `JonesTerm` -- full 2x2 Jones matrix per frequency.

The base classes handle the co-moving frame (carrier phase/delay removal and reporting), GDD/TOD reporting and
use inside nonlinear segments. Set `Mechanism` so the loss budget names it. A built-in element type is mapped to terms in `Engine/Compiler.cs` (`CompileElement`); a new element type from a plugin registers its compiler with
`ctx.RegisterElement` ([Plugin system](plugins.md#writing-a-plugin-c)), which adds the terms to `compiled.Post` or the nonlinear segment.

## A new instrument

```csharp
public sealed class PeakPowerMeter : IInstrument
{
    public string Kind => "myMeter";
    public void Measure(InstrumentContext ctx, InstrumentOutput o)
    {
        o.Values.Set("peakPowerW", PulseMetrics.Summarize(ctx.Pulse).PeakPowerW);
        o.Add("trace", someArray, "W", "what it is");   // someArray: any double[]
    }
}
InstrumentFactory.Register("myMeter", spec => new PeakPowerMeter());
```

## Another FFT

Implement `Numerics.IFft` (`Name` and `TransformInPlace(Complex[] data, int exponentSign, bool normalize)`, one in-place transform of any length; time grids are powers of two) and set `Fft.Provider = new MyFft();` once at
start-up. The providers `managed`, `fftw` and `accelerate` are built in (`"compute": {"fft"}`, [Speed](speed.md)); MKL is used through the FFTW library path
(`ULTRAFAST_FFTW_LIBRARY=mkl_rt`), not as a provider of its own. Results report the back-end name. The managed provider handles other lengths with its mixed-radix / Bluestein transforms.

## A compute back-end

To run whole nonlinear segments elsewhere (a GPU, a cluster), implement `Numerics.IComputeBackend` and register it
(`ComputeBackends.Register(...)` or a plugin's `ctx.RegisterBackend(...)`); a run selects it with
`"compute": {"backend": "name"}`. See [Speed and compute back-ends](speed.md#compute-back-ends); the
[gpu plugin](../plugins/gpu.md) is a complete example (`IDeviceBackend : IComputeBackend`; the problem classes `EnvelopeProblem`, `SpatialProblem` and `Chi2Problem` describe themselves to a device with `DeviceSpec()`).

## A retrieval algorithm

Implement `Retrieval.IRetrieval` (`Name`, `Description`, `TraceKinds`, `Retrieve(TraceData, JsonObject options)`) and register it
with `Retrievals.Register(...)` or `ctx.RegisterRetrieval(...)` ([Pulse retrieval](retrieval.md#your-own-algorithm)).

## Materials from refractiveindex.info

No code needed: `{"index": {"kind": "refractiveindex", "page": "..."}}` reads any page of the database
([Materials](materials.md#refractiveindexinfo-pages)).

## Conventions for contributors

See `CONTRIBUTING.md`: SI units in names (`lengthM`), physics sign convention, every new physics feature comes
with an analytic or cross-code test, no NuGet dependencies in Core.

## Validation

`PluginTests` registers an element and an instrument through a plugin and runs them; `Chi2Tests` and `GainTests` register callbacks in code, and `ComputeTests` a back-end. The
examples `docs/examples/GettingStarted/custom-index-python` and `user-element` use callbacks from Python.

## Limits

* Contracts of a model kind: whole arrays per call, no references kept to the arrays, NaN outside the range of validity, deterministic results.
* A callback response runs on the CPU, also when a GPU back-end is selected.
* The engine does not load C# code from a system file: extension code is registered by the program or the plugin that starts the run.
