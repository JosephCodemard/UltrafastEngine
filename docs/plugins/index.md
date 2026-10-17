# Plugins

The core engine is complete on its own. Capabilities that not everyone needs, or that depend on outside software,
ship as **plugins**: installed separately, loaded through public interfaces, never required by the core.

| plugin | what it adds | page |
|---|---|---|
| `fitting` | `"measured"` on any instrument (compare with CSV / NPZ / JSON recordings), the `"fit"` run mode (calibration with error bars and predictions), the `measurements` and `fit` figures | [Fitting](fitting.md) |
| `zemax` | `ultrafast zemax-build`, `zemax-replay`, `zemax-demo`; the OpticStudio extension program (Windows) | [Zemax](zemax.md) |

Both ship with the engine and are installed in the CLI's and the Python package's `plugins/` folders. How plugins
are found, installed and written: [Plugin system](../reference/plugins.md).
