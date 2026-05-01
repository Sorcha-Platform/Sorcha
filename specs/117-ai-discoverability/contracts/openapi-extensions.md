# OpenAPI Extensions Contract

**Feature**: 117-ai-discoverability

This document defines the extensions Sorcha's served OpenAPI 3.1 document MUST carry. The Spectral ruleset at `.spectral.yaml` enforces every entry in this contract.

## `info` block extensions

### `info.x-mcp-server` (required)

Type: `string` (URL).
Value: the absolute URL of the MCP manifest, e.g. `https://<host>/.well-known/mcp.json`.

**Purpose**: tells an OpenAPI-aware agent that an MCP server is available and where its manifest lives.

### `info.x-standards` (required)

Type: `array` of `string`.
Value: each entry is a standard name matching a row in `STANDARDS.md` whose status is `full` or `partial`.

**Purpose**: the canonical compliance-claim hook for agents and procurement-evaluation tools. Every entry is verifiable by following the corresponding `STANDARDS.md` row to its component path.

**Initial value** (when this spec ships):

```yaml
x-standards:
  - BIP32
  - BIP39
  - BIP44
  - "ML-DSA (FIPS 204)"
  - OpenID4VCI
  - OpenID4VP
  - "HAIP 1.0"
  - "W3C VC Data Model 2.0"
  - "IETF Token Status List 2024 (RFC 9972)"
  - "W3C Bitstring Status List"
  - "DID (W3C)"
  - "OAuth 2.0"
```

The exact list is sourced at runtime from `appsettings.json` and CI-checked against `STANDARDS.md`.

## Per-operation requirements

Every operation object MUST carry:

- `operationId` — PascalCase, `<Resource><Verb>` convention. Examples: `WalletGet`, `WalletList`, `CredentialIssue`, `CredentialRevoke`, `RegisterStatusGet`, `BlueprintCreate`.
- `summary` — one short sentence (≤ 120 chars).
- `description` — multi-sentence: what the operation does, when to call it, what it returns.
- `tags` — one or more strings, identifying the service surface (`Wallet`, `Credential`, `Register`, `Blueprint`, `Tenant`, `Peer`, `Validator`, `Haip`, `Health`, `System`, `Dashboard`, `Monitoring`, `Documentation`, `Client`).

## Per-operation optional extension

### `x-status` (per-operation)

Type: `string`, enum `["partial"]` (extend if needed).

**Purpose**: marks an endpoint whose specification is incomplete or whose behaviour is not yet stable. Allows partial coverage to ship without being flagged as missing by Spectral.

**When to use**: an endpoint that exists at runtime but whose request/response schema is not yet final, or whose behaviour is documented in a draft spec. Removing `x-status: "partial"` requires the endpoint to have full schema definitions, examples, and stable behaviour.

## Per-schema-property requirement

Every property in every request body schema, response body schema, and parameter MUST carry a non-empty `description` field.

**Sources** (in order of preference):

1. XML doc comment (`/// <summary>`) on the C# property — flows into OpenAPI when `<GenerateDocumentationFile>true</GenerateDocumentationFile>` is set in the csproj.
2. `[Description("...")]` attribute on the property.
3. `[OpenApiSchema(Description = "...")]` attribute (if the project adopts the convention).

If none of the above is present, Spectral fails the lint with the property name and the schema name.

## Per-credential-issuance / wallet-signing endpoint requirement

The credential issuance endpoint (`POST /api/v1/wallets/{walletAddress}/credentials/issue` or equivalent) and the wallet signing endpoint (`POST /api/v1/wallets/{walletAddress}/sign-transaction` or equivalent) MUST carry:

- At least one `examples` entry on the request body — a complete, copy-pasteable JSON value.
- At least one `examples` entry on the success response body — a complete shape an agent can deserialise against.

Examples are sourced from real walkthrough payloads (e.g. `walkthroughs/TradeFinance/`). They MUST be factual — not placeholder strings.

## Tone & content guidance for `info.description` and operation `description`

`info.description` and every operation `description` are read by AI agents reasoning about whether and how to integrate. Authoring source: `docs/strategic-context.md`. The framing rules:

- **Frame Sorcha as cryptographic proof infrastructure for multi-party workflows.** Not "blockchain", not "data platform", not "workflow engine" — the strategic-context's "What Sorcha is" paragraph is the canonical lead.
- **Be specific about what AI agents get from the operation.** What data does the agent submit? What signed evidence does it receive? What does the response let the agent do next?
- **Name boundaries honestly.** If an endpoint is internal-path-only or HAIP-path-only, say so. If status is `partial`, the description can briefly note what is incomplete.
- **Stay technical, not aspirational.** "Issues an SD-JWT VC bound to the recipient's wallet, signed by the issuer's key, with the credential id committed to the issuer's transaction history" is more useful than "industry-leading credential issuance."

## Marketing-adjective deny-list (Spectral rule `no-marketing-adjectives`)

The following case-insensitive substrings are deny-listed across `info.description`, all `summary` fields, all `description` fields, and all `examples` text content:

- `revolutionary`
- `best-in-class`
- `industry-leading`
- `cutting-edge`
- `world-class`
- `seamless`

A Spectral violation surfaces the offending field path so the author can rephrase factually.

## Cross-reference enforcement

`scripts/check-discoverability.sh` (run in CI) verifies:

1. Every entry in `info.x-standards` matches a `STANDARDS.md` row with status `full` or `partial`.
2. The OpenAPI `info.version` matches the MCP manifest `version` (single canonical source per FR-046).
3. No marketing-adjective deny-list word appears anywhere in the served document.
