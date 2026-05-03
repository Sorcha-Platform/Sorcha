# Quickstart: Verifying AI Discoverability Locally

**Feature**: 117-ai-discoverability

## Prerequisites

- Spec 117 implementation merged (or, during development, the `117-ai-discoverability` branch checked out).
- `node` (for `swagger-cli` and `@stoplight/spectral-cli`) — only needed if running the lint chain locally.
- `docker` and `docker compose` available.
- The platform booted: `docker compose up -d` from the repo root.

## 1. Verify the OpenAPI well-known endpoint

### Steps

```bash
curl -sS http://localhost/.well-known/openapi.json | jq '.info'
```

### Expected

```json
{
  "title": "Sorcha API",
  "version": "<assembly version>",
  "description": "...",
  "contact": { "url": "https://github.com/Sorcha-Platform/Sorcha" },
  "x-mcp-server": "http://localhost/.well-known/mcp.json",
  "x-standards": ["BIP32", "BIP39", "BIP44", "ML-DSA (FIPS 204)", ...]
}
```

```bash
curl -sS http://localhost/.well-known/openapi.yaml | head -20
```

### Expected

A YAML representation of the same document, beginning with `openapi: "3.1.0"` and `info:`.

### Lint

```bash
npx swagger-cli validate http://localhost/.well-known/openapi.json
npx @stoplight/spectral-cli lint http://localhost/.well-known/openapi.json --ruleset .spectral.yaml
```

Both commands MUST exit 0 with no errors.

## 2. Verify the MCP manifest

### Steps

```bash
curl -sS http://localhost/.well-known/mcp.json | jq '.'
```

### Expected

A JSON document with `name: "sorcha-mcp"`, `version` matching the OpenAPI `info.version`, `transports` array containing both `stdio` and `http+sse`, `authentication.type: "jwt-bearer"`, `tool_categories` keyed by `admin` / `designer` / `participant` with counts 13 / 13 / 10, and `tool_catalogue_url` resolving to `http://localhost/api/mcp/tools`.

### Schema validation

```bash
npx ajv-cli validate \
  -s specs/117-ai-discoverability/contracts/mcp-manifest.schema.json \
  -d <(curl -sS http://localhost/.well-known/mcp.json)
```

MUST exit 0.

## 3. Verify the tool catalogue

### Steps

```bash
curl -sS http://localhost/api/mcp/tools | jq '. | length'
```

### Expected

`36` (13 admin + 13 designer + 10 participant).

```bash
curl -sS http://localhost/api/mcp/tools | jq '.[] | select(.description | length < 80)'
```

### Expected

Empty output — no tool has a description shorter than 80 characters (proxy for the 2-sentence rule).

## 4. Verify `llms.txt` and `STANDARDS.md`

### Steps

```bash
wc -c llms.txt                    # MUST be ≤ 8192
head -5 llms.txt                  # MUST begin with "# Sorcha" then a blockquote
test -f STANDARDS.md && echo "OK"
```

### Cross-reference check

```bash
./scripts/check-discoverability.sh
```

MUST exit 0.

## 5. Run the full discoverability gate

### Steps

```bash
./scripts/check-discoverability.sh
```

This runs (in order):
1. `swagger-cli validate` against `/.well-known/openapi.json`
2. `spectral lint` with the project ruleset
3. JSON-schema validation of `/.well-known/mcp.json`
4. Tool description audit (in-process unit test)
5. `llms.txt` structure check
6. `STANDARDS.md` parse + path-resolution check
7. Standards cross-reference (every standard named in `llms.txt`, frontmatter, OpenAPI `x-standards` matches a `STANDARDS.md` row)
8. Marketing-adjective deny-list scan

### Expected

Single-line `[discoverability] OK — all checks passed` on success. Single-line failure messages naming the offending file and reason on failure.

## 6. Verify the quickstart end-to-end (US5)

### Steps

```bash
# Fresh clone scenario
git clone https://github.com/Sorcha-Platform/Sorcha.git /tmp/sorcha-fresh
cd /tmp/sorcha-fresh
./scripts/sorcha-setup.sh
curl -sS http://localhost/api/health | jq '.status'
```

### Expected

`./scripts/sorcha-setup.sh` exits 0. Final health response is `"healthy"` or `"degraded"` (both 200). On any prerequisite failure, the script exits non-zero with a single-line `[sorcha-setup] missing prerequisite: <name> (≥ <version>); install via <link>` message.

## 7. End-to-end agent test (manual, captured in verification log)

### Steps

1. Boot the platform locally.
2. Start an MCP-aware test agent (e.g. `mcp-cli` or an Anthropic API harness) pointed at `http://localhost/.well-known/mcp.json`.
3. Authenticate with a JWT acquired via `scripts/get-jwt-token.sh`.
4. Drive `walkthroughs/TradeFinance/` end-to-end using only MCP tool calls.

### Expected

The walkthrough completes successfully. The agent's transcript is captured in `docs/mcp-server.md` as the worked example (T040).

## Sign-off criteria

- [ ] All spec 117 acceptance scenarios pass automated checks
- [ ] `swagger-cli validate` and `spectral lint` exit 0 against the served OpenAPI document
- [ ] `/.well-known/mcp.json` validates against `mcp-manifest.schema.json`
- [ ] All 36 MCP tools have descriptions ≥ 2 sentences with disambiguating phrases
- [ ] `llms.txt` is present, ≤ 8 KB, structurally valid
- [ ] `STANDARDS.md` is present and parseable, every `Components` cell resolves to real paths
- [ ] Standards cross-reference passes — every standard named anywhere matches a `STANDARDS.md` row
- [ ] Marketing-adjective deny-list returns no hits
- [ ] `scripts/sorcha-setup.sh` runs clean on a fresh `ubuntu-latest` runner
- [ ] `docker-compose.yml` carries a topology comment block in the first 30 lines
- [ ] All four published `docs/` documents have valid YAML frontmatter
- [ ] An MCP-aware agent drives the TradeFinance walkthrough end-to-end
- [ ] The `ai-discoverability-check.yml` workflow is required in master branch protection
