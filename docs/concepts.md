# Concepts and conventions

## Units

Everything is SI: metres, seconds, rad/s, watts, joules, W/m^2, J/m^2. JSON keys say so (`lengthM`,
`durationFwhmS`, `gddS2`, `todS3`, `n2` in m^2/W). A few inputs also accept convenience units where noted
(`centerWavelengthNm`, `durationFwhmFs`, `fluenceJPerCm2`, `linesPerMm`, `aoiDeg`).

## The pulse

The pulse is a two-component (x, y) complex **spectral envelope** A(omega) on a uniform grid around the carrier
omega0, in units of sqrt(W): |Ax(t)|^2 + |Ay(t)|^2 is the instantaneous power. The real field is
E(t) = Re{A(t) exp(-i omega0 t)} (physics sign convention, as in Agrawal and pyNLO; the real-field mode below uses
e(t) = sqrt(2) Re{a(t) exp(-i omega0 t)} so that the cycle average of e^2 is the power |a|^2):

* multiplying A(omega) by exp(+i phi(omega)) with dphi/domega = tau **delays** the pulse by tau;
* positive GDD puts red first; Kerr SPM is +i gamma |A|^2 A; self-steepening is (1 + Omega/omega0);
  Raman red-shifts. The test-suite checks all of these directions explicitly.

## Co-moving frame and lab time

Each element's carrier phase and carrier group delay are removed before it is applied, i.e. the pulse is
propagated in a frame moving with the carrier group velocity -- otherwise a metres-long system (tens of ns of
delay) would never fit an FFT window. The removed delay is not lost: the engine accumulates it as the **arrival
time** (`arrivalTimeS` per element and in `totals`). Time-domain models receive both the co-moving time `t` and
the lab time `t_lab = t + t_arrival(z)`, so effects fixed in the lab (a pump pulse, a modulated medium, a plasma
that outlives the pulse) are expressed naturally.

## Absolute index

The engine propagates with absolute (vacuum-referenced) indices. Catalogue glasses quoted relative to air
(Schott, OpticStudio) are converted with the Ciddor index of air (`indexReference: "air"`). Data exported from
OpticStudio is relative to air; for air paths the engine adds the dispersion of air itself (`airPathM`,
switch: `physics.absoluteIndex`).

## Systems, runs and the compile -> execute split

A **system file** (`.ufs`) describes optics; a **run spec** describes one experiment on it (pulse, grid,
physics switches, solver, probes, instruments, overrides, outputs). The engine first **compiles** both into
an execution plan:

1. resolve materials, coatings and overrides (recording provenance);
2. turn each element into *linear terms* (dispersion, Fresnel/coating, clip, scatter, absorption, filter,
   polarisation optics...) and, where the medium is nonlinear and a beam size is known, a *nonlinear segment*;
3. size the time/frequency grid from the pulse and the largest group-delay spread anywhere in the system
   (and the expected nonlinear broadening), snap the carrier onto the frequency grid, build the input pulse.

It then **executes**: linear terms are applied exactly (one complex multiplication per frequency, any length),
each with its own energy accounting; nonlinear segments are integrated in the interaction picture with the
element's full linear operator; diagnostics are recorded after every element; instruments read the pulse where
they are placed.

## Elements, terms and mechanisms

Every linear term carries a **mechanism** tag -- `dispersion`, `absorption`, `fresnel`, `coating`, `clip`,
`scatter`, `grating`, `filter`, `polarization`, `transmission`, `ghost`, `custom` -- and the energy each removes is
booked to it. Inside nonlinear segments the energy change is booked as `absorption` (absorbing medium or
user responses), `raman+numerical` or `numerical`. Other entries have their own names: `ASE (spontaneous emission)`
and `from pump (aux)` (energy brought in, negative), `harmonics (out of band)` (carrier-resolved field), `to <field>`
(by-products of a crystal), `mode mismatch`, `virtual output <port>`, `left in loop` and `earlier passes` (loops) in
Branched systems. The sum of the loss budget equals the input minus output energy exactly.

## Grids

`grid.auto` (default) sizes the window to hold the pulse after every element (a stretcher followed by a
compressor needs the stretched length even though the total spread is ~0) and the time step to resolve the
spectrum with a margin (1.3 for linear systems, `max(3, 1 + B_estimate)` with nonlinear elements, and at
least +-10 % of the carrier so user responses have room), with at least 12 samples across the transform-limited
FWHM. Explicit `n`/`windowS` act as a resolution floor. `edgeEnergyFraction` in the results warns about
wrap-around; `outsideDataFraction` warns when part of the spectrum relies on extrapolated sampled data.

## Grid tails and delayed light

