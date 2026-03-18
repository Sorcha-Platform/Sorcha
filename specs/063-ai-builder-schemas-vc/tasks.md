# Tasks: AI Blueprint Builder Enhancement — Schema Library, VC/DPP Integration, and UX Overhaul

**Input**: Design documents from `/specs/063-ai-builder-schemas-vc/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/tool-definitions.md, quickstart.md

**Tests**: Included (per constitution requirement: >85% coverage for new code)

**Organization**: Tasks grouped by user story. US6 (Chat UI) has no dependencies on other stories. US1 (Schemas) is foundational for US2-US5. US3/US4 (Credentials) depend on US1. US5 (DPP) depends on US3/US4.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US6)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Create directory structure and schema file format foundation

- [x] T001 Create schema directory structure: `blueprints/schemas/{people-identity,financial,documents-evidence,compliance-governance,supply-chain,healthcare,credentials}/`
- [x] T002 Create example schema file `blueprints/schemas/people-identity/uk-address.json` with full format (identifier, title, description, version, category, tags, keywords, schema, formLayout, disclosure) per data-model.md

---

## Phase 2: Foundational (Schema Library Infrastructure)

**Purpose**: Schema seeding service and storage — MUST complete before AI tools can query schemas

**CRITICAL**: No schema-aware AI tools can function until this phase is complete

- [x] T003 Create `SchemaSeedService.cs` in `src/Services/Sorcha.Blueprint.Service/Services/` — IHostedService that scans `blueprints/schemas/` subdirectories for `*.json` files, deserialises to schema file format, maps to `SchemaEntry`, and upserts via `ISchemaStore`. Follow `TemplateSeedService` pattern: directory walkup resolution, version-based idempotency, per-file error logging, summary log on completion
- [x] T004 Register `SchemaSeedService` as `IHostedService` in `src/Services/Sorcha.Blueprint.Service/Program.cs` after schema store registration
- [x] T005 Write `SchemaSeedServiceTests.cs` in `tests/Sorcha.Blueprint.Service.Tests/Services/` — tests: seeds from directory, skips existing same-version, upserts newer version, skips invalid JSON, handles missing directory, handles empty directory, logs summary counts (~7 tests)

**Checkpoint**: Schema seeding infrastructure ready. `dotnet test tests/Sorcha.Blueprint.Service.Tests/ --filter SchemaSeedService` passes.

---

## Phase 3: User Story 1 — AI Uses Standardised Schemas (Priority: P1) MVP

**Goal**: AI can search, describe, and apply standardised schemas to blueprint actions. System prompt has ambient schema awareness.

**Independent Test**: Open chat designer, ask "what schemas do you have?", verify AI lists schemas by category. Ask to create a workflow with address data, verify AI suggests and applies UK Address schema.

### Schema Files for US1

- [x] T006 [P] [US1] Author People & Identity schemas in `blueprints/schemas/people-identity/`: `uk-address.json` (5 fields: addressLine1, addressLine2, city, county, postcode with regex), `international-address.json` (6 fields: + country ISO 3166), `contact-details.json` (4 fields: fullName, email format, phone pattern, organisation), `personal-identity.json` (4 fields: fullName, dateOfBirth date, nationalInsuranceNumber pattern, sensitive=[nationalInsuranceNumber]), `company-identity.json` (5 fields: companyName, registrationNumber, vatNumber, sicCode, registeredAddress)
- [x] T007 [P] [US1] Author Financial schemas in `blueprints/schemas/financial/`: `payment-details.json` (4 fields: amount number min 0, currency ISO 4217 enum, reference, paymentDate), `invoice-line-item.json` (5 fields: description, quantity integer, unitPrice number, vatRate number, lineTotal number), `bank-account.json` (4 fields: sortCode pattern, accountNumber pattern, accountName, sensitive=[sortCode, accountNumber])
- [x] T008 [P] [US1] Author Documents & Evidence schemas in `blueprints/schemas/documents-evidence/`: `document-upload.json` (4 fields: file type, documentType enum, description, version), `signature-block.json` (4 fields: signatoryName, role, signatureDate date, digitalSignatureRef), `audit-entry.json` (4 fields: actionTaken, performedBy, timestamp date-time, notes)
- [x] T009 [P] [US1] Author Compliance & Governance schemas in `blueprints/schemas/compliance-governance/`: `risk-assessment.json` (4 fields: riskLevel enum [low/medium/high/critical], likelihood integer 1-5, impact integer 1-5, mitigationPlan), `approval-decision.json` (3 fields: decision enum [approved/rejected/deferred], rationale, conditions), `due-diligence-check.json` (4 fields: checkType, outcome enum [pass/fail/pending], evidenceReference, expiryDate date)
- [x] T010 [P] [US1] Author Supply Chain schemas in `blueprints/schemas/supply-chain/`: `product-item.json` (5 fields: sku, description, quantity integer, unit enum, batchLotNumber), `shipment-details.json` (5 fields: origin, destination, carrier, trackingReference, estimatedDelivery date), `inspection-record.json` (5 fields: inspector, inspectionDate date, result enum [pass/fail], defectCodes array, photos file)
- [x] T011 [P] [US1] Author Healthcare schemas in `blueprints/schemas/healthcare/`: `patient-reference.json` (3 fields: nhsNumber pattern, patientName, dateOfBirth date, sensitive=[nhsNumber, dateOfBirth]), `clinical-observation.json` (5 fields: observationType, value, units, observationDate date-time, practitioner)

### AI Tools for US1

- [x] T012 [US1] Add `search_schemas` tool to `src/Services/Sorcha.Blueprint.Service/Services/BlueprintToolExecutor.cs` — new `ToolDefinition` per contracts/tool-definitions.md, `ExecuteSearchSchemas` method that queries `ISchemaStore.ListAsync()` with search term and optional category filter, returns schema summaries (identifier, title, category, description, fieldCount, fieldNames, tags). Inject `ISchemaStore` into `BlueprintToolExecutor` constructor
- [x] T013 [US1] Add `use_standard_schema` tool to `src/Services/Sorcha.Blueprint.Service/Services/BlueprintToolExecutor.cs` — `ExecuteUseStandardSchema` method that loads schema via `ISchemaStore.GetByIdentifierAsync()`, extracts JSON Schema from Content, merges into action's DataSchemas via builder, applies formLayout to action's Form (Control), returns applied fields list and disclosure recommendation. Handle merge=true (default) vs replace
- [x] T014 [US1] Write tests for `search_schemas` tool in `tests/Sorcha.Blueprint.Service.Tests/Services/BlueprintToolExecutorTests.cs` — tests: search by query returns matching schemas, filter by category, no results returns empty, handles ISchemaStore errors (~4 tests)
- [x] T015 [US1] Write tests for `use_standard_schema` tool in `tests/Sorcha.Blueprint.Service.Tests/Services/BlueprintToolExecutorTests.cs` — tests: applies schema to action, merges with existing fields, replaces when merge=false, schema not found returns error, action not found returns error (~5 tests)

### System Prompt for US1

- [x] T016 [US1] Rewrite system prompt and make `BuildSystemPrompt()` dynamic in `src/Services/Sorcha.Blueprint.Service/Services/ChatOrchestrationService.cs` — replace the static `SystemPrompt` const with a dynamic method that: (1) builds base prompt with professional/inquisitive personality per research.md R3, (2) queries `ISchemaStore.ListAsync()` for all active schemas and appends compact summary table (one line per schema: identifier | title | category), (3) queries `IBlueprintTemplateService.GetPublishedTemplatesAsync()` for all templates and appends compact summary. Inject `ISchemaStore` and `IBlueprintTemplateService` into constructor. System prompt structure: role → conversation workflow → tools → schema table → template table → blueprint rules → data types → disclosure best practices
- [x] T017 [US1] Write tests for dynamic system prompt in `tests/Sorcha.Blueprint.Service.Tests/Services/ChatOrchestrationServiceTests.cs` — tests: prompt includes schema summary table, prompt includes template summary, prompt stays under 4000 tokens estimate, prompt includes all 13 tool references (~4 tests)

**Checkpoint**: AI can search and apply standardised schemas. `dotnet test tests/Sorcha.Blueprint.Service.Tests/ --filter "SearchSchemas or UseStandardSchema or SystemPrompt"` passes.

---

## Phase 4: User Story 2 — Professional, Inquisitive Conversation Flow (Priority: P1)

**Goal**: AI follows consultative conversation flow — clarify → confirm → propose → checkpoint → build → validate → save.

**Independent Test**: Open chat, type "I need a permit process", verify AI asks clarifying questions before calling any tools. Verify it confirms participant roster and proposes minimal disclosure before building.

- [x] T018 [US2] Update system prompt conversation workflow section in `src/Services/Sorcha.Blueprint.Service/Services/ChatOrchestrationService.cs` — replace the "CALL TOOLS IMMEDIATELY" instructions with the consultative flow: (1) understand intent — ask about the problem being solved, (2) confirm participants and motives — name each participant and their role, (3) propose schema choices — suggest standardised schemas from the library, (4) suggest credentials — if workflow implies proof or attestation, (5) confirm disclosure approach — default to minimal, state what each participant sees, (6) checkpoint — present summary and wait for confirmation before calling tools, (7) validate and offer save. Include example conversations demonstrating the inquisitive style. Remove the "IMMEDIATELY CALL" language
- [x] T019 [US2] Add `search_templates` tool to `src/Services/Sorcha.Blueprint.Service/Services/BlueprintToolExecutor.cs` — `ExecuteSearchTemplates` method that queries `IBlueprintTemplateService.GetPublishedTemplatesAsync()`, filters by title/description/category/tags matching search query, returns template summaries (id, title, category, description, version, participantCount, actionCount). Inject `IBlueprintTemplateService` into constructor
- [x] T020 [US2] Write tests for `search_templates` tool in `tests/Sorcha.Blueprint.Service.Tests/Services/BlueprintToolExecutorTests.cs` — tests: search by query, filter by category, no results, template with participant/action counts (~4 tests)

**Checkpoint**: System prompt enforces consultative flow. AI no longer races to call tools. `dotnet test tests/Sorcha.Blueprint.Service.Tests/ --filter SearchTemplates` passes.

---

## Phase 5: User Story 3 — Credential Requirements (Priority: P2)

**Goal**: AI can add Verified Credential requirements to blueprint actions via `require_credential` tool. Preview shows credential badges.

**Independent Test**: Open chat, request a workflow where applicants need training certification, verify AI suggests adding a credential requirement and the preview shows a credential badge.

**Dependencies**: Requires Phase 2 (schema store for credential schema lookup) and Phase 3 (system prompt with credential awareness)

### Credential Schema Files

- [x] T021 [P] [US3] Author Credential schemas in `blueprints/schemas/credentials/`: `training-certificate.json` (claims: courseName, completionDate, grade, institution), `professional-license.json` (claims: licenseType, licenseNumber, issuingAuthority, validFrom, validTo), `right-to-work.json` (claims: documentType, nationality, expiryDate), `identity-verification.json` (claims: fullName, dateOfBirth, verificationMethod, verifiedAt), `inspection-certificate.json` (claims: inspectionType, result, inspector, validUntil), `approval-attestation.json` (claims: approvalType, approvedBy, approvalDate, conditions)

### Tool Implementation

- [x] T022 [US3] Add `require_credential` tool to `src/Services/Sorcha.Blueprint.Service/Services/BlueprintToolExecutor.cs` — `ExecuteRequireCredential` method that: builds draft, finds action by ID, creates `CredentialRequirement` with type/acceptedIssuers/requiredClaims/revocationPolicy/description, adds to action's `CredentialRequirements` list, returns confirmation with details. Use existing `CredentialRequirement` and `ClaimConstraint` models from `Sorcha.Blueprint.Models.Credentials`
- [x] T023 [US3] Update `validate_blueprint` in `src/Services/Sorcha.Blueprint.Service/Services/BlueprintToolExecutor.cs` — add validation: if action has `CredentialRequirements`, warn if `AcceptedIssuers` is empty (any issuer accepted — may be intentional), warn if credential type doesn't match a known schema in the credentials category
- [x] T024 [US3] Write tests for `require_credential` tool in `tests/Sorcha.Blueprint.Service.Tests/Services/BlueprintToolExecutorTests.cs` — tests: adds requirement to action, with accepted issuers, with required claims, with revocation policy, action not found error, multiple requirements on same action (~6 tests)

### Preview Enhancement

- [x] T025 [US3] Add credential requirement badge to `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/BlueprintPreview.razor` — when rendering action cards, check if `action.CredentialRequirements` has any entries, if so display a MudChip with shield icon, Color.Warning, showing credential type name (e.g., "Requires: TrainingCertificate")

**Checkpoint**: Credential requirements work end-to-end. Preview shows badges. `dotnet test tests/Sorcha.Blueprint.Service.Tests/ --filter RequireCredential` passes.

---

## Phase 6: User Story 4 — Credential Issuance (Priority: P2)

**Goal**: AI can configure actions to issue Verified Credentials via `issue_credential` tool. Preview shows issuance badges.

**Independent Test**: Open chat, create a training workflow, tell AI to issue a credential on completion, verify claim mappings and preview badge.

**Dependencies**: Requires Phase 5 (credential patterns in system prompt and preview)

- [x] T026 [US4] Add `issue_credential` tool to `src/Services/Sorcha.Blueprint.Service/Services/BlueprintToolExecutor.cs` — `ExecuteIssueCredential` method that: builds draft, finds action by ID, creates `CredentialIssuanceConfig` with credentialType/claimMappings/recipientParticipantId/expiryDuration/usagePolicy, validates recipientParticipantId exists in participants, validates claim source fields use JSON Pointer format, sets on action's `CredentialIssuanceConfig`. Use existing models from `Sorcha.Blueprint.Models.Credentials`
- [x] T027 [US4] Update `validate_blueprint` in `src/Services/Sorcha.Blueprint.Service/Services/BlueprintToolExecutor.cs` — add validation: if action has `CredentialIssuanceConfig`, warn if `RecipientParticipantId` doesn't reference a valid participant, warn if claim source fields reference non-existent data schema fields
- [x] T028 [US4] Write tests for `issue_credential` tool in `tests/Sorcha.Blueprint.Service.Tests/Services/BlueprintToolExecutorTests.cs` — tests: configures issuance on action, with claim mappings, validates recipient exists, validates source fields format, with expiry duration, with usage policy, action not found error (~7 tests)
- [x] T029 [US4] Add credential issuance badge to `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/BlueprintPreview.razor` — when rendering action cards, check if `action.CredentialIssuanceConfig` is not null, if so display a MudChip with certificate icon, Color.Success, showing credential type name (e.g., "Issues: TrainingCompletionCertificate")

**Checkpoint**: Credential issuance works end-to-end. Preview shows both requirement and issuance badges. `dotnet test tests/Sorcha.Blueprint.Service.Tests/ --filter IssueCredential` passes.

---

## Phase 7: User Story 5 — Digital Product Passport Lifecycle (Priority: P3)

**Goal**: AI recognises DPP patterns and configures multi-action credential chains.

**Independent Test**: Open chat, describe a supply chain workflow with "product passport", verify AI creates DPP issuance at first action and requirements at subsequent actions.

**Dependencies**: Requires Phase 5 and Phase 6 (credential requirement + issuance tools)

- [x] T030 [US5] Author `product-passport.json` credential schema in `blueprints/schemas/credentials/` — DPP-specific claims following EU ESPR guidelines: productIdentifier, gtin, materialComposition, carbonFootprint, repairabilityScore, recycledContentPercentage, hazardousSubstances, countryOfManufacture, manufacturerIdentifier. Include disclosure recommendation: all fields publicly readable
- [x] T031 [US5] Add DPP awareness to system prompt in `src/Services/Sorcha.Blueprint.Service/Services/ChatOrchestrationService.cs` — add section explaining DPP pattern: (1) recognise product lifecycle workflows across multiple participants, (2) suggest creating DPP at first action using `issue_credential` with ProductPassport type, (3) suggest requiring DPP at subsequent actions using `require_credential`, (4) explain lifecycle event accumulation pattern, (5) reference EU ESPR compliance. Include example DPP conversation
- [x] T032 [US5] Write integration test for DPP workflow construction in `tests/Sorcha.Blueprint.Service.Tests/Services/BlueprintToolExecutorTests.cs` — test: create blueprint with 4 participants (manufacturer, inspector, shipper, retailer), 4 actions, issue ProductPassport at action 0, require ProductPassport at actions 1-3, validate blueprint passes with consistent credential chain (~2 tests)

**Checkpoint**: DPP workflows can be constructed through AI chat. `dotnet test tests/Sorcha.Blueprint.Service.Tests/ --filter "DPP or ProductPassport"` passes.

---

## Phase 8: User Story 6 — Chat UI Layout Fixed (Priority: P1)

**Goal**: Chat input pinned to bottom, auto-scroll to latest message, layout fills viewport.

**Independent Test**: Send 50+ messages in chat, verify input stays at bottom, messages auto-scroll, no page-level scrollbar.

**Dependencies**: None (independent of backend changes)

- [x] T033 [P] [US6] Fix chat input pinning in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/ChatPanel.razor` — ensure `.input-area` has `flex-shrink: 0` to prevent compression, verify `.messages-area` has `flex: 1` and `overflow-y: auto`, add a `.scroll-sentinel` div at the end of the messages area for auto-scroll detection
- [x] T034 [P] [US6] Create `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/js/chat-scroll.js` — export `initAutoScroll(container)` function using IntersectionObserver on sentinel element: when sentinel is visible (user at bottom) set autoScroll=true, when hidden (user scrolled up) set autoScroll=false. MutationObserver on container childList: when autoScroll is true, call `sentinel.scrollIntoView({ behavior: 'smooth' })`. Export `dispose()` to clean up observers
- [x] T035 [US6] Wire JS interop for auto-scroll in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/ChatPanel.razor` — in `OnAfterRenderAsync(firstRender)`, import `chat-scroll.js` module and call `initAutoScroll` with the messages container ElementReference. On `Dispose`, call the JS `dispose` function. Remove the placeholder `ScrollToBottom` method
- [x] T036 [US6] Fix viewport height calculation in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/BlueprintChat.razor` — verify `.chat-designer-container` height calc accounts for actual MudAppBar height and any padding/margin. Test with browser dev tools at different viewport sizes. Ensure no page-level scrollbar appears

