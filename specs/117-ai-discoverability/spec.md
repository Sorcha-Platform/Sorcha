# Feature Specification: AI Discoverability & Machine-Readable Marketing

**Feature Branch**: `117-ai-discoverability`
**Created**: 2026-05-02
**Status**: Draft
**Input**: User description: "Make Sorcha discoverable, parseable, and integrable by AI agents — OpenAPI 3.1 surface, MCP manifest, llms.txt, STANDARDS.md, hardened quickstart, and selected technical docs published from planning to repo."

## Context

AI agents and AI coding assistants are an emerging class of consumer for any platform claiming to be "open" or "integrable." When such an agent encounters Sorcha today it finds a good `README.md` and then hits a wall: there is no machine-readable OpenAPI specification served at a stable URL, no MCP manifest under `/.well-known/`, no `llms.txt`, no `STANDARDS.md`, and the quickstart is human-paced rather than agent-runnable.

The platform already has the substance — a 36-tool MCP server, real OpenID4VCI/OpenID4VP issuance and verification (specs 097/098), HAIP 1.0 conformance work (spec 094 onwards), post-quantum cryptography internals, IETF Token Status List 2024 publishing (spec 095), and a clean service decomposition behind a YARP gateway. None of that is presented in a shape an agent can crawl, parse, or reason about without human mediation.

This spec ships the missing surface. It is not a feature in the product sense — it is documentation, OpenAPI metadata, and a small number of well-known endpoints. The cost is mostly carefulness: machine-readable artefacts must be accurate or they are worse than absent. The benefit is that an AI agent (whether an LLM-backed coding assistant, a procurement-evaluation tool, or a runtime workflow orchestrator) can answer "can I integrate Sorcha?" without a human in the loop.

**Numbering note.** This spec is numbered 117 rather than 116 because Feature 116 is already in flight (Account Linking & Auth-Method Management, foundation + US1/US2/US4 on master, US3 in `.worktrees/116-us3-password-lifecycle`). The originating prompt named this feature 116; that number is unavailable.

**Related specs.**
- **Builds on** specs 095 / 096 / 097 / 098 — those specs added real OpenID4VC, HAIP, X.509 trust, and IETF Token Status List behaviour. This spec ensures that behaviour is *findable*. Spec 095's IETF endpoint, spec 097's issuer metadata endpoint, and spec 098's verifier metadata endpoint are all things an external agent should be able to discover via the assets this spec ships.
- **Builds on** spec 099 (System Register Genesis) — the trust-anchor model is one of the platform claims STANDARDS.md must describe accurately.
- **Builds on** spec 113 (Storage Durability Audit) — the storage-provider posture is one of the production-readiness claims STANDARDS.md will surface.
- **Independent of** spec 116 (Account Linking) — the two share neither code paths nor artefacts and can ship in any order.
- **Required by** any future Sorcha AI-agent integration showcase or marketplace listing — a marketplace will scrape `llms.txt` and `STANDARDS.md` first.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An AI agent discovers, parses, and reasons over the Sorcha API surface (Priority: P1)

A developer using an AI coding assistant types "integrate my app with Sorcha credential issuance." The assistant has no prior knowledge of Sorcha. It fetches `https://<sorcha-host>/.well-known/openapi.json`, receives a valid OpenAPI 3.1 document, parses it, identifies the `IssueCredential` operation by `operationId`, reads the `description` to confirm the operation matches the user's intent, follows the linked schema to understand the request body shape, and emits an integration sketch for the user.

After this spec ships, this flow works against any running Sorcha instance. The OpenAPI document is auto-generated from ASP.NET Core endpoint metadata using `Microsoft.AspNetCore.OpenApi`, served by the API Gateway, complete enough that no field is unannotated, and accurate enough that `swagger-cli validate` and `spectral lint` both pass with zero errors.

**Why this priority**: An OpenAPI-discoverable platform is the table stakes for AI integration in 2026. Without it, every other artefact in this spec is decoration on a closed surface. P1 because the rest of US2-US6 cross-link to URLs that this US makes real.

**Independent Test**: From a fresh container with no Sorcha-specific knowledge, run `curl -s http://localhost/.well-known/openapi.json | swagger-cli validate -` and confirm zero errors. Run `spectral lint http://localhost/.well-known/openapi.json` and confirm zero violations beyond a curated allowlist of style preferences. Pipe the same document into an off-the-shelf OpenAPI-aware code generator (`openapi-typescript`, `openapi-generator-cli`) and confirm a usable typed client is produced.

**Acceptance Scenarios**:

1. **Given** a running Sorcha API Gateway, **When** an AI agent issues `GET /.well-known/openapi.json`, **Then** the response is a valid OpenAPI 3.1 JSON document with `Content-Type: application/json` and the `info.version` field equal to the running platform version.
2. **Given** the same gateway, **When** the agent issues `GET /.well-known/openapi.yaml`, **Then** the response is a YAML representation of the same document with `Content-Type: application/yaml`.
3. **Given** the served OpenAPI document, **When** every endpoint definition is inspected, **Then** each one has non-empty `operationId` (PascalCase, `<Resource><Verb>` convention), `summary`, `description`, and `tags` fields.
4. **Given** the served OpenAPI document, **When** every request and response schema is inspected, **Then** every property has a non-empty `description` field.
5. **Given** a credential issuance endpoint and a wallet signing endpoint, **When** the OpenAPI document is inspected, **Then** at least one example value is present for each request body and successful response body.
6. **Given** the `info` block of the OpenAPI document, **When** an agent reads it, **Then** it carries `x-mcp-server` (string URL pointing at `/.well-known/mcp.json`) and `x-standards` (array of standard names) extensions.
7. **Given** the same document, **When** it is fed through `swagger-cli validate` and `spectral lint`, **Then** both report zero validation errors.

