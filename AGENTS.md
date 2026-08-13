# AGENTS.md

Entry point for an autonomous coding agent or AI assistant working in this repository. Read
this first, in order.

## 1. Know what you're working with

- [`llms.txt`](llms.txt) — one-screen factual summary of what Sorcha is and what it implements.
- [`docs/reference/maturity-and-limitations.md`](docs/reference/maturity-and-limitations.md) —
  what's production-shaped versus demo-grade, and named limitations you need to know before you
  test anything (governance-key custody, rate limiting, replication requiring an explicit
  subscription, the shared/wipe-able `n1.sorcha.dev` sandbox). Read this before you draw
  conclusions from a live node's behaviour.
- [`STANDARDS.md`](STANDARDS.md) — the standards this platform implements, with a `full` /
  `partial` / `planned` status per row and a code path for every `full`/`partial` claim.

## 2. Set up and run it

- [`docs/quickstart.md`](docs/quickstart.md) — agent-runnable setup against a clean Docker host.
  Every prerequisite, every documented failure mode with its fix, and a verify-installation
  `curl`. Prefer this over improvising a setup sequence.
- [`README.md`](README.md) — capability overview, blueprint example, architecture diagram, and
  the one-line installer if you're not driving setup yourself.

## 3. Act on a running instance via MCP

- [`docs/mcp-server.md`](docs/mcp-server.md) — connect via the Model Context Protocol (stdio or
  Streamable HTTP), how to obtain a JWT, and the three role-scoped tool slices (admin / designer
  / participant). This is the preferred way for an agent to drive the platform rather than
  hand-rolling REST calls.
- `GET /.well-known/mcp.json` on a running gateway — the live manifest (transports,
  authentication, tool catalogue link).
- `GET /.well-known/openapi.json` on a running gateway — the full aggregated REST/gRPC surface,
  if you need to call an endpoint no MCP tool wraps yet.

## 4. If you're changing code, not just using the platform

- [`CLAUDE.md`](CLAUDE.md) — architectural conventions, critical patterns, and the DO / DON'T
  list. Read this before editing service code, even if you were spawned by a different tool.
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — branch/PR workflow, commit conventions, and how changes
  land.
- [`SECURITY.md`](SECURITY.md) — if you find something that looks like a vulnerability, report it
  through GitHub private vulnerability reporting, not a public issue.

## Ground rules

- Treat `n1.sorcha.dev` as a shared public sandbox: no real secrets, no expectation of privacy or
  durability, and it is reset periodically. See the maturity page above before relying on
  anything you observe there.
- A green test suite is not the same as live-verified behaviour on this platform — several
  documented defects were only found by executing a flow end-to-end against a real deployment,
  not by unit tests. Prefer verifying a claim by running it over trusting a comment or a doc that
  hasn't been re-checked recently.
- Don't invent standards compliance. If you're describing what Sorcha implements, cite a row in
  `STANDARDS.md` with status `full` or `partial` — a `planned` row is not yet true.
