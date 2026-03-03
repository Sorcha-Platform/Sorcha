# SORCHA Project Constitution

**Version:** 1.3
**Last Updated:** 2025-12-13
**Status:** Active

## Purpose

This constitution establishes the foundational principles, standards, and guidelines for the SORCHA project. All development, architectural decisions, and implementations must align with these principles.

## Project Overview

SORCHA is a distributed ledger platform built on microservices architecture, providing secure, scalable blockchain capabilities through a suite of specialized services including Wallet, Tenant, Register, Peer, Blueprint, Action, and Validator services.

## Core Principles

### 1. Architecture Principles

**Microservices-First Architecture**
- Each service must be independently deployable and maintainable
- Services communicate via well-defined APIs and message buses
- Use .Net Aspire for microservices orchestration and service-to-service communication
- Maintain clear service boundaries with minimal coupling

**Service Communication Standards**
- Internal service-to-service communication MUST use gRPC (not REST/HTTP)
- External client-facing APIs MAY use REST/HTTP (via API Gateway)
- All gRPC services MUST use mutual TLS (mTLS) for authentication
- Service contracts MUST be defined using Protocol Buffers (.proto files)
- gRPC services MUST support delegation tokens via metadata for user context propagation
- Service health checks MUST use gRPC health protocol
- API Gateway MUST translate external REST requests to internal gRPC calls where appropriate

**Cloud-Native Design**
- Design for containerization (Docker) from the start
- Support both Kubernetes and Docker Compose deployments
- Implement health checks and readiness probes for all services
- Use configuration management appropriate for cloud environments

**Scalability and Resilience**
- Services must be horizontally scalable
- Implement circuit breakers and retry policies
- Use distributed caching (Redis) for performance optimization
- Design for eventual consistency where appropriate

### 2. Security Principles

**Zero Trust Security Model**
- All service-to-service communication must be authenticated
- Implement API tokens for Dapr communication
- Never commit secrets or sensitive configuration to source control
- Use secret management (Azure Key Vault, Kubernetes secrets, local secret stores)

**Cryptographic Standards**
- Use industry-standard cryptographic libraries (Sorcha.Cryptography)
- Encrypt sensitive data at rest (wallet private keys using AES-256-GCM)
- Implement secure key management practices (Azure Key Vault, AWS KMS, or local DPAPI)
- Support multiple cryptographic algorithms (ED25519, SECP256K1, RSA)
- Implement BIP32/BIP39/BIP44 for HD wallet support
- Maintain audit trails for cryptographic operations
- Never store mnemonics - user responsibility to backup

**Identity and Access Management**
- Integrate with external identity providers (Azure AD, B2C)
- Implement proper authentication and authorization
- Use JWT tokens for API authentication
- Support multi-tenant security isolation

### 3. Development Standards

**Code Quality**
- Maintain minimum 80% unit test coverage for core libraries
- Write integration tests for all service APIs
- Follow C# coding conventions and .NET best practices
- Use async/await patterns for I/O operations

**.NET Framework Standards**
- Target .NET 10 or later for all projects
- Use modern C# language features appropriately
- Leverage dependency injection throughout
- Follow RESTful API design principles

**API Documentation Standards**

*For External REST APIs:*
- All REST endpoints MUST have OpenAPI documentation
- Use .NET 10's built-in OpenAPI support (Microsoft.AspNetCore.OpenApi)
- Use Scalar.AspNetCore for interactive API documentation UI
- OpenAPI specifications must be auto-generated from code annotations
- Document all request/response models with XML comments
- Include examples for complex request/response payloads
- Define appropriate HTTP status codes for all endpoints
- Document authentication and authorization requirements
- Version APIs appropriately and document breaking changes
- DO NOT use Swagger/Swashbuckle - use built-in .NET 10 OpenAPI

*For Internal gRPC Services:*
- All gRPC services MUST have documented .proto files with comments
- Use gRPC reflection for service discovery and tooling
- Document all message types, fields, and enums with inline comments
- Include usage examples in service documentation
- Document error handling and status codes (gRPC status codes)
- Document authentication requirements (mTLS + delegation tokens)
- Version .proto files with package versioning (e.g., sorcha.wallet.v1)
- Maintain backwards compatibility or provide migration guides for breaking changes

