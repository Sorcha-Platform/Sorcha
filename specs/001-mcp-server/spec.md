# Feature Specification: Sorcha MCP Server

**Feature Branch**: `001-mcp-server`
**Created**: 2026-01-29
**Status**: Draft
**Input**: User description: "Sorcha MCP Server - A Model Context Protocol server that enables AI assistants to interact with the Sorcha decentralised register platform on behalf of three user personas: System Administrators, Workflow Process Designers, and Participants"

## Overview

The Sorcha MCP Server provides a standardized interface for AI assistants (such as Claude, GPT, or other LLM-based tools) to interact with the Sorcha decentralised register platform. By implementing the Model Context Protocol (MCP), the server exposes Sorcha's capabilities as tools and resources that AI assistants can invoke on behalf of authenticated users.

The server supports three distinct user personas, each with access to different capabilities based on their role and permissions within the platform.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - System Administrator Monitors Platform Health (Priority: P1)

A system administrator uses an AI assistant to check the health and status of all Sorcha microservices. The administrator asks the AI to identify any services experiencing issues and provide diagnostic information.

**Why this priority**: Platform health monitoring is critical for maintaining system availability. Administrators need immediate visibility into service status to respond to incidents quickly.

**Independent Test**: Can be fully tested by authenticating as an administrator and invoking health check tools. Delivers immediate value by providing real-time platform status without requiring manual dashboard navigation.

**Acceptance Scenarios**:

1. **Given** an authenticated administrator, **When** requesting platform health status, **Then** the system returns health status for all seven core services (Blueprint, Register, Wallet, Tenant, Validator, Peer, API Gateway) with up/down status, response times, and any error conditions.

2. **Given** an authenticated administrator, **When** a service is degraded or down, **Then** the health check clearly identifies the affected service and provides actionable diagnostic information.

3. **Given** a non-administrator user, **When** attempting to access administrative tools, **Then** the system denies access with a clear permission error.

---

### User Story 2 - Workflow Designer Creates and Validates a Blueprint (Priority: P1)

A workflow process designer uses an AI assistant to create a new blueprint for a multi-party data sharing workflow. The designer describes the workflow in natural language, and the AI generates the blueprint definition, validates it, and analyzes the disclosure rules to ensure data privacy requirements are met.

**Why this priority**: Blueprint creation is the core value proposition of Sorcha. Enabling AI-assisted blueprint design dramatically reduces the learning curve and accelerates workflow deployment.

**Independent Test**: Can be fully tested by creating a blueprint through the MCP tools and verifying it passes validation. Demonstrates end-to-end workflow design capability.

**Acceptance Scenarios**:

1. **Given** an authenticated workflow designer, **When** providing a blueprint definition in JSON or YAML format, **Then** the system creates the blueprint and returns its unique identifier.

2. **Given** an authenticated workflow designer, **When** requesting blueprint validation, **Then** the system checks syntax, semantic correctness, and returns detailed validation results including any errors or warnings.

3. **Given** an authenticated workflow designer with a valid blueprint, **When** requesting disclosure analysis, **Then** the system returns a clear breakdown of what data each participant can see at each action step.

4. **Given** an authenticated workflow designer, **When** simulating blueprint execution with mock data, **Then** the system executes a dry-run and returns the expected flow, calculated values, and disclosure outputs.

---

### User Story 3 - Participant Processes Pending Actions (Priority: P1)

A workflow participant uses an AI assistant to review and respond to pending actions in their inbox. The participant asks the AI to show what actions are waiting, reviews the disclosed data, and submits responses.

**Why this priority**: Participant engagement is essential for workflows to progress. AI-assisted participation reduces friction and enables faster workflow completion.

**Independent Test**: Can be fully tested by listing inbox items, viewing action details, and submitting action data. Delivers immediate value by enabling workflow participation through conversational AI.

**Acceptance Scenarios**:

1. **Given** an authenticated participant with pending actions, **When** requesting inbox contents, **Then** the system returns a list of all actions awaiting their response with action titles, workflow names, and sender information.

2. **Given** an authenticated participant viewing a pending action, **When** requesting action details, **Then** the system returns the disclosed data they are permitted to see, the required response schema, and any instructions.

3. **Given** an authenticated participant with valid response data, **When** submitting action data, **Then** the system validates the data, records the action, and returns confirmation with the next workflow step.

4. **Given** an authenticated participant with invalid response data, **When** submitting action data, **Then** the system returns clear validation errors explaining what data is incorrect or missing.

---

