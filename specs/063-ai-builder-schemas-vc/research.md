# Research: AI Blueprint Builder Enhancement

**Branch**: `063-ai-builder-schemas-vc` | **Date**: 2026-03-18

## R1: Schema File Format for Standardised Schemas

**Decision**: Use a JSON file format that extends the existing `SchemaEntry` model with additional metadata for form layout and disclosure recommendations. Each file is self-contained.

**Rationale**: The `SchemaEntry` model in `Sorcha.Blueprint.Schemas` already has `Identifier`, `Title`, `Description`, `Version`, `Category`, `Source`, `Status`, `Content` (JsonDocument), `SectorTags`, and `Keywords`. However, it lacks form layout and disclosure metadata. Rather than modifying the domain model (which serves the Register/Schema management purpose), we add these as properties within the `Content` JSON Schema document itself, using the standard JSON Schema `x-` extension pattern.

**File Structure**:
```json
{
  "identifier": "uk-address",
  "title": "UK Address",
  "description": "Standard UK postal address with postcode validation",
  "version": "1.0.0",
  "category": "people-identity",
  "tags": ["address", "uk", "postal", "location"],
  "keywords": ["address", "postcode", "street", "city", "county"],
  "schema": {
    "type": "object",
    "properties": {
      "addressLine1": { "type": "string", "title": "Address Line 1", "minLength": 1, "maxLength": 100 },
      "addressLine2": { "type": "string", "title": "Address Line 2", "maxLength": 100 },
      "city": { "type": "string", "title": "City", "minLength": 1, "maxLength": 50 },
      "county": { "type": "string", "title": "County", "maxLength": 50 },
      "postcode": { "type": "string", "title": "Postcode", "pattern": "^[A-Z]{1,2}\\d[A-Z\\d]?\\s?\\d[A-Z]{2}$" }
    },
    "required": ["addressLine1", "city", "postcode"]
  },
  "formLayout": {
    "type": "VerticalLayout",
    "elements": [
      { "type": "TextLine", "scope": "addressLine1", "title": "Address Line 1" },
      { "type": "TextLine", "scope": "addressLine2", "title": "Address Line 2" },
      { "type": "HorizontalLayout", "elements": [
        { "type": "TextLine", "scope": "city", "title": "City" },
        { "type": "TextLine", "scope": "county", "title": "County" }
      ]},
      { "type": "TextLine", "scope": "postcode", "title": "Postcode" }
    ]
  },
  "disclosure": {
    "sensitive": [],
    "recommendation": "Full address is generally needed by all recipients. Consider restricting to postcode only for summary views."
  }
}
```

**Alternatives Considered**:
- Modifying `SchemaEntry` model to add formLayout/disclosure properties → rejected because it couples the Register schema management domain with the AI builder's needs.
- Storing form layout in separate files → rejected because it breaks the self-contained file principle and complicates seeding.
- Using JSON Schema's standard `x-sorcha-layout` extensions → rejected because form layout is complex enough to warrant a top-level section.

## R2: Schema Seeding Approach

**Decision**: Create `SchemaSeedService` as an `IHostedService` that scans `blueprints/schemas/` subdirectories on startup, similar to `TemplateSeedService`. Store schemas in the existing `ISchemaStore` (MongoDB-backed).

**Rationale**: The `TemplateSeedService` pattern is proven and well-tested. The `ISchemaStore` already provides `CreateAsync`, `GetByIdentifierAsync`, and `ExistsAsync` methods. Seeded schemas use `SchemaCategory.System` and `SchemaSource.Internal()`.

**Key Differences from Template Seeding**:
- Templates use `IDocumentStore<BlueprintTemplate, string>` (in-memory). Schemas use `ISchemaStore` (MongoDB).
- Templates have simple integer version. Schemas use semantic version strings ("1.0.0").
- Schema files are in subdirectories by category. Template files are flat.
- Schema seeding creates `SchemaEntry` objects from the JSON file fields.

**Alternatives Considered**:
- In-memory store for schemas (like templates) → rejected because schemas are already MongoDB-backed in the existing `ISchemaStore` infrastructure.
- Seeding via migration script → rejected because startup seeding is idempotent and doesn't require manual intervention.

## R3: System Prompt Architecture

**Decision**: Build the system prompt dynamically in `ChatOrchestrationService.BuildSystemPrompt()`. The base prompt contains the personality, workflow, rules, and credential awareness. A compact schema/template summary table is appended dynamically from the seeded data.

**Rationale**: The current system prompt is a `const string` (~130 lines, ~2000 tokens). Adding schema summaries, template lists, and credential guidance will grow it to ~3500-4000 tokens. Dynamic building allows the summary to stay current as schemas are added/removed without code changes.

