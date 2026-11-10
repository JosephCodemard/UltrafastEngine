# DigitalTwin

A digital twin is a simulation that has been fitted to a real system *and knows how far it can be trusted*. These examples use the `fitting` and
`twin` plugins on a synthetic lab with a hidden truth, so every claim can be checked against the answer.

| example | the story |
|---|---|
| [transforms](transforms/README.md) | the original fitting example: a transform (residual dispersion, gain narrowing, post-pulse, ASE pedestal) fitted to a spectrum, d-scan, cross-correlator and power meter; the fitted line on a new seed |
| [build-the-twin](build-the-twin/README.md) | fit spectra at five energies with one set of shared parameters, recover the hidden values, predict two held-out energies with credible bands; a validation report that catches a twin that only fits its training data |
| [explore-the-twin](explore-the-twin/README.md) | what-if comparisons with bands, the most informative next measurement, where to put the spectrometer, drift tracking with an ensemble Kalman filter |

Run: `python docs/examples/make_all.py --group DigitalTwin`. Plugin pages: [fitting](../../plugins/fitting.md), [twin](../../plugins/twin.md).

**Not here yet:** a comparison of a spline and a network transform (the network transform exists and is tested in the fitting plugin), a
wavefront twin (needs a spatial project template that does not exist yet), and agent-driven design through an MCP server (the MCP
server is not built; the design is in [the S4 notes](../../design/notes/s4-twin.md)).
