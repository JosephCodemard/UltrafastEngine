# Material catalogue

One JSON file (comments allowed) per material, gas or Raman model. Embedded in `UltrafastEngine.Core` (`Catalogue.cs` loads it); the generated, readable
form is `docs/reference/materials-catalogue.md`. User files in `~/.ultrafast/materials` replace single quantities (see `docs/reference/materials.md`).

```
solids/<id>.json   kind "solid": index, n2, raman (a reference to raman/<id>), ramanFraction, kerrAnisotropy, ionization
gases/<id>.json    kind "gas":   index, n2Electronic, ionization, rotation, dephasing (air also: composition)
raman/<id>.json    kind "raman": response (model + parameters), ramanFraction, anisotropicFraction
gain/<id>.json     kind "gain":  doping, lifetime, zeroLine, thermal, stress, emission, absorption (bands in cm^2)
crystals/<id>.json kind "crystal": indexX / indexY / indexZ (formula + coefficients), d (pm/V), n2, validity; entry-level displayName, opticalClass, references
                   (Air, in gases/, also holds the constants of Ciddor's formulas)
```

Rules (a test, `CatalogueTests.CatalogueFiles_HaveSourceStatusAndUnitsInTheirKeys`, enforces them):

* every quantity has a `source` and a `status` (`verified` | `approximate` | `unverified`); one that is not verified also states an uncertainty;
* a number has its unit in the key name (`valueM2PerW`, `tau1S`, `ionizationEv`); coefficients keep the units of their source;
* `verified` means the number was read from the cited source (or its database page) and compared; write the comparison into `verification`;
* never put licensed paper text or figures here, only the extracted numbers and the citation.

Changing a number: edit the file, run `/regen` (the reference page), and re-pin the values on purpose with
`ULTRAFAST_WRITE_SNAPSHOT=1 dotnet tests/UltrafastEngine.Tests/bin/Release/net10.0/UltrafastEngine.Tests.dll CatalogueSnapshot`; say in the commit which value moved and why.