**Blueprint Creation Standards**
- Blueprints MUST be created as JSON or YAML documents (primary format)
- Use YAML for improved readability and space savings when appropriate
- Fluent API (C# code-based) should ONLY be used for rare developer scenarios requiring runtime blueprint generation
- Blueprint templates MUST use JSON-e (JsonE.NET) for runtime variable replacement (e.g., wallet addresses, timestamps)
- AI agents and developers MUST create blueprint demos and templates as JSON/YAML files, not Fluent API code
- Store blueprint templates in JSON/YAML format for version control and sharing

**Version Control**
- Use Git with feature branch workflow
- Write clear, descriptive commit messages
- Reference work items in commits where applicable
- Maintain clean commit history

### 4. Testing Principles

**Comprehensive Testing Strategy**
- Unit tests for business logic and utilities
- Integration tests for service APIs and database interactions
- End-to-end tests for critical user workflows
- Performance testing for scalability validation

**Test Organization**
- Maintain separate test projects (UnitTests, IntegrationTests, EndToEndTests)
- Use xUnit as the primary testing framework
- Mock external dependencies appropriately
- Maintain test data isolation

### 5. Documentation Standards

**Code Documentation**
- Document all public APIs with XML comments
- All REST endpoints must include OpenAPI documentation using .NET 10 built-in support
- Maintain README files for each service
- Document configuration requirements and dependencies
- Keep architectural decision records (ADRs)
- API documentation must be accessible via /scalar endpoint (interactive UI)
- OpenAPI specifications must be available at /openapi/v1.json

**AI-Generated Code Documentation Requirements**
- ALL automated or AI-generated code MUST follow the [AI Code Documentation Policy](AI-CODE-DOCUMENTATION-POLICY.md)
- When AI generates code, the following documentation MUST be updated:
  - README files (project and component level)
  - Documentation files in docs/ directory
  - Spec-Kit files (.specify/) including MASTER-TASKS.md and specs
  - Status files and development status reports
  - OpenAPI/XML documentation in code
- Documentation updates are NOT optional - they are a mandatory part of code completion
- PRs with AI-generated code will not be approved without corresponding documentation updates
- See [AI-CODE-DOCUMENTATION-POLICY.md](AI-CODE-DOCUMENTATION-POLICY.md) for complete requirements

**Operational Documentation**
- Document deployment procedures
- Maintain troubleshooting guides
- Document configuration management
- Keep dependency upgrade procedures current

### 6. Infrastructure as Code

**Declarative Infrastructure**
- Use Bicep for Azure infrastructure definitions
- Maintain Kubernetes manifests for service deployments
- Version control all infrastructure code
- Use Docker Compose for local development consistency

**Configuration Management**
- Separate configuration from code
- Use environment-specific configuration files
- Implement configuration validation on startup
- Document all configuration parameters

### 7. Data Management

**Data Storage Principles**
- Use appropriate storage for each service (PostgreSQL, MongoDB, Redis)
- Prefer PostgreSQL for relational data (Wallet Service)
- Use MongoDB for document-oriented data (Blueprint, Register Services)
- Use Redis for distributed caching and session state
- Implement database migrations for schema changes
- Maintain data backup and recovery procedures
- Design for data sovereignty and compliance
- Implement soft deletes for sensitive data (wallets)

**State Management**
- Use .NET Aspire for service orchestration and messaging
- Implement event-driven architecture for service communication
- Implement event sourcing where appropriate
- Maintain audit logs for critical operations (especially wallet operations)
- Design for data consistency across services
- Use optimistic concurrency control where appropriate

### 8. Observability

**Logging Standards**
- Use structured logging (Serilog) via shared `AddSerilogLogging()` extension
- Log to Aspire Dashboard via OTLP (cloud: configure `OTEL_EXPORTER_OTLP_ENDPOINT`)
- Include correlation IDs for distributed tracing
- Log at appropriate levels (Debug, Info, Warning, Error)

**Monitoring and Tracing**
- Implement distributed tracing (Zipkin)
- Monitor service health and performance
- Set up alerting for critical failures
- Track key performance indicators (KPIs)

### 9. Dependency Management

**Dependency Strategy**
- Keep dependencies up to date with security patches
- Document dependency upgrade procedures
- Minimize external dependencies

**Package Management**
- Use NuGet for .NET package management
- Version internal packages semantically
- Test dependency upgrades thoroughly
- Maintain dependency compatibility matrix

### 10. License and Copyright

**Licensing Requirements**
- All code is license under MIT terms 
- Maintain copyright notices in all source files
- Document third-party licenses and attributions
- Ensure compliance with license terms

**Copyright Notice Format**
```
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

```

## Constitution Enforcement

### Compliance Checking
- Constitutional compliance must be verified during code reviews
- Architectural decisions must reference relevant constitutional principles
- Violations must be documented and justified
- Regular audits to ensure ongoing compliance

### Amendment Process
- Constitutional amendments require team consensus
- Major version increments for breaking principle changes
- Minor version increments for principle additions
- Patch version increments for clarifications

### Conflict Resolution
- Technical disputes resolved by referencing constitutional principles
- Escalation path: Team Lead → Architecture Review → Project Stakeholders
- Document all significant architectural decisions

## Related Documents

- [Project Specification](spec.md)
- [Master Plan](MASTER-PLAN.md)
- [Master Tasks](MASTER-TASKS.md)
- [AI Code Documentation Policy](AI-CODE-DOCUMENTATION-POLICY.md)
- [Architecture Decision Records](adrs/README.md) ⭐ NEW
  - [ADR-001: gRPC Service Communication](adrs/adr-001-grpc-service-communication.md) ⭐ NEW
- [gRPC Migration Plan](GRPC-MIGRATION-PLAN.md) ⭐ NEW
- [Project README](../README.md)
- [Contributing Guidelines](../CONTRIBUTING.md)
- [License](../LICENCE.txt)
- [Troubleshooting Guide](../docs/guides/TROUBLESHOOTING.md)

---

**Document Control**
- **Created:** 2025-11-11
- **Authority:** SORCHA Architecture Team
- **Review Frequency:** Quarterly
- **Next Review:** 2026-02-11