### User Story 4 - Administrator Manages Tenants and Users (Priority: P2)

A system administrator uses an AI assistant to manage tenants (organizations) and users on the platform. This includes creating new tenants, managing user permissions, and handling security incidents such as token revocation.

**Why this priority**: Tenant and user management is important for platform operations but is less frequently used than monitoring or core workflow functions.

**Independent Test**: Can be tested by creating a tenant, adding users, and modifying permissions. Demonstrates administrative control over the platform.

**Acceptance Scenarios**:

1. **Given** an authenticated administrator, **When** creating a new tenant, **Then** the system creates the tenant with the specified name and configuration and returns the tenant identifier.

2. **Given** an authenticated administrator, **When** listing users for a tenant, **Then** the system returns all users with their roles and permission levels.

3. **Given** an authenticated administrator responding to a security incident, **When** revoking a user's JWT tokens, **Then** all active tokens for that user are immediately invalidated.

---

### User Story 5 - Designer Manages Blueprint Versions (Priority: P2)

A workflow process designer uses an AI assistant to manage multiple versions of blueprints, compare changes between versions, and export blueprints for documentation or backup purposes.

**Why this priority**: Version management becomes important as workflows mature and evolve, but is secondary to initial creation and validation.

**Independent Test**: Can be tested by creating multiple versions of a blueprint, comparing them, and exporting. Demonstrates lifecycle management capability.

**Acceptance Scenarios**:

1. **Given** an authenticated designer with an existing blueprint, **When** updating the blueprint, **Then** the system creates a new version and preserves the previous version.

2. **Given** an authenticated designer with multiple blueprint versions, **When** requesting a diff between two versions, **Then** the system returns a clear comparison showing added, removed, and modified elements.

3. **Given** an authenticated designer, **When** exporting a blueprint, **Then** the system returns the blueprint in the requested format (JSON, YAML, or markdown documentation).

---

### User Story 6 - Participant Views Transaction History and Workflow Status (Priority: P2)

A workflow participant uses an AI assistant to review their transaction history and check the status of workflows they are involved in.

**Why this priority**: Historical visibility is valuable for audit trails and understanding workflow progress, but secondary to active participation.

**Independent Test**: Can be tested by querying transaction history and workflow status. Demonstrates read access to historical data.

**Acceptance Scenarios**:

1. **Given** an authenticated participant, **When** requesting transaction history, **Then** the system returns a chronological list of their past actions with timestamps, workflow names, and action summaries.

2. **Given** an authenticated participant in an active workflow, **When** requesting workflow status, **Then** the system returns the current state, pending participants, and completed actions.

---

### User Story 7 - Administrator Queries Logs and Metrics (Priority: P3)

A system administrator uses an AI assistant to query service logs and performance metrics for troubleshooting and capacity planning.

**Why this priority**: Detailed log and metric analysis is important for operations but represents deeper investigation beyond basic health checks.

**Independent Test**: Can be tested by querying logs with filters and retrieving metrics. Demonstrates operational observability.

**Acceptance Scenarios**:

1. **Given** an authenticated administrator, **When** querying logs with filters (service, time range, log level, correlation ID), **Then** the system returns matching log entries in chronological order.

2. **Given** an authenticated administrator, **When** requesting performance metrics, **Then** the system returns latency, throughput, and error rate data for the specified time period.

3. **Given** an authenticated administrator, **When** querying audit logs, **Then** the system returns security-relevant events including authentication attempts, permission changes, and administrative actions.

---

### User Story 8 - Participant Manages Wallet and Signs Transactions (Priority: P3)

A workflow participant uses an AI assistant to view their wallet information, linked identities, and sign messages or transactions when required by workflow actions.

**Why this priority**: Direct wallet operations are less common since most signing happens automatically during action submission, but explicit signing capability is needed for advanced use cases.

**Independent Test**: Can be tested by retrieving wallet info and signing a test message. Demonstrates cryptographic identity operations.

**Acceptance Scenarios**:

1. **Given** an authenticated participant, **When** requesting wallet information, **Then** the system returns their wallet address and linked identity information.

2. **Given** an authenticated participant with a signing request, **When** signing a message, **Then** the system returns the cryptographic signature using their wallet key.

---

### User Story 9 - AI Assistant Accesses MCP Resources for Context (Priority: P3)

An AI assistant retrieves read-only context from MCP resources to inform its responses without requiring explicit tool calls. This includes blueprint definitions, inbox status, and schema references.

