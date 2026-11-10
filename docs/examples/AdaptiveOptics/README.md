# AdaptiveOptics

Shaping light in space, in phase and in time: the `ao` plugin (deformable mirror, spatial light modulator, tiled apertures, spectral
shapers, AOPDF) and the algorithms in `ultrafast.algorithms` (Gerchberg-Saxton holography, wavefront sensing and control, sensorless
optimisers). Every example below is a story with a before-and-after picture, runs on a laptop in under a minute (the full-size SLM
panel in 15 s), asserts its key number against a reference, and has an animation where an algorithm iterates.

| example | the story | the number it checks |
|---|---|---|
| [dm-correction](dm-correction/README.md) | measure, calibrate and correct a defocus + astigmatism + coma aberration with a 10 x 10 deformable mirror; Zernike decomposition before and after | Strehl 0.32 to 0.89 (modal 0.97); slope rms falls by 1 - gain per loop step |
| [sensorless-correction](sensorless-correction/README.md) | no wavefront sensor: modal search and SPGD from the focal peak alone | Strehl 0.39 to 0.94; 36 against 240 camera exposures |
| [aberrated-high-power](aberrated-high-power/README.md) | the deformable mirror protects a Kerr plate from hot spots in a 2 GW beam | peak nonlinear phase 1.55 to 0.60 rad |
| [slm-beam-shaping](slm-beam-shaping/README.md) | smiley, square, ring and spot array from a phase-only SLM with 8-bit grey levels; a full 1920 x 1080 panel | engine against the Fourier prediction of the written phase: correlation 0.98-0.99 |
| [tiled-array](tiled-array/README.md) | 7 and 61 coherent beams: N times the peak, piston and delay cost, SPGD phase locking | N x peak; `|sum exp(i phi)|^2 / N^2`; Gaussian-overlap delay cost |
| [temporal-shaping](temporal-shaping/README.md) | double pulse, pulse train, side pulses of pixel gaps, a restored spectrum, a closed-loop chirp search | Weiner side-pulse energy 0.0159; GDD x bandwidth |
| [scatterer-focusing](scatterer-focusing/README.md) | measure a scatterer's transmission matrix from intensities and focus through it | enhancement 47 against (pi/4)(N - 1) + 1 = 50 |

Run them all: `python docs/examples/make_all.py --group AdaptiveOptics` (hero figures and animations, written next to each README).
The plugin page with every parameter and what each test checks is [ao plugin](../../plugins/ao.md).

**Not here yet** (the S5 plan listed them): a component gallery of the `ao` elements, and an example of complex-field shaping with a
phase-only device (double-phase and Bolduc encoding through an SLM; the algorithms exist and are tested, the end-to-end SLM path is not).
The building blocks that existed before are in [Wavefronts](../Wavefronts/) and in the Python module
[`ultrafast.plotting.zernike`](../../reference/python-api.md).
