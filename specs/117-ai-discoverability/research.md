# Phase 0 Research: AI Discoverability & Machine-Readable Marketing

**Feature**: 117-ai-discoverability
**Date**: 2026-05-02

## Research items

1. The .NET 10 OpenAPI generation pipeline — what it produces by default, how to extend it, and where the existing Sorcha wiring sits
2. The `/.well-known/` URI namespace — naming convention and precedent for `openapi.json` and `mcp.json`
3. OpenAPI 3.1 `info` extensions (`x-mcp-server`, `x-standards`, `x-status`) — convention compliance and tool support
4. The `llms.txt` standard — current shape, size limits, structure rules
5. MCP manifest discovery shape — what an MCP-aware agent actually consumes, and which fields are required vs aspirational
6. Spectral lint rule authoring — what custom rules the project needs and how to ship them
7. The 36 MCP tool descriptions — current state and the disambiguation-clue heuristic
8. Source narratives for US6 — what exists in the planning folder and what needs writing
9. CI workflow shape — single workflow vs split, runner constraints, failure surfacing on PRs
10. Marketing-adjective deny-list — what list to ship and how to enforce it
11. Strategic-context-driven content authoring — what voice and framing to use across machine-readable artefacts

---

## R1. The .NET 10 OpenAPI generation pipeline

### Reference

- `Microsoft.AspNetCore.OpenApi` (built into .NET 10).
- Existing wiring in `src/Services/Sorcha.ApiGateway/Program.cs:71` via `builder.AddSorchaOpenApi(...)` (a shared extension under `src/Common/Sorcha.ServiceDefaults/`).
- `app.MapOpenApi()` mounted at line 529, default route `/openapi/v1.json`.
- An aggregator endpoint at `/openapi/aggregated.json` already exists, plus Scalar UI at `/openapi`.

### Decision

Re-use the existing OpenAPI generation pipeline. Add an `OpenApiDocumentTransformer` (the .NET 10 extension hook) that injects `info.x-mcp-server`, `info.x-standards`, and the canonical `info.version`. Mount two new well-known routes (`/.well-known/openapi.json` and `/.well-known/openapi.yaml`) that proxy the same generated document with appropriate content types and `Cache-Control` headers.

**Rationale**. The .NET 10 built-in is mandated by Constitution III. The existing wiring is correct; the gap is metadata completeness, not infrastructure. A document transformer is the lowest-risk extension point — it runs once per document fetch, has no side effects, and is the documented mechanism for `info`-block extensions.

**Alternatives considered**.
- Hand-maintain a static OpenAPI YAML file. Rejected — would drift immediately and contradict FR-003.
- Use Swashbuckle. Rejected — Constitution III explicitly disallows.
- Generate the document at build time and check in. Rejected — `info.version` must reflect runtime, and a build-time artefact would either be stale or require a bespoke release pipeline.

---

## R2. The `/.well-known/` URI namespace

### Reference