**Why this priority**: Resource access provides contextual awareness that improves AI responses but is supplementary to the core tool functionality.

**Independent Test**: Can be tested by reading various resource URIs and verifying returned content matches expected data.

**Acceptance Scenarios**:

1. **Given** an authenticated user, **When** accessing `sorcha://blueprints`, **Then** the resource returns a list of blueprints the user has permission to view.

2. **Given** an authenticated participant, **When** accessing `sorcha://inbox`, **Then** the resource returns their current pending actions.

3. **Given** an authenticated user, **When** accessing `sorcha://blueprints/{id}`, **Then** the resource returns the full blueprint definition if they have read permission.

---

### Edge Cases

- What happens when a user's JWT token expires during an operation? The system returns a clear authentication error indicating re-authentication is required.
- How does the system handle network timeouts to backend services? The system returns a timeout error with the affected service name and suggests retry.
- What happens when a participant submits an action for a workflow that has been cancelled? The system returns an error indicating the workflow is no longer active.
- How does the system handle concurrent updates to the same blueprint? The system uses optimistic concurrency and returns a conflict error if the version has changed.
- What happens when an administrator tries to delete a tenant with active workflows? The system prevents deletion and returns the count of active workflows that must be completed or cancelled first.
- How does the system handle requests for resources the user doesn't have permission to access? The system returns a permission denied error without revealing whether the resource exists.

## Requirements *(mandatory)*

### Functional Requirements

#### Core Server Requirements

- **FR-001**: System MUST implement the Model Context Protocol (MCP) specification for tool and resource exposure.
- **FR-002**: System MUST support stdio transport mode for local AI assistant integration.
- **FR-003**: System MUST support HTTP/SSE transport mode for remote AI assistant integration.
- **FR-004**: System MUST authenticate users via JWT tokens issued by the Sorcha Tenant Service.
- **FR-005**: System MUST enforce role-based access control, exposing only tools permitted for the user's role.
- **FR-006**: System MUST communicate with backend services using the existing Sorcha.ServiceClients library.
- **FR-007**: System MUST implement rate limiting per user and per tenant to prevent abuse.
- **FR-008**: System MUST return actionable error messages that help users understand and resolve issues.
- **FR-009**: System MUST log all tool invocations with user identity, tool name, and outcome for audit purposes.

#### Administrator Tools

- **FR-010**: System MUST provide a health check tool that returns status of all Sorcha microservices.
- **FR-011**: System MUST provide a log query tool with filters for service, level, time range, and correlation ID.
- **FR-012**: System MUST provide a metrics tool that returns latency, throughput, and error rates.
- **FR-013**: System MUST provide tools to list, create, update, and suspend tenants.
- **FR-014**: System MUST provide tools to manage users and their role assignments within tenants.
- **FR-015**: System MUST provide a tool to view peer network status and replication state.
- **FR-016**: System MUST provide a tool to check validator consensus status and chain integrity.
- **FR-017**: System MUST provide a tool to query register statistics and storage usage.
- **FR-018**: System MUST provide a tool to query audit logs for security events.
- **FR-019**: System MUST provide a tool to revoke JWT tokens for security incident response.

#### Designer Tools

- **FR-020**: System MUST provide a tool to list blueprints with filtering by status, version, date, and title.
- **FR-021**: System MUST provide a tool to retrieve full blueprint definitions by ID.
- **FR-022**: System MUST provide a tool to create new blueprints from JSON or YAML definitions.
- **FR-023**: System MUST provide a tool to update existing blueprints, creating new versions.
- **FR-024**: System MUST provide a tool to validate blueprint syntax and semantic correctness.
- **FR-025**: System MUST provide a tool to simulate blueprint execution with mock data.
- **FR-026**: System MUST provide a tool to analyze disclosure rules, showing what each participant can see.
- **FR-027**: System MUST provide a tool to compare two blueprint versions (diff).
- **FR-028**: System MUST provide a tool to export blueprints in JSON, YAML, or markdown documentation format.
- **FR-029**: System MUST provide a tool to validate JSON Schema definitions.
- **FR-030**: System MUST provide a tool to generate JSON Schema from sample data.
- **FR-031**: System MUST provide a tool to test JSON Logic expressions with sample data.
- **FR-032**: System MUST provide a tool to list active workflow instances of a blueprint.

#### Participant Tools

