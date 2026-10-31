# Branched systems

A system is `Sequential` (the default: a list, each element continuing into the next) or `Branched` (each element states where its light goes). Both compile to the same graph and run through the same element code. Design: [Branched systems](../design/branching.md).

## Writing one

```jsonc
{ "format": "ultrafast-system", "version": 3, "name": "mach-zehnder", "topology": "branched",
  "elements": [
    { "id": "bs1",  "type": "beamSplitter", "reflectivity": 0.5, "next": { "c": "armA", "d": "armB" } },
    { "id": "armA", "type": "gap", "lengthM": 0.2, "next": "bs2:1" },
    { "id": "armB", "type": "gap", "lengthM": 0.2, "next": "bs2:2" },
    { "id": "bs2",  "type": "beamSplitter", "reflectivity": 0.5, "next": { "c": "end", "d": "virtual" } }
  ] }
```

In Python:

```python
s = b.system("mz", b.Branched([
    b.link(b.beam_splitter("bs1"), {"c": "armA", "d": "armB"}),
    b.link(b.gap("armA", 0.2), "bs2:1"),
    b.link(b.gap("armB", 0.2), "bs2:2"),
    b.link(b.beam_splitter("bs2"), {"c": "end", "d": "virtual"}),
]))
```

The first entry takes the run's input. Otherwise the order of the list has no optical meaning.

### `next`

| form | meaning |
|---|---|
| `"bs1"` | the element's default input port |
| `"bs1:2"`, `"bs1:b"` | an input port by number (from 1, in declared order) or by name |
| `{"id": "bs1", "port": 2}` | the same as a dict |
| `"end"` | the system's output; one element may lead there |
| `"virtual"` | a virtual output, declared |
| `{"c": "armA", "d": "bs2:2"}` | one target per output port (names or numbers) |

### Ports

Every element type declares its ports; most have `in` and `out`.

| element | inputs | outputs |
|---|---|---|
| most elements | `in` | `out` |
| `beamSplitter`, `polarizingSplitter`, `dichroic` | `a`, `b` | `c`, `d` |
| `leakMirror` | `in`, `back` | `out`, `leak` |
| `crystal` | `in`, and each `aux` beam with `fromPort: true` by its role | `out` (the wave named by `output`), and each other wave by name: `fundamental`, `sh`; `a`, `b`, `sum`; `signal`, `idler`, `pump` |

`at` in an instrument takes `"id:port"` or `"id#port"`.

### Unused ports

In a Branched system every port is accounted for. A port with nothing connected raises a warning and gets a virtual port, so the run continues:

* a virtual input is empty: no light enters;
* a virtual output keeps the light: it is recorded, and booked in the budget as `virtual output d`.

Declaring it silences the warning: `"next": {"d": "virtual"}`, `"inputs": {"b": {"virtual": true}}`. In a Sequential system unused outputs are normal, are recorded, and raise no warning.

### Virtual inputs

An input port with no beamline can hold a beam, in Sequential and Branched systems alike. The keys are those of an [auxiliary beam](crystals.md), with two more:

```jsonc
"inputs": { "b": { "pulse": { "shape": "gaussian", "centerWavelengthM": 1030e-9, "durationFwhmS": 100e-15, "energyJ": 1e-9 },
                   "delayS": 100e-15, "phaseRad": 0, "coherentWith": "input" } }
```

* `coherentWith`: `"input"` makes it interfere with the run's pulse. Without it the beam is an independent source and its energy simply adds.
* A Sequential system keeps one pulse, so there a virtual beam at a splitter must be coherent.
* In a Branched system `delayS` and `phaseRad` are measured from the source of the run's pulse. In a Sequential system they are measured from the main beam at the element.

## Beam splitters

`beamSplitter` is a lossless 2×2 element with real amplitudes, obeying the Stokes relations: c = t a − r b, d = r a + t b, with r² = `reflectivity`, t² = 1 − r². `transmitted` and `reflected` are other names for `c` and `d`, correct for light entering at `a`. In a Sequential system, `output` picks the port that continues and the other is recorded (`at: "bs:d"`) and booked.

| element | keys |
|---|---|
| `polarizingSplitter` | `transmissionP` (x to `c`), `reflectivityS` (y to `d`) |
| `leakMirror` | `transmission`, or `wavelengthsM` with `transmissionSpectrum`; `gddFs2` of the reflection, `leakGddFs2` of the leak |
| `dichroic` | `edgeWavelengthM`, `edgeWidthM` (10-90 %), `reflectLong` |

**Michelson and Sagnac interferometers** pass one plate twice. Write the return as a second element, `{"type": "beamSplitter", "sameAs": "bs", "side": 2}`: `sameAs` takes the first's settings, and `side: 2` reverses the sign of r, because the light meets the other face. With equal arms all the light then goes back towards the source.

## Delays

`delay` is a pure delay, with `delayS` or a vacuum `lengthM`: phase ω τ, no dispersion, no beam propagation. `tiltXRad`, `tiltYRad`, `offsetXM`, `offsetYM` misalign the arm.

## Interference

The field is stored in a co-moving frame: each element removes its carrier phase and group delay. A ledger keeps them for each path. An element runs once per packet per input port, and packets arriving at one port merge when they share a source and fit one time window:

