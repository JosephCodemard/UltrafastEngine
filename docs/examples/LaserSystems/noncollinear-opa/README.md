# 42 -- A non-collinear OPA with the angle from the pump arm

A NOPA on the command line, as a Branched system. A 1030 nm pulse is doubled in BBO. The second harmonic goes to an arm tilted by 4° and pumps a second BBO crystal. The 800 nm seed is a virtual input on that crystal's `signal` port. The crystal takes the crossing angle from the arm's tilt.

```bash
cd docs/examples/42-noncollinear-opa
ultrafast run nopa.ufs run.json -o results
python report.py
```

```
cut 21.96 deg; pump 4.00 deg outside, 2.42 deg inside the crystal; idler 6.82 deg inside
leaving at 0.00 deg (signal), 4.00 deg (pump), 11.28 deg (idler)
transverse momentum balance 0.0e+00
signal 14.12 uJ from a 10 nJ seed; idler 7.81 uJ; Manley-Rowe residual -2e-08
```

The crystal's face is normal to the seed, which enters along the lab axis. The pump arm's tilt is therefore the pump's external angle. Inside the crystal the pump refracts to 2.42°, close to BBO's broadband "magic" angle for 515 nm pumping (about 2.4° to 2.55°). `cut: "auto"` solves the phase matching for that geometry.

The idler takes the transverse momentum of the pump, k_i sin β = k_p sin α. It leaves on the pump's side, beyond the pump, at ω_i sin t_i = ω_p sin t_p. That is why a NOPA separates its idler without a dichroic. Each port's path carries its tilt onward.

The seed's `"delayS": "auto"` centres it on the pump inside the crystal, whatever group delay the pump picked up in the first crystal.

**What this does not model.** This is the 1-D model with Gaussian overlap. The coupling falls as the pump and the seed part along the crystal. The geometric smearing across the beams is reported as `geometricSmearingS` (77 fs here) but is not propagated; a Cartesian grid carries it. The gain is saturated here, and depletion is uniform across the beam in 1-D. See [Crystals](../../reference/crystals.md#non-collinear-opa-and-dfg).
