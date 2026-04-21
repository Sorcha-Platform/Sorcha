# Sorcha Project Constitution

## Core Principles

### I. Microservices-First Architecture
Each service MUST be independently deployable. Use .NET Aspire for orchestration. Minimal coupling between services. Dependencies flow downward only - no upward dependencies. Core/Domain layer MUST NOT depend on Application or Infrastructure layers.

### II. Security First
Zero trust security model. All sensitive data encrypted at rest (AES-256-GCM). Support for Azure Key Vault, AWS KMS. Never commit secrets to source control. Input validation REQUIRED on all external boundaries using DataAnnotations & FluentValidation. JSON Schema validation REQUIRED for all blueprints.

### III. API Documentation
Use .NET 10 built-in OpenAPI (NOT Swagger/Swashbuckle). Use Scalar.AspNetCore for interactive API docs. All public APIs MUST have XML documentation. OpenAPI specification exposed at `/openapi/v1.json`. Include examples for complex payloads.

### IV. Testing Requirements
Minimum 80% unit test coverage for core libraries. Target >85% for new code. Use xUnit as primary testing framework. All tests MUST be deterministic and isolated. Integration tests for all service APIs. Follow Arrange-Act-Assert pattern.

### V. Code Quality
Follow C# coding conventions. Use async/await for I/O operations. Leverage dependency injection. Target .NET 10 framework with C# 14. Nullable reference types MUST be enabled. No compiler warnings in Release builds.

### VI. Blueprint Creation Standards
Always create blueprints as JSON or YAML documents (primary format). Use JSON-e (JsonE.NET) for runtime variable replacement. Fluent API is ONLY for rare developer scenarios requiring runtime generation. Store blueprint templates as JSON/YAML files, not C# code.

### VII. Domain-Driven Design
Rich domain models with ubiquitous language. Use these terms consistently: Blueprint (not "workflow"), Action (not "step"), Participant (not "user"), Disclosure (not "visibility"), Publish (not "deploy"). Aggregates protect business invariants.

### VIII. Observability by Default
Comprehensive OpenTelemetry telemetry from day one. Structured logging required - no string interpolation. All services MUST integrate health check endpoints (`/health`, `/alive`). Export metrics, traces, and logs.

## Technology Stack

| Category | Technology | Version |
|----------|------------|---------|
| Framework | .NET | 10.0 |
| Language | C# | 13 |
| Orchestration | .NET Aspire | 13.0.0 |
| API Gateway | YARP | 2.2.0 |
| gRPC | Grpc.Net | 2.71.0 |
| Caching | Redis/Aspire | 13.0.0 |
| Validation | FluentValidation | 11.10.0 |
| Schema | JsonSchema.Net | 7.4.0 |
| Telemetry | OpenTelemetry | 1.12.0 |
| API Docs | Scalar | 2.10.0 |

## Standards References

All development MUST comply with these standards documents:

| Document | Purpose |
|----------|---------|
| [Coding Standards](../standards/coding-standards.md) | Code style, conventions, best practices |
| [Architecture Rules](../standards/architecture-rules.md) | System design, patterns, layers |
| [Security Guidelines](../standards/security-guidelines.md) | OWASP Top 10, authentication, authorization |
| [Testing Requirements](../standards/testing-requirements.md) | Test coverage, strategies, frameworks |

## Development Workflow

### Before Writing Code
- Read relevant standards documents
- Review existing patterns in the codebase
- Verify architectural fit
- Check dependencies

### During Development
- Follow coding standards strictly
- Write tests alongside code (TDD encouraged)
- Document public APIs with XML comments
- Use meaningful commit messages

### Before Committing
- Run full test suite (`dotnet test`)
- Check code coverage (>80% minimum)
- Run static analysis
- Verify no build warnings
- Update documentation if needed

## AI Assistant Guidelines

When assisting with the Sorcha project, AI assistants MUST:

1. **Read the Constitution First** - Understand principles before proposing solutions
2. **Follow Standards** - All code must comply with coding, architecture, security, and testing standards
3. **Maintain Consistency** - Use existing patterns and styles
4. **Prioritize Quality** - Write tests, document APIs, validate inputs, handle errors
5. **Be Proactive** - Identify issues early, suggest improvements, flag security concerns

## Governance

Constitution supersedes all other practices. Amendments require:
- Documentation of the change
- Review and approval
- Migration plan for existing code

All PRs must verify compliance with this constitution. Complexity must be justified.

**Version**: 1.1.0 | **Ratified**: 2025-11-12 | **Last Amended**: 2025-12-03
