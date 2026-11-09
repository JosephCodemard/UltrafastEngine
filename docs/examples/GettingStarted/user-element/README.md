# 12 -- A user element in Python: a programmable pulse shaper

`{"type": "user", "callback": "@dazzler", "params": {...}}` hands the main beam's spectrum to a function you
register (`uf.run(..., elements={"dazzler": dazzler})` or `Run.element("dazzler", fn)`), and continues with the
spectrum it returns. The function also gets the frequency axis, the carrier, the time axis and the element's
`params`, so one callback serves many elements.

```bash
python docs/examples/12-user-element/dazzler.py
```

The script measures the dispersion of 25 mm of SF11 plus 1 m of air with the engine itself
(`summary["totals"]["systemGddS2"]`, `"systemTodS3"`), then puts the shaper in front of the glass with the
opposite phase:

```
accumulated dispersion: GDD = 4711 fs^2, TOD = 3111 fs^3
no shaper             : FWHM   522.8 fs (TL  25.0 fs), energy 1.000 nJ
shaper (GDD + TOD)    : FWHM    25.0 fs (TL  25.0 fs), energy 1.000 nJ
shaper + 40 nm window : FWHM    46.2 fs (TL  46.2 fs), energy 0.775 nJ
```

The energy the window removes is booked as "user" in the element's energy budget. The same callback works from
C# (`registry.RegisterCallback("dazzler", (ElementCallback)(ctx => ...))`) and from C/C++
(`uf_run_set_element_callback`); see docs/active-elements.md.
