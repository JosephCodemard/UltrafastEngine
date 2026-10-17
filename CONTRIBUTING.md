# Contributing

Thank you for helping! A few rules keep the engine trustworthy:

1. **Every physics change comes with a test** against an analytic result, a conservation law, or another code
   (see docs/testing.md). Run `dotnet run --project tests/UltrafastEngine.Tests -f net10.0 -- --slow` and
   `python -m pytest python/tests` before submitting.
2. **Conventions.** SI units, with the unit in the name (`lengthM`, `gddS2`); physics sign convention
   (E = Re{A e^{-i w0 t}}, positive dphi/dw delays); power-normalised amplitudes (sqrt(W)).
3. **No dependencies in Core.** UltrafastEngine.Core must keep building offline, under NativeAOT, and on .NET
   Framework 4.8 (C# `LangVersion latest`, no reflection emit, no `dynamic`).
4. **Vectorised extension points.** Models evaluate whole arrays; never per sample.
5. **Formats are contracts.** Changing `.ufs` / run spec / C API semantics needs a version bump, a migration
   note in CHANGELOG.md and docs updates. New optional fields are fine.
6. **Document as you go.** Public types and members get XML doc comments; user-visible features get a docs
   page entry and, ideally, an example.

Code style: 4 spaces, braces on new lines for types/methods (see `.editorconfig`), `var` where the type is obvious.
