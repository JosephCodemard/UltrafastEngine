# zemax plugin

The OpticStudio (Zemax) integration:

* `src/UltrafastEngine.Zemax.Sampling` (net10.0 + net48, any OS): the `IOpticStudio` abstraction, the sampler, the
  raw-sample format, the sample -> system-file builder, a fake OpticStudio and record/replay; the plugin class
  registering the CLI commands `ultrafast zemax-build`, `zemax-replay` and `zemax-demo`.
* `src/UltrafastEngine.Zemax` (Windows, .NET Framework 4.8): the OpticStudio extension program, laid out in the
  plugin folder's `tools/net48/`.
* `tools/ZosApiStubs`: compile-only ZOS-API stand-in (build without OpticStudio).

Documentation: [docs/plugins/zemax.md](../../docs/plugins/zemax.md). Example: `docs/examples/07-zemax-workflow`.
Stage 3 of the roadmap deepens this plugin.