**Checkpoint**: Chat UI layout is correct. Input stays pinned, messages auto-scroll, no page scrollbar.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and cross-story integration verification

- [x] T037 Update `src/Services/Sorcha.Blueprint.Service/README.md` with new AI tools documentation (search_schemas, use_standard_schema, require_credential, issue_credential, search_templates)
- [x] T038 Update `docs/ai-prompts/blueprint-builder-system-prompt.md` with the new system prompt content (sync with the dynamic prompt in ChatOrchestrationService)
- [x] T039 Update `.specify/MASTER-TASKS.md` with feature completion status
- [x] T040 Run full test suite: `dotnet build --force && dotnet test` — verify no regressions across all 30 test projects
- [ ] T041 [P] Write E2E smoke test (deferred — requires Docker) for chat UI in `tests/Sorcha.UI.E2E.Tests/Docker/BlueprintChatTests.cs` — test: navigate to `/designer/chat`, verify connection, send a message, verify input stays at bottom after multiple messages, verify no console errors

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — create directories and example file
- **Phase 2 (Foundational)**: Depends on Phase 1 — schema seeding service
- **Phase 3 (US1 Schemas)**: Depends on Phase 2 — needs seeded schemas to query
- **Phase 4 (US2 Conversation)**: Depends on Phase 3 — needs system prompt rewrite from US1
- **Phase 5 (US3 Credential Req)**: Depends on Phase 3 — needs schema store and system prompt
- **Phase 6 (US4 Credential Issue)**: Depends on Phase 5 — builds on credential UI patterns
- **Phase 7 (US5 DPP)**: Depends on Phase 5 + Phase 6 — needs both credential tools
- **Phase 8 (US6 Chat UI)**: No dependencies — can run in parallel with any phase
- **Phase 9 (Polish)**: Depends on all desired phases being complete

