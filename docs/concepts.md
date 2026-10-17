# Concepts and conventions

## Units

Everything is SI: metres, seconds, rad/s, watts, joules, W/m^2, J/m^2. JSON keys say so (`lengthM`,
`durationFwhmS`, `gddS2`, `todS3`, `n2` in m^2/W). A few inputs also accept convenience units where noted
(`centerWavelengthNm`, `durationFwhmFs`, `fluenceJPerCm2`, `linesPerMm`, `aoiDeg`).

## The pulse

The pulse is a two-component (x, y) complex **spectral envelope** A(omega) on a uniform grid around the carrier
omega0, in units of sqrt(W): |Ax(t)|^2 + |Ay(t)|^2 is the instantaneous power. The real field is
E(t) = Re{A(t) exp(-i omega0 t)} (physics sign convention, as in Agrawal and pyNLO):

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
`scatter`, `grating`, `filter`, `polarization`, `transmission`, `custom` -- and the energy each removes is
booked to it. Inside nonlinear segments the energy change is booked as `absorption` (absorbing medium or
user responses), `raman+numerical` or `numerical`. The sum of the loss budget equals the input minus output
energy exactly.

## Grids

`grid.auto` (default) sizes the window to hold the pulse after every element (a stretcher followed by a
compressor needs the stretched length even though the total spread is ~0) and the time step to resolve the
spectrum with a margin (1.3 for linear systems, `max(3, 1 + B_estimate)` with nonlinear elements, and at
least +-10 % of the carrier so user responses have room), with at least 12 samples across the transform-limited
FWHM. Explicit `n`/`windowS` act as a resolution floor. `edgeEnergyFraction` in the results warns about
wrap-around; `outsideDataFraction` warns when part of the spectrum relies on extrapolated sampled data.
