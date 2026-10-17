# 07 -- The OpticStudio workflow

```
 OpticStudio (Windows)                         anywhere (Windows / macOS / Linux / Python)
 ---------------------                         -------------------------------------------
 your .zos  --UltrafastEngine.Zemax export-->  system.ufs  --ultrafast run / Python-->  results
            (ray tracing + merit operands)        ^
                    |  --save-sample              |  ultrafast zemax-build (re-export without Zemax)
                    v                             |
              raw sample (.ufsample.json) --------+
```

1. **Export** (on the OpticStudio PC): open the system, *Programming > Interactive Extension*, then
   `UltrafastEngine.Zemax.exe export export.json -o system.ufs --save-sample system.ufsample.json`.
   The Wavelength Data Editor is rewritten in batches and restored afterwards; your merit function is untouched
   (temporary GBPS/GBPW rows are removed).
2. **Run** anywhere: `ultrafast run system.ufs run.json -o results`, or from Python.
3. **Re-export without Zemax**: change `export.json` (media, coatings, merge settings...) and run
   `ultrafast zemax-build system.ufsample.json export.json -o system.ufs` on any machine.

Try it now without OpticStudio, with the built-in fake argon cell:

```bash
ultrafast zemax-demo mpc export.json -o mpc_from_zemax.ufs
ultrafast run mpc_from_zemax.ufs run.json -o results
```

See docs/plugins/zemax.md (the zemax plugin) for every export setting, what is read from OpticStudio and how.
