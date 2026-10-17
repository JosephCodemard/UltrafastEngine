# 18 -- A nonlinear medium you define: n(x, y, z, t, I, F)

A 10 mm "doped glass" rod on a radial grid. The built-in Kerr effect is replaced (`"replaceKerr": true`) by an
expression that uses every variable a time response sees:

```
dn = n2 (1 + z/L) exp(-(r/rc)^2) I  -  kF F  +  dn_mod sin(2 pi f_mod t_lab)
```

| term | variables | meaning |
|---|---|---|
| n2 grows along the rod | `z`, `L` | a doping gradient along the rod |
| only the core (r ≲ 0.4 mm) is nonlinear | `r` (also `x`, `y`, `theta`) | a doped core in undoped glass |
| slow negative term | `F` = ∫I dt (running fluence) | free carriers, a fast thermal effect |
| 1 THz modulation | `t_lab` | a co-propagating driver |

On top of that the rod has a static GRIN (`indexProfile`) and a lossy cladding (`absorptionProfile`)
([docs/spatial-properties.md](../../reference/spatial-properties.md)).

```bash
ultrafast run medium.ufs run.json -o results         # the expression in the system file
python custom_medium.py                               # the same in Python, and an x-gradient on a cartesian grid
```

```
expression (medium.ufs)              B = 0.420 rad, spectrum FWHM 7.85 nm, beam after 200 mm 351 x 351 um, centroid x = -0.0 um
Python callback, same physics        B = 0.420 rad, spectrum FWHM 7.85 nm, beam after 200 mm 351 x 351 um, centroid x = +0.0 um
cartesian grid, no Kerr effect       B = 0.000 rad, spectrum FWHM 7.81 nm, beam after 200 mm 381 x 381 um, centroid x = +0.0 um
Python: n2 (1 + x / 0.5 mm)          B = 0.368 rad, spectrum FWHM 7.83 nm, beam after 200 mm 361 x 361 um, centroid x = +3.4 um
```

The expression and the Python function agree exactly. A Kerr coefficient that grows across the rod tilts the
nonlinear phase front and steers the beam towards +x. A Python response is called for every grid point at
every step, with `ctx.x`, `ctx.y`, `ctx.r`, `ctx.theta`, `ctx.z`, `ctx.t`, `ctx.t_lab` and `ctx.intensity`
([docs/python.md](../../reference/python.md)). It is slower than an expression on large cartesian grids.
