# twin plugin

A digital twin of a lab: the project layout, the multi-condition fit (the [fitting plugin](../fitting/README.md)), validation with
held-out conditions and credible bands, a measurement planner, a what-if explorer and drift tracking. The `ultrafast twin` command
(C#, `src/`) starts the Python module `python/ultrafast_twin`.

Documentation: [docs/plugins/twin.md](../../docs/plugins/twin.md). Tests: `python/tests/test_twin.py`. `ultrafast twin serve <folder>` starts the core MCP server (`ultrafast-mcp`, see [agents](../../docs/agents.md#mcp-server)) with the project folder as its workspace; its twin tools are `twin_init`, `twin_fit`, `twin_validate`, `twin_predict`, `twin_design` and `twin_track`.
