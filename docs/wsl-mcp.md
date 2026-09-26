# Windows ONI with a WSL MCP client

Run ONI through **Windows Steam**. A WSL client can stay in WSL: use the local
stdio bridge instead of assuming WSL `localhost` reaches Windows loopback.
The OniMcp default endpoint is `http://localhost:8788/mcp/`.

From the repository in WSL:

```sh
python3 scripts/oni_mcp_bridge.py --check
```

This performs initialize, initialized, and tools/list through one persistent
Windows PowerShell worker, reports the backend, endpoint, server version, and
available tools, then exits. It does not start ONI or alter the save. Python 3
and Windows PowerShell interop are required; no Python packages are needed.

For Codex, use the project's `.codex/config.toml` and set `cwd` to your clone:

```toml
[mcp_servers.oni]
command = "python3"
args = ["scripts/oni_mcp_bridge.py"]
cwd = "/absolute/path/to/OniMods"
startup_timeout_sec = 30
env_vars = ["ONI_MCP_TOKEN"]
```

Keep existing tool policies below this block. Remove the old `url` field from
this server entry: a stdio server uses `command`/`args`. Reload the Codex session
or extension after changing its MCP configuration. Codex supports stdio servers
and project configuration as described in the [official MCP documentation](https://developers.openai.com/codex/mcp).
If another profile/global entry overrides `oni`, inspect `codex mcp get oni` and
correct the active configuration; do not run two competing entries with that name.

For other clients, register the same command, arguments and working directory
using their stdio configuration format. Native Windows clients can also connect
directly by HTTP to port 8788; `.mcp.json` is the direct-HTTP example.

The bridge auto-selects Windows interop in WSL and direct loopback elsewhere.
`--backend windows|direct` and `--url http://localhost:<port>/mcp/` are available
for diagnosis/custom ports. It forwards UTF-8, session IDs and negotiated MCP
protocol headers. If the server requires authentication, provide
`ONI_MCP_TOKEN` in the client's environment; never put it in command arguments
or a URL. It never widens the listener, changes the firewall, follows redirects,
uses an ambient HTTP proxy, or retries a potentially applied game command.

This bridge supports sequential MCP request/response calls, including JSON and
POST SSE responses. It does not support independent server push, subscriptions,
sampling, or elicitation, and removes those advertised capabilities during
initialization. ONI's normal tools remain available.

Troubleshooting:

- Connection failure: start ONI through Steam and ensure OniMcp is enabled and
  its configured port matches. Test the bridge once; avoid repeated WSL HTTP
  probes when Windows loopback is the actual host.
- `UtilBindVsockAnyPort` or an exited worker: Windows interop is unavailable to
  that execution environment (for example, a restricted WSL sandbox). Run the
  MCP client in an environment that permits Windows interop, or on Windows.
- Game restart: required only after deploying a new mod DLL. A client config
  change needs a client reload. Keep ONI paused during connection diagnosis.
- HTTP/session failure after restarting ONI: reconnect the client; the bridge
  intentionally does not replay the failed request.

Host regressions: `python3 -m unittest discover -s scripts -p test_oni_mcp_bridge.py`.
