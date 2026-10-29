# Model levels

The same system file can be propagated at different levels of spatial fidelity. Each element chooses its level;
nothing in the file has to change to move between them.

| Level | Field | Spatial physics | Use for |
|---|---|---|---|
| **Temporal** (default) | A(ω) in both polarisations, or the real field e(t); the beam enters through its effective area A_eff(z) | the beam size follows the element's beam model or the tracked Gaussian beam | most systems: stretchers, compressors, plates, multipass cells, fibres, amplifiers |
| **Gaussian tracking** | the complex beam parameters q_x, q_y followed through every element | focusing by lenses and curved (off-axis, astigmatic) mirrors sets the intensity in later nonlinear elements, crystals, cameras and fluence checks | free-space systems where the beam stays Gaussian ([Components](reference/components.md#gaussian-beam-tracking)) |
| **Full field** | E(r, ω) or E(x, y, ω) on a radial or cartesian grid, per element (`"transverse"`) | diffraction, self-focusing and Kerr lensing, spatial depletion and walk-off in crystals, spatial gain and thermal lensing, aberrations, vector fields, M² | where the transverse profile changes ([Spatial models](reference/transverse.md)) |
| **UPPE** | full field with the unidirectional coupling n ω / (c k_z) per plane wave (`"equation": "uppe"`) | non-paraxial focusing, filamentation with ionisation and plasma | tight focusing and collapse ([Ionisation](reference/ionization.md)) |

## What the temporal level does and does not include

Included: dispersion to all orders, losses with exact energy accounting, Jones polarisation, Kerr + Raman +
self-steepening with full vector coupling, user time responses, ionisation, beam-size variation along each element
(focusing inside a multipass-cell pass), carrier-resolved propagation, fluence and LIDT checks, every instrument.

Not included: anything that changes the transverse profile -- self-focusing, Kerr lensing, diffraction by apertures,
the effect of aberrations on the focus. The engine reports `criticalPowerRatio` (peak power / critical
power for self-focusing) for every nonlinear element and warns above 0.3; give the element a `"transverse"` model
where it matters. Wavefront errors on a 1-D beam are reported as an rms and a Strehl estimate.

With a Gaussian `inputBeam` the beam is tracked through the system (q parameters, thermal lenses of gain media), and
so are the spatio-temporal couplings of gratings, prisms and tilted plates: angular dispersion, spatial chirp,
pulse-front tilt and the dispersion they add (Kostenbauder matrices; [Components](reference/components.md#spatio-temporal-couplings)).
The tracked beam stays Gaussian; a carried transverse profile does not take the angular dispersion.