---

### User Story 2 - An AI agent discovers and connects to the Sorcha MCP server (Priority: P1)

An AI workflow orchestrator looking for an MCP server that can drive a credential issuance flow queries the Sorcha gateway's `/.well-known/mcp.json` endpoint. It receives a JSON manifest naming the server (`sorcha-mcp`), version, available transports (`stdio` for local agents, `http+sse` for hosted agents), authentication method (`jwt-bearer` with the issuer URL), tool category counts (admin, designer, participant), and a link to the full tool catalogue. The orchestrator picks the participant slice, authenticates, and drives a TradeFinance walkthrough end-to-end via MCP tool calls without any out-of-band knowledge of Sorcha-specific semantics beyond what the per-tool descriptions provide.

After this spec ships, the MCP server is one of three things an AI agent finds first when crawling a Sorcha instance: `openapi.json`, `mcp.json`, and `llms.txt`. The 36 existing tools each carry a description that explains what the tool does **and** when an agent should call it versus alternatives, in at least two sentences. A new `docs/mcp-server.md` documents the connection pattern, role slices, and includes a worked example session driving the TradeFinance walkthrough through MCP.

**Why this priority**: The MCP server already exists and is the highest-value differentiator for AI integration — it is the entry point that lets an agent *act*, not just read. Without discovery (P1) the asset is invisible. Without per-tool descriptions agents cannot disambiguate between similarly-named tools and either pick the wrong one or refuse to act.

**Independent Test**: From a fresh agent harness with no Sorcha knowledge, fetch `/.well-known/mcp.json`, follow its tool catalogue link, drive `walkthroughs/TradeFinance/` from start to finish using only MCP tool calls. The agent succeeds end-to-end. Separately, scan all 36 tools' description fields and confirm each is at least two sentences and names at least one situation where the tool is the right choice versus a sibling.

**Acceptance Scenarios**:

1. **Given** a running Sorcha API Gateway, **When** an agent issues `GET /.well-known/mcp.json`, **Then** the response is a JSON document carrying `name`, `version`, `description`, `transports[]`, `authentication{}`, `tool_categories{}`, `tool_catalogue_url`, and `documentation_url` fields.
2. **Given** the manifest, **When** an agent inspects `transports[]`, **Then** it lists at least `stdio` and `http+sse` with the routing details (command/args for stdio, base URL for http+sse).
3. **Given** the manifest, **When** an agent inspects `authentication`, **Then** it names `jwt-bearer`, the issuer URL, the audience, and a link to the JWT acquisition flow.
4. **Given** the manifest, **When** an agent inspects `tool_categories`, **Then** it sees the three categories (`admin`, `designer`, `participant`) with tool counts and short descriptions of each slice's intent.
5. **Given** the full tool catalogue, **When** an agent reads any tool's `description`, **Then** the description is at least two sentences and names at least one disambiguating situation ("call this when X, not when Y").
6. **Given** an AI agent following only the manifest plus per-tool descriptions, **When** it attempts to drive the TradeFinance walkthrough, **Then** it succeeds end-to-end without human intervention.

---

### User Story 3 - An LLM evaluating Sorcha for a procurement/integration decision parses `llms.txt` (Priority: P1)

An LLM-backed procurement-evaluation tool is given a list of candidate platforms to evaluate against a brief ("we need a multi-party workflow platform with verifiable credentials and post-quantum readiness"). For each candidate it tries `https://<host>/llms.txt` first. For Sorcha, that file is present, returns 200, contains a one-paragraph factual summary, a bulleted capability list, a bulleted standards list with spec URLs, and links to the OpenAPI spec, MCP manifest, quickstart, and architecture document. The tool reads it in under 8 KB, summarises it for the human evaluator, and ranks Sorcha appropriately.