### User Story Dependencies

```
US6 (Chat UI) ──────────────────────────────────────────┐
                                                         │
Phase 1 → Phase 2 → US1 (Schemas) → US2 (Conversation) │
                         │                               ├→ Phase 9 (Polish)
                         └→ US3 (Cred Req) → US4 (Cred Issue) → US5 (DPP)
```

### Parallel Opportunities

- **US6 can run in parallel with everything** (pure UI, no backend dependency)
- **T006-T011 can all run in parallel** (independent schema JSON files)
- **T012 and T013 can run in parallel** (different tool implementations)
- **T021 can run in parallel with T022** (schema files vs tool code)
- **T033 and T034 can run in parallel** (CSS fix vs JS module)

---

## Parallel Example: User Story 1

```bash
# Launch all schema file authoring tasks together:
Task T006: "Author People & Identity schemas in blueprints/schemas/people-identity/"
Task T007: "Author Financial schemas in blueprints/schemas/financial/"
Task T008: "Author Documents & Evidence schemas in blueprints/schemas/documents-evidence/"
Task T009: "Author Compliance & Governance schemas in blueprints/schemas/compliance-governance/"
Task T010: "Author Supply Chain schemas in blueprints/schemas/supply-chain/"
Task T011: "Author Healthcare schemas in blueprints/schemas/healthcare/"

# Then launch tool implementations together:
Task T012: "Add search_schemas tool to BlueprintToolExecutor.cs"
Task T013: "Add use_standard_schema tool to BlueprintToolExecutor.cs"
```

