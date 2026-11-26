# HANDOFF -- WP P2 (delayed-response window planning)

State (WIP): code + tests + most docs written; full suite running; regen, CHANGELOG, history, roadmap status, physics review still to do.

* `GridPlanner.ResponseMemoryS`: geometric scan (unchanged) + dense per-decade scan; dense used only if > 1.1 x geometric (bit-identical for smooth kernels).
* `GridPlanner.MediumMemoryS`: material Raman + declared `memoryS` (`Models.IResponseMemory`) of timeResponse expression/callback; rotational Raman deliberately NOT counted (zero-padded linear convolution, no wrap).
* `GridPlanner.MemoryWarning`: window < 2 x memory -> warning with element, needed window and N.
* Compiler: memory from 1-D and spatial elements; Executor.PlanDownstreamGrid includes memory.
* Schema: new `model:timeResponse` specs (expression, callback) with `memoryS` -> needs regen (build/wp/regen.sh).
* Tests: GridSolverTests (3 new), GasTests (1 new). Docs: concepts grids, nonlinear limits/validation, gases, delayed-light, models, testing.md.
