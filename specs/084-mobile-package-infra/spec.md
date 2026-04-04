# Feature Specification: Mobile Package Infrastructure

**Feature Branch**: `084-mobile-package-infra`
**Created**: 2026-04-04
**Status**: Draft
**Input**: User description: "Mobile Package Infrastructure — Extract Sorcha.Wallet.Portable and Sorcha.ServiceClients.Http, set up NuGet.org publishing pipeline, unblock SorchaMobile consumption."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Mobile Developer Consumes Wallet Package (Priority: P1)

A developer working on SorchaMobile needs to perform HD key derivation, mnemonic handling, and wallet address generation on the mobile device. They add a portable wallet package from NuGet.org that contains wallet entities, enums, derivation logic, and crypto interfaces — without pulling in any server-side database or hosting dependencies. The package installs cleanly on a .NET 10 MAUI project with zero dependency conflicts.

**Why this priority**: The mobile app cannot be built without access to the wallet domain model and key derivation logic. This is the primary blocker for SorchaMobile development.

**Independent Test**: Can be fully tested by creating a new .NET 10 MAUI project, adding the portable wallet package from NuGet.org, and verifying that wallet entities, enums, key derivation, and mnemonic handling all work without any server-side dependency errors.

**Acceptance Scenarios**:

1. **Given** a new .NET 10 MAUI project, **When** the developer adds the portable wallet package from NuGet.org, **Then** the package installs without dependency conflicts and does not bring in any database, hosting, or server-side packages.
2. **Given** the portable wallet package is installed, **When** the developer uses the derivation path builder with an organisation ID, user ID, and key usage, **Then** a deterministic BIP32 derivation path is generated matching the Sorcha path structure.
3. **Given** the portable wallet package is installed, **When** the developer accesses wallet entities and enums, **Then** all domain types (Wallet, OrgMasterKey, DerivedKeyRecord, KeyUsage, CustodyMode, etc.) are available without compilation errors.
4. **Given** the portable wallet package is installed, **When** the developer references service interfaces, **Then** all wallet service interfaces are available for implementation or mocking in the mobile app.

---

### User Story 2 - Mobile Developer Consumes REST Client Package (Priority: P1)

A developer working on SorchaMobile needs to communicate with the Sorcha API Gateway over REST and receive real-time updates via SignalR. They add an HTTP service clients package from NuGet.org that contains all REST clients and a shared SignalR hub connection helper — without pulling in any gRPC or server-side protocol dependencies. The package provides pre-built clients for every Sorcha service accessible via the API Gateway.

**Why this priority**: The mobile app communicates exclusively via REST and SignalR through the API Gateway. Without a clean HTTP-only client package, the mobile app would either duplicate client code or pull in unnecessary gRPC dependencies that increase app size and cause build conflicts on mobile platforms.

**Independent Test**: Can be tested by creating a .NET 10 MAUI project, adding the HTTP client package, and verifying that all service clients (wallet, register, blueprint, participant, etc.) and the SignalR hub helper are available without gRPC dependency errors.

**Acceptance Scenarios**:

1. **Given** a new .NET 10 MAUI project, **When** the developer adds the HTTP service clients package from NuGet.org, **Then** the package installs without pulling in any gRPC, protobuf, or server-hosting dependencies.
2. **Given** the HTTP client package is installed, **When** the developer registers the HTTP service clients in dependency injection, **Then** all REST clients (wallet, register, blueprint, participant, subscription, validator, events, passkey) are available.
3. **Given** the HTTP client package is installed, **When** the developer uses the SignalR hub connection helper with a JWT token provider, **Then** a hub connection is created with automatic reconnection and JWT authentication.
4. **Given** the HTTP client package is installed, **When** the developer calls any service client method, **Then** the request is routed as a standard HTTP call to the configured API Gateway base URL.

---

### User Story 3 - Automated Package Publishing on Release (Priority: P1)

When the team merges code to master or tags a release, all portable packages are automatically built, tested, and published to NuGet.org. The mobile development team can immediately consume the latest versions without manual intervention. Pre-release versions are published on every merge to master; stable versions are published when a version tag is pushed.

**Why this priority**: Without an automated pipeline, packages must be built and published manually, creating friction and delays for the mobile team. The pipeline ensures packages are always available and up-to-date.

**Independent Test**: Can be tested by pushing a commit to master and verifying that packages appear on NuGet.org within minutes, with correct version numbers and metadata.

**Acceptance Scenarios**:

1. **Given** code is merged to master, **When** the packaging pipeline runs, **Then** all 9 designated packages are built, tested, and published to NuGet.org as pre-release versions.
2. **Given** a version tag is pushed (e.g., v1.2.0), **When** the packaging pipeline runs, **Then** all packages are published with the stable version matching the tag.
3. **Given** the pipeline publishes packages, **When** the mobile developer checks NuGet.org, **Then** the packages appear with correct metadata (name, description, license, repository URL, SourceLink for debugging).
4. **Given** a build or test failure occurs during packaging, **When** the pipeline detects the failure, **Then** no packages are published and the team is notified of the failure.
5. **Given** the published packages, **When** the mobile developer adds them to a project, **Then** SourceLink allows stepping into Sorcha source code during debugging.

---

### User Story 4 - Existing Server Projects Continue Working (Priority: P1)

All existing Sorcha services and test projects continue to build, run, and pass tests after the package extraction. Server-side projects that previously referenced the monolithic packages now reference either the portable package, the full package (which includes the portable one transitively), or both — with zero behaviour change.

**Why this priority**: The extraction must not break the existing platform. All 638+ existing tests must continue to pass. This is a non-negotiable constraint on the refactoring.

**Independent Test**: Can be tested by running the full solution build and complete test suite after the extraction, verifying zero regressions.

