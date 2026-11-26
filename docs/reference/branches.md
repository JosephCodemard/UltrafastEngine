# Branched systems

A system is `Sequential` (the default: a list, each element continuing into the next) or `Branched` (each element states
where its light goes). Both compile to the same graph (`Systems/SystemGraph.cs`) and run through the same element code.
Use a branched system for interferometers, splitters whose two outputs both matter, beams that meet in a crystal, and
loops (cavities, rings).

## Keys

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

`"topology": "branched"` makes the file branched; the current system-file version is 3, and version 2 files load
unchanged. The first entry takes the run's input; otherwise the order of the list has no optical meaning. A Sequential
file that uses `next` or `loop` is an error that says it needs `"topology": "branched"`. The keys `next`, `inputs` and
`loop` are declared with every element ([options](options.md#elementcommon)); the splitters' keys and defaults are in
[`beamSplitter`](options.md#beamsplitter), [`polarizingSplitter`](options.md#polarizingsplitter),
[`leakMirror`](options.md#leakmirror), [`dichroic`](options.md#dichroic) and [`delay`](options.md#delay).

### `next`

| form | meaning |
|---|---|
| `"bs1"` | the element's default input port |
| `"bs1:2"`, `"bs1:b"` | an input port by number (from 1, in declared order) or by name |
| `{"id": "bs1", "port": 2}` | the same as an object |
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

In a Branched system every port is accounted for. A port with nothing connected raises a warning and gets a virtual port,
so the run continues:

* a virtual input is empty: no light enters;
* a virtual output keeps the light: it is recorded, and booked in the budget as `virtual output d`.

Declaring it silences the warning: `"next": {"d": "virtual"}`, `"inputs": {"b": {"virtual": true}}`. In a Sequential
system unused outputs are normal, are recorded, and raise no warning.

### Virtual inputs

An input port with no beamline can hold a beam, in Sequential and Branched systems alike. The keys are those of an
[auxiliary beam](crystals.md), with two more:

```jsonc
"inputs": { "b": { "pulse": { "shape": "gaussian", "centerWavelengthM": 1030e-9, "durationFwhmS": 100e-15, "energyJ": 1e-9 },
                   "delayS": 100e-15, "phaseRad": 0, "coherentWith": "input" } }
```

* `coherentWith`: `"input"` makes it interfere with the run's pulse. Without it the beam is an independent source and its
  energy adds.
* A Sequential system keeps one pulse, so there a virtual beam at a splitter must be coherent.
* A virtual beam of another colour is built on a grid around its own carrier, passes the splitter (and a `leakMirror`'s
  `gddFs2` / `leakGddFs2`, a quadratic phase about the beam's own carrier) at its own frequencies, and is then brought onto
  the run's grid in a Sequential system. Whatever lies outside the run's frequency band is booked as `fed beam outside the
  run's grid` and reported as a warning: use a Branched system or widen the grid.
* In a Branched system `delayS` and `phaseRad` are measured from the source of the run's pulse. In a Sequential system
  they are measured from the main beam at the element.
* Only the splitters (`beamSplitter`, `polarizingSplitter`, `leakMirror`, `dichroic`) and crystals read `inputs`.

## Model

### Beam splitters

`beamSplitter` is a lossless 2×2 element with real amplitudes, obeying the Stokes relations: c = t a − r b,
d = r a + t b, with r² = `reflectivity` (default 0.5) and t² = 1 − r². `transmitted` and `reflected` are other names for
`c` and `d`, correct for light entering at `a`. In a Sequential system, `output` picks the port that continues (default
`c`) and the other is recorded (`at: "bs:d"`) and booked.

| element | what it does (keys) |
|---|---|
| `polarizingSplitter` | x to `c` with `transmissionP`, y to `d` with `reflectivityS` (both default 1) |
| `leakMirror` | the reflection continues at `out`, the leak leaves at `leak`: `transmission` (default 0.01), or `wavelengthsM` with `transmissionSpectrum`; `gddFs2` of the reflection, `leakGddFs2` of the leak |
| `dichroic` | R = 1/2 at `edgeWavelengthM`; `edgeWidthM` (10-90 %, default 10 nm); `reflectLong` (default true: reflects the long wavelengths) |

**Glass in a splitter.** A plate splitter's substrate and a cube's prisms add dispersion. `beamSplitter` and
`polarizingSplitter` take it per path: `material` (any catalogue material or one from `materials`),
`transmittedThicknessM` (glass path of the transmitted beam, a → c and b → d) and `reflectedThicknessM` (a → d and b → c),
both in m, default 0. Each path gets the glass after the split, as a `plate` with ideal anti-reflection faces at normal
incidence would add it in that arm: n(ω) ω L / c, and the material's absorption if it has one. The phase and delay go into
each output's ledger, so arms that meet again interfere as with plates in them. Give the path length, not the plate
thickness: a plate of thickness d at 45° has a transmitted path d / cos θ_t inside, and the reflected beam crosses no
glass if the coating faces the source. A `sameAs` copy has the same glass, so a Michelson return passes it again. Delayed
light (`grid.tail`) crosses the same glass. A `material` with no path length adds nothing and says so in the notes.

**Michelson and Sagnac interferometers** pass one splitter twice. Write the return as a second element,
`{"type": "beamSplitter", "sameAs": "bs", "side": 2}`: `sameAs` takes the first's settings (it works for every element:
[identical elements](system-file.md#identical-elements)), and `side: 2` reverses the sign of r, because the light meets the
other face. With equal arms all the light then goes back towards the source.

### Delays

`delay` is a pure delay, with `delayS` or a vacuum `lengthM`: phase ω τ, no dispersion, no beam propagation. `tiltXRad`,
`tiltYRad`, `offsetXM`, `offsetYM` misalign the arm.

### Interference

The field is stored in a co-moving frame: each element removes its carrier phase and group delay. A ledger keeps them for
each path. An element runs once per packet per input port, and packets arriving at one port merge when they share a
source and fit one time window:

A₂(ω) → A₂(ω) exp(i Δφ + i (ω − ω₀) Δτ)

Packets too far apart for the window stay separate, which is right: they do not overlap. A nonlinear element then treats
each alone. At `end`, `result` holds the strongest, and `graph.energyOutJ` counts them all (`packetsAtEnd`). Beams with
different carriers merge too, on the grid that spans more: that is what an f-2f interferometer needs.

**Carrier phase.** Every element reports the carrier phase it adds, except these, which make the phase downstream unknown
(`phaseKnown: false`): `oscillator`, `resonator`, `user` elements, a regenerative `amplifier` (`geometry: "regenerative"`),
an idler continuing from a crystal (its phase depends on the pump's), any other active element that does not report its
phase (`IReportsCarrierPhase`; for example a plugin's element), and traced group-delay data, which do not contain it. If such a path meets another, a warning says the fringe position is
arbitrary; the visibility is right. A crystal adds k L of the output wave; a second harmonic carries twice the path's
phase, and a sum beam fed from two ports carries both. A tracked beam adds its Gouy phase, −arctan(z / z_R) from a waist. A
carried profile holds that in its diffraction.

**Meeting in space.**

| beams | what happens |
|---|---|
| tracked Gaussians | the second is scaled by the amplitude overlap of the modes (q, tilt, offset), which holds their relative phase. The rest is booked as `mode mismatch` |
| carried profiles | the fields add point by point, so tilt fringes appear. Both need the same transverse grid |
| one profile, one not | an error |
| a tilt or offset with no tracked beam | an error: there is nothing to work out an overlap from |

The output keeps the first beam's tracked mode. An offset is not applied to a carried profile (a warning says so).

### Loops

A link back to an earlier element makes a loop. The element where light enters needs a `loop` object:

```jsonc
{ "id": "m1", "type": "beamSplitter", "reflectivity": 0.8, "next": { "c": "forth", "d": "virtual" },
  "loop": { "kind": "ringDown", "maxRoundTrips": 1000, "tolerance": 1e-9 } }
```

| `kind` | stops when | output |
|---|---|---|
| `ringDown` (default) | energy in the loop < `tolerance` × energy entering, or `maxRoundTrips` | every pass that leaves |
| `steadyState` | the field's shape and its energy both repeat within `tolerance`, or `maxRoundTrips` | the last pass |

Defaults: `maxRoundTrips` 1000, `tolerance` 1e-6. `tolerance` is on *energy*, and fields add in amplitude: for a resonant
sum use a tolerance near the square of the accuracy you want. Light left at the end is booked as `left in loop`; a
steady-state loop books earlier output as `earlier passes`. `roundTrips` and `loopConverged` appear in the entry
element's values. A single-frequency (`cw`) source in a `ringDown` loop gives the steady-state interference directly: a
Fabry-Perot gives the Airy transmission (see Validation).

A `steadyState` loop needs gain. The input enters once, so a passive loop only decays: its shape repeats but it has no
steady state, and it reports `loopConverged: 0` with a warning. Use `ringDown` for passive cavities. `resonator` and
`oscillator` remain the simpler tools for a cavity's mode and for pulses building up. A graph loop has one direction, so a
linear cavity is written with separate forward and return entries.

### Crystals

An `aux` beam with `"fromPort": true` comes from a link to the port named by its `role`. Its timing, phase and carrier
come from the paths, so `delayS` is not used, and `cut: "auto"` solves for the colours that arrive: a fundamental and a
second harmonic give the third-harmonic cut. Example [39](../examples/Diagnostics/third-order-cross-correlator/README.md)
is such a chain.

A fed port with no link can hold a virtual beam, `"inputs": {"signal": {"pulse": ..., "delayS": "auto"}}`. It keeps its
own carrier and enters along the lab axis, so the tilt of the other arm sets the crossing angle. Example
[42](../examples/LaserSystems/noncollinear-opa/README.md) seeds a NOPA this way.

```jsonc
{ "id": "x", "type": "crystal", "crystal": "BBO", "lengthM": 20e-6, "interaction": { "process": "sfg", "type": "I" }, "cut": "auto",
  "beam": { "kind": "gaussian", "w0M": 100e-6 }, "output": "sum", "next": "end",
  "aux": [ { "role": "b", "fromPort": true, "beam": { "kind": "gaussian", "radiusM": 100e-6 } } ] }
```

### Delayed light

With `grid.tail`, each path carries its own tail: splitters divide it like the main field, and tails add when packets
merge.

### Studies

A study of a Branched system reuses elements upstream of what it changes: outputs of the nominal run are kept by a hash
of everything that feeds them, so a delay scan runs an expensive amplifier once. Results are the same (`prefixCache:
false` turns it off), and the note `node cache: N element runs reused from the nominal run` gives the count. Loops always
rerun. `"arrayOutputs": ["spec/spectrum"]` keeps a whole instrument array per point, in `study.npz`, with one row per
point. A FROG trace is a delay scan of a spectrometer.

### Checking a system

```bash
ultrafast validate system.ufs --graph              # links, loops, virtual ports, alerts
ultrafast validate system.ufs --graph --mermaid    # a Mermaid diagram: element types, virtual ports, loops as subgraphs (drawn: reference/plotting.md)
```

| problem | result |
|---|---|
| an element id used twice; an unknown target id or port in `next`; an unknown output or input port name; two sources on one input port; two links to `end`; an unreachable element; a loop with no `loop` object, or with more than one; `loop` on an element that is on no loop; a loop with no way out; a `loop.kind` other than `ringDown` / `steadyState` | error, all reported at once |
| an unused port; no element leads to `end` | warning |

`summary.json` gains `graph`: links, loops, virtual ports and their energies, and the budget `energyInJ`, `energyOutJ`,
`energyBookedJ`, `energyClosureJ`, with `packetsAtEnd`.

## Validation

* Path ledger: the carrier phase of glass, n ω L / c, to 1e-9 (`BranchingTests.PathLedger_KeepsTheCarrierPhaseTheFieldDoesNotCarry`).
* Mach-Zehnder: output (1 − cos φ) / 2 of the input, 1e-9 (`BranchingTests.MachZehnder_FringesFollowThePathDifference`);
  spectral fringes |1 − exp(iωτ)|² / 4, 1e-6, which pins the sign of phase against delay
  (`BranchingTests.MachZehnder_SpectralFringes_PinTheSignOfPhaseAgainstDelay`); arms further apart than the window stay
  separate packets (`BranchingTests.ArmsFurtherApartThanTheWindow_StaySeparatePackets_AndDoNotInterfere`).
* Loops: a cw Fabry-Perot in a `ringDown` loop gives the Airy transmission (1 − R)² / (1 + R² − 2R cos δ) to 1e-5, with the
  budget closed to 1e-7 (`BranchingTests.FabryPerot_LoopGivesTheAiryFunction`); a ring longer than the window gives a pulse
  train with pass n carrying R^(n−1) T², 1e-9 (`BranchingTests.Ring_RoundTripLongerThanTheWindow_GivesAPulseTrain`); an
  all-pass ring keeps |r + t² q / (1 + r q)|² = 1 to 1e-6 (`ReviewRegressionTests.RingDownRing_IsAllPassAtEveryLength`); a
  steady-state loop settles only when shape and energy repeat, and a passive one does not claim convergence
  (`BranchingTests.SteadyStateLoop_SettlesOnlyWhenShapeAndEnergyRepeat`,
  `ReviewRegressionTests.SteadyStateLoop_DoesNotClaimConvergenceWhileTheLightDecays`).
* Splitters: the 2×2 relation c = t a − r b in a Sequential system with a virtual beam, 1e-9
  (`BranchingTests.Sequential_VirtualBeam_InterferesAtTheSplitter`); glass in a splitter equals the splitter followed by a
  plate in each arm to 1e-12, and delays the pulse by the group index of fused silica (Malitson, J. Opt. Soc. Am. 55, 1205
  (1965)) to 2e-5 (`SplitterGlassTests`).
* Crystals: a branch-fed port equals the same beam given as `aux` to 1e-6
  (`BranchingTests.CrystalPort_FedFromABranch_MatchesTheSameBeamGivenAsAux`); a modelled autocorrelator with a thin crystal
  gives the Gaussian intensity autocorrelation width √2 τ to 1 % (`BranchingTests.ModelledAutocorrelator_ThinCrystal_GivesTheIdealWidth`).
* Graph checks: errors reported all at once, two sources on one port, targets by number, name and object, unused ports and
  a missing `next` (`BranchingTests.GraphErrors_AreReported_AllAtOnce`, `..._TwoSourcesOnOnePort_AreAnError_NamingThePort`,
  `..._Targets_ByNumberNameAndDict_AreTheSame`, `..._UnusedPorts_RaiseAlerts_AndBecomeVirtual_UnlessDeclared`,
  `..._MissingNext_IsAnAlert_NotASilentFallThrough`).

## Limits

* Mode overlap assumes simple Gaussian beams at one plane and ignores M².
* A linked crystal port takes the strongest packet only, and only the strongest main packet meets it. Other packets are
  booked as `packets not overlapping in crystal`, with a warning.
* `gddFs2` of a `leakMirror` acts on the field, but is not in the `system` dispersion the element reports.
* The x tilts of the arms into a crystal set the crossing angle: `sfg` ([Crystals](crystals.md#non-collinear-sum-frequency)),
  and `opa` and `dfg` when the signal and the pump are the two inputs ([Crystals](crystals.md#non-collinear-opa-and-dfg)).
  A carried profile entering with its own tilt phase would count the tilt twice, so use tracked beams for crossing arms.
* Merged carried profiles share one transverse shape per stored frequency, so spectral fringes finer than
  `spectralSamples` are averaged.
* Glass in a splitter has no surfaces (Fresnel losses, coatings), tilt or Kerr effect; use a `plate` in the arm for those.
