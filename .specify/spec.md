# SORCHA Project Specification

**Version:** 1.0
**Date:** 2025-11-11
**Status:** Active
**Constitution Version:** 1.0

## Executive Summary

SORCHA is a distributed ledger platform designed to provide secure, scalable, and enterprise-ready blockchain capabilities through a microservices architecture. The platform enables organizations to build transparent, immutable, and auditable business processes while maintaining data sovereignty and regulatory compliance.

## Vision and Goals

### Primary Vision
Create a production-grade distributed ledger platform that combines the benefits of blockchain technology with enterprise-scale performance, security, and operational requirements.

### Strategic Goals

1. **Enterprise Readiness**
   - Provide production-ready microservices for blockchain operations
   - Support multi-tenant deployments with tenant isolation
   - Enable integration with existing enterprise identity systems
   - Maintain audit trails for compliance and governance

2. **Developer Experience**
   - Provide comprehensive SDKs for application development
   - Offer clear documentation and examples
   - Enable local development with Docker Compose
   - Support multiple deployment targets (local, Azure, Kubernetes)

3. **Security and Compliance**
   - Implement cryptographic best practices
   - Support secure wallet management
   - Enable regulatory compliance through audit capabilities
   - Maintain data sovereignty controls

4. **Performance and Scalability**
   - Support horizontal scaling of all services
   - Optimize for high-throughput transaction processing
   - Minimize latency in distributed operations
   - Efficient resource utilization

5. **Operational Excellence**
   - Comprehensive observability (logging, monitoring, tracing)
   - Automated deployment capabilities
   - Clear troubleshooting procedures
   - Disaster recovery and backup strategies

## System Architecture

### Core Services

#### 1. Tenant Service (To Be Specified)
**Purpose:** Multi-tenant management and identity federation

**Key Features:**
- Tenant provisioning and management
- Integration with identity providers (Azure AD, Azure B2C)
- Tenant-specific configuration and policies
- API authentication and authorization

**Current Status:** Boilerplate specification available at [sorcha-tenant-service.md](.specify/specs/sorcha-tenant-service.md)
Basic tenant isolation implemented at application level.

**Dependencies:**
- Redis (caching)
- .NET Aspire messaging
- PostgreSQL (tenant data)
- Identity providers

#### 2. Wallet Service
**Purpose:** Secure wallet management and cryptographic operations

**Specification:** [sorcha-wallet-service.md](.specify/specs/sorcha-wallet-service.md)

**Key Features:**
- HD wallet creation and recovery (BIP32/BIP39/BIP44)
- Multi-algorithm support (ED25519, SECP256K1, RSA)
- Private key encryption and storage (AES-256-GCM)
- Transaction signing and verification
- Encrypted key storage with multiple providers (Azure KV, AWS KMS, local)
- Access control and delegation
- Event-driven architecture
- Cloud-native design with .NET Aspire

**Dependencies:**
- PostgreSQL (wallet storage with EF Core)
- Redis (distributed caching)
- Azure Key Vault or AWS KMS (encryption key management)
- Sorcha.Cryptography (cryptographic operations)
- Sorcha.TransactionHandler (transaction management)
- .NET Aspire (service orchestration and messaging)

#### 3. Register Service (To Be Specified)
**Purpose:** Distributed ledger and block management

**Current Status:** Boilerplate specification available at [sorcha-register-service.md](.specify/specs/sorcha-register-service.md)
Stub implementation provided for wallet service integration.

**Key Features:**
- Block creation and validation
- Transaction registry
- Merkle tree management
- Block synchronization
- Query capabilities for ledger data
- Transaction history retrieval (for wallet recovery)

**Dependencies:**
- MongoDB (block storage)
- .NET Aspire messaging
- Redis (caching)
- Sorcha.TransactionHandler

#### 4. Peer Service
**Purpose:** Network communication and peer discovery

**Key Features:**
- Peer-to-peer communication
- Service discovery (mDNS support)
- Network routing
- TLS configuration
- Internal/external IP management

**Dependencies:**
- gRPC (messaging)
- Redis (state)
- Network infrastructure

#### 5. Blueprint Service
**Purpose:** Transaction template and workflow management

**Key Features:**
- Blueprint definition and storage
- Workflow validation
- Template versioning
- Blueprint execution coordination

**Dependencies:**
- MongoDB (blueprint storage)
- RabbitMQ (messaging)
- Redis (caching)

#### 6. Action Service
**Purpose:** Transaction processing and execution

**Key Features:**
- Action creation and submission
- Transaction validation
- State transitions
- Event emission

**Dependencies:**
- RabbitMQ (messaging)
- Redis (state management)

#### 7. Validator Service
**Purpose:** Transaction and block validation

