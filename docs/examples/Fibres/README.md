# Fibres

The `fiber` element (single-mode fibres and waveguides: Taylor coefficients of the propagation constant, γ, Raman, loss and gain; see
[Fibres and waveguides](../../reference/fibers.md)) on three textbook problems whose answers are known. Each shows the evolution along
the fibre as a heat map and ends with an assertion against theory.

| example | the story | the number it checks |
|---|---|---|
| [soliton-breathing](soliton-breathing/README.md) | N = 1 soliton keeps its shape; N = 3 breathes and returns after z0 = (pi/2) L_D | peak power within 0.03 % (N = 1); back to 1.003 x at z0 |
| [raman-soliton-shift](raman-soliton-shift/README.md) | Raman scattering slides a soliton to the red, faster the shorter it is | rate proportional to T0^-4 (fitted -4.3) |
| [spm-compression](spm-compression/README.md) | SPM broadens the spectrum in a normal-dispersion fibre, a compressor shortens the pulse | pure-SPM bandwidth growth 8.85 x = formula; 300 fs to 88 fs |

Also in the repository: the Dudley PCF supercontinuum benchmark ([Nonlinear/supercontinuum](../Nonlinear/supercontinuum/README.md)) and the
coherence study that goes with it. Run: `python docs/examples/make_all.py --group Fibres`.
