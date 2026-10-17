# Model levels

UltrafastEngine is built so that more physics can be added without changing system files or run specs:
the same `.ufs` can be propagated at different levels of fidelity.

| Level | Field description | Spatial physics | Status |
|---|---|---|---|
| **L0** temporal | A(omega) (or the real field e(t)), beam size from a model | effective area A_eff(z) only | **implemented (2.0)** |
| L1 Gaussian | + q-parameter / Kostenbauder matrices per element | Kerr lensing on a Gaussian beam, spatial chirp, pulse-front tilt | planned |
| L2 full field | E(r, omega) or E(x, y, omega) on a transverse grid per element (`"transverse"`) | diffraction, self-focusing, spatial depletion in crystals, walk-off, M² | **implemented (2.1)** for bulk/gap/crystal elements, lenses and curved mirrors ([Spatial models](reference/transverse.md)); Zernike aberrations and apertures as masks: planned |
| L3 UPPE | 3D unidirectional pulse propagation | self-focusing, filamentation, ionisation | planned (Phase 3, GPU) |

### What L0 does and does not include

Included: dispersion to all orders (from data or models), losses of every kind with exact energy accounting,
polarisation (Jones), Kerr + Raman + self-steepening with full vector coupling, user time responses, beam-size
variation along each element (focusing in multipass cells), carrier-resolved field propagation, LIDT/fluence
checks, instruments.

Not included at L0 (use a transverse model, L2, where it matters): anything that changes the transverse profile -- Kerr lensing/self-focusing, spatial chirp,
diffraction, aberration effects on the focus. The engine reports `criticalPowerRatio` (peak power / critical
power for self-focusing) for every nonlinear element and warns above 0.3. Wavefront aberrations (Zernike
terms from OpticStudio) have an extension point (`IAberrationModel`) but are exported and applied from L2 on.
