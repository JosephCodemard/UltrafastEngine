"""Local MCP server for agents (``ultrafast-mcp``): workspace-sandboxed, stdio only, no dependencies. See :mod:`ultrafast.mcp.server` and docs/agents.md."""
from __future__ import annotations

from .server import McpServer, main  # noqa: F401

__all__ = ["McpServer", "main"]
