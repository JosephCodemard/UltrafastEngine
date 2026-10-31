# Key processes

## A run

`Simulation.Run(system, run)`:

1. `Compiler.Compile` gives a `CompiledRun`.
2. `Executor.Execute` gives a `RunResult`.
3. The CLI or Python writes `summary.json`, `result.npz` and CSVs.

## Compile

In `Compiler.Compile`, in order:

1. Check keys against `Schema` (`ULTRAFAST_STRICT_OPTIONS=1` makes unknown keys errors).
2. Set up the air, and start beam tracking if there is a Gaussian `inputBeam`.
3. `SystemGraph.Build`: connect ports, order the elements, and verify. Its alerts join the warnings.
4. For each element, in graph order, `CompileElement`. It reads the keys, and fills `Pre` and `Post` (linear terms), `Nonlinear` or `Active`. In a Branched system each element starts from a copy of the tracked beam on its first linked input.
5. Work out probes and instrument locations.
6. `BuildGridAndPulse`: plan the grid and build the input pulse.
7. Pick the field mode of each nonlinear segment.

## Execute: one element

`Executor.RunElement(state, index, element, pulse, path)`, for a pulse on one path:

1. `Pre` linear terms.
2. `Active.Execute`, or the nonlinear segment.
3. `Post` linear terms.
4. Add the element's phase and delay to the `PathLedger`; record energies, losses, beam sizes and damage margins in an `ElementRecord`.

A Sequential system calls this for each element in turn, in the loop in `Executor.Execute`.

## Execute: a Branched system

`Executor.RunGraph`:

1. Queue the run's pulse at the first element's default port, and virtual beams at theirs.
2. Run the graph's stages in order. `RunNode` takes what is queued on each input port, merges it (`Packets.Merge`), calls `RunElement` once per packet, and delivers each output to its link.
3. A multi-port element gives its other outputs as by-products named after their ports, so `RunElement` needs no special case. `ActiveContext.InputPort` says which input it is on.
4. Output with no link goes to a virtual output.
5. The strongest packet at `end` becomes `RunResult.Output`, and the budget is added up over the graph. `graph.energyOutJ` counts every packet at `end`.

A crystal's input angle comes from the arms: the compiler tracks a carrier and an x tilt per output port (`PortOmega`, `PortTiltX`). `ThreeWave.At` works out the directions, and each output port's path leaves with its own tilt.

Interference is not a step of its own. An element sees one packet at a time, which is exact for a linear element, and fields add when the packets on one port merge. Crystals are the exception: beams on their ports run together, because they mix.

## A loop

`Executor.RunLoop` repeats a loop's stage. A packet sent back to an earlier element waits in that element's queue for the next pass, so no edge needs cutting. It stops by energy (`ringDown`), when shape and energy both repeat (`steadyState`), or at `maxRoundTrips`, and books what is left.

## The frame and the ledger

A linear term removes its carrier phase φ₀ and group delay τ₀ from the field, so a pulse stays centred in its window. `PhaseTerm.DispersionAt` reports them in `DispersionPoint.FramePhaseRad` and `FrameDelayS`, and `PathLedger.Add` keeps them:

true field = stored field × exp(i (φ + (ω − ω₀) τ))

When packets merge, each is moved to a common frame by the difference of their ledgers. Tracked Gaussians take their mode overlap (`Packets.Overlap`); carried profiles add point by point (`TransverseOps.Expand`, `CollapseVector`). Active elements report phase through `ctx.Own`, and `ctx.PathPhaseFactor` scales it when the carrier changes.

## Beam size and quality

A carried profile's size and M² are central moments: `SecondMomentRadius` measures about the centroid, and `BeamQuality` removes the mean tilt and the fitted curvature first. `Curvature` fits the phase step between neighbouring samples, so a vortex or the sign change between lobes does not disturb it.

## Narrow lines and the tail

With a tail, `Executor.SplitNarrow` divides a solver's background through `LinearTerm.TrySplitNarrow`: the smooth part stays in the solver, and half of the narrow part goes exactly on main + tail before and after it. `ApplyLinear` sends polarisation-mixing terms of a vector profile to the full field, and everything else to the ledger.

## Grids

`GridPlanner.Plan` sizes the time window for the pulse, the dispersion it will meet and satellites (`Compiler.SatelliteRoom`). An element that changes the carrier asks for a new grid through `ActiveContext.DownstreamGrid`.

## Studies

`Studies` compile a variant per point. In a Sequential system, when only later elements change, `Executor.ExecuteFrom` reuses the earlier ones (prefix cache). In a Branched system the nominal run fills a `GraphCache`, keyed by a hash of everything feeding each element, and each point replays what matches. `arrayOutputs` keep instrument arrays per point.

## Adding things

| to add | do |
|---|---|
| an option | declare it in `Schema/BuiltIn*.cs`, read it in the compiler, test, `/regen`, document |
| an element | add a spec, add its name to `Compiler.ElementTypes`, add a `case` in lower case in `CompileElement` |
| ports | `.Ports(inputs, outputs)` on the spec; implement `IMultiPortElement`; give other outputs by `ctx.Byproduct(port, pulse)` |
| a carrier phase | implement `IReportsCarrierPhase` and report through `ctx.Own`; otherwise paths through the element have `PhaseKnown = false` |