---

## Implementation Strategy

### MVP First (US1 + US6 — Schemas + Chat UI)

1. Complete Phase 1: Setup (directories)
2. Complete Phase 2: Foundational (SchemaSeedService)
3. Complete Phase 3: US1 — AI has schema awareness
4. Complete Phase 8: US6 — Chat UI is usable (in parallel)
5. **STOP and VALIDATE**: AI suggests schemas, chat layout works
6. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Schema infrastructure ready
2. US1 (Schemas) + US6 (Chat UI) → MVP with schema awareness + usable layout
3. US2 (Conversation) → Professional, inquisitive AI personality
4. US3 (Credential Req) → VC requirements on actions
5. US4 (Credential Issue) → VC issuance from actions
6. US5 (DPP) → Digital Product Passport lifecycle patterns
7. Polish → Documentation, E2E tests, final validation

### Task Summary

| Phase | Story | Task Count | Parallel Tasks |
|-------|-------|-----------|----------------|
| Phase 1 | Setup | 2 | 0 |
| Phase 2 | Foundational | 3 | 0 |
| Phase 3 | US1 Schemas | 12 | 8 |
| Phase 4 | US2 Conversation | 3 | 0 |
| Phase 5 | US3 Cred Req | 5 | 1 |
| Phase 6 | US4 Cred Issue | 4 | 0 |
| Phase 7 | US5 DPP | 3 | 0 |
| Phase 8 | US6 Chat UI | 4 | 2 |
| Phase 9 | Polish | 5 | 1 |
| **Total** | | **41** | **12** |

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently testable after its phase completes
- Schema files are the bulk of the work (19 data + 7 credential = 26 files)
- System prompt rewrite spans US1 (schema awareness) and US2 (conversation flow) — US1 does the structural rewrite, US2 refines the personality
- Credential models (`CredentialRequirement`, `CredentialIssuanceConfig`) already exist — tools just expose them
- US6 (Chat UI) is a quick win and can be done first or in parallel
