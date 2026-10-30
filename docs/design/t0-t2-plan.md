# Plan: finishing T0, T1 and T2 before branches

Status: **done** (2026-10-30), in this order. What is left of each item is in the [roadmap](../roadmap.md).

Owner's scope (2026-10-30): the rest of T0 (without the LightPipes cross-check), T1 (without branches), then T2. Branches come after.
This note records the order, why, and what each change means for branches.

## Order

Each stage is built on the one before it, and each ends with tests and docs.

| stage | work | why here |
|---|---|---|
| 1 | **T0**: review the tests with no external reference; say what compares the engine only with itself | first: later stages rely on these tests |
| 2 | **T1.1**: absorbing boundary in nonlinear spatial elements; Hankel matrix cache; cw / single-frequency source | small, and the cw source makes stage 3 and T2 cases cheap to run |
| 3 | **T1.4**: Gerchberg-Saxton; near-field centring and floor; image files | independent of everything else |
| 4 | **T1.3**: ray-traced chief-ray dispersion (all orders), `prism` element, `spaceTime` from the tracked beam, angular dispersion on carried profiles | the largest change to the tracker; the prism needs the exact ray trace, and the profile work needs both |
| 5 | **T2**: amplifier thermal lens on the tracked beam, stress birefringence, partial coherence, resonators | resonators need the matrix of a nested sequence, which comes from the tracker work |
| 6 | Docs, roadmap, status, history, examples, full verification | |

## Branching impact

Branches ([roadmap](../roadmap.md)) will replace "one beam, element after element" by a graph. What the work here touches:

| item | impact | what we do now |
|---|---|---|
| Tracker state (`Compiler.Ctx.Track`, `BeamState`) | **High.** One mutable beam threaded through compilation: a graph needs one per edge | new state (ray bundle, round-trip matrix) stays inside `BeamState` and is copied by `Clone`, so an edge can own a clone |
| Pending GDD on an element | Medium. Applied to the element's own output, so it stays valid per edge | keep it per element, never global |
| Carried profile with angular dispersion | Medium. Two arms recombine coherently only if their profiles share a grid and stored frequencies | the dispersion lives in the profile data; no hidden state |
| Partial coherence | **High, by design.** Each coherent mode is a separate run; a splitter must act on every mode | a run mode around the executor, not inside it: branches stay independent of it |
| Resonators | **High.** A true cavity is a cycle in the graph | a `resonator` element holding a nested sequence, like the `oscillator`. It can be rewritten as a graph cycle later with the same keys |
| Cw source | Low. It is a one-bin spectrum, so all paths treat it as a pulse; interference of arms is the natural use | none |
| Masks, `prism`, Gerchberg-Saxton, thermal terms | None: one element at a time | none |

Not changed: `Executor`'s element loop, which is what branches will replace.
