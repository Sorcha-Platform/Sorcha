# Feature Specification: Digital Product Passport (DPP) Schema Provider

**Feature Branch**: `056-dpp-schema-provider`
**Created**: 2026-03-12
**Status**: Draft
**Input**: User description: "Digital Product Passport (DPP) External Schema Provider — A new external schema provider that fetches DPP-related JSON schemas from UNTP, Battery Pass, and Catena-X/Tractus-X repositories. Schemas tagged with 'sustainability' sector plus appropriate cross-tags."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover DPP Schemas in Schema Library (Priority: P1)

A blueprint designer needs to define data fields for a product compliance workflow. They open the Schema Library, filter by the "Sustainability" sector, and see Digital Product Passport schemas from UNTP, Battery Pass, and Catena-X sources. They select the UNTP Digital Product Passport schema, view its field hierarchy, and use it as the data schema for their blueprint action.

**Why this priority**: Without discoverable DPP schemas, users cannot build sustainability-focused workflows. This is the core value proposition — making standards-based DPP data structures available for blueprint design.

**Independent Test**: Can be fully tested by navigating to the Schema Library page, selecting the Sustainability sector filter, and confirming DPP schemas appear with correct metadata, fields, and source attribution.

**Acceptance Scenarios**:

1. **Given** the DPP provider has been refreshed, **When** a user opens the Schema Library and filters by "Sustainability", **Then** they see DPP schemas from UNTP, Battery Pass, and Catena-X sources with titles, descriptions, and sector tags displayed
2. **Given** a UNTP Digital Product Passport schema is listed, **When** the user clicks to view details, **Then** they see the full field hierarchy, source attribution (UNTP/UN CEFACT), version information, and a link to the source specification
3. **Given** DPP schemas exist in the library, **When** a user searches for "battery passport", **Then** the Battery Pass schemas appear in results ranked by relevance
4. **Given** a DPP schema is available, **When** a user uses it in the blueprint designer, **Then** the schema's fields are available for data mapping in blueprint actions

---

### User Story 2 - Monitor DPP Provider Health (Priority: P2)

A system administrator navigates to the Schema Providers admin page to check that the DPP provider is healthy and fetching schemas correctly. They see the provider's status (healthy/degraded/unavailable), the number of schemas fetched, last successful fetch time, and any errors. They can trigger a manual refresh if needed.

**Why this priority**: Administrators need visibility into whether the external DPP sources are reachable and returning valid schemas, especially since these standards are evolving (UNTP is pre-v1.0). This builds on the existing provider health infrastructure.

**Independent Test**: Can be tested by navigating to the admin Schema Providers page and confirming the DPP provider card displays status, schema count, last fetch timestamp, and that the manual refresh button triggers a fetch.

**Acceptance Scenarios**:

1. **Given** the three DPP providers are registered, **When** an administrator views the Schema Providers page, **Then** they see provider cards for `dpp-untp`, `dpp-batterypass`, and `dpp-catenax`, each with independent health status, schema count, and last fetch time
2. **Given** one DPP provider (e.g., `dpp-untp`) is unreachable, **When** it refreshes, **Then** that provider shows "Unavailable" status while the other two DPP providers continue to operate independently
3. **Given** a DPP provider is in an unavailable state, **When** the administrator clicks "Refresh" on that provider, **Then** the system re-attempts that source and updates its status accordingly
4. **Given** a DPP provider has failed consecutively, **When** the next automatic refresh occurs, **Then** the system applies exponential backoff to that provider while the other DPP providers refresh normally

---

### User Story 3 - Cross-Sector Schema Discovery (Priority: P2)

A user building a supply chain workflow filters the Schema Library by "Commerce & Trade" sector. Among the results, they also see DPP schemas that are cross-tagged with both "Sustainability" and "Commerce & Trade" (e.g., UNTP Digital Product Passport, GS1-aligned traceability schemas). This allows them to discover relevant DPP schemas without explicitly knowing to look under "Sustainability".

**Why this priority**: DPP schemas are inherently cross-sector — a battery passport is relevant to manufacturing, a product passport to commerce, a construction materials passport to construction. Cross-tagging ensures discoverability from any relevant sector entry point.

**Independent Test**: Can be tested by filtering the Schema Library by non-sustainability sectors (commerce, construction) and confirming that relevant DPP schemas appear alongside other sector schemas.

**Acceptance Scenarios**:

1. **Given** DPP schemas are loaded with cross-sector tags, **When** a user filters by "Commerce & Trade", **Then** they see DPP schemas tagged with both "sustainability" and "commerce" (e.g., UNTP Digital Product Passport)
2. **Given** Battery Pass schemas are loaded, **When** a user filters by "Construction & Planning", **Then** they see battery/materials-related DPP schemas relevant to construction
3. **Given** a user selects multiple sector filters including "Sustainability", **When** the results load, **Then** DPP schemas matching any of the selected sectors are included

---

### User Story 4 - DPP Schema Versioning Awareness (Priority: P3)

