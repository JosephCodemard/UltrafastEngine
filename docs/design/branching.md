# Design: branched systems

Status: **built** (stages 1 to 8, 2026-10-30; reviewed 2026-10-31). This page records the design and why it took this form. Where the plan and the code differ, the code and [what changed in building it](#15-what-changed-in-building-it) are right. User reference: [Branched systems](../reference/branches.md). Code: [Architecture](../architecture/index.md).
Background: auxiliary beams were built as "ports with a pluggable source, so a later branch layer only connects ports". This design is that layer.

## 1. The plan in brief

* **Every element has declared input and output ports**, in the schema, for all element types.
* **Two ways to write a system.** `Sequential([...])` is today's list. `Branched([...])` requires every element to say where its light goes, with `next`, naming the target's input port (`"BS1:1"`, or `{"id": "BS1", "port": 1}`). A link back makes a loop.
* **Internally every system is a graph.** A separate compile step builds it and verifies it. That step, not a different executor, is the difference between sequential and branched systems.
* **Virtual ports.** Any input port can be fed by a beam that belongs to no beamline: a `pulse`, or a result file. Today's `aux` beams are exactly this. Any output port can be chosen to continue, so a beam splitter works in a plain Sequential system. In a Branched system an unused port raises an alert and gets a virtual input or output.
* **Loops are in the design from the start**: the graph compiler handles them from its first version, and they run in the stage straight after acyclic graphs. They cannot come sooner, because a loop needs combining and packets.
* **Phase is the hard part.** The engine discards each element's carrier phase. A per-path ledger has to keep it ([section 6](#6-the-path-ledger-phase-and-time)).
* **Modelled instruments** become examples compared with the ideal instruments ([section 10](#10-modelled-instruments)).

## 2. Writing a system: `Sequential` and `Branched`

There are two ways to write a system, and one graph underneath.

**Sequential** is today's list: entries run in file order. `"output"` chooses the port that continues. Unused ports are normal here (a crystal's by-products), and are recorded and booked.

**Branched** is a list in which **every element states where its light goes**. There is no fall-through. In a file, `"topology": "branched"`; in Python, `Branched([...])`. The list order has no optical meaning, except that the first entry takes the run's input.

`next` is a string or a dict, and it names the *input port* of the target:

```jsonc
"next": "BS1:1"                                  // element BS1, input port 1
"next": { "id": "BS1", "port": 1 }               // the same
"next": "BS1"                                    // the default input port
"next": "BS1:b"                                  // ports also have names
"next": "end"                                    // the system's output
"next": { "c": "armA", "d": "bs2:2" }            // several outputs: one target per output port (name or number)
```

Port numbers start at 1 and follow the order declared for the element type, so port 1 of a splitter's inputs is `a`. A dict with the key `id` is one target; any other dict is keyed by output port.

A Mach-Zehnder:

```jsonc
{ "format": "ultrafast-system", "version": 3, "name": "mach-zehnder", "topology": "branched",
  "elements": [
    { "id": "bs1",  "type": "beamSplitter", "reflectivity": 0.5, "next": { "c": "armA", "d": "armB" } },
    { "id": "armA", "type": "gap", "lengthM": 0.2, "next": "bs2:1" },
    { "id": "armB", "type": "gap", "lengthM": 0.2, "next": "bs2:2" },
    { "id": "bs2",  "type": "beamSplitter", "reflectivity": 0.5, "next": { "c": "end", "d": "virtual" } }
  ] }
```

A ring with one output coupler, where the jump to `oc:2` closes the loop:

```jsonc
"elements": [
  { "id": "oc",  "type": "beamSplitter", "reflectivity": 0.9, "next": { "c": "leg", "d": "end" },
    "loop": { "kind": "ringDown", "maxRoundTrips": 200, "tolerance": 1e-6 } },
  { "id": "leg", "type": "gap", "lengthM": 0.01, "material": "Vacuum", "next": "oc:2" }
]
```

### Unused ports are alerted, and become virtual

In a Branched system, **every port of every element is accounted for**. The compiler raises a warning for each port nothing is connected to, and adds a virtual port so the system still runs:

| unused port | what the compiler adds | what it means |
|---|---|---|
| input | a virtual input with no beam | vacuum: no light enters there |
| output | a virtual output | the light is recorded (`at: "id:port"`) and booked in the budget |

To say a port is unused on purpose, and silence the warning, declare it: `"next": { "d": "virtual" }` for an output, `"inputs": { "b": { "virtual": true } }` for an input. A virtual input may also hold a beam, exactly as in a Sequential system ([section 4](#virtual-inputs)).

**Mis-wiring is caught.** A port takes one source, so two arms pointed at one input is a compile error: beams only meet at an element with two input ports. A forgotten `next` gives a warning naming the element and port.

| alternative | verdict |
|---|---|
| fall-through inside Branched, as in the previous revision | dropped: the owner prefers every link stated, which reads the same in any order |
| `connections` list, first draft | dropped: it moves the structure away from the elements |
| inferring the topology from the presence of `next` | dropped: a stated `topology` lets the compiler require a complete description |

Ids in study paths such as `element:armB/lengthM` do not change. Files without `topology` are Sequential, so no migration is needed. `id#port` keeps working in `at`, and `id:port` is accepted there too.

## 3. Compile and verify

Sequential and branched systems differ at this step, as the owner suggested, and only in how much it has to do.

```text
system file  ->  GraphCompiler  ->  SystemGraph  ->  segments, loops  ->  Compiler (as today)  ->  scheduler
                 ports, jumps       nodes, edges     cut at multi-port
                 verification       canonical form   nodes, cycles
```

`SystemGraph` is a plain data structure and the only input the rest of the engine sees. For a chain it has one segment, and verification finds nothing to do.

| check | result |
|---|---|
| unknown id or port; a target that is an output | error |
| an input port with two sources; a required port with none | error |
| unreachable element | error, naming it |
| loop with no `loop` settings, or with no way out, or with zero path length | error |
| two sources meeting that are not coherent, with no declared relation | warning: intensities add |
| two arms at different carriers meeting | error, unless they share a wide enough grid |
| window needed for arm delays and echoes, against `grid.maxN` | error, or packets ([section 6](#6-the-path-ledger-phase-and-time)) |
| a nonlinear element in a loop whose round trip is shorter than the pulse | warning: passes overlap in that element |
| net gain above 1 around a loop | warning for a `ringDown` |
| shared gain state (`stateId`) reached in an order that depends on arrival times | error when the order is ambiguous |
| a port nothing is connected to (Branched) | warning; a virtual port is added |
| a Branched element with no `next` | warning; a virtual output is added |

`ultrafast validate` already exists and gains all of this without running a simulation. New: `ultrafast validate system.ufs --graph` prints the nodes, edges, paths, loops, and each arm's delay and phase, as text or as a Mermaid diagram for the docs. The same output goes into `summary.json` as `graph`.

## 4. Ports on every element

`ComponentSpec` gains a declaration:

```csharp
.Ports(inputs: new[] { "a", "b" }, outputs: new[] { "c", "d" }, defaultIn: "a", defaultOut: "c")
```

The default for every element type is `in` and `out`, so only multi-port types declare anything. Ports appear in the schemas, `options.md`, and the Python types. Multi-port behaviour is a new interface beside `IActiveElement`:

```csharp
IReadOnlyDictionary<string, PulseField> Execute(ActiveContext ctx, IReadOnlyDictionary<string, PulseField> inputs);
```

| element | inputs | outputs |
|---|---|---|
| almost all | `in` | `out` |
| crystal | `in` and its aux roles (`pump`, `b`, ...) | its waves (`signal`, `idler`, `sum`, ...) |
| `thinFilmPolarizer` | `a`, `b` | `transmitted`, `reflected` |
| `beamSplitter` and variants | `a`, `b` | `c`, `d` |
| amplifier, later | `in`, `pump` | `out` |

A 2×2 splitter uses neutral names: c = t a + r' b and d = r a + t' b. `transmitted` and `reflected` are accepted aliases of `c` and `d`, correct for light entering at `a`. The ring above shows why neutral names are needed: light from `b` *reflects* to `c`.

### Virtual inputs

An input port with no beamline feeding it can hold a beam described in place. This works in sequential and branched systems alike:

```jsonc
{ "id": "bs", "type": "beamSplitter", "reflectivity": 0.5, "output": "d",
  "inputs": { "b": { "pulse": { "shape": "gaussian", "centerWavelengthM": 1030e-9, "durationFwhmS": 100e-15, "energyJ": 1e-6 },
                     "delayS": 100e-15, "phaseRad": 0, "coherentWith": "input" } } }
```

* The fields are those of `aux`: `pulse` or `from`, `beam`, `delayS`.
* **`aux` becomes a spelling of `inputs`**, with role as the port name. It stays valid forever and needs no migration, and its code path becomes the virtual-input path.
* `delayS` and `phaseRad` belong to virtual inputs only. A port fed by a beamline takes its timing from the path, so `delayS` there is an error rather than a silent offset. A `delay` element in the arm does the job.
* A virtual beam is its own source, so it adds in intensity with the main beam unless `coherentWith` says otherwise.
* The rule is the same everywhere: **a real beam, a virtual beam and an empty virtual port are three states of one port.** Replacing a virtual input with a link is how a Sequential sketch grows into a Branched model.

## 5. Execution

```text
Scheduler            order, copies at splits, loops, packets
   |
Segment runner       today's Executor loop, moved into a method: an unbroken chain of elements
   |
Elements, solvers    unchanged: linear terms, NonlinearSegment, IActiveElement, instruments
```

There is **one engine**. A separate executor would mean every physics change is made and tested twice. A chain is one segment and the scheduler does nothing.

| | sequential | branched |
|---|---|---|
| Speed per element | same code | same code |
| Overhead | one check at the start | a spectrum copy per split, a sum at each meeting |
| Time window | one path | must hold relative delays, or packets are used |
| Work done | one path | every arm and pass: the physics asked for |
| Studies | prefix cache | better: only what is downstream of the change reruns |

So sequential is not faster per element. A system that is really a chain takes the same time, and stage 1 has a benchmark to hold that. The advantage of a chain is that it is simpler to write and read.

The order is topological, with ties in file order so results repeat. A field is released after its last consumer unless an instrument asks for it. Arms do not run in parallel at first: the solvers already use threads.

### What the existing code has to change

| where | today | change |
|---|---|---|
| `Executor.Execute` | one `pulse`; `arrival`, `cumLen`, `cumB`, `system` as locals | loop body becomes the segment runner; locals become `PathLedger` |
| `PhaseTerm.FrameReference` | subtracts φ₀ and τ₀; `arrival` keeps τ₀, **φ₀ is lost** | adds them to the ledger |
| `Compiler` `c.Track` | one mutable `BeamState` | set per segment to a clone of the state on its input edge; a loop iterates it |
| `PlanDownstreamGrid`, `SatelliteRoom` | `Skip(index)` | elements reachable in the graph |
| `AuxBeamSpec` | an element's own list | the virtual-input source |
| `Byproducts`, `res.At["id#field"]` | recorded | also output ports, same names |
| `TailLedger` | one | per path |
| `Studies`, `ExecuteFrom` | prefix by index | cache per node, stage 8 |
| `PartialCoherence` | one run per mode | unaffected: the graph runs per mode |
| `Resonator`, `Oscillator` | nested sequences | stay, and give cross-checks for graph loops |

## 6. The path ledger: phase and time

`PathLedger` travels with each packet:

```text
ArrivalS        group delay of the carrier since the source (today's `arrival`)
PhaseRad        carrier phase since the source (new)
SourceId        which source this light came from
PhaseKnown      false once an element could not report its phase
Dispersion, CumulativeLengthM, BIntegralRad     today's locals
```

* `PhaseTerm` adds φ₀ and τ₀ to the ledger when it removes them from the frame. `DispersionPoint` gains a phase, so `own` still works. No field changes, so **all sequential results stay identical**.
* Active elements report phase through `ctx.Own`. One that does not sets `PhaseKnown = false`; a meeting downstream then warns that the fringe position is arbitrary, while the visibility remains right.
* Double precision is enough: 10 m is about 6×10⁷ rad, known to about 10⁻⁸ rad.
* When the carrier changes, the ledger follows: SHG gives φ₂ω = 2 φω plus the crystal's phase. An f-2f interferometer tests this.
* The sign follows `docs/concepts.md`, E = Re{A e^(−iω₀t)}. A wrong sign only moves fringes, and nothing else would catch that, so a test pins it.
* **Audit needed in stage 2.** The ray bundle and `DispersiveSurface` drop their piston. Spatial propagation uses k_z directly, and I have not confirmed how it treats the carrier term or the tracked beam's Gouy phase. Each must end up counted exactly once.

### Meeting at a port

With arm 2 arriving Δτ later, with phase difference Δφ, on a common grid in the frame of arm 1:

```text
A₂(ω) -> A₂(ω) exp(i Δφ) exp(i (ω − ω₀) Δτ)         (sign fixed by the convention test)
```

* **Grids.** Same grid: direct. Related: `GridResampler.Resample`. Different carriers must share a grid wide enough for both.
* **Polarisation.** x and y add separately, with x = p and y = s at a reflection. Signs of reflection belong to each element's definition.
* **Coherence.** Same `SourceId`, or `coherentWith`: fields add. Otherwise intensities add.

### Packets

Loops make this necessary at once: a cavity's output is a pulse train, and 10 ns of round trip cannot fit in a 10 ps window. So a beam on an edge is a **list of packets**, each a `PulseField` with its own ledger. A chain always has one.

* Packets closer than the window merge coherently, by the sum above. Others stay separate. A single-frequency (`cw`) source always merges, so a cw loop gives the Airy result directly.
* Linear elements act on each packet, which is exact by superposition. Nonlinear elements do too, which is exact only if packets do not overlap in that element, and the compiler knows from the ledgers whether they do.
* Cost scales with packets, so `maxPackets` limits it; a packet below `tolerance` is dropped and booked as "truncated".
* Instruments: `energyMeter` and budgets sum all packets. Others default to the strongest, with a note, or take `"packet": n`. A `scope`-like view of the train comes later.

Packets also solve pulse stacking, which the first draft had put off.

### Beams meeting in space

| model | what happens |
|---|---|
| 1-D with tracked Gaussians | fields add, scaled by the amplitude overlap of the two beams (q, position, angle), from their `BeamState`s. The rest is booked as "mode mismatch". The output follows the first arm |
| carried profiles | resampled to a common grid, added per frequency: tilt fringes appear by themselves |
| one profile, one not | error |

Check: for two equal unit beams with visibility V, the two outputs of a lossless 50:50 splitter hold `1 + V cos Δφ` and `1 − V cos Δφ`. Beams crossing at an angle in a crystal do not meet at a splitter: they enter separate ports of the crystal.

## 7. Loops

A cycle is found as a strongly connected component of the graph. The compiler cuts its back edges, and the scheduler sweeps the rest repeatedly. One sweep is one pass:

```text
pass 0: the external inputs run through the loop's acyclic part
pass n: the packets on the cut edges of pass n−1 run through it
output: packets leaving the loop on all passes, with their ledgers
```

| `kind` | stops when | output | use |
|---|---|---|---|
| `ringDown` | energy in the loop < `tolerance` × energy in, or `maxRoundTrips` | every packet leaving | etalons, Gires-Tournois, cavity ring-down, ring-down of a pulse |
| `steadyState` | the loop's field repeats within `tolerance`, or `maxRoundTrips` | the last pass | Fox-Li, a laser built from elements |

* The first sweep is the sum of a geometric series for a linear loop, so it can be tested exactly.
* Gain with `stateId` keeps its state across passes, in arrival order. That allows a regenerative amplifier written out as elements.
* Light still in the loop at the end is booked as "left in loop". The budget still closes.
* The tracked beam is iterated too; a stable loop's q converges to the eigenmode, which the existing `Resonator` already computes.
* The `loop` key goes on the element where light enters the loop. Several external inputs share the settings of the first in file order.
* Several back edges in one component are allowed, and packets multiply. `maxPackets` stops the run with a message rather than silently truncating.
* `resonator` and `oscillator` stay. They are simpler for what they do, and they check the graph loop: the same cavity must give the same eigenmode, Fox-Li mode and loss. Their keys do not change.
* A linear cavity is a ring unfolded: forward and return paths are separate entries, joined with `sameAs`. A loop's elements have one direction each.

## 8. New elements

| element | ports | model | check |
|---|---|---|---|
| `beamSplitter` | `a`, `b` → `c`, `d` | 2×2 unitary, `reflectivity` or `coating`, `substrate` | Stokes relations; energy at any phase |
| `leakMirror` | `in` → `out`, `leak` | a splitter with small T(λ) from a mirror coating, each side's GDD | T integrated over the spectrum |
| `polarizingSplitter` | `a`, `b` → `c`, `d` | the `thinFilmPolarizer` model with both ports kept | Malus; extinction |
| `dichroic` | `in` → `c`, `d` | spectral R(λ) | sum of outputs = input at each λ |
| `delay` | `in` → `out` | a path or `delayS` on a stage | Michelson fringes of period λ/2 |

These are variants of one definition, so reflection signs live in one place: side 2 has r' = −r\* t / t\* and t' = t. All work in sequential systems through `output` and virtual inputs.

**Michelson and Sagnac interferometers** pass one splitter twice, but the light does not circulate, so this is not a loop. It is unfolded as an optician draws it: `{ "id": "bsBack", "type": "beamSplitter", "sameAs": "bs", "side": 2 }`. The graph stays acyclic and the signs are right by construction.

## 9. Stored outputs and references

`build/example_check.py --against <ref>` runs every example with an older engine and compares every number in the summaries, so refactors are checked against the `d6fb4e9` engine with no stored files. That guards against change, not against an error that was always there. pyNLO cannot run in the owner's Python 3 environment, so the propagation cross-check in `CLAUDE.md` and the figures in `PROJECT_STATUS.md` are not currently reproducible here. That needs resolving separately, by a Python 3 route or a reworded claim.

## 10. Modelled instruments

An instrument is an ideal measurement of a field; a modelled one is the apparatus. The difference is what a user wants to know: *how far can I trust my autocorrelator?*

### First result, already possible

A real crystal in today's engine (`sfg`, a virtual copy of the pulse through `aux`, `delayScan`) against the `autocorrelator` instrument with default settings. Gaussian input, so ideal = √2 × FWHM. Run on 2026-10-30; a sketch, not yet a committed example.

| pulse | BBO | crystal (fs) | instrument (fs) | ideal (fs) | crystal vs ideal |
|---|---|---:|---:|---:|---:|
| 100 fs | 20 µm | 141.48 | 141.52 | 141.42 | +0.04 % |
| 30 fs | 20 µm | 42.44 | 42.45 | 42.43 | +0.03 % |
| 30 fs | 1 mm | 42.66 | 42.45 | 42.43 | +0.5 % |
| 15 fs | 1 mm | 22.45 | 21.23 | 21.21 | +5.8 % |

The two agree for thin crystals, and a thick crystal measures a 15 fs pulse 6 % long. The expected cause is phase-matching bandwidth and group-velocity mismatch, which the example should confirm. The instrument's `shgAcceptanceFwhmM` was off here; comparing it with the crystal deserves a test.

### Plan

Each is a `.ufs` in an example folder, with `report.py` comparing it with the instrument on the same pulse.

| instrument | modelled as | what the model shows |
|---|---|---|
| `autocorrelator` | splitter, delay, noncollinear thin crystal, energy detector | crystal bandwidth, splitter GDD, crossing angle |
| `iac`, `frac` | Michelson, SHG, detector | the 8:1 ratio; dispersion imbalance; compensator plate |
| `crossCorrelator` | gate arm with its own line, SHG or THG | gate quality, dynamic range |
| Fourier-transform spectrometer | Michelson on a detector | Wiener-Khinchin: an analytic check against `spectrometer` |
| `frog` | the autocorrelator with a spectrometer | phase-matching filter on the trace; retrieval on a modelled trace |
| `dscan` | wedges, SHG, spectrometer (no branch needed) | SHG bandwidth |
| `contrast` | third-order cross-correlator | post-pulses making artefact pre-pulses |
| f-2f | SHG, dichroic, delay, spectrometer | CEP fringes: the end-to-end test of the ledger |

Scanning is a study on `element:stage/lengthM`. One new feature is needed: a study that records a detector's *array* per point, for 2-D traces.

## 11. Stages

Each stage can merge alone and leaves the engine working.

| stage | work | done when |
|---|---|---|
| **0. Prerequisites** | Commit this plan; `mkdocs --strict`; physics review of the ray bundle and T2; a baseline for `build/example_check.py --against` (the `d6fb4e9` engine); architecture docs and file headers ([section 14](#14-architecture-docs-and-file-headers)); ship the crystal autocorrelator example; first CI run when the owner pushes | review findings handled; docs build |
| **1. Ports and segments** | Port declarations for all elements; multi-port interface; `inputs` with `aux` as its spelling; segment runner; `PathLedger`-shaped state. `beamSplitter` works sequentially with `output` | reference outputs **bit-identical**; timing on three examples within 1 %; a splitter's ports sum to the input |
| **2. Ledger** | φ₀ through `DispersionPoint`; phase audit; report phase in `paths` | fields unchanged; phase of glass = n ω₀ L / c; fringe-direction test pins the sign |
| **3. Graph compiler** | Version 3, `topology`, `next` in both forms, port numbers, virtual ports with alerts, `SystemGraph`, all checks of [section 3](#3-compile-and-verify) including loops; `validate --graph`; Python `Sequential`, `Branched` | a test per check; a chain compiles to one segment; canonical form stable |
| **4. Acyclic execution** | Scheduler, packets, meeting at a port, window pre-pass, global budget, `delay`, `source` | Mach-Zehnder cos² fringes; energy at any phase; budget closes on every graph |
| **5. Loops** | Cut edges, sweeps, `ringDown`, `steadyState`, booking | ring and Fabry-Perot **Airy function**; Gires-Tournois group delay; graph Fox-Li = `resonator` |
| **6. Crystal ports, instruments** | Branch-fed crystal ports, by-product ports, `dichroic`, array studies, modelled AC, IAC, FTS, FROG, f-2f | branch-fed = `aux.pulse` to numerical precision; IAC 8:1; FTS = `spectrometer` |
| **7. Space** | Per-edge tracker, Gaussian overlap, profile meeting, `sameAs`, `polarizingSplitter`, `leakMirror`, tail ledgers | analytic Gaussian overlap; tilt fringe spacing λ/θ; visibility against overlap |
| **8. Node cache** | Upstream-hash cache for studies | same results as without; speed-up measured |

Stages 1 and 2 change the most existing code and show the least, and they need the most care. Stages 3 to 5 are the core of the feature. Each stage updates the roadmap, history, `PROJECT_STATUS.md`, `docs/testing.md` and the reference pages. New pages: `docs/reference/branches.md`, and a tutorial on the Mach-Zehnder.

## 12. Risks

| risk | answer |
|---|---|
| Fringes that look right but are shifted | stage 2 tests; f-2f; `PhaseKnown` |
| A refactor changes sequential numbers | bit-identical outputs in stage 1, before anything is added |
| Packets multiply in nonlinear loops | `maxPackets`; merge in window; validation warns before running |
| Windows explode with long arms | packets; `validate` shows each arm's delay |
| Mis-wired systems | compile errors naming ports; `validate --graph`; a port takes one source |
| A model has perfect stability | say so in the docs: a modelled phase has no vibration or drift |
| No independent propagation check | [section 9](#9-stored-outputs-and-references) |

## 13. Decisions

Taken from the owner's review: `Sequential` and `Branched` forms, explicit `next` naming an input port, an internal graph, a compile and verify step, ports on all elements, alerts and virtual ports, loops in the first design.

Recommended, and assumed unless the owner objects:

| question | choice |
|---|---|
| Key names | `topology`, `next`, `inputs`, `output`, `loop` |
| Name | "branched system": in OpticStudio "non-sequential" means rays find their own order, which this does not do |
| Port numbers | start at 1, in declared order; names also accepted |
| Splitter ports | `a`, `b`, `c`, `d`, with `transmitted`, `reflected` as aliases |
| Michelson | unfolded with `sameAs`, not a loop |
| `resonator`, `oscillator` | stay, and check graph loops |
| First demonstrations | Mach-Zehnder, then Airy function, then modelled autocorrelator |

## 14. Architecture docs and file headers

The engine is about to gain its largest structural change, so its structure gets written down.

* **`docs/architecture/`**: what the main folders, files and classes do, and the key processes (compile, execute, grids, studies, the graph).
* **File headers.** Every new or substantially changed source file starts with a short summary: what it does, its main classes, and their key signatures. This is a rule in `AGENTS.md`.
* Both are kept current with each stage.

## 15. What changed in building it

* **No `source` element.** A virtual beam on any input port is a source, so a separate element was not needed.
* **No `dump` element.** Unused outputs are virtual outputs.
* **No segments as objects.** The scheduler works element by element through `RunElement`, with the sequential loop kept as it was.
* **No cut edges.** Packets sent back wait in their target's queue for the next pass.
* **Pairing at a splitter was not needed.** It runs once per packet, which is exact for a linear element; interference happens when packets merge.
* **No window pre-pass.** Packets that do not fit a window stay separate.
* A virtual beam at a Sequential splitter must be coherent, since only one pulse continues.
* `leakMirror` has ports `in`, `back`, `out`, `leak`, and the splitter family is one class with coefficients and port names per type.
* The cache's nominal run stores and the others only read, so nothing depending on a scanned parameter accumulates.
* Tilts and offsets live on `delay`: arms need a way to be misaligned, and no other element owned that.
* Tails travel in the path ledger, so a branch's copy is automatic.
* A `steadyState` loop settles when both the field's shape and its energy repeat. Shape alone is not enough: a passive loop keeps its shape from the first pass while it decays. Unitarity means a lossless linear network cannot hold light, so a steady state needs gain.
* `graph.energyOutJ` counts every packet at `end`; the field in `RunResult.Output` is the strongest. A crystal's fed ports interact with one main packet, and other packets are booked, so fed energy is not used twice.
* Node cache speed-up, 81-point delay scan: 58.2 s to 9.6 s with 0.3 m of nonlinear glass upstream; 0.65 s to 0.60 s with 5 mm, where the crystal dominates. Rows are identical. The scanned parameter must be an `element:<id>` override, which only that element's key contains.
* Not built: beams crossing at an angle in a crystal; a driven loop with the source fed every round trip.
