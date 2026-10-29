# hitran plugin

Line-by-line absorption and dispersion of air (H2O, CO2, O2) from HITRAN line lists, as the index model `"kind": "hitran"`.
Docs: [docs/plugins/hitran.md](../../docs/plugins/hitran.md). Delayed light: [docs/reference/delayed-light.md](../../docs/reference/delayed-light.md).

* `src/` -- the C# plugin (`HitranAirIndex`, Faddeeva function, `.par` / CSV readers). No NuGet packages.
* `python/ultrafast_hitran/` -- builders (`air`, `species`) and the downloader (`python -m ultrafast_hitran download H2O`; needs the
  HITRAN API, `pip install hitran-api`).
* No HITRAN data are shipped; HITRAN has its own terms of use (https://hitran.org).