A user viewing a DPP schema in the Schema Library sees version information and maturity indicators. Pre-release schemas (e.g., UNTP v0.6.x) are clearly labelled as draft/pre-release, distinguishing them from stable releases. When a new version of a schema is fetched during a refresh, the previous version is deprecated (not deleted) so existing blueprints continue to work.

**Why this priority**: DPP standards are rapidly evolving. Users need to understand which schemas are stable vs. draft, and existing blueprints must not break when a standard publishes a new version.

**Independent Test**: Can be tested by confirming version labels and maturity badges appear on DPP schema detail pages, and that a simulated version update results in the old schema being deprecated while the new version is active.

**Acceptance Scenarios**:

1. **Given** a UNTP schema at version 0.6.1 is loaded, **When** a user views its details, **Then** the version "0.6.1" and a "Pre-release" maturity indicator are displayed
2. **Given** a provider refresh fetches UNTP schema v0.7.0, **When** the refresh completes, **Then** the v0.6.1 schema is deprecated (still accessible) and v0.7.0 is the active version
3. **Given** a blueprint references a deprecated DPP schema version, **When** the blueprint is opened, **Then** the schema still resolves correctly (no breaking change)

---

### Edge Cases

- What happens when all three DPP sources (UNTP, Battery Pass, Catena-X) are simultaneously unreachable? Provider status shows "Unavailable", previously cached schemas remain accessible, and the system retries on the next scheduled refresh with exponential backoff.
- What happens when a source returns invalid JSON Schema (malformed or non-compliant)? The provider logs a warning, skips the invalid schema, and reports the error in the provider status. Valid schemas from other sources are still imported.
- What happens when a source returns schemas with no version identifier? The provider assigns a version based on the fetch date (e.g., "2026-03-12") and tags the schema as "unversioned" in metadata.
- What happens when the same conceptual schema (e.g., "Digital Product Passport") exists in multiple sources (UNTP and Catena-X)? Each source's version is imported as a separate schema entry, distinguished by source attribution, allowing users to choose the one that fits their needs.
- What happens when a source changes its API endpoint or URL structure? The provider detects repeated failures, enters backoff, and the admin is alerted via the provider health page. Configuration allows administrators to update source URLs without code changes.
- What happens with GitHub API rate limiting? GitHub-hosted providers (`dpp-batterypass`, `dpp-catenax`) use raw content URLs (`raw.githubusercontent.com`) which are not subject to GitHub API rate limits, avoiding this concern entirely.

## Clarifications

### Session 2026-03-14

- Q: Should DPP be implemented as one provider with internal sub-source tracking, or three separate providers reusing existing per-provider health infrastructure? → A: Three separate providers (`dpp-untp`, `dpp-batterypass`, `dpp-catenax`), each implementing `IExternalSchemaProvider` independently. This reuses existing health/backoff infrastructure without extension.
- Q: How should non-JSON-Schema source formats (e.g., Catena-X SAMM/Turtle models) be handled? → A: Only fetch schemas already in JSON Schema format from each source; skip non-JSON-Schema files. SAMM-to-JSON-Schema conversion is deferred to a future enhancement.
- Q: How should versioned schemas be uniquely identified to support version coexistence (FR-012)? → A: Encode version in `SourceUri` (e.g., `untp/dpp/0.6.1/DigitalProductPassport`). Each version is a distinct index entry. Provider marks old-version entries as deprecated during refresh. No storage layer changes required.
- Q: Should schema content be persisted beyond in-memory cache for offline resilience? → A: In-memory cache only, matching existing provider patterns (`SchemaStoreOrgProvider`, `FhirSchemaProvider`). If the source is offline, `GetSchemaAsync` returns null. Persistent caching can be added later if needed.
- Q: How should GitHub-hosted sources (Battery Pass, Catena-X) be fetched to avoid API rate limits? → A: Use raw content URLs (`raw.githubusercontent.com`) which bypass GitHub API rate limits entirely. Requires known file paths or a tree listing, but avoids API complexity and authentication.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide three external schema providers (`dpp-untp`, `dpp-batterypass`, `dpp-catenax`), each implementing the existing `IExternalSchemaProvider` interface independently. Together they form the DPP schema family.
- **FR-002**: System MUST fetch schemas from three sources: UNTP vocabulary endpoint (`dpp-untp` provider), Battery Pass GitHub repository via raw content URLs (`dpp-batterypass` provider), and Eclipse Tractus-X semantic models repository via raw content URLs (`dpp-catenax` provider)
- **FR-003**: System MUST add a new "Sustainability" sector to the platform-curated sector list, with an appropriate display name, description, and icon
- **FR-004**: System MUST tag all DPP schemas with the "sustainability" sector as a primary tag
- **FR-005**: System MUST cross-tag DPP schemas with appropriate additional sectors based on their content domain:
  - Battery Pass schemas: "sustainability" + "commerce" + "construction"
  - UNTP Digital Product Passport: "sustainability" + "commerce"
  - Catena-X automotive/manufacturing models: "sustainability" + "commerce"
  - Catena-X construction materials: "sustainability" + "construction"
  - Carbon footprint schemas: "sustainability" + "government" (regulatory compliance)
