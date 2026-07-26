# Publishing Sorcha to the MCP Registry

Sorcha ships a 36-tool MCP server (`sorchadev/mcp-server`). Listing it in the public
[MCP Registry](https://registry.modelcontextprotocol.io) makes it directly discoverable and
installable by MCP-aware AI agents (Claude Desktop, Cursor, Cline, …) — the highest-leverage
"promote to other AIs" channel.

`server.json` (repo root) is the registry manifest. It is a **draft** — complete the steps below
to publish.

## Prerequisites
- The `sorchadev/mcp-server:latest` OCI image is published to Docker Hub (it is — `docker-publish` workflow).
- A verified registry **namespace**. Sorcha uses the GitHub namespace `io.github.sorcha-platform`,
  verified automatically via GitHub OIDC when publishing from a GitHub Action in this repo.
- Pin a real `version` in `server.json` (replace `0.1.0`) per release.

## One-off / manual publish
```bash
# 1. Install the publisher CLI
#    https://github.com/modelcontextprotocol/registry/releases  (or: brew install mcp-publisher)
# 2. Authenticate the GitHub namespace (opens browser / device flow)
mcp-publisher login github
# 3. Validate + publish the manifest
mcp-publisher publish        # reads ./server.json
```

## CI publish (recommended)
Add a workflow (e.g. `.github/workflows/mcp-registry-publish.yml`) triggered on release tags that
uses GitHub OIDC (no stored secret) to authenticate the `io.github.sorcha-platform` namespace and
runs `mcp-publisher publish`. Gate it on the `docker-publish` job so the OCI image referenced by
`server.json` exists first.

## Other directories (manual submission, also scraped by assistants)
- **Smithery** — https://smithery.ai (submit the GitHub repo)
- **Glama** — https://glama.ai/mcp/servers
- **PulseMCP** — https://www.pulsemcp.com
- **mcp.so** — https://mcp.so

## Notes / follow-ups
- ~~Placeholder auth metadata in the manifest~~ **Done** (#826 / spec 136): the manifest now derives
  the real per-installation issuer + platform-tier audience from `JwtSettings:InstallationName`
  (verified live on n1: `urn:sorcha:n1.sorcha.dev` / `n1.sorcha.dev:platform`). An explicit
  `Discoverability:AuthIssuer`/`AuthAudience` still overrides.
- Since the 2026-07-26 audit the manifest also always advertises the Streamable HTTP transport,
  deriving `{origin}/mcp` when `McpManifest:HttpSseUrl` is unset — previously the live endpoint was
  omitted from the manifest entirely, and the advertised stdio command
  (`dotnet run --project …`) assumes a repo checkout the registry audience won't have.
- If a hosted streamable-HTTP MCP endpoint is exposed, add a `remotes` entry to `server.json`
  alongside the `packages` (OCI) entry so agents can connect without self-hosting.