- [RFC 8615](https://datatracker.ietf.org/doc/html/rfc8615) — Well-Known URIs — defines the `/.well-known/` namespace and the IANA registry for entries.
- Established precedents: `/.well-known/openid-configuration`, `/.well-known/jwks.json`, `/.well-known/security.txt`.

### Decision

Use `/.well-known/openapi.json` and `/.well-known/openapi.yaml` for the OpenAPI surface. Use `/.well-known/mcp.json` for the MCP manifest.

**Rationale**. Neither is yet IANA-registered, but both are emerging conventions: the OpenAPI Initiative has discussed `openapi.json` at `/.well-known/` since 2023, and several agent frameworks (Anthropic MCP, OpenAI tools) have started serving manifests under the well-known namespace. Squatting on the names early aligns Sorcha with the convention that is forming.

**Alternatives considered**.
- `/api/openapi.json`. Rejected — not where agents look first.
- `/.well-known/api.json`. Rejected — too generic, conflicts with potential future API-discovery standards.
- Submit IANA registrations. Out of scope per spec; tracked as future work.

**Cache-Control**. Both endpoints set `Cache-Control: public, max-age=300` per NFR-006. This is a 5-minute cache window — short enough that platform changes propagate within a release cycle, long enough that downstream caches and agents do not pound the gateway.

---

## R3. OpenAPI 3.1 `info` extensions

### Reference

- OpenAPI 3.1 specification §4.7.2 — extensions begin with `x-` and may appear in any object.
- HAIP's `well-known/openid-credential-issuer` precedent for `info`-level metadata extensions.

### Decision

Three `info`-block extensions:

- `info.x-mcp-server`: string URL pointing at `/.well-known/mcp.json` — tells an OpenAPI-aware agent that an MCP manifest is available.
- `info.x-standards`: array of strings, each matching a `STANDARDS.md` row name with status `full` or `partial`. Cross-referenced in CI.
- `info.x-status` (per-endpoint, on `operation` objects): `"partial"` for endpoints whose specification is incomplete or whose behaviour is not yet stable. Allows CI lint to permit partial coverage without flagging it as missing.

**Rationale**. `x-mcp-server` is the canonical "I have an MCP server, here is its manifest" signal. `x-standards` is the canonical compliance-claim hook agents will scrape. `x-status` lets us be honest about partial work without omitting the endpoint.

**Tool support**. Both `swagger-cli validate` and `spectral lint` accept `x-` extensions without complaint (they are part of OAS by design). Spectral can enforce custom rules over them — the project's `.spectral.yaml` will require `info.x-mcp-server` and `info.x-standards` to be present.

**Alternatives considered**.
- Use a custom `info.contact` field for the MCP server. Rejected — `contact` is for humans, not machines.
- Embed the MCP manifest contents directly in the OpenAPI doc. Rejected — duplicates a separately-discoverable artefact.

---

## R4. The `llms.txt` standard

### Reference

- [llmstxt.org](https://llmstxt.org) — the emerging convention. Defines a Markdown-shaped plain-text file at the repo root (or web root) describing a project for LLM consumption.

### Decision

Follow the [llmstxt.org](https://llmstxt.org) structure verbatim:

```
# Sorcha
> <one-paragraph factual summary>
## Capabilities
- <one-line per capability>
## Standards
- <name>: <stable spec URL>
## Links
- <named link>: <URL>
```

Size cap: 8 KB plain text. Longer-form companion `docs/llms-full.txt` cap: 32 KB.

**Rationale**. Following the convention exactly maximises tool compatibility — emerging LLM crawlers expect this shape. The 8 KB cap matches the upper end of what fits comfortably in a single LLM context-window read. The 32 KB cap on `llms-full.txt` accommodates richer guidance without bloating the canonical entry-point.

**Cross-reference**. Every standard named in `llms.txt` MUST appear as a `full` or `partial` row in `STANDARDS.md`. CI-enforced.

**Alternatives considered**.
- `humans.txt`. Rejected — different audience, different convention.
- `ai.txt`. Rejected — not a real standard, would not be crawled.
- Embed in `README.md`. Rejected — `llms.txt` is the convention LLM crawlers look for first.

---

## R5. MCP manifest discovery shape

### Reference

- The Model Context Protocol specification at [modelcontextprotocol.io](https://modelcontextprotocol.io) does not yet define an HTTP discovery manifest. The convention emerging in Anthropic's reference implementations is a manifest at `/.well-known/mcp.json` describing how to connect.
- Existing Sorcha MCP server at `src/Apps/Sorcha.McpServer/` already supports stdio and HTTP+SSE transports.

### Decision

Ship a manifest with the following minimum fields (all required):

```json
{
  "$schema": "<URL of mcp-manifest.schema.json>",
  "name": "sorcha-mcp",
  "version": "<canonical version, same as info.version>",
  "description": "Sorcha MCP server — drives credential issuance, blueprint design, and participant workflows for the Sorcha platform.",
  "transports": [
    { "type": "stdio", "command": "...", "args": ["..."] },
    { "type": "http+sse", "url": "<base URL>/mcp/sse" }
  ],
  "authentication": {
    "type": "jwt-bearer",
    "issuer": "<JWT issuer URL>",
    "audience": "sorcha-mcp",
    "acquisition_url": "<repo URL>/blob/master/scripts/get-jwt-token.sh"
  },
  "tool_categories": {
    "admin": { "count": 13, "description": "..." },
    "designer": { "count": 13, "description": "..." },
    "participant": { "count": 10, "description": "..." }
  },
  "tool_catalogue_url": "<base URL>/api/mcp/tools",
  "documentation_url": "<repo URL>/blob/master/docs/mcp-server.md"
}
```

**Rationale**. The fields above are what an MCP-aware agent actually needs to connect: which transports, which auth scheme, where to discover tools, where to read docs. Avoiding any aspirational fields (e.g. predicted tool intent embeddings) — those are not implemented today and including them in the manifest would lie.

**Alternatives considered**.
- Embed the full tool list inline. Rejected — the tool list grows; a separate endpoint with `Cache-Control` is cleaner. The manifest links to it via `tool_catalogue_url`.
- Omit `transports` and assume HTTP+SSE. Rejected — Sorcha's MCP server supports both stdio and HTTP+SSE, and stdio is the default for local agents.

---

## R6. Spectral lint rule authoring

### Reference

- [@stoplight/spectral-cli](https://github.com/stoplightio/spectral) — OSS, widely adopted, the canonical OpenAPI lint tool.

### Decision

Ship a `.spectral.yaml` at the repo root extending `spectral:oas` (the built-in OpenAPI 3.x ruleset) plus six custom rules:

- `operationId-pascalcase` — every `operationId` matches `^[A-Z][a-zA-Z0-9]+$`.
- `description-required-on-properties` — every schema property has a non-empty `description`.
- `examples-required-on-credential-issuance` — credential issuance and wallet signing endpoints have at least one example each.
- `info-x-mcp-server-required` — `info.x-mcp-server` is present and matches a URL pattern.
- `info-x-standards-required` — `info.x-standards` is a non-empty array of strings.
- `no-marketing-adjectives` — substring deny-list (case-insensitive) applied across `summary`, `description`, and `info.description`. Words: `revolutionary`, `best-in-class`, `industry-leading`, `cutting-edge`, `world-class`, `seamless`.

**Rationale**. Spectral is OSS, the canonical OpenAPI linter, and runs cleanly in GitHub Actions. The six custom rules collapse the spec's quality signal into mechanical checks.

**Alternatives considered**.
- Vacuum or built-in `swagger-cli validate` only. Rejected — `swagger-cli` checks structure, not the project-specific quality bars.
- Custom Roslyn analyzer running at compile time. Rejected — runs against C# attributes, not the served document; would miss runtime drift.

---

## R7. The 36 MCP tool descriptions — current state and audit heuristic

### Reference

- 13 Admin tools at `src/Apps/Sorcha.McpServer/Tools/Admin/`
- 13 Designer tools at `src/Apps/Sorcha.McpServer/Tools/Designer/`
- 10 Participant tools at `src/Apps/Sorcha.McpServer/Tools/Participant/`
- All decorated with `[Description("...")]` (or equivalent) attribute consumed by the MCP server SDK.

### Decision

Audit (T008) inventories the current state. The standard for a passing description is:

1. At least 2 sentences (heuristic: ≥ 1 period + ≥ 1 word after it, or ≥ 2 lines of non-trivial content).
2. Names at least one disambiguating situation. The unit test (T032) checks for one of these substrings (case-insensitive): `"call this when"`, `"use when"`, `"prefer this when"`, `"not when"`, `"versus"`, `"instead of"`, `"as opposed to"`. The set is configurable in the test; the audit may surface additional acceptable phrases.

**Rationale**. The two-sentence rule prevents one-liners that an agent cannot disambiguate against neighbours. The disambiguator-substring heuristic is mechanical (passes/fails in CI) and cheap to satisfy.

**Alternatives considered**.
- LLM-judged description quality. Rejected — non-deterministic, expensive in CI.
- Word-count threshold only. Rejected — long descriptions can still be uninformative.

---

## R8. Source narratives for US6

### Reference

The brief names four planning-folder narratives:
- `sorcha-architecture-narrative.md` — "Five Layers of Open"
- `sorcha-openid4vc-mdl-integration.md` — HAIP integration story
- `sorcha-applicability.md` — DPP, trade finance, IPC-1782, municipal
- `sorcha-architecture-evaluation.md` — security-model source material

### Decision

T009 inventories these narratives during phase 2. If any are missing or unsuitable, the implementation phase surfaces the gap before T060–T063 begin and either finds an alternative source or down-scopes the document. The four documents must ship; their *exact* source content can be re-elected per narrative.

**Rationale**. The narratives are the source of truth for content quality. Re-writing from scratch would diverge from existing internal positioning. If a narrative is missing, recompose from existing repo content (READMEs, specs, skills) before resorting to fresh writing.

---

## R9. CI workflow shape

### Reference

- GitHub Actions, `ubuntu-latest` runners. Existing workflows under `.github/workflows/`.
- Branch protection on master.

### Decision

A single workflow `ai-discoverability-check.yml` triggered on `pull_request` to master. Steps:

1. Checkout.
2. Boot the gateway in the runner via `docker compose up -d`.
3. Wait for `http://localhost/api/health` to return 200.
4. Run `scripts/check-discoverability.sh` which orchestrates: spectral lint, swagger-cli validate, MCP manifest schema validation, llms.txt structure check, STANDARDS.md parse, cross-reference checks, marketing-adjective deny-list.
5. On failure, post a comment on the PR with the failing check and a link to the workflow run.

A separate nightly cron-triggered workflow runs the quickstart on a clean `ubuntu-latest` runner (T052) — independent of the main lint workflow because it is slower and does not gate PRs.

**Rationale**. Single PR-gating workflow keeps the failure signal in one place and makes branch protection setup trivial. The nightly quickstart is an independent observability check, not a merge gate.

**Alternatives considered**.
- Per-artefact workflows (one for OpenAPI, one for MCP, one for STANDARDS.md). Rejected — multiplies the failure signal across PRs and makes branch protection harder to configure.
- Run lint on push to all branches. Rejected — too noisy; PR is the right gate.

---

## R10. Marketing-adjective deny-list

### Reference

Empirical observation across vendor copy. The bar is "if a human reading would notice it as marketing", reject.

### Decision

Initial deny-list (case-insensitive substring match):

- `revolutionary`
- `best-in-class`
- `industry-leading`
- `cutting-edge`
- `world-class`
- `seamless`

Applied to: `llms.txt`, `docs/llms-full.txt`, `STANDARDS.md`, served `/.well-known/openapi.json`, served `/.well-known/mcp.json`. **Not** applied to user-facing UI text or marketing-site content (out of scope; this spec governs machine-readable artefacts only).

**Rationale**. Six words is small enough to maintain manually and broad enough to catch the obvious cases. The list will grow if PRs surface other patterns; tracked as a maintenance concern.

**Alternatives considered**.
- Use an off-the-shelf "weasel words" list. Rejected — over-broad, would flag legitimate technical phrasing.
- Train a sentiment classifier. Rejected — non-deterministic, overkill.

---

---

## R11. Strategic-context-driven content authoring

### Reference

- `docs/strategic-context.md` — strategic and market context that is not derivable from the codebase. Authored by the platform team as the canonical voice and framing source for externally-facing content.

### Decision

Every authoring task in `tasks.md` that produces externally-visible machine-readable content cites `docs/strategic-context.md` as the source of voice, framing, and tone. Specifically:

- **OpenAPI `info.description`** (T017) — frames Sorcha as cryptographic proof infrastructure for multi-party workflows. One paragraph, ≤ 1000 characters.
- **`llms.txt`** (T044) — blockquote summary uses the strategic-context "What Sorcha is" frame.
- **`docs/llms-full.txt`** (T045) — opens with the strategic frame (problem, AI fraud + AI decision-maker context, regulatory pull) before diving into specifics.
- **MCP tool descriptions** (T039, all 36 tools) — describe what the tool *does* and *when an AI agent should call it*, per strategic-context's "How to Describe Sorcha to an AI Audience" section.
- **`STANDARDS.md` introduction** (T049) — names what is core (ML-DSA, ML-KEM, BIP32/39/44, JSON Pointer selective disclosure, Merkle dockets) and the honest gaps (HAIP classical-only at boundary, SLH-DSA not implemented, BBS+ not implemented).
- **Four `docs/` published documents** (T060-T063) — each leans on a specific strategic-context section: architecture.md ↔ "Architecture in One Paragraph", openid4vc-haip-integration.md ↔ "Sorcha is the workflow layer above GOV.UK Wallet / EUDIW", applicability.md ↔ "Target Markets and Regulatory Pull", security-model.md ↔ "Cryptographic Posture" + honest gaps.

**Rationale**. The technical research above resolved *what shape* the artefacts take. Strategic-context resolves *what to say* in them. Without explicit reference, content authors default to README-style language that reads as marketing to an AI evaluator — or worse, fabricate positioning that contradicts the platform team's intended frame. Citing the source per-task makes drift visible at PR review.

**Consequence**. Authors MUST read `docs/strategic-context.md` before authoring any of the artefacts above. CI cannot enforce voice; reviewers do.

**Alternatives considered**.
- Inline the strategic-context guidance into each task. Rejected — duplicates content that will drift, and dilutes the central reference.
- Treat strategic-context as a `STANDARDS.md`-equivalent CI artefact. Rejected — it is a voice document, not a verifiable claim. CI checking voice is brittle.

---

## Summary

All NEEDS CLARIFICATION items resolved. No design blockers. Phase 1 may proceed. `docs/strategic-context.md` is the authoring source for all machine-readable content; every relevant task cites it.
