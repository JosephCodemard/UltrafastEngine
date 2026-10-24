# Plugins

The core engine is complete on its own. Capabilities that not everyone needs, or that depend on outside software,
ship as **plugins**: installed separately, loaded through public interfaces, never required by the core.

| plugin | what it adds | page |
|---|---|---|
| `fitting` | `transform` elements (fittable physical changes to the beam), the `"fit"` run mode against the instruments' recorded data (replaceable estimators, priors, three result layers, influence map, fitted system for new inputs), the `"backward"` run mode (propagate a measured field back to the input), the `fit` figure. Recorded data itself (`"measured"`) is core: [Measured data](../reference/measured-data.md) | [Fitting](fitting.md) |
| `zemax` | `ultrafast zemax-build`, `zemax-replay`, `zemax-demo`; the OpticStudio extension program (Windows) | [Zemax](zemax.md) |

Both ship with the engine and are installed in the CLI's and the Python package's `plugins/` folders by every build.
How to install plugins elsewhere, check them and fix loading problems: [Installing plugins](installing.md). How
plugins are written: [Plugin system](../reference/plugins.md).