**System Prompt Structure (new)**:
1. Role & personality (professional, inquisitive)
2. Conversation workflow (understand → confirm → propose → checkpoint → build → validate → save)
3. Available tools (13 total)
4. Schema awareness (compact table injected dynamically)
5. Template catalogue (compact list injected dynamically)
6. Credential concepts (requirements, issuance, DPP patterns)
7. Blueprint rules (participants, actions, disclosures, routing)
8. Data type reference (field types, constraints)
9. Disclosure best practices (minimal by default)

**Token Budget**:
- Base prompt: ~2500 tokens
- Schema summary (~25 schemas, one line each): ~500 tokens
- Template summary (~10 templates, one line each): ~200 tokens
- Total: ~3200 tokens (well within Anthropic's system prompt capacity)

**Alternatives Considered**:
- Keep static const string → rejected because schema/template lists change on deployment.
- Full schema injection → rejected because 25 full schemas would be 10,000+ tokens.
- Tool-only (no system prompt awareness) → rejected because the AI needs ambient knowledge to proactively suggest schemas.

## R4: New Tool Definitions

**Decision**: Add 5 new tools to `BlueprintToolExecutor`, bringing the total from 8 to 13.

| Tool | Purpose | Input | Output |
|------|---------|-------|--------|
| `search_schemas` | Query schema library | `query` (string), `category` (optional) | List of matching schema summaries (id, title, category, field count) |
| `use_standard_schema` | Apply schema to action | `schemaId` (string), `actionId` (int) | Schema fields merged into action data definition |
| `require_credential` | Add VC requirement | `actionId`, `credentialType`, `acceptedIssuers[]`, `requiredClaims[]`, `revocationPolicy` | CredentialRequirement added to action |
| `issue_credential` | Configure VC issuance | `actionId`, `credentialType`, `claimMappings[]`, `recipientParticipantId`, `expiryDuration`, `usagePolicy` | CredentialIssuanceConfig set on action |
| `search_templates` | Query template catalogue | `query` (string), `category` (optional) | List of matching template summaries (id, title, category) |

**Rationale**: These tools map directly to the existing domain operations. `search_schemas` and `search_templates` enable the AI to look up details on demand (per the summary-injection clarification). `use_standard_schema` applies a full schema to an action. `require_credential` and `issue_credential` map to the fluent API's `RequiresCredential()` and `IssuesCredential()` builders.

**Alternatives Considered**:
- Single `manage_credentials` tool with subcommands → rejected because separate tools are clearer for the AI to select.
- `use_template` tool (instantiate full template) → deferred because the existing template evaluation API handles this. Could be added later.

## R5: Chat UI Auto-Scroll Pattern

**Decision**: Use a small JS interop module (`chat-scroll.js`) that observes the messages container and auto-scrolls when new content is appended, pausing when the user scrolls up.

**Rationale**: Blazor WASM cannot directly observe scroll position or mutation events. A lightweight JS module with `IntersectionObserver` on a sentinel element at the bottom of the messages area is the established pattern. When the sentinel is visible (user is at bottom), auto-scroll is active. When hidden (user scrolled up), auto-scroll pauses.

**Implementation**:
```javascript
// Sentinel-based auto-scroll
export function initAutoScroll(container) {
    const sentinel = container.querySelector('.scroll-sentinel');
    let autoScroll = true;

    const observer = new IntersectionObserver(([entry]) => {
        autoScroll = entry.isIntersecting;
    }, { root: container });

    observer.observe(sentinel);

    const mutationObserver = new MutationObserver(() => {
        if (autoScroll) sentinel.scrollIntoView({ behavior: 'smooth' });
    });

    mutationObserver.observe(container, { childList: true, subtree: true });
}
```

**Alternatives Considered**:
- Scroll to bottom on every `StateHasChanged` → rejected because it would override user's manual scroll-up.
- Pure C# approach with `scrollTop` interop → rejected because it requires polling and doesn't handle the "user scrolled up" pause.

## R6: Credential Badge UI Pattern

**Decision**: Add MudBlazor `MudChip` badges to action cards in `BlueprintPreview.razor` when `CredentialRequirements` or `CredentialIssuanceConfig` are present.

**Rationale**: MudBlazor chips with icons are the established pattern in Sorcha UI for status indicators. A shield icon for requirements and a certificate icon for issuance provide clear visual distinction.

**Alternatives Considered**:
- MudBadge overlay → rejected because badges are too small for meaningful content.
- Separate credential panel → rejected because credentials are per-action and should be visually associated with their action.
