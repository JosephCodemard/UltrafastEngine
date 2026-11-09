# 05 -- Polarisation, coatings and Fresnel losses

Jones calculus with s/p decomposition at every surface. Things to check in the results:

* `after_mirror`: the 45-degree quarter-wave stack reflects s and p with different phase -> the 30-degree linear
  input becomes slightly elliptical (`s3` != 0).
* `after_window`: the Brewster window transmits p (x) fully and loses ~15 % of s (y) per surface -> the
  polarisation rotates towards x.
* `after_polarizer` / `energyMeter`: what survives the half-wave plate + thin-film polariser.
* `lossBudget`: `coating` (mirror), `fresnel` (window), `polarization` (polariser).

Switch `"polarization": "scalar"` in `run.json` to see the s/p-averaged model for comparison.
With a Zemax export, the plane of incidence of every surface comes from the traced ray vectors instead of
`planeOfIncidenceDeg` (docs/polarization.md).
