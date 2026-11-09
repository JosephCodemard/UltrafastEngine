# 38 -- A four-grating compressor, grating by grating

A compressor on the command line. Each `grating` and slant path is its own element, so the tracker follows the beam's spatial chirp through the line ([Components](../../reference/components.md)).

```bash
cd docs/examples/38-four-grating-compressor
ultrafast run compressor.ufs run.json -o results
python report.py
```

```
g1   spatial chirp     0.000 mm per 100 nm   GDD          0 fs2
a    spatial chirp    28.279 mm per 100 nm   GDD     773731 fs2
g2   spatial chirp    23.561 mm per 100 nm   GDD    -773731 fs2
mid  spatial chirp    23.561 mm per 100 nm   GDD    -773731 fs2
g3   spatial chirp    28.279 mm per 100 nm   GDD   -2321192 fs2
b    spatial chirp     0.000 mm per 100 nm   GDD   -1547462 fs2
g4   spatial chirp     0.000 mm per 100 nm   GDD   -1547462 fs2
Treacy, two pairs  -1547449 fs2
```

The second pair is the mirror image of the first, so the spatial chirp opens and closes exactly, and the total GDD is that of two single passes (Treacy, 0.001 %). The GDD changes sign inside a pair because a slant path adds its own dispersion before the second grating removes more.
