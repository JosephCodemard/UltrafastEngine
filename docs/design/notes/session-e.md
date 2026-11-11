# Session E (WP7 leftovers): what was done

Done 2026-11-11 on `dev/2026-11`, from [plan-next.md](../plan-next.md) section 2, WP7: the three findings left open by [session A](session-a.md). All three have a cause; one needed an
engine warning, none needed a physics change.

## Raman shift rate (`Fibres/raman-soliton-shift`)

* **Finding:** the engine's shift rate was 16-36 % above Gordon's `-8 T_R |beta2| / (15 T0^4)`.
* **Method (the three steps of the plan).** (1) pyNLO, same fibre (no self-steepening, 2^14 points over 20 ps, 60 steps): rates over 3-6 m of -1.319, -0.504, -0.162 THz/m for T0 = 40, 50, 65 fs; the engine
  (fresh library) gives -1.319, -0.503, -0.162: within 0.3 %. (2) Gordon's rate with the model's exact Raman response. From `phi_z = gamma V`, `V = (1 - f_R) P + f_R (h_R * P)`, the mean frequency changes as
  `d<Omega>/dz = -gamma f_R (1/E) integral (h_R * P) P' dt` (the Kerr part integrates to zero). For `h_R = T_R delta'` it gives Gordon's formula exactly (checked analytically: `integral P'^2 = 16 P0^2 / (15 T0)`);
  with the Blow-Wood response and the sech soliton it gives -1.344, -0.499, -0.160 THz/m, and the engine is within 2 % of that (0.981, 1.007, 1.013). (3) The fixed-width variant is (2): the formula assumes the
  initial width; the engine's soliton changes slowly and the rate is fitted over the last two thirds of the fibre.
* **Cause:** Gordon's first-moment approximation `h_R ~ T_R delta'` is not valid for a 40-65 fs soliton (spectral width about 9 THz, Raman gain peak at 13 THz); the exact kernel gives 1.39, 1.26, 1.15 times Gordon's rate. **Not an engine error.**
* **Changes:** `raman_shift.py` prints and asserts the exact-gain rate (5 %); README states the cause; `python/tests/test_raman_shift.py` (slow): engine vs pyNLO 1 %, vs exact 3 %, Gordon low by > 20 %; and the delta-prime limit of the exact formula (1 %).

## SLM against the Fourier prediction (`AdaptiveOptics/slm-beam-shaping`, [wp0.md](wp0.md))

* **Finding:** correlation 0.98-0.99 with the Fourier prediction; an ideal 8-pixel blaze kept 0.854 of the light (1.000 for the Fourier sum), 16 pixels 0.962 (0.983 quoted in wp0.md was the first order only).
* **Method.** Compared the engine with an exact angular-spectrum propagation (NumPy) of the same sampled field and switched things off one at a time. The retained fraction did not depend on the beam size,
  the lens, the absorbing edge or `f`: it equals `cos^2(pi/P)` to four digits (0.8536 and 0.9619). That is the energy left when each sample is the mean of two adjacent pixels.
* **Cause.** The cartesian grid is `x = (i - nx/2) dx` (a sample on the axis); the modulator's cell centres are `(i - (N-1)/2) pitch` (an even panel has an edge on the axis). With pitch = dx the fast path in
  `ModulatorElement.CellsAt` never applies and the 6 x 6 sub-sampling averages two pixels. Not a bug in the averaging; a geometry that the user has to fix by half a pixel.
* **Check:** with `offset = +-pitch/2` the total is 1.0000 for both periods. The example with the offset and the exact reference: relative L2 distance to the exact propagation 0.9-1.0 % with the absorbing edge off (a bound); 12-14 % with
  it on, the difference being the replica orders that the periodic reference wraps round the window (the engine absorbs them). The residual 0.9 % was not separated further.
* **Changes:** warning in `ModulatorElement.Execute` ("averages two device cells ... offsetXM / offsetYM = pitch / 2"); the example (offset, grid-point illumination, `exact_prediction`, quoted output); `docs/plugins/ao.md` "Placement on
  the engine's grid"; tests `test_slm_pixels_on_the_grid_points_...` (2). The full-panel run now gives 0.87 on the smiley (design 0.86), was 0.91. `make_figures.py` reuses the example functions: the PNGs need regenerating (see below).
* **Not done:** the cell average as an option (`align: grid`) was considered and left out: the average is correct physics for a grid coarser than the pixel structure.

## Tiled-array SPGD reference (`AdaptiveOptics/tiled-array`)

* **Finding:** SPGD ended at 1.02 of the in-phase value on the 128-cell grid.
* **Method.** Each tile alone (`amplitudes` one-hot) on the axis, for 128 cells over 10 mm at f = 0.5 m: relative fluences 1, 1, 0.08, 4.6, 0.08, 1, 1 for seven identical tiles. With f = 1 m on 128 cells, or 256 cells at f = 0.5 m: equal to 1e-3.
* **Cause:** the lens phase `k r / f` at a tile 3 mm out is 3.7 rad per cell (> pi): the lens is under-sampled, the tiles reach the axis with unequal amplitudes, and the zero-piston sum is not the maximum. (A NumPy
  sampled-field model puts 1.015 for that case but does not reproduce the engine's individual weights in the aliased regime, so it is not used as evidence.)
* **Changes:** the lock runs at f = 1 m (1.9 rad per cell) and ends at 1.00 with `0.95 < result < 1.005` asserted (start 0.04); README; test `test_tiled_array_identical_tiles_reach_the_axis_alike_only_when_the_lens_is_resolved`.
  A warning for an under-sampled thin lens is not in the engine (the aperture-resolution warning of lenses is a separate matter): worth a backlog line.

## Leftover for the owner / next steps

* Regenerate the figures of the two AdaptiveOptics examples (`make_figures.py`; the SLM hero and the full-panel PNG change with the new numbers).
* A lens under-sampling warning (phase step per cell above pi at the beam edge) would have caught the tiled-array case.