**Key Features:**
- Transaction validation rules
- Block validation
- Consensus mechanisms
- Validation engine (ValidationEngine project)

**Dependencies:**
- RabbitMQ (messaging)
- Redis (state)

#### 8. Proxy Service
**Purpose:** API gateway and reverse proxy

**Key Features:**
- Request routing
- NGINX-based reverse proxy
- SSL/TLS termination
- API consolidation

**Dependencies:**
- Upstream services
- SSL certificates

### Supporting Components

#### Common Libraries
- **Sorcha.Cryptography:** Cryptographic operations and key management (ED25519, SECP256K1, RSA, BIP32/BIP39/BIP44)
- **Sorcha.TransactionHandler:** Transaction building, payload management, and versioning
- **Sorcha.Blueprint.Models:** Blueprint and domain models
- **Sorcha.ServiceDefaults:** Shared service configurations, OpenTelemetry, health checks
- **Sorcha.WalletService:** Wallet management library (new)

#### SDK
- **Sorcha.ServiceClients:** Client SDK for application development
- **Sorcha.Blueprint.Fluent:** Fluent API for simplified SDK usage

#### UI Components
- **AdminUI:** Administration interface (Blazor-based)
  - AdminUI.Server: Backend API
  - AdminUI.Client: Frontend application
- **Sorcha CLI:** Command-line interface tool

### Infrastructure Components

#### Dapr Integration
- State management (component-state-*.yaml)
- Pub/sub messaging (pubsub.yaml)
- Secret management (localsecretstore.yaml)
- Service bindings (component-binding-action.yaml)
- Configuration (config.yaml)

#### Data Stores
- **MongoDB:** Document storage for Register and Blueprint services
- **MySQL:** Relational storage for Wallet service
- **Redis:** Distributed caching and state management
- **RabbitMQ:** Message broker for service communication

#### Observability Stack
- **Serilog:** Structured logging with enrichment (machine name, thread ID, application)
- **OTLP → Aspire Dashboard:** Centralized log aggregation (cloud: Grafana/Datadog/Azure Monitor)
- **Zipkin:** Distributed tracing
- **Application Insights:** Azure monitoring (cloud deployments)

## User Scenarios

### Scenario 1: Enterprise Integration
**Actor:** Enterprise Developer

**Goal:** Integrate Sorcha into existing enterprise applications

**Steps:**
1. Install and configure Sorcha services
2. Configure identity provider integration (Azure AD)
3. Use Sorcha.ServiceClients SDK to integrate with application
4. Create tenant and provision wallets
5. Define blueprints for business processes
6. Submit and track transactions

**Success Criteria:**
- Successful authentication and authorization
- Transactions processed and recorded on ledger
- Audit trail available for compliance
- Integration with existing systems

### Scenario 2: Local Development
**Actor:** Platform Developer

**Goal:** Develop and test new features locally

**Steps:**
1. Clone SORCHA repository
2. Configure local development environment
3. Start services using Docker Compose
4. Run integration tests
5. Make code changes
6. Verify changes with tests
7. Commit and create pull request

**Success Criteria:**
- All services running locally
- Tests passing
- Changes properly tested
- Code review approved

### Scenario 3: Multi-Tenant Deployment
**Actor:** Platform Operator

**Goal:** Deploy SORCHA to production with multiple tenants

**Steps:**
1. Provision Azure infrastructure using Bicep templates
2. Deploy services to Kubernetes cluster
3. Configure identity providers for each tenant
4. Set up monitoring and alerting
5. Onboard tenants
6. Monitor system health and performance

**Success Criteria:**
- Services deployed and healthy
- Tenants isolated and operational
- Monitoring providing visibility
- SLAs being met

### Scenario 4: Wallet Management
**Actor:** End User

**Goal:** Create and manage cryptographic wallet

**Steps:**
1. Authenticate with tenant
2. Create new wallet through AdminUI or API
3. View wallet details
4. Sign transactions with wallet
5. Manage wallet permissions

**Success Criteria:**
- Wallet created securely
- Private keys encrypted at rest
- Transactions signed successfully
- Access control enforced

## Requirements

### Functional Requirements

#### FR-1: Multi-Tenant Support
- System must support multiple independent tenants
- Tenants must be completely isolated (data, configuration, users)
- Each tenant can have custom identity provider configuration
- Tenant-specific policies and quotas

#### FR-2: Wallet Management
- Users can create and manage multiple wallets
- Private keys encrypted with tenant-specific encryption keys
- Support for various key types and algorithms
- Wallet access control and delegation
- Wallet recovery mechanisms

#### FR-3: Transaction Processing
- Submit transactions based on blueprint templates
- Validate transactions against business rules
- Record transactions immutably on distributed ledger
- Query transaction history and status
- Support for atomic multi-step transactions

