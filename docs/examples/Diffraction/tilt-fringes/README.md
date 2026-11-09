# 35 -- Fringes from a tilted mirror

A Michelson interferometer on the command line, in a Branched `.ufs` file. One arm is tilted by 2 mrad, and carried profiles add point by point at the return, so straight fringes appear with no fringe model.

The plate is met twice: the return is a second element with `"sameAs": "bs"` and `"side": 2`, which reverses the sign of r ([Beam splitters](../../reference/branches.md#beam-splitters)).

```bash
cd docs/examples/35-tilt-fringes
ultrafast validate michelson.ufs --graph
ultrafast run michelson.ufs run.json -o results
python report.py
```

```
fringe period 515.1 um   lambda / angle 515.0 um
```

Half the energy leaves towards the screen: the tilt gives many fringes across the beam, so the arms' interference averages out.