**Acceptance Scenarios**:

1. **Given** the package extraction is complete, **When** the full solution is built, **Then** it compiles with zero errors.
2. **Given** the package extraction is complete, **When** the full test suite runs, **Then** all previously passing tests continue to pass.
3. **Given** a server project previously referenced wallet entities from the monolithic package, **When** the extraction is complete, **Then** those entities are still accessible (either directly from the portable package or transitively through the full package).
4. **Given** a server project previously used gRPC clients, **When** the extraction is complete, **Then** gRPC clients remain available through the full service clients package.

---

### Edge Cases

- What happens when a mobile developer references both the portable wallet package and the full wallet package? The full package includes the portable one transitively — no duplication or version conflict should occur.
- What happens when the NuGet API key expires or is revoked? The packaging pipeline fails and notifies the team. No packages are published. The key must be rotated in repository secrets.
- What happens when a package dependency version changes in the main solution? The next pipeline run publishes updated packages with the new dependency version. SorchaMobile updates by bumping the package version.
- What happens when a mobile developer needs a type that was intentionally left in the server-only package? They cannot access it — they must request it be moved to the portable package or find a mobile-appropriate alternative.
- What happens when SourceLink is enabled but the mobile developer doesn't have access to the GitHub repository? SourceLink degrades gracefully — debugging works but source stepping is unavailable. The packages themselves function normally.

## Requirements *(mandatory)*

### Functional Requirements

**Portable Wallet Package:**

- **FR-001**: System MUST provide a portable wallet package containing all wallet domain entities, enums, service interfaces, and key derivation logic without any database, hosting, or server-framework dependencies.
- **FR-002**: System MUST include the derivation path builder in the portable package, enabling mobile clients to construct Sorcha-specific BIP32 derivation paths locally.
- **FR-003**: The portable wallet package MUST depend only on the cryptography package and HD wallet library — no database drivers, ORM frameworks, or server hosting packages.
- **FR-004**: The full wallet package MUST reference the portable wallet package, providing transitive access to all portable types for existing server projects.

**HTTP Service Clients Package:**

- **FR-005**: System MUST provide an HTTP service clients package containing all REST API clients and authentication helpers without any gRPC or server-protocol dependencies.
- **FR-006**: The HTTP service clients package MUST include a shared SignalR hub connection helper that configures JWT authentication and automatic reconnection with exponential backoff.
- **FR-007**: The HTTP service clients package MUST provide a dependency injection registration method for all HTTP clients that can be used independently of the gRPC registrations.
- **FR-008**: The full service clients package MUST reference the HTTP package, providing transitive access to all HTTP clients for existing server projects.

**NuGet Publishing Pipeline:**

- **FR-009**: System MUST automatically build, test, and publish all 9 designated packages to NuGet.org when code is merged to master (as pre-release) or when a version tag is pushed (as stable release).
- **FR-010**: All published packages MUST include correct metadata: package name, description, MIT license, repository URL, and SourceLink for source-level debugging.
- **FR-011**: The pipeline MUST NOT publish any packages if the build or test step fails.
- **FR-012**: All packages MUST share a single version derived from git tags, ensuring consistent versioning across the package set.

**Backward Compatibility:**

- **FR-013**: All existing server projects MUST continue to compile and function correctly after the extraction, with zero changes required to their source code.
- **FR-014**: All existing tests MUST continue to pass after the extraction with no modifications.

### Key Entities

- **Portable Wallet Package**: A distributable package containing wallet domain types (entities, enums, interfaces) and derivation logic. No server dependencies. Consumed by both SorchaMobile and the server-side full wallet package.
- **HTTP Service Clients Package**: A distributable package containing REST API clients, authentication helpers, and SignalR connection builder. No gRPC dependencies. Consumed by both SorchaMobile and the server-side full service clients package.
- **NuGet Publishing Pipeline**: An automated workflow that builds, validates, and publishes packages to a public registry on code changes and release tags.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new .NET 10 MAUI project can install the portable wallet package and use derivation path builder, entities, and enums with zero server-side dependency errors.
- **SC-002**: A new .NET 10 MAUI project can install the HTTP client package and call all REST service clients and the SignalR hub helper with zero gRPC dependency errors.
- **SC-003**: All 9 packages are published to the public package registry within 10 minutes of a merge to master.
- **SC-004**: All 638+ existing tests continue to pass after the extraction with zero modifications.
- **SC-005**: The portable wallet package has zero dependencies on database drivers, ORM frameworks, or server hosting packages — verifiable by inspecting the package dependency tree.
- **SC-006**: The HTTP client package has zero dependencies on gRPC or protocol buffer packages — verifiable by inspecting the package dependency tree.
- **SC-007**: Published packages include SourceLink, enabling source-level debugging from consuming projects.
- **SC-008**: Pre-release packages are published automatically on every merge to master; stable packages are published on version tag push.
- **SC-009**: The mobile development team can go from zero to a working SorchaMobile project with wallet derivation and API calls within 30 minutes of package availability.

## Assumptions

- SorchaMobile targets .NET 10, eliminating the need for multi-target (net8.0;net10.0) packaging.
- The existing NuGet.org API key in the repository secrets is valid and has publish permissions.
- All wallet domain entities use fluent API configuration in the DbContext (not data annotations on the entities), making them safe to extract without the ORM dependency.
- The gRPC clients in ServiceClients are only used for server-to-server communication and are never needed by mobile consumers.
- The existing Directory.Build.props in src/Common/ provides shared package metadata (authors, license, repository URL) that will be inherited by new projects.
- SignalR client packages are lightweight and mobile-compatible, with no server-side hosting dependencies.
- The SorchaMobile repository is a separate Git repository that consumes Sorcha packages via NuGet, not project references.