After this spec ships, `llms.txt` is the canonical machine-readable executive summary of the platform. It follows the [llmstxt.org](https://llmstxt.org) structure: H1 project name, blockquote summary, body links. A longer-form `docs/llms-full.txt` is also published for agents that have already decided to integrate and want richer guidance.

**Why this priority**: `llms.txt` is the entry-point file for LLM-led discovery. It is the new `robots.txt`. Without it, an LLM evaluating Sorcha falls back to scraping README.md, which is human-formatted and contains marketing language that LLMs over-weight. A factual `llms.txt` is the easiest single-file improvement to LLM-led search ranking and integration decisions.

**Independent Test**: An LLM agent given only `https://<host>/llms.txt` produces an accurate one-sentence summary of Sorcha that mentions verifiable credentials, multi-party workflows, and the implemented standards. A human reviewing the same file can verify every factual claim against the codebase in under 10 minutes.

**Acceptance Scenarios**:

1. **Given** a running Sorcha instance, **When** an agent fetches `https://<host>/llms.txt`, **Then** the response is plain text, ≤ 8 KB, with HTTP 200 and `Content-Type: text/plain; charset=utf-8`.
2. **Given** the file content, **When** parsed against the [llmstxt.org](https://llmstxt.org) structure, **Then** it has a single H1 (project name), a blockquote summary paragraph, and section headers for capabilities, standards, and links.
3. **Given** the standards section, **When** an agent reads each entry, **Then** every entry has a stable canonical URL (RFC, NIST, ISO, W3C, BIP, OpenID Foundation specification page).
4. **Given** the links section, **When** an agent follows each link, **Then** every URL resolves to HTTP 200 against the same instance (no broken links to external-only resources for things the platform itself serves).
5. **Given** `docs/llms-full.txt`, **When** an agent fetches it, **Then** it receives a longer-form (≤ 32 KB) document covering architecture, quickstart, MCP integration, and security model.
6. **Given** any marketing language ("revolutionary", "best-in-class", "industry-leading"), **When** the file is reviewed, **Then** none is present — the file is factual.

---

### User Story 4 - A human auditor or AI agent verifies a standards compliance claim (Priority: P2)

A regulatory auditor or an AI agent assembling a vendor-due-diligence report needs to verify that Sorcha's claim of "implements ML-DSA FIPS 204" is real and not a marketing assertion. They fetch `STANDARDS.md` from the repo root, find the row for ML-DSA FIPS 204 with status `full`, click the linked spec URL to confirm the standard, and click the linked component path (`src/Common/Sorcha.Cryptography/Pqc/`) to see the implementation. The same flow handles partial-status claims with notes — for example, HAIP 1.0 is `partial` because the HAIP profile at the wire boundary is classical-only by design even though the platform internals are post-quantum.

After this spec ships, every standards compliance claim Sorcha makes (in marketing, in `llms.txt`, in OpenAPI `x-standards`, in pitch decks) is backed by a row in `STANDARDS.md` that points at the implementation and admits any known gaps. A stale row is treated as a defect: the PR checklist requires `STANDARDS.md` to be updated whenever a standards-related change merges, and a CI check verifies the file is present and parseable.

**Why this priority**: Trust is asymmetric — claiming a standard you do not implement, or implementing one and not claiming it accurately, is worse than not claiming. P2 because nothing in US1-3 depends on this file existing, but it is the single artefact most likely to be cited in adversarial procurement reviews.

**Independent Test**: A reviewer with no codebase familiarity reads `STANDARDS.md` end-to-end, picks any three rows at random, and verifies each by following the spec URL and the component path. All three pass — the claim, the status, the gap notes (if any), and the implementation location are accurate.

**Acceptance Scenarios**:

1. **Given** the repo root, **When** a reviewer opens `STANDARDS.md`, **Then** the file contains a single tabular section listing every standard Sorcha implements with name, version, issuing body, spec URL, component path(s), compliance status (`full` / `partial` / `planned`), and notes columns.
2. **Given** the table, **When** a reviewer picks a `partial` row, **Then** the notes column names the specific gap (e.g. "HAIP 1.0 wire boundary is classical-only by spec; PQC is internal").
3. **Given** any `full` row, **When** a reviewer clicks the component path, **Then** the linked code is present and observably implements the standard.
4. **Given** any `planned` row, **When** a reviewer reads the notes, **Then** the notes name the spec or roadmap item that will deliver it.
5. **Given** the PR checklist for standards-related changes, **When** a PR touches a file under any path listed in `STANDARDS.md`, **Then** the PR description must confirm `STANDARDS.md` was reviewed and updated if applicable.
6. **Given** CI, **When** any PR opens against master, **Then** a check verifies `STANDARDS.md` is present and parses as a Markdown table without structural errors.

---

### User Story 5 - An AI coding agent runs the Sorcha quickstart with no human assistance (Priority: P2)

An AI coding agent is asked to "set up Sorcha locally and call its credential issuance endpoint." It clones the repo, reads the quickstart section of `README.md` and follows the link to `docs/quickstart.md`. Every prerequisite is named with a version constraint. The setup script (`scripts/sorcha-setup.sh`) exits with code 0 on success and a non-zero code with a human-readable error on any prerequisite failure. After setup completes, the agent runs the documented `verify your installation` curl command against `/health` and confirms the expected JSON response. The agent then proceeds to issue a credential.

After this spec ships, the entire setup-to-first-call path is executable by an agent with no human in the loop. Every silent failure mode is replaced by a non-zero exit and a remediation hint. `docker-compose.yml` carries a topology comment block near the top explaining which container is which service — the artefact agents parse to understand the system shape.

**Why this priority**: AI coding agents can already follow instructions; the bar for *AI-runnable* is "no silent failures, every step verifiable." The current quickstart almost gets there. P2 because the cost is small (script hardening + a verify step + topology comments) and the payoff is that agent-led demos and POCs become possible.

**Independent Test**: A fresh Linux VM with Docker Desktop ≥ 4 and PowerShell 7.5 installed, given only the repo URL and `docs/quickstart.md`, can complete the setup and run the verify-installation curl in under 15 minutes with no human input. If any prerequisite is missing, the setup script exits with a clear error and a remediation hint.

**Acceptance Scenarios**:

1. **Given** a fresh Linux VM with Docker Desktop and PowerShell installed, **When** an agent clones the repo and runs `./scripts/sorcha-setup.sh`, **Then** the script exits with code 0 and the gateway becomes reachable on `http://localhost`.
2. **Given** the same VM with Docker not running, **When** an agent runs `./scripts/sorcha-setup.sh`, **Then** the script exits with a non-zero code and an error message naming Docker as the missing prerequisite and pointing at the install instructions.
3. **Given** a successful setup, **When** an agent runs the documented verify-installation curl (`curl -s http://localhost/api/health`), **Then** the response is HTTP 200 with the expected aggregated-health JSON shape.
4. **Given** `docker-compose.yml`, **When** an agent reads the file, **Then** a comment block within the first 30 lines names every service, its port, and its purpose in one line each.
5. **Given** `docs/quickstart.md`, **When** an agent reads it, **Then** every prerequisite is named with a minimum version, every command is copy-pasteable, every common failure mode has a documented fix, and the verify-installation step is present.
6. **Given** a missing prerequisite, **When** the setup script encounters it, **Then** the script exits with code 1 (or higher) and prints a single-line message of the form `[sorcha-setup] missing prerequisite: <name> (≥ <version>); install via <link>`.

---

### User Story 6 - Selected technical documents are published from planning into the public repo (Priority: P2)

A technical buyer evaluating Sorcha for a real-world deployment needs more than a quickstart — they need an architecture document, a security model, an applicability summary, and an integration guide. Today this content exists as planning-folder narratives that are not exposed publicly. After this spec ships, four documents live under `docs/`, each formatted for public consumption and each carrying YAML frontmatter that AI indexers, OpenGraph crawlers, and human readers can all use.

The four documents:

- `docs/architecture.md` — adapted from the existing "Five Layers of Open" architecture narrative.
- `docs/openid4vc-haip-integration.md` — adapted from the existing OpenID4VC + HAIP integration narrative. Authoritative on how Sorcha sits beside GOV.UK Wallet / EUDIW.
- `docs/applicability.md` — adapted from the existing applicability narrative covering DPP, trade finance, IPC-1782, municipal governance.
- `docs/security-model.md` — synthesised from the architecture evaluation and applicability narratives. Covers selective disclosure, aggregate inference threat model, PQC posture and HAIP boundary tension, and the mTLS gap honestly.

After this spec ships, every document carries `title`, `description`, `standards`, and `last_updated` frontmatter, every standards reference resolves to a row in `STANDARDS.md`, and the `last_updated` field is enforced fresh by the PR checklist for any change to the file.

**Why this priority**: These documents are the longest-form artefacts an AI agent (and a human) will read after deciding Sorcha is in scope. They are also the artefacts most likely to age out of accuracy. P2 because they unlock deeper integration work but are not on the critical path for first-contact discovery (US1-US3).

**Independent Test**: A technical reviewer with no prior exposure to Sorcha reads the four documents in sequence and can answer (a) what Sorcha is, (b) how it integrates with HAIP wallets, (c) what domains it is applicable to, and (d) what its known security trade-offs are, without referring to any other source.

**Acceptance Scenarios**:

1. **Given** the repo, **When** a reader opens `docs/architecture.md`, **Then** the document opens with YAML frontmatter (`title`, `description`, `standards[]`, `last_updated`) and reads as a coherent architecture narrative without proprietary internal references.
2. **Given** `docs/openid4vc-haip-integration.md`, **When** a reader reads it, **Then** it accurately describes how Sorcha sits beside GOV.UK Wallet and EUDIW, names the HAIP boundary, and identifies what is and is not in scope.
3. **Given** `docs/applicability.md`, **When** a reader reads it, **Then** it covers at least the four named domains (DPP, trade finance, IPC-1782, municipal governance) with one worked example each.
4. **Given** `docs/security-model.md`, **When** a reader reads it, **Then** it is honest about the mTLS gap and the HAIP classical-only boundary; it does not claim mitigations that are not implemented.
5. **Given** any of the four documents, **When** an entry in the `standards[]` frontmatter array is checked, **Then** that entry has a corresponding row in `STANDARDS.md`.
6. **Given** any change to the body of one of the four documents, **When** the PR is reviewed, **Then** the `last_updated` field is bumped to the merge date.

---

### Edge Cases

- What happens when an agent fetches `/.well-known/openapi.json` against a Sorcha instance whose gateway endpoint generation has fallen out of sync with the underlying services (e.g. a service shipped a new endpoint that the gateway has not yet been rebuilt to expose)? The OpenAPI document reflects the gateway's current view; the new endpoint is missing until the gateway is rebuilt. This is acceptable and matches general OpenAPI semantics.
- What happens when an OpenAPI endpoint is correct but its `description` is empty or one-word? CI lint (US1 acceptance scenario 7) flags it before merge. Existing endpoints are audited and brought up to standard during this spec's phase 3.
- What happens when an MCP tool is added in a future PR but its description does not meet the two-sentence rule? The PR checklist (US4 mechanism) requires description audit; the test infrastructure asserts a minimum length per tool.
- What happens when `llms.txt` and `STANDARDS.md` drift out of agreement (e.g. `llms.txt` claims a standard that `STANDARDS.md` lists as `planned`)? CI lint (a small custom check) verifies any standard named in `llms.txt` is present in `STANDARDS.md` with a `full` or `partial` status.
- What happens when a setup script error message cites a prerequisite version that is no longer accurate (e.g. Docker Desktop introduces a breaking change at 5.0)? The version constraints live in the script itself; the PR checklist requires reviewing them when bumping the upstream-supported version.
- What happens when `docs/security-model.md` claims a mitigation that is later revealed to be incomplete? The document's `last_updated` field surfaces staleness to readers; a known-issues section at the bottom of the document is the correct home for ongoing gaps. This spec does not mandate a separate vulnerability disclosure process.
- What happens when an AI agent attempts the TradeFinance walkthrough via MCP and one of the 36 tools is missing or renamed? The manifest's tool catalogue link returns a 404 or a tool list that no longer matches the agent's expectation. The agent fails fast. This is preferable to silent partial-success.
- What happens when the OpenAPI spec is large enough (multi-MB) that an LLM context window cannot ingest it whole? Off-the-shelf OpenAPI tools handle paging; the gateway should set `Cache-Control: max-age=300` so repeated fetches are fast, but no chunking is mandated by this spec.
- What happens when a third-party indexer (e.g. an MCP registry) caches `mcp.json` and the live manifest changes? The cached version is correct as of cache time; the manifest's `Cache-Control` header is set to `max-age=300` so changes propagate within 5 minutes.

## Requirements *(mandatory)*

### Functional Requirements

**OpenAPI 3.1 specification surface (US1):**
- **FR-001**: The API Gateway MUST serve a valid OpenAPI 3.1 document at `GET /.well-known/openapi.json` with `Content-Type: application/json` and `Cache-Control: public, max-age=300`.
- **FR-002**: The same document, in YAML form, MUST be served at `GET /.well-known/openapi.yaml` with `Content-Type: application/yaml` (or `text/yaml`).
- **FR-003**: The OpenAPI document MUST be auto-generated from ASP.NET Core endpoint metadata via `Microsoft.AspNetCore.OpenApi` (the .NET 10 built-in). No hand-maintained OpenAPI source file is permitted as the canonical source.
- **FR-004**: Every endpoint MUST carry `operationId`, `summary`, `description`, and `tags`. `operationId` MUST be PascalCase and follow `<Resource><Verb>` convention (e.g. `WalletGet`, `CredentialIssue`, `RegisterStatusGet`).
- **FR-005**: Every request body schema, response body schema, and parameter MUST carry a non-empty `description`.
- **FR-006**: At minimum, the credential issuance and wallet signing endpoints MUST carry at least one `example` value for the request body and one for a successful response body.
- **FR-007**: The `info` block MUST include `title`, `version` (matching the running platform version), `description` (one paragraph describing Sorcha factually), and `contact.url` (GitHub organisation URL).
- **FR-008**: The `info` block MUST include an `x-mcp-server` extension whose value is the absolute URL of `/.well-known/mcp.json`.
- **FR-009**: The `info` block MUST include an `x-standards` extension whose value is an array of standard names (strings) corresponding to rows in `STANDARDS.md` with status `full` or `partial`.
- **FR-010**: An endpoint whose specification is incomplete or behaviour is not yet stable MUST carry an `x-status: partial` extension rather than be omitted from the document.
- **FR-011**: The OpenAPI document MUST validate cleanly against `swagger-cli validate`. The OpenAPI document MUST pass `spectral lint` against the project's ruleset (a Spectral config file ships in this spec).

**MCP discovery and tool catalogue (US2):**
- **FR-012**: The API Gateway MUST serve a JSON manifest at `GET /.well-known/mcp.json` with `Content-Type: application/json` and `Cache-Control: public, max-age=300`.
- **FR-013**: The manifest MUST include `name` (`sorcha-mcp`), `version` (matching the running MCP server version), `description` (one sentence), `transports` (array), `authentication` (object), `tool_categories` (object keyed by category name), `tool_catalogue_url`, and `documentation_url`.
- **FR-014**: The `transports` array MUST list at least the `stdio` and `http+sse` transports with the routing details required to connect to each.
- **FR-015**: The `authentication` object MUST identify `jwt-bearer` as the authentication method and provide the JWT issuer URL, audience, and a link to the JWT acquisition flow.
- **FR-016**: The `tool_categories` object MUST list the three categories (`admin`, `designer`, `participant`) with each entry carrying a tool count and a one-sentence description of when an agent should use that slice.
- **FR-017**: Every MCP tool's `description` field MUST be at least two sentences and MUST name at least one disambiguating situation ("call this when X, not when Y" or equivalent). All 36 existing tools MUST be brought up to this standard during this spec's phase 4.
- **FR-018**: A new file `docs/mcp-server.md` MUST exist describing how to connect to the server, the JWT acquisition flow, the role slices, and a worked example agent session driving the TradeFinance walkthrough.
- **FR-019**: The repository's GitHub topics MUST include `mcp-server` (manual step, not enforced in code; tracked in tasks).

**`llms.txt` and project summary (US3):**
- **FR-020**: A file `llms.txt` MUST exist at the repo root, follow the [llmstxt.org](https://llmstxt.org) structure, be ≤ 8 KB plain text, and contain: a single H1 (project name), a blockquote summary paragraph, a capabilities section, a standards section, and a links section.
- **FR-021**: The capabilities section of `llms.txt` MUST list each capability with a one-line factual description. No marketing adjectives.
- **FR-022**: The standards section of `llms.txt` MUST list every standard whose row in `STANDARDS.md` has status `full` or `partial`, with a stable canonical URL for each.
- **FR-023**: The links section of `llms.txt` MUST link to (at minimum) the OpenAPI spec URL, the MCP manifest URL, the quickstart, and the architecture document.
- **FR-024**: A longer-form `docs/llms-full.txt` MUST exist, ≤ 32 KB plain text, covering architecture, quickstart, MCP integration, and security model.
- **FR-025**: Any standard named in `llms.txt` or `docs/llms-full.txt` MUST have a corresponding row in `STANDARDS.md` (CI-enforced via a small custom check).

**`STANDARDS.md` and structured compliance claims (US4):**
- **FR-026**: A file `STANDARDS.md` MUST exist at the repo root containing a single table with columns: standard name, version, issuing body, spec URL, component path(s), compliance status (`full` / `partial` / `planned`), notes.
- **FR-027**: The table MUST cover at minimum: BIP32, BIP39, BIP44, ML-DSA FIPS 204, OpenID4VCI, OpenID4VP, HAIP 1.0, W3C Verifiable Credentials Data Model 2.0, IETF RFC 9972 (Token Status List 2024), and ISO 18013-5 (mdoc, if applicable; otherwise marked `planned`).
- **FR-028**: Every `partial` row MUST carry a notes-column entry naming the specific gap.
- **FR-029**: Every `planned` row MUST carry a notes-column entry naming the spec or roadmap item that will deliver it.
- **FR-030**: The PR template MUST include a checkbox affirming that `STANDARDS.md` was reviewed and updated if the change touches a standards-related path.
- **FR-031**: A CI check on PRs to master MUST verify `STANDARDS.md` is present and parses as a Markdown table without structural errors.

**Quickstart hardening (US5):**
- **FR-032**: `scripts/sorcha-setup.sh` MUST exit with code 0 on success and a non-zero code with a human-readable single-line error message on any prerequisite failure (Docker not installed, Docker not running, Docker version too low, PowerShell missing for walkthroughs, port conflict).
- **FR-033**: Each prerequisite check MUST emit a remediation hint (URL or `apt`/`brew`/`winget` command) on failure.
- **FR-034**: `docker-compose.yml` MUST carry a topology comment block within the first 30 lines naming every service, its port, and a one-line description of its purpose.
- **FR-035**: A file `docs/quickstart.md` MUST exist covering: prerequisites with version constraints, setup steps, common failure modes with documented fixes, and a verify-installation step (a `curl` command hitting `/api/health` and the expected JSON response shape).
- **FR-036**: The `README.md` quickstart section MUST link to `docs/quickstart.md` for detailed instructions.
- **FR-037**: If an `org-profile-README.md` (GitHub organisation profile README) is present in the repo, its quickstart section MUST also link to `docs/quickstart.md`.

**Technical documentation publication (US6):**
- **FR-038**: A file `docs/architecture.md` MUST exist, adapted from the existing planning-folder architecture narrative, with YAML frontmatter (`title`, `description`, `standards[]`, `last_updated`).
- **FR-039**: A file `docs/openid4vc-haip-integration.md` MUST exist, adapted from the existing planning-folder OpenID4VC + HAIP integration narrative, with the same frontmatter shape.
- **FR-040**: A file `docs/applicability.md` MUST exist, adapted from the existing planning-folder applicability narrative, covering at least DPP, trade finance, IPC-1782, and municipal governance domains, with the same frontmatter shape.
- **FR-041**: A file `docs/security-model.md` MUST exist, synthesised from the architecture evaluation and applicability narratives, covering at minimum: selective disclosure, aggregate inference threat model, PQC posture and HAIP boundary tension, mTLS gap (named honestly).
- **FR-042**: Every `standards[]` entry across the four documents MUST correspond to a row in `STANDARDS.md`. CI-enforced via the same check that gates `llms.txt` standards.
- **FR-043**: The PR checklist MUST require `last_updated` to be bumped to the merge date when the body of any of the four documents changes.

**Cross-cutting:**
- **FR-044**: The OpenAPI lint, MCP manifest validity, `STANDARDS.md` parseability, and standards cross-reference checks MUST run as a single GitHub Actions workflow (`ai-discoverability-check.yml`) on every PR to master. Failures block merge.
- **FR-045**: All machine-readable artefacts (`/.well-known/*`, `llms.txt`, `STANDARDS.md`) MUST be written factually. Marketing adjectives (`revolutionary`, `best-in-class`, `industry-leading`, `cutting-edge`) are explicitly disallowed; the lint check flags them.
- **FR-046**: The `info.version` of the OpenAPI document and the `version` of the MCP manifest MUST be sourced from the same single source of truth (a shared assembly version or an environment variable), so they never drift.

### Key Entities

- **OpenAPI 3.1 document** (new, but auto-generated): the canonical machine-readable description of the API Gateway surface. Served at two URLs (`/.well-known/openapi.json` and `/openapi.yaml`). Generated from endpoint metadata at runtime; not checked into the repo as a static file.
- **MCP server manifest** (new): a small JSON document (`/.well-known/mcp.json`) describing the server name, version, transports, authentication, tool categories, and links to deeper resources. Hand-curated content but served by the gateway.
- **Tool catalogue endpoint** (new): a JSON endpoint listing every tool with its name, category, description, and a link to its parameter schema. Served by the MCP server itself or by the gateway proxy. The manifest's `tool_catalogue_url` points here.
- **`llms.txt`** (new): plain-text file at repo root following [llmstxt.org](https://llmstxt.org) structure. Hand-maintained, ≤ 8 KB, factual.
- **`docs/llms-full.txt`** (new): longer-form companion. ≤ 32 KB. Hand-maintained.
- **`STANDARDS.md`** (new): Markdown table at repo root. Hand-maintained. The single source of truth for compliance claims; cross-referenced from OpenAPI `x-standards`, `llms.txt`, and the four published documents.
- **`docs/architecture.md`, `docs/openid4vc-haip-integration.md`, `docs/applicability.md`, `docs/security-model.md`** (new — adapted from planning narratives where applicable): human-readable technical documents with YAML frontmatter for indexing.
- **`docs/quickstart.md`** (new): the agent-runnable setup guide, with verify-installation step.
- **`docs/mcp-server.md`** (new): MCP connection guide and worked example session.
- **`scripts/sorcha-setup.sh`** (existing, hardened): the setup script. Receives prerequisite check enhancements and exit-code discipline.
- **`docker-compose.yml`** (existing, annotated): receives a topology comment block.
- **CI workflow `ai-discoverability-check.yml`** (new): single workflow gating PRs to master on lint and cross-reference checks.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `GET /.well-known/openapi.json` returns a valid OpenAPI 3.1 document. `swagger-cli validate` and `spectral lint` both report zero errors. Confirmed by a CI check that fails the build on validation regression.
- **SC-002**: 100 % of endpoints in the served OpenAPI document carry non-empty `operationId`, `summary`, `description`, and `tags`. Confirmed by a Spectral rule that fails on missing fields.
- **SC-003**: 100 % of request and response schema properties in the served OpenAPI document carry non-empty `description` fields. Confirmed by a Spectral rule.
- **SC-004**: At minimum, the credential issuance and wallet signing endpoints carry at least one `example` value for both request and response. Confirmed by Spectral.
- **SC-005**: `GET /.well-known/mcp.json` returns a valid JSON document with all FR-013 fields present. Confirmed by a JSON-schema validation step in CI.
- **SC-006**: 100 % of the 36 MCP tools have description fields of at least two sentences and naming at least one disambiguating situation. Confirmed by a unit test that scans every tool.
- **SC-007**: `llms.txt` is present at repo root, ≤ 8 KB, and parses against the [llmstxt.org](https://llmstxt.org) structure. Confirmed by a CI lint step.
- **SC-008**: `STANDARDS.md` is present at repo root and parses as a Markdown table with the required columns. Confirmed by a CI parse step.
- **SC-009**: Every standard named in `llms.txt`, `docs/llms-full.txt`, the four `docs/` documents' frontmatter, and OpenAPI `x-standards` exists as a `full` or `partial` row in `STANDARDS.md`. Confirmed by a CI cross-reference check.
- **SC-010**: An AI coding agent on a fresh Linux VM can complete the quickstart end-to-end (clone → setup → verify-installation curl returns 200) in under 15 minutes with zero human input. Confirmed by a periodic GitHub Actions cron job that runs the quickstart against a clean ubuntu-latest runner and reports on success/failure.
- **SC-011**: `docs/architecture.md`, `docs/openid4vc-haip-integration.md`, `docs/applicability.md`, and `docs/security-model.md` are all present with valid YAML frontmatter. Confirmed by CI.
- **SC-012**: A single CI workflow (`ai-discoverability-check.yml`) runs all of the above checks on every PR to master and blocks merge on failure. Confirmed by the workflow being present and required in branch protection.
- **SC-013**: A reference AI agent (a small test harness, e.g. an off-the-shelf MCP-aware agent) can drive the `walkthroughs/TradeFinance/` walkthrough end-to-end using only `/.well-known/mcp.json` and per-tool descriptions. Confirmed by a manual end-to-end test recorded in this spec's verification log.
- **SC-014**: `info.version` in the OpenAPI document and `version` in the MCP manifest are sourced from the same canonical version variable; they cannot drift. Confirmed by code review and a unit test asserting they read from the same source.
- **SC-015**: No marketing adjectives appear in any machine-readable artefact (`/.well-known/*`, `llms.txt`, `STANDARDS.md`). Confirmed by a lint check with an explicit deny-list.

## Non-Functional Requirements

- **NFR-001 (Accuracy over completeness)**: a partial but accurate OpenAPI spec is preferable to a complete but inaccurate one. Endpoints whose specification is incomplete carry `x-status: partial` rather than being omitted or guessed.
- **NFR-002 (Maintenance burden)**: every artefact has a clear maintenance owner. The PR template names which artefacts must be reviewed for changes touching specific paths. The `ai-discoverability-check.yml` workflow makes drift visible.
- **NFR-003 (Factual tone)**: machine-readable artefacts are written factually. Marketing adjectives are deny-listed by lint.
- **NFR-004 (Versioning)**: the OpenAPI `info.version` and the MCP manifest `version` are bumped as part of the platform release process. Both read from the same source.
- **NFR-005 (Performance)**: `/.well-known/openapi.json` and `/.well-known/mcp.json` MUST return in under 200 ms at P95 under cached conditions, matching the existing OpenAPI endpoint's profile.
- **NFR-006 (Caching)**: both well-known endpoints MUST set `Cache-Control: public, max-age=300` so downstream caches and AI agents can refresh on a 5-minute window without pinging the gateway.
- **NFR-007 (Observability)**: CI workflow failures MUST be linkable from PR comments. A failing lint or cross-reference check MUST surface the offending file and line so an author can fix it without reading the workflow logs.
- **NFR-008 (Security)**: serving `/.well-known/openapi.json` and `/.well-known/mcp.json` anonymously MUST NOT expose any secret, internal-only endpoint, or admin-only route. The OpenAPI generation pipeline MUST exclude `[ApiExplorerSettings(IgnoreApi = true)]` endpoints and any endpoint marked `.ExcludeFromDescription()`.

## Out of Scope

- `schema.org` structured data on a public marketing website. The website is not yet built; this is deferred to whichever spec ships the website.
- Submission of the MCP server to a public MCP registry. The repository topic addition is captured (FR-019) but registry submission is a manual step out of scope.
- Standards body participation (TC, working group membership) or reference implementation submission to standards bodies. Out of scope.
- BBS+ or SLH-DSA implementation. Architectural gaps not addressed by this spec.
- A public sandbox endpoint at `sandbox.sorcha.dev` or similar. Deferred — requires deployment decisions out of scope.
- A signed and verifiable build attestation (SLSA, in-toto) of the served OpenAPI document. Worth considering but out of scope here.
- Internationalisation of `llms.txt` or `STANDARDS.md`. English only.
- A separate human-readable site (e.g. Docusaurus or MkDocs) generated from `docs/`. Files are Markdown for human reading directly; site generation is a separate decision.
- Automated migration of MCP tool descriptions via LLM. Audit and rewrite is a manual task in this spec; tooling that automates description quality is out of scope.

## Assumptions

- ASP.NET Core's `Microsoft.AspNetCore.OpenApi` (the .NET 10 built-in) is sufficient to generate an OpenAPI 3.1 document of the required completeness when endpoint metadata is correctly annotated. If specific generator gaps are found during implementation, the planning phase will document workarounds (extension hooks, transformers).
- The platform version sourced for `info.version` and MCP manifest `version` is the assembly informational version, available at runtime via `Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()`. If the platform uses a different canonical version source, the implementation phase will adapt.
- The 36 MCP tools enumerated in `src/Apps/Sorcha.McpServer/Tools/` (13 admin + 13 designer + 10 participant) is the current authoritative count. If new tools land before this spec ships, they are added to the audit.
- The four planning-folder narratives referenced by US6 exist and are publication-ready with minor adaptation. If a narrative is missing or unsuitable, the implementation phase will surface it and either find an alternative source or down-scope the document.
- The repo is currently public on GitHub or will be by the time this spec ships. `llms.txt`, `STANDARDS.md`, and the four published documents are written for a public audience.
- The PR template lives at `.github/pull_request_template.md` (or similar) and can be edited to add the `STANDARDS.md` and `last_updated` checkboxes.
- A Spectral-compatible OpenAPI ruleset is permissible to add to the repo. Spectral is widely adopted, OSS, and the canonical OpenAPI lint tool.
- The CI workflow may run on `ubuntu-latest` GitHub-hosted runners. No self-hosted runner dependency.
- The IETF Token Status List 2024 standard is referenced as RFC 9972; if at the time of authoring `STANDARDS.md` the RFC number has changed, the row will be updated to match the actual published designation.

## Dependencies

- **Builds on** specs 095 / 096 / 097 / 098 / 099 / 113 — those specs added the substance the discoverability artefacts describe. None must be merged for this spec to ship; partial-status rows in `STANDARDS.md` cover the in-progress ones.
- **Independent of** spec 116 (Account Linking) — no shared code paths or artefacts.
- **Required by** any subsequent marketplace or registry submission spec — those will lean on the `/.well-known/*` endpoints and `STANDARDS.md`.
- **Not blocked by** any in-flight spec. Phase 1 (setup) and Phase 2 (audit) can begin immediately.

## What success looks like

After this spec ships, a brand-new AI agent encountering Sorcha for the first time can:

1. Find the platform via `llms.txt`.
2. Verify what standards it implements via `STANDARDS.md`.
3. Discover and parse its API surface via `/.well-known/openapi.json`.
4. Connect to its MCP server via `/.well-known/mcp.json`.
5. Run the quickstart end-to-end without human assistance.
6. Read the architecture, security model, applicability, and HAIP integration documents to inform deeper work.

Every machine-readable artefact is accurate, factual, and CI-validated. The maintenance burden is bounded by clear PR-checklist rules and a single discoverability CI workflow. A stale claim is a defect, treated as such, and visible to anyone following the standards cross-reference chain.

The spec is internal engineering documentation, not marketing. The artefacts it ships are aimed at machines, but the trust signal they carry — accuracy without hyperbole — works equally well on humans who happen to read them.
