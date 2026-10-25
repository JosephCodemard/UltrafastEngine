---
description: Add a new option, element, instrument or model kind end to end (declaration, parsing, physics, test, regen, docs, Python)
argument-hint: "<key or element and what it does>"
---
Add: $ARGUMENTS

1. Declare it in `src/UltrafastEngine.Core/Schema/BuiltInElements.cs` / `BuiltInInstruments.cs` / `BuiltInOptions.cs`
   (name with unit suffix, unit, description, default). Model kinds also go into `Models/ModelRegistry.cs`.
2. Read it where it is used (compiler, spec parser, instrument) with the same default. Validate ranges with a clear
   `JsonException` message naming the key.
3. Test it (`tests/UltrafastEngine.Tests/<Area>Tests.cs`), including a bad value.
4. `/regen`. If Python users will set it often, add a keyword to the builder in `python/src/ultrafast/build.py`
   (typed return), then `gen_api_docs.py`, and a Python test.
5. Document it on the matching `docs/reference/*.md` page (and in an example if it changes how people work).
6. CHANGELOG (Unreleased → Added). Renamed key? Add a migration in `Schema/Migrations.cs`, keep the old name.
