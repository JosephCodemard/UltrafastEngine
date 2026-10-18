# Active elements, aux beams and by-products

Most elements are *passive*: dispersion, loss and the Kerr effect act on the one beam the system follows.
**Active elements** can bring in other beams, run their own internal loops and change what the main beam *is*. The system stays sequential: one main beam goes in, one comes out.

```
                 aux beams (pump, seed, ...)
                         │
   main beam ──▶ [ active element ] ──▶ main beam (the field you choose with "output")
                         │
                         └──▶ by-products (other fields, recorded: res.At["<id>#<field>"])
```

| element | what it does | docs |
|---|---|---|
| `crystal` | χ(2) mixing: SHG, SFG, OPA, DFG (+ χ(3)) | [Crystals](crystals.md) |
| `bulk`/`gap` with `"transverse"` | spatially resolved propagation | [Spatial models](transverse.md) |
| `lens`, `mirror` with `radiusOfCurvatureM` | thin lens on a carried transverse profile | [Spatial models](transverse.md) |
| `user` | your own function of the main beam's spectrum | below |
| `gain`, `amplifier` | pumped gain media, multipass and regenerative amplifiers | [Gain media](gain.md) |
| `oscillator`, `saturableAbsorber` | mode-locked lasers | [Oscillators](oscillators.md) |

## Why one main beam

Following one beam keeps the system file, the results and the Zemax mapping simple: every element has one input
and one output, the energy budget is a single ledger, and instruments sit anywhere along one line. Interactions
that need more beams get them as **aux beams** declared on the element; fields the element produces but the
system does not follow are kept as **by-products**. Branching systems (beam splitters that recombine later) are not
modelled; they are on the [roadmap](../roadmap.md).

## Aux beams

```jsonc
"aux": [
  { "role": "pump",
    // where the beam comes from -- one of:
    "pulse": { "shape": "gaussian", "centerWavelengthM": 515e-9, "durationFwhmS": 0.8e-12, "energyJ": 200e-6 },
    // "from": "../08-shg-lbo/results",      // the output field of a saved run (summary.json + result.npz)
    // "energyJ": 150e-6,                    //   ... optionally rescaled to this energy
    "beam": { "kind": "gaussian", "radiusM": 0.6e-3 },   // also superGaussian (+ "order"), flatTop;
                                                          // radiusXM/radiusYM; radiusOfCurvatureM
    "delayS": "auto" }                       // or a delay in s relative to the main beam (positive = later)
]
```

* `pulse` takes the same keys as the run's pulse (chirp, measured spectrum, ...).
* `from` reads the **output** of a previous run: the exact field (time, spectrum, phase) on its own grid. Paths are
  relative to the system file. This is how a pump line (example 08) feeds an amplifier (example 09) without
  simulating both in one system.
* `delayS: "auto"` puts the aux beam's energy centroid on the main beam's. The `delayScan` instrument re-runs the
  element for a range of delays.
* Roles depend on the element (`pump`, `signal`, `idler` for OPA/DFG; `b` for SFG).
* An `aux` override in the run spec (`{"target": "element:nopa", "property": "aux", "value": [...]}`) replaces the
  list without touching the system file.

Aux energy enters the energy budget as a **negative** entry (`"from pump (aux)"`): energy brought in, not lost.

## By-products

Fields that leave an active element but do not continue as the main beam -- the depleted pump, the idler, the
unconverted fundamental -- are kept when listed in `"record"` (default: all of them):

* `res.At["<id>#<field>"]` (C#), snapshots `probe_<id>_<field>_*` in `result.npz` (time, power, wavelength,
  spectrum, phase), summaries in `summary.json → elements[i].byproducts`;
* the `"to <field>"` entries of the energy budget;
* the `byproducts` figure of `ultrafast-plot` (spectra and powers of every field leaving each element);
* `"from": "<results>"` of a later run can only read a run's main output, so make the field you want to reuse the
  `output` of its own run (e.g. a run whose crystal has `"output": "idler"`).

## Carrier changes

If the output field has a different carrier (SH, idler, ...), the main beam continues on a **new grid** planned
for the rest of the system (window and resolution for the new spectrum and the downstream dispersion). Notes record
the change ("carrier changed 1029.97 -> 514.95 nm", "grid re-planned ..."), `elements[i].carrierWavelengthM`
tracks it, `summary.json → outputGrid` describes the final grid, and the system dispersion bookkeeping restarts
(it describes the new field). Grids are related by powers of two in window and time step and carriers are snapped to
grid bins, so the resampling between them is exact.

## User elements

A user element hands the main beam's spectrum to your function and continues with what it returns:

```jsonc
{ "id": "shaper", "type": "user", "callback": "@dazzler", "params": { "gddFs2": -4700, "todFs3": -3100 } }
```

**Python** (example [12](../examples/12-user-element/README.md)):

```python
def dazzler(ctx: uf.ElementContext):
    w = ctx.omega - ctx.omega0                           # rad/s, ascending
    phase = -0.5 * ctx.params["gddFs2"] * 1e-30 * w**2 - ctx.params["todFs3"] * 1e-45 * w**3 / 6
    return ctx.ax * np.exp(1j * phase)                   # or (ax, ay), or ctx.set(ax, ay); None = unchanged

res = uf.run(system, spec, elements={"dazzler": dazzler})   # or uf.Run(...).element("dazzler", dazzler)
```

`ElementContext`: `omega`, `t`, `omega0`, `dt`, `arrival`, `element_id`, `params`, `ax`, `ay` (complex spectra,
√W per bin), `time_x()`.

**C#**:

```csharp
var reg = ModelRegistry.CreateDefault();
reg.RegisterCallback("dazzler", (ElementCallback)(ctx => {
    for (int i = 0; i < ctx.N; i++) { /* modify ctx.AxRe[i], ctx.AxIm[i], ctx.AyRe[i], ctx.AyIm[i] */ }
}));
var result = Simulation.Run(system, run, reg);
```

**C / C++**: `uf_run_set_element_callback(run, "dazzler", fn, user)` with
`int fn(void* user, uf_element_ctx* ctx)` modifying `ctx->ax_re`, `ax_im`, `ay_re`, `ay_im` in place
(see `src/UltrafastEngine.Native/include/ultrafast.h`).

Energy removed or added by the function is booked as `"user"` in the element's energy budget; NaN output is an
error. `lengthM` (optional) is only used for the cumulative length.

## Writing a new active element (C#)

Implement `IActiveElement`:

```csharp
public sealed class MyElement : IActiveElement
{
    public string Kind => "myElement";
    public string Description => "...";                  // shown in the element's terms
    public double OutputOmega0 => double.NaN;            // nominal output carrier, if it changes
    public bool TransverseAware => false;                // true if it uses/produces PulseField.Profile
    public PulseField Execute(ActiveContext ctx, PulseField p)
    {
        // ctx.Run (compiled run), ctx.Record (notes, values), ctx.Energy("mechanism", joules),
        // ctx.Byproduct("name", field), ctx.DownstreamGrid(field) (grid for a new carrier), ctx.Progress, ctx.Cancel
        return p;
    }
}
```

and compile it from JSON in `Compiler.CompileElement` (or through `CompilerServices` from your own namespace, as
`CrystalCompiler` does). [Extending](extending.md) covers registries and models.