- **FR-006**: System MUST support searching DPP schemas by keyword, returning results from all three sources ranked by relevance
- **FR-007**: System MUST display each DPP provider (`dpp-untp`, `dpp-batterypass`, `dpp-catenax`) on the admin Schema Providers health page with independent status, schema count, last fetch time, and error details
- **FR-008**: System MUST support manual refresh of each DPP provider independently from the admin page
- **FR-009**: System MUST apply exponential backoff per DPP provider when that provider's source fails consecutively, while other DPP providers continue to refresh independently (handled by existing per-provider backoff infrastructure)
- **FR-010**: System MUST preserve schema version information from source metadata and display it on schema detail pages
- **FR-011**: System MUST indicate schema maturity (stable vs. pre-release/draft) based on version numbering conventions (semantic versioning < 1.0.0 = pre-release)
- **FR-012**: System MUST deprecate (not delete) previous schema versions when a newer version of the same schema is fetched, ensuring backward compatibility for existing blueprints. Each version uses a distinct `SourceUri` (version-encoded), and the provider marks old-version entries as deprecated during refresh
- **FR-013**: System MUST normalise fetched DPP schemas to JSON Schema draft-2020-12 format using the existing normalisation pipeline. Providers MUST only fetch files already in JSON Schema format; non-JSON-Schema formats (e.g., SAMM/Turtle, JSON-LD vocabularies) are skipped
- **FR-014**: System MUST include source attribution metadata for each DPP schema (governing body, specification URL, source repository)
- **FR-015**: System MUST allow administrators to configure source URLs via application configuration without requiring code changes
- **FR-016**: System MUST gracefully degrade when individual sources are unavailable — schemas from healthy sources continue to be served

### Key Entities

- **DPP Schema Providers**: Three independent provider instances (`dpp-untp`, `dpp-batterypass`, `dpp-catenax`), each registered in the schema index system with its own health status, backoff state, and refresh schedule. Together they form the DPP schema family.
- **DPP Source**: The upstream origin for each provider — UNTP vocabulary endpoint, Battery Pass GitHub repository, or Catena-X/Tractus-X GitHub repository. Each provider maps to exactly one source.
- **Sustainability Sector**: A new platform-curated sector added to the existing 8 sectors. Used as primary tag for all DPP schemas.
- **Schema Version**: Version metadata attached to each DPP schema entry, including version string, maturity indicator (stable/pre-release), and relationship to previous versions of the same schema. Each version is a distinct index entry identified by a version-encoded `SourceUri`; previous versions are deprecated (not deleted) when a newer version is fetched.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can discover DPP schemas by filtering the Schema Library by "Sustainability" sector within 2 seconds of page load
- **SC-002**: At least 10 DPP schemas are available from across the three sources after initial provider refresh
- **SC-003**: Schema detail pages display complete field hierarchies, version information, maturity indicators, and source attribution for all DPP schemas
- **SC-004**: When one source is unavailable, schemas from the remaining two sources are still discoverable with no user-facing errors
- **SC-005**: Administrators can view provider health status and trigger manual refresh, seeing updated results within 30 seconds
- **SC-006**: DPP schemas appear in search results when users search by relevant terms (e.g., "product passport", "battery", "carbon footprint") from any sector filter
- **SC-007**: Existing blueprints referencing a DPP schema continue to function after the schema is updated to a newer version (backward compatibility)
- **SC-008**: Cross-sector tagging results in DPP schemas appearing under at least 3 different sector filters (sustainability, commerce, construction)

## Assumptions

- The UNTP vocabulary endpoint (`test.uncefact.org/vocabulary/untp/dpp/0/`) remains publicly accessible without authentication. If it moves or requires auth, the admin can update the URL in configuration.
- Battery Pass and Catena-X GitHub repositories host JSON Schema files in predictable directory structures (e.g., `/gen` folders for generated schemas), fetched via `raw.githubusercontent.com` URLs to avoid GitHub API rate limits. Changes to repository structure may require provider updates.
- The existing schema normalisation pipeline (JSON Schema draft-2020-12) is used only for files already in JSON Schema format. Non-JSON-Schema formats (SAMM/Turtle, JSON-LD vocabularies) are skipped; SAMM-to-JSON-Schema conversion is out of scope for this feature.
- Semantic versioning conventions are followed by the standards bodies (versions < 1.0.0 = pre-release). Where version strings don't follow SemVer, the system falls back to treating them as stable.
- The existing exponential backoff and provider health infrastructure is reused as-is, with each DPP source registered as an independent provider (`dpp-untp`, `dpp-batterypass`, `dpp-catenax`).
- Cross-sector tag assignments (FR-005) are initial defaults based on current understanding of each schema's domain applicability. These can be refined through admin configuration or future schema metadata improvements.

## Dependencies

- Existing `IExternalSchemaProvider` interface and provider registration infrastructure
- Existing `SchemaSector` model and sector filtering in Schema Library
- Existing `SchemaIndexRefreshService` for background refresh orchestration
- Existing `JsonSchemaNormaliser` for draft-2020-12 normalisation
- External network access to UNTP, GitHub (Battery Pass and Catena-X repositories)