- **FR-033**: System MUST provide a tool to list pending actions in the user's inbox.
- **FR-034**: System MUST provide a tool to retrieve details of a specific pending action including disclosed data.
- **FR-035**: System MUST provide a tool to submit data for an action with validation.
- **FR-036**: System MUST provide a tool to validate action data before submission (dry-run).
- **FR-037**: System MUST provide a tool to view the user's transaction history.
- **FR-038**: System MUST provide a tool to check workflow status for workflows the user participates in.
- **FR-039**: System MUST provide a tool to view data that has been disclosed to the user.
- **FR-040**: System MUST provide a tool to retrieve wallet information including address and linked identities.
- **FR-041**: System MUST provide a tool to sign messages or transactions with the user's wallet key.
- **FR-042**: System MUST provide a tool to query registers the user has access to.

#### MCP Resources

- **FR-043**: System MUST expose a `sorcha://blueprints` resource listing accessible blueprints.
- **FR-044**: System MUST expose a `sorcha://blueprints/{id}` resource for individual blueprint definitions.
- **FR-045**: System MUST expose a `sorcha://inbox` resource showing the user's pending actions.
- **FR-046**: System MUST expose a `sorcha://workflows/{id}` resource for workflow instance state.
- **FR-047**: System MUST expose a `sorcha://registers/{id}` resource for register data filtered by disclosure permissions.
- **FR-048**: System MUST expose a `sorcha://schemas/{name}` resource for reusable JSON Schema definitions.

### Key Entities

- **MCP Tool**: A callable operation exposed to AI assistants with defined input schema, output schema, and description. Tools are grouped by persona (admin, designer, participant).

- **MCP Resource**: A read-only data source that AI assistants can access for context. Resources use URI patterns and return structured data filtered by user permissions.

- **User Session**: An authenticated context containing JWT claims, user identity, roles, and tenant membership. Sessions determine which tools and resources are accessible. Session lifetime is JWT-driven—the session remains valid as long as the JWT token is valid, with no separate idle timeout.

- **Tool Invocation**: A record of an AI assistant calling a tool on behalf of a user. Contains timestamp, user identity, tool name, input parameters, output, and success/failure status.

- **Rate Limit**: A constraint on how frequently a user or tenant can invoke tools. Measured in requests per time window with configurable limits per tool category.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: AI assistants can successfully authenticate and invoke tools within 2 seconds of receiving credentials.

- **SC-002**: Platform health checks return status for all services within 5 seconds under normal load.

- **SC-003**: Blueprint validation completes within 3 seconds for blueprints with up to 50 actions and 20 participants.

- **SC-004**: Participants can list their inbox, view action details, and submit responses in a single conversational exchange (under 30 seconds total interaction time).

- **SC-005**: 95% of tool invocations complete successfully without timeout or error under normal operating conditions.

- **SC-006**: Users attempting to access unauthorized tools receive clear denial within 500ms without exposing system internals.

- **SC-007**: All three user personas (admin, designer, participant) can accomplish their primary tasks using only AI assistant conversation without falling back to direct UI or API access.

- **SC-008**: New users can understand available tools and their purposes through AI-provided descriptions without requiring external documentation.

- **SC-009**: When one or more backend services are unavailable, the MCP server remains operational and reports partial availability, allowing tools that don't depend on the unavailable service to continue functioning.

## Clarifications

### Session 2026-01-29

- Q: Expected concurrent scale (simultaneous MCP connections)? → A: Medium scale: 10-50 concurrent connections
- Q: Availability requirements for the MCP server? → A: Best effort with graceful degradation when backends unavailable
- Q: Session timeout behavior? → A: JWT-driven (session valid as long as JWT token is valid)

## Assumptions

- The system is designed for medium scale deployment supporting 10-50 concurrent MCP connections.
- The MCP SDK for .NET is available or can be implemented following the MCP specification.
- Existing Sorcha.ServiceClients provide all necessary backend communication capabilities.
- The Tenant Service JWT tokens contain sufficient claims to determine user roles and permissions.
- AI assistants using the MCP server are trusted to respect rate limits and not abuse the API.
- SignalR subscription for real-time notifications will be handled through a dedicated notification resource rather than active push to the AI assistant.

## Dependencies

- Sorcha.ServiceClients library for backend communication
- Sorcha Tenant Service for authentication and authorization
- All seven Sorcha microservices operational for full functionality
- .NET 10 runtime
- .NET Aspire for telemetry integration

## Out of Scope

- Direct database access (all data access goes through service APIs)
- Modification of Sorcha core services to support MCP
- AI assistant training or fine-tuning
- Multi-language support for tool descriptions (English only for initial release)
- Offline operation (requires connectivity to Sorcha services)