A₂(ω) → A₂(ω) exp(i Δφ + i (ω − ω₀) Δτ)

Packets too far apart for the window stay separate, which is right: they do not overlap. A nonlinear element then treats each alone. At `end`, `result` holds the strongest, and `graph.energyOutJ` counts them all (`packetsAtEnd`).

Beams with different carriers merge too, on the grid that spans more: that is what an f-2f interferometer needs.

### Carrier phase

Every element reports the carrier phase it adds, with these exceptions, which make the phase downstream unknown (`phaseKnown: false`): `oscillator`, `resonator`, `user` elements, regenerative amplifiers, an idler continuing from a crystal (its phase depends on the pump's), and traced group-delay data, which do not contain it. If such a path meets another, a warning says the fringe position is arbitrary; the visibility is right.

A crystal adds k L of the output wave; a second harmonic carries twice the path's phase, and a sum beam fed from two ports carries both. A tracked beam adds its Gouy phase, −arctan(z / z_R) from a waist. A carried profile holds that in its diffraction.

### Meeting in space

| beams | what happens |
|---|---|
| tracked Gaussians | the second is scaled by the amplitude overlap of the modes (q, tilt, offset), which holds their relative phase. The rest is booked as `mode mismatch` |
| carried profiles | the fields add point by point, so tilt fringes appear. Both need the same transverse grid |
| one profile, one not | an error |
| a tilt or offset with no tracked beam | an error: there is nothing to work out an overlap from |

The output keeps the first beam's tracked mode. An offset is not applied to a carried profile (a warning says so).

## Loops

A link back to an earlier element makes a loop. The element where light enters needs a `loop`:

```jsonc
{ "id": "m1", "type": "beamSplitter", "reflectivity": 0.8, "next": { "c": "forth", "d": "virtual" },
  "loop": { "kind": "ringDown", "maxRoundTrips": 1000, "tolerance": 1e-9 } }
```

| `kind` | stops when | output |
|---|---|---|
| `ringDown` | energy in the loop < `tolerance` × energy entering, or `maxRoundTrips` | every pass that leaves |
| `steadyState` | the field's shape and its energy both repeat within `tolerance`, or `maxRoundTrips` | the last pass |

`tolerance` is on *energy*, and fields add in amplitude: for a resonant sum use a tolerance near the square of the accuracy you want. Light left at the end is booked as `left in loop`; a steady-state loop books earlier output as `earlier passes`. `roundTrips` and `loopConverged` appear in the entry element's values. A single-frequency (`cw`) source gives steady-state interference directly in a `ringDown` loop.

A `steadyState` loop needs gain. The input enters once, so a passive loop only decays: its shape repeats but it has no steady state, and it reports `loopConverged: 0` with a warning. Use `ringDown` for passive cavities.

`resonator` and `oscillator` remain the simpler tools for a cavity's mode and for pulses building up. A graph loop has one direction, so a linear cavity is written with separate forward and return entries.

## Crystals

An `aux` beam with `"fromPort": true` comes from a link to the port named by its `role`. Its timing and phase come from the paths, so `delayS` is not used.

```jsonc
{ "id": "x", "type": "crystal", "crystal": "BBO", "lengthM": 20e-6, "interaction": { "process": "sfg", "type": "I" }, "cut": "auto",
  "beam": { "kind": "gaussian", "w0M": 100e-6 }, "output": "sum", "next": "end",
  "aux": [ { "role": "b", "fromPort": true, "beam": { "kind": "gaussian", "radiusM": 100e-6 } } ] }
```

## Delayed light

With `grid.tail`, each path carries its own tail: splitters divide it like the main field, and tails add when packets merge.

## Studies

A study of a Branched system reuses elements upstream of what it changes: outputs of the nominal run are kept by a hash of everything that feeds them, so a delay scan runs an expensive amplifier once. Results are the same (`prefixCache: false` turns it off), and a note gives the count. Loops always rerun.

`"arrayOutputs": ["spec/spectrum"]` keeps a whole instrument array per point, in `study.npz`, with one row per point. A FROG trace is a delay scan of a spectrometer.

## Checking a system

```bash
ultrafast validate system.ufs --graph              # links, loops, virtual ports, alerts
ultrafast validate system.ufs --graph --mermaid    # a Mermaid diagram
```

| problem | result |
|---|---|
| unknown id or port; two sources on one input; two links to `end`; unreachable element; loop without settings or delay | error, all reported at once |
| unused port; nothing leads to `end` | warning |

`summary.json` gains `graph`: links, loops, virtual ports and their energies, and the budget `energyInJ`, `energyOutJ`, `energyBookedJ`, `energyClosureJ`, with `packetsAtEnd`.

## Present limits

* Mode overlap assumes simple Gaussian beams at one plane and ignores M².
* A linked crystal port takes the strongest packet only, and only the strongest main packet meets it. Other packets are booked as `packets not overlapping in crystal`, with a warning.
* `gddFs2` of a `leakMirror` acts on the field, but is not in the `system` dispersion the element reports.
* Beams crossing at an angle in a crystal are collinear in the model; `noncollinearDeg` still comes from the element, not the arms.
* Merged carried profiles share one transverse shape per stored frequency, so spectral fringes finer than `spectralSamples` are averaged.
