# Extending the engine

There are three levels, from no code to C#:

1. **Data or expressions** in the system file / run spec ([Models](models.md)) -- no code.
2. **Callbacks** in Python, C/C++ or C#: register a function under a name, reference it as `"@name"`.
3. **New model kinds, element types, instruments or FFT back-ends** in C#.

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

Delegates: `IndexCallback`, `TimeResponseCallback`, `FilterCallback`, `BeamCallback`, `ArrayCallback`.

## A new model kind

Implement the interface (`IRefractiveIndex`, `IAttenuation`, `ITimeDomainResponse`, `ISpectralFilter`,
`IBeamModel`, `IAperture`, `IDelayedResponse`) and register a factory for a `kind`:

```csharp
public sealed class ThermalSellmeier : IRefractiveIndex
{
    private readonly double _t;
    public ThermalSellmeier(double temperatureK) { _t = temperatureK; }
    public string Description => "thermal Sellmeier at " + _t + " K";
    public void Evaluate(double[] omega, double[] n) { /* fill n[i] */ }
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
use inside nonlinear segments. Set `Mechanism` so the loss budget names it. Element types are mapped to terms in
`Engine/Compiler.cs` (`CompileElement`).

## A new instrument

```csharp
public sealed class BeamQualityMeter : IInstrument
{
    public string Kind => "myMeter";
    public void Measure(InstrumentContext ctx, InstrumentOutput o)
    {
        o.Values.Set("peakPowerW", PulseMetrics.Summarize(ctx.Pulse).PeakPowerW);
        o.Add("trace", someArray, "W", "what it is");
    }
}
InstrumentFactory.Register("myMeter", spec => new BeamQualityMeter());
```

## Another FFT

Implement `Numerics.IFft` (one in-place power-of-two transform) and set `Fft.Provider = new MyFft();` once at
start-up (e.g. FFTW or MKL through P/Invoke, cuFFT for GPU work). Results report the back-end name. Other lengths
use the built-in mixed-radix / Bluestein transforms ([Speed](speed.md)).

## A compute back-end

To run whole nonlinear segments elsewhere (a GPU, a cluster), implement `Numerics.IComputeBackend` and register it
(`ComputeBackends.Register(...)` or a plugin's `ctx.RegisterBackend(...)`); a run selects it with
`"compute": {"backend": "name"}`. See [Speed and compute back-ends](speed.md#compute-back-ends).

## A retrieval algorithm

Implement `Retrieval.IRetrieval` (`Name`, `TraceKinds`, `Retrieve(TraceData, JsonObject options)`) and register it
with `Retrievals.Register(...)` or `ctx.RegisterRetrieval(...)` ([Pulse retrieval](retrieval.md#your-own-algorithm)).

## Materials from refractiveindex.info

No code needed: `{"index": {"kind": "refractiveindex", "page": "..."}}` reads any page of the database
([Materials](materials.md#refractiveindexinfo-pages)).

## Conventions for contributors

See `CONTRIBUTING.md`: SI units in names (`lengthM`), physics sign convention, every new physics feature comes
with an analytic or cross-code test, no NuGet dependencies in Core.
