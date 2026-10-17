# 16 -- Position-dependent material properties

`indexProfile`, `absorptionProfile` and `n2Scale` make any material property a function of position (x, y, z, r,
θ). Combined with time responses, that gives n(t, r, θ, z, I). See
[docs/spatial-properties.md](../../reference/spatial-properties.md).

```bash
ultrafast run grin.ufs run.json -o results     # GRIN rod written as an expression
python grin_variants.py                         # expression, table, Python function, tapered profile
```

A 5 mm fused-silica rod with Δn(r) = −½·n0·g²·r² is a thin lens f = 1/(n0·g²·L) = 0.2 m, so a 0.5 mm beam
focuses to λf/(πw0) = 131 µm at 0.2 m:

```
thin-lens prediction: w(f) = 131.1 um at f = 200 mm
  expression          : w =  132.3 um
  table (r)           : w =  132.3 um
  Python f(x, y, z)   : w =  132.3 um
tapered x (1 - z/L): focus expected at 2f = 400 mm with w = 262.3 um: w = 263.7 um
```

The 1 % difference from the thin-lens formula is the rod's own thickness and diffraction. The tapered profile
depends on z, so it is evaluated at every step along the rod. The same keys work on gain elements (where the
thermal lens is added to them) and on crystals. Without a `transverse` model, absorption and n2 scaling are
averaged over the beam and an index profile has no effect.