#### FR-4: Blueprint Management
- Define transaction templates (blueprints)
- Version control for blueprints
- Blueprint validation and testing
- Blueprint deployment and activation
- Blueprint lifecycle management

#### FR-5: Identity Integration
- Support Azure AD integration
- Support Azure B2C integration
- Custom identity provider support
- Token-based authentication (JWT)
- Role-based access control

#### FR-6: API Access
- RESTful APIs for all services
- Comprehensive API documentation
- Rate limiting and throttling
- API versioning
- SDK support for common languages

### Non-Functional Requirements

#### NFR-1: Performance
- Transaction throughput: minimum 1000 TPS per service instance
- API response time: p95 < 500ms for read operations
- API response time: p95 < 2s for write operations
- Support horizontal scaling to meet throughput requirements

#### NFR-2: Availability
- Target 99.9% uptime for production services
- Zero-downtime deployments
- Automatic service recovery
- Health monitoring and alerting

#### NFR-3: Security
- All data encrypted in transit (TLS 1.2+)
- Sensitive data encrypted at rest
- Security audit logging
- Compliance with security best practices (OWASP)
- Regular security assessments

#### NFR-4: Scalability
- Horizontally scalable microservices
- Database sharding capabilities
- Efficient caching strategies
- Load balancing support
- Auto-scaling in cloud environments

#### NFR-5: Maintainability
- Comprehensive logging and tracing
- Clear error messages and diagnostics
- Automated testing (unit, integration, E2E)
- Code coverage > 80% for core libraries
- Documentation for all public APIs

#### NFR-6: Operability
- Docker container support
- Kubernetes orchestration
- Infrastructure as Code (Bicep)
- CI/CD pipeline integration
- Monitoring and alerting

#### NFR-7: Reliability
- Circuit breakers for external dependencies
- Retry policies with exponential backoff
- Graceful degradation
- Data backup and recovery procedures
- Disaster recovery capabilities

## Technical Constraints

### Technology Stack
- **.NET 10** (target framework)
- **C#** (primary programming language)
- **Docker** (containerization)
- **Kubernetes** (orchestration)
- **Dapr** (microservices runtime)
- **Azure** (cloud platform)

### Integration Requirements
- **Identity Providers:** Azure AD, Azure B2C
- **Monitoring:** Application Insights, Seq, Zipkin
- **Storage:** MongoDB, MySQL, Redis
- **Messaging:** RabbitMQ

### Deployment Targets
- Local development (Docker Compose)
- Azure Kubernetes Service (AKS)
- Azure Container Registry (ACR)
- Azure infrastructure (Bicep templates)

## Dependencies and Assumptions

### External Dependencies
- Azure Active Directory for identity
- Azure Key Vault for secret management
- NuGet feed for internal packages (https://projectbob.pkgs.visualstudio.com/SORCHA/_packaging/SORCHAfeed/)
- Third-party NuGet packages

### Assumptions
- Development team has access to Azure DevOps
- Personal Access Tokens (PATs) for NuGet feed
- Development environment with Docker Desktop
- Visual Studio 2026 or VS Code with C# extensions

## Success Metrics

### Technical Metrics
- Test coverage > 80% for core libraries
- Build success rate > 95%
- Deployment success rate > 98%
- API response time SLAs met
- Zero critical security vulnerabilities

### Operational Metrics
- System uptime > 99.9%
- Mean time to recovery (MTTR) < 15 minutes
- Number of production incidents < 5 per month
- Successful deployments per week > 3

### Business Metrics
- Number of active tenants
- Transaction volume per tenant
- API usage growth
- Developer adoption (SDK downloads)

## Out of Scope

The following items are explicitly out of scope for the current specification:

1. Public blockchain integration
2. Advanced consensus mechanisms (beyond validation)
3. Smart contract execution environment


## Glossary

- **Blueprint:** Transaction template defining structure and validation rules
- **Dapr:** Distributed Application Runtime for microservices
- **Ledger:** Immutable distributed log of transactions
- **Register:** Service maintaining the distributed ledger
- **Tenant:** Independent customer organization using the platform
- **Wallet:** Cryptographic key pair for signing transactions

## References

- [Project Constitution](.specify/constitution.md)
- [Implementation Plan](.specify/plan.md)
- [Project README](../README.md)
- [Deployment Documentation](../deploy/bicep/README.md)
- [Troubleshooting Guide](../docs/TROUBLESHOOTING.md)
- [Dependency Upgrade Plan](../DEPENDENCY_UPGRADE_PLAN.md)

---

**Document Control**
- **Author:** SORCHA Architecture Team
- **Approved By:** Project Stakeholders
- **Review Schedule:** Quarterly
- **Next Review:** 2026-02-11
