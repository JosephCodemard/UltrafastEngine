# tests/UltrafastEngine.Tests -- C# tests (custom runner, no NuGet)

* Framework: `TestFramework.cs`. `[Test]` (optional `Category = "..."`, `Slow = true`) on a public parameterless
  method; `Assert.Close(expected, actual, relTol, absTol, "what")`, `Assert.Less/Greater/True/Equal/Throws<T>`.
* Helpers: `TestUtil.cs` (`using static UltrafastEngine.Tests.U;`): `O(k, v, ...)` JSON objects, `A(...)` arrays,
  `Sys(elements...)`, `Spec(pulse, extra)`, `Gauss(λ, fwhm, E)`, `Go(sys, spec)`, `FlatMaterial(n2)` (constant
  index), `RmsSpectralWidth`, `SpectralCentroid`. JSON gotcha: `O()` accepts JsonValue, double, int, bool, string,
  double[] -- a `JsonObject` is a JsonValue.
* Run: build `-f net10.0`, then `dotnet bin/Release/net10.0/UltrafastEngine.Tests.dll <filter...>` (substring of
  `Class.Method`; a filter includes slow tests) or `--slow` for all. Mono: `mono bin/Release/net48/UltrafastEngine.Tests.exe --slow`
  (unset `ULTRAFAST_STRICT_OPTIONS`). Add `--verbose` for timings.
* Physics tests compare against an analytic result or a published value and declare it:
  `[Test(Reference = "Treacy, IEEE JQE 5, 454 (1969)", Tolerance = "0.1 %")]`, with detail in a comment. Tolerances: as tight as
  the numerics allow, with a reason. Every test class has `[TestArea("...")]`.
* Name tests as sentences: `Circular_AtOneAndAHalfTimesTheEnergy_MatchesLinear`.
* A new test file needs a row in `docs/testing.md`. `docs/reference/validation-matrix.md` lists every test with its reference:
  it is generated (`UltrafastEngine.Tests.dll --matrix docs/reference/validation-matrix.md`, part of `/regen`).
* `SchemaTests.CommittedSchemasAndReference_AreUpToDate` and `ValidationMatrixTests.ValidationMatrix_IsUpToDate` fail until you run `/regen`.