Weak light that arrives long after the pulse (ghosts, mirror-substrate echoes, coating-ripple pedestals, delayed
scatter, gas-line ring-down) would force a window of nanoseconds on the nonlinear grid. With `grid.tail` the engine
keeps a second, longer, **linear** grid with the same time step and carrier next to the main one. The field is the sum
of the two; linear elements act on the sum exactly and split the result between the grids with a smooth window,
nonlinear elements act on the main grid only, and the tail passes through their linear terms. Energy is conserved
across the split. `grid.extendForSatellites` and `grid.strict` plan or check the window for satellites without a tail
([Delayed light](reference/delayed-light.md)).

## Systems with branches: topology and ports

A system is `Sequential` (each element continues into the next) or `Branched` (`"topology": "branched"`: each element
says where its light goes with `next`). Both compile to the same graph and run through the same element code. Elements
have named **ports**: most have `in` and `out`; splitters have inputs `a`, `b` and outputs `c`, `d`; a crystal
adds one output port per generated wave. Unconnected ports are accounted for (virtual ports, with their energy in the
budget). Light that arrives at the same element by different paths adds in amplitude. For this each path keeps a
**path ledger** of the carrier phase and carrier group delay that the co-moving frame removed, so that the fields meet
with the right relative phase and time; tracked beams are scaled by their mode overlap and carried profiles add point
by point ([Branched systems](reference/branches.md)). A link back to an earlier element makes a loop (`ringDown`,
`steadyState`).

## The beam: tracked Gaussian and carried profile

Two descriptions of the transverse beam are available and an element uses what the run provides:

* a **tracked Gaussian beam** (`beam` models, `inputBeam`, ABCD matrices per axis x and y, complex q parameter): cheap,
  analytic, gives the effective area for the Kerr effect and the overlap for χ(2) and for meeting beams;
* a **carried transverse profile** P(r, omega) or P(x, y, omega) on a radial (Hankel) or cartesian (FFT) grid, created
  by an element with a `transverse` model and handed on to the next spatial element
  ([Spatial models](reference/transverse.md)).

Conventions: the transverse axes are x and y (the principal axes of a tracked beam); polarisation components are x and y in
the transported basis (s and p are worked out per surface from the angle of incidence). Spatial position is measured
from the beam axis; angles are signed in the unfolded beam.

### Spatio-temporal couplings

With the beam tracked, the engine follows per axis the spatial dispersion E = dx/domega and the angular dispersion
F = dtheta/domega (reduced angle n theta) with the ray-pulse matrix (Kostenbauder) formalism: E' = A E + B F + E_el,
F' = C E + D F + F_el. Gratings, prisms, tilted interfaces and tilted plates add their own E_el, F_el. The 1-D spectrum
A(omega) is the field on each frequency's own ray; the elements apply the extra spectral phase (GDD, TOD, FOD) that
this costs, and report spatial chirp, angular dispersion and pulse-front tilt. The switch is `physics.spatioTemporal`
(`auto`, `on`, `track`, `off`, `profile`) ([Components](reference/components.md#spatio-temporal-couplings)).

## Carrier-resolved mode

`physics.fieldMode` is `auto`, `envelope` or `carrier`. In `carrier` mode (or `auto` when a time response declares
`carrierResolved`) the element propagates the real field on a finer grid reaching `physics.fieldHarmonic` times the
carrier frequency, so third-harmonic generation, broadband chi(2) and optical-cycle effects emerge without envelope
approximations. The mode is scalar and runs on the CPU ([Nonlinear physics](reference/nonlinear.md#carrier-resolved-field-model)).

## Measured data

A measured spectrum can be the input pulse (`pulse.spectrum`), and any instrument can be given a recording that the run
compares with the simulated reading, on the recording's own axis and at the instrument's resolution
([Measured data](reference/measured-data.md)). A measured contrast trace can be the input pedestal
(`pedestal` kind `measured`).

## Physics switches

The `physics` object of the run spec switches model ingredients without editing the system file
([Run spec](reference/run-spec.md#physics)):

| key | what it switches (default) |
|---|---|
| `nonlinear` | master switch for nonlinear propagation (true) |
| `selfSteepening`, `raman` | self-steepening, delayed response of materials that have one (true, true) |
| `fieldMode`, `fieldHarmonic` | envelope or carrier-resolved field; highest harmonic on the carrier grid (auto, 3) |
| `absoluteIndex` | add air dispersion to air paths quoted relative to air (true) |
| `polarization` | `jones` (two polarisations) or `scalar` (jones) |
| `losses` | false removes flat loss terms (true) |
| `beamTracking` | Gaussian beam (q) tracking: `auto`, `on`, `off` (auto) |
| `spatioTemporal` | couplings: `auto`, `on`, `track`, `off`, `profile` (auto) |
| `transverse` | default spatial model for every bulk, gap and crystal without one |
| `ionization` | photo-ionisation and plasma in every nonlinear bulk element (false) |
