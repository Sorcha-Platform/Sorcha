# Feature Specification: Hardware Cryptographic Storage and Execution Enclaves

**Feature Branch**: `001-hardware-crypto-enclaves`
**Created**: 2025-12-23
**Status**: Draft
**Input**: User description: "hardware cryptographic storage and execution enclaves should be used in all but developer environments. We should be able to interface to different cloud providers secure storage like AWS KMS, Azure KeyVault etc as well as local execution environments such as Kubernetes, Docker Desktop or direct Windows / OSx / Linux OS or browser Crypto Storage."

## Clarifications

### Session 2025-12-23

- Q: When multiple cryptographic backends are simultaneously available (e.g., Kubernetes cluster running on Azure with both Azure Key Vault and Kubernetes Secrets accessible), which backend should the system prioritize? → A: Cloud HSM takes precedence (Azure Key Vault/AWS KMS/GCP KMS over Kubernetes Secrets when both available)
- Q: How should key rotation be triggered? → A: Automatic time-based rotation with manual override (system rotates keys automatically based on configured intervals, admins can trigger immediate rotation)
- Q: What authentication mechanism should the system use for accessing cryptographic operations? → A: Managed identities for cloud (Azure Managed Identity/AWS IAM Roles), service accounts for Kubernetes, process identity for local
- Q: At what scope should HSM fallback to software signing be configured? → A: Per-deployment configuration in environment settings (operators configure fallback policy once per environment: enabled/disabled, affects all operations uniformly)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cloud Production Deployment with Hardware Security (Priority: P1)

An operations team deploys Sorcha to Azure production environment. The system automatically detects Azure environment and provisions cryptographic keys in Azure Key Vault with Hardware Security Module (HSM) backing. All wallet private keys are stored exclusively in the HSM, never exposed to application memory. Transaction signing operations execute within the secure enclave boundary.

**Why this priority**: Production security is the highest priority requirement. Without secure key storage, the entire platform's cryptographic security model is compromised. This is the primary use case for any production deployment.

**Independent Test**: Can be fully tested by deploying to Azure, creating a wallet, signing a transaction, and verifying through Azure Key Vault audit logs that keys were never exported and all cryptographic operations occurred within HSM. Delivers production-grade security for wallet operations.

**Acceptance Scenarios**:

1. **Given** Sorcha is deployed to Azure with Key Vault configured, **When** a new wallet is created, **Then** the private key is generated and stored in Azure Key Vault HSM and never appears in application logs or memory dumps
2. **Given** a wallet exists in Azure Key Vault, **When** a transaction is signed, **Then** the signing operation executes within the Key Vault HSM boundary and the private key is never retrieved to application memory
3. **Given** Azure Key Vault is temporarily unavailable, **When** a signing operation is attempted, **Then** the system fails gracefully with a clear error message and queues the operation for retry
4. **Given** wallet operations are executing, **When** Azure Key Vault audit logs are reviewed, **Then** all cryptographic operations are recorded with appropriate access policies enforced

---

### User Story 2 - Multi-Cloud Provider Support (Priority: P1)

A global enterprise uses AWS infrastructure. Their operations team deploys Sorcha to AWS and configures it to use AWS KMS for cryptographic operations. The system seamlessly integrates with AWS KMS HSM, storing wallet keys and executing signing operations within AWS secure enclaves. The same application code works identically on Azure, AWS, or GCP without modification.

**Why this priority**: Multi-cloud support is essential for enterprise adoption. Organizations have existing cloud investments and cannot be locked into a single provider. This is a P1 requirement because it's a common blocking concern in enterprise sales/adoption.

**Independent Test**: Can be fully tested by deploying identical Sorcha configuration to AWS, creating wallets, signing transactions, and verifying through AWS CloudTrail that all key operations occurred within KMS. Swap configuration to Azure and verify identical behavior with different provider. Delivers cloud-agnostic security.

**Acceptance Scenarios**:

1. **Given** Sorcha is deployed to AWS with KMS configured, **When** a wallet is created, **Then** keys are stored in AWS KMS with HSM backing and all operations are logged in CloudTrail
2. **Given** the same application binary, **When** deployed to Azure vs AWS vs GCP, **Then** the system automatically detects the environment and uses the appropriate provider's HSM service without code changes
3. **Given** a wallet stored in AWS KMS, **When** migrating to Azure Key Vault, **Then** the system provides a secure key export/import mechanism that maintains HSM-to-HSM transfer without exposing keys
4. **Given** multiple cloud providers configured, **When** selecting a storage backend for a new wallet, **Then** users can specify which provider to use while maintaining consistent security guarantees

---

### User Story 3 - Local Development Environment with Software Fallback (Priority: P2)

A developer sets up Sorcha on their local Windows workstation using Docker Desktop. Since no cloud HSM is available and this is a development environment, the system automatically falls back to OS-level cryptographic storage (Windows DPAPI). Keys are still encrypted and protected by the OS, but don't require cloud connectivity. Development workflows proceed normally with reduced security appropriate for local testing.

**Why this priority**: Developer productivity is critical, but local development doesn't require production-grade HSM security. P2 priority because it's essential for team velocity but doesn't block production deployments.

**Independent Test**: Can be fully tested by running Sorcha locally without cloud credentials, creating wallets, signing transactions, and verifying keys are stored using Windows DPAPI (inspectable via certutil or registry). Delivers frictionless local development while maintaining reasonable key protection.

**Acceptance Scenarios**:

1. **Given** Sorcha is running on a developer's local machine without cloud provider credentials, **When** a wallet is created, **Then** keys are stored using OS-native secure storage (Windows DPAPI / macOS Keychain / Linux Secret Service)
2. **Given** local development mode, **When** the application starts, **Then** a clear warning indicates this is not production-grade security and should only be used for development
3. **Given** a wallet created in local development mode, **When** attempting to deploy to production, **Then** the system prevents migration of development keys to production and requires new key generation in production HSM
4. **Given** Docker Desktop environment, **When** creating a wallet, **Then** keys are stored in the container's secure storage with appropriate volume mounting for persistence

---

### User Story 4 - Kubernetes Production Deployment with Secret Management (Priority: P2)

A DevOps team deploys Sorcha to a Kubernetes cluster. The system integrates with Kubernetes Secrets and external secret management solutions (e.g., HashiCorp Vault, sealed-secrets). Wallet keys are stored as encrypted Kubernetes secrets with optional external KMS encryption. Pod security policies prevent key exposure through container introspection.

**Why this priority**: Kubernetes is a common production deployment target, but can leverage cloud HSM when available. This is P2 because it's important for cloud-native deployments but can delegate to underlying cloud provider HSM (covered in P1).

**Independent Test**: Can be fully tested by deploying to a Kubernetes cluster, creating wallets stored as encrypted secrets, verifying pod security contexts prevent key access, and optionally configuring external KMS for envelope encryption. Delivers cloud-native secret management.

**Acceptance Scenarios**:

1. **Given** Sorcha is deployed to Kubernetes, **When** a wallet is created, **Then** the private key is stored as an encrypted Kubernetes Secret with appropriate RBAC policies
2. **Given** Kubernetes cluster with external KMS integration (e.g., cloud provider encryption), **When** wallet secrets are stored, **Then** envelope encryption is used with KEK stored in cloud HSM
3. **Given** a running pod with wallet access, **When** attempting to exec into the pod, **Then** security policies prevent reading raw secret values from the container filesystem
4. **Given** Kubernetes secret rotation policies, **When** KEK rotation occurs, **Then** wallet secrets are transparently re-encrypted without service disruption

---

### User Story 5 - Browser-Based Wallet with Web Crypto API (Priority: P3)

An end-user accesses the Sorcha Blueprint Designer web interface. For client-side signing scenarios, the browser-based application uses the Web Crypto API to generate and store keys in the browser's secure storage (IndexedDB with encryption). Keys are bound to the origin and protected by browser security policies. This enables client-side transaction signing for improved privacy.

**Why this priority**: Browser-based crypto enables client-side signing and improved privacy, but is less critical than server-side production security. P3 because it's a nice-to-have for specific use cases but not required for core platform operation.

**Independent Test**: Can be fully tested by using the Blueprint Designer in a browser, generating a client-side wallet, signing a transaction, and verifying the private key never leaves the browser (network tab shows only signed transactions, not keys). Delivers client-side signing capability.

**Acceptance Scenarios**:

1. **Given** a user accesses the Blueprint Designer in a web browser, **When** they create a client-side wallet, **Then** keys are generated using Web Crypto API and stored in browser secure storage (non-extractable)
2. **Given** a browser-based wallet, **When** signing a transaction, **Then** the signing operation executes entirely in the browser and only the signed transaction is transmitted to the server
3. **Given** browser-based wallet storage, **When** the user clears browser data, **Then** they are warned that wallet keys will be lost and must be backed up (exported) first
4. **Given** Web Crypto API limitations, **When** attempting to create a browser wallet, **Then** the system clearly indicates browser-specific constraints (e.g., no BIP39 mnemonic export) and suitability for low-value testing only

---

### Edge Cases

- **What happens when a cloud provider's HSM is temporarily unavailable during critical signing operations?**
  - System maintains an operation queue with exponential backoff retry
  - Clear error messages with estimated retry times
  - Fallback to software signing is configurable per-deployment in environment settings (operators decide at deployment time whether fallback is enabled or disabled)
  - When fallback is enabled, all operations use software signing during HSM outage with prominent warnings and comprehensive audit logging
  - Production environments typically configure fallback as disabled (fail-closed approach), while staging/testing environments may enable it for resilience testing

- **How does the system handle migrating keys between providers (e.g., AWS KMS to Azure Key Vault)?**
  - Secure HSM-to-HSM transfer using provider-specific export/import mechanisms
  - Keys remain encrypted during transfer using asymmetric wrapping
  - Audit trail records the migration with cryptographic proof of integrity
  - Migration requires explicit administrative approval

- **What happens when running in an environment where no secure storage is available (e.g., minimal Linux without Secret Service)?**
  - System refuses to start in production mode
  - Development mode allows plaintext storage with prominent warnings and audit logs
  - Configuration requires explicit acknowledgment of security implications

- **How does the system prevent accidental exposure of keys through logging, error messages, or diagnostics?**
  - All key material is marked as sensitive and automatically redacted from logs
  - Error messages never include key data, only key identifiers/handles
  - Memory dumps exclude key material through secure memory allocation
  - Diagnostic endpoints require authentication and omit sensitive data

- **What happens when OS-level secure storage (DPAPI, Keychain) is corrupted or unavailable?**
  - System detects corruption during startup health checks
  - Clear error messages guide user to recovery procedures
  - Backup key export mechanism (encrypted with user password) enables recovery
  - Development environments may allow re-initialization with data loss warning

- **How does the system handle key rotation and versioning?**
  - Each key has a version identifier and creation timestamp
  - Old key versions remain accessible for signature verification
  - New signatures use current key version
  - Rotation occurs automatically based on configured time intervals (e.g., 90-day rotation in production)
  - Administrators can trigger immediate manual rotation (e.g., for security incidents or suspected compromise)
  - Rotation policies are configurable per environment

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support hardware-backed cryptographic storage using cloud provider HSMs (Azure Key Vault, AWS KMS, GCP Cloud KMS) in production environments
- **FR-002**: System MUST automatically detect the deployment environment and select the appropriate cryptographic storage backend (cloud HSM, Kubernetes secrets, OS secure storage, browser Web Crypto API), with cloud HSM taking precedence over Kubernetes Secrets when both are available
- **FR-003**: System MUST ensure private keys are generated within and never leave the secure enclave boundary when using HSM-backed storage
- **FR-004**: System MUST execute all signing operations within the secure enclave (HSM/TPM) without exporting private key material to application memory
- **FR-005**: System MUST support OS-native secure storage for local development environments (Windows DPAPI, macOS Keychain, Linux Secret Service)
- **FR-006**: System MUST integrate with Kubernetes secret management and external KMS providers for envelope encryption
- **FR-007**: System MUST support browser-based Web Crypto API for client-side key generation and signing in web applications
- **FR-008**: System MUST provide configuration-driven selection of cryptographic backend without requiring code changes
- **FR-009**: System MUST fail gracefully when the configured cryptographic backend is unavailable, with clear error messages and retry mechanisms
- **FR-010**: System MUST audit log all cryptographic operations (key generation, signing, encryption) with provider-specific audit trails
- **FR-011**: System MUST prevent deployment of development-mode keys to production environments
- **FR-012**: System MUST support secure key migration between providers using HSM-to-HSM encrypted transfer
- **FR-013**: System MUST mark all key material as sensitive and automatically redact from logs, error messages, and diagnostics
- **FR-014**: System MUST enforce different security policies based on environment type (production requires HSM, development allows OS storage, local development allows software-only with warnings)
- **FR-015**: System MUST support envelope encryption where applicable (e.g., Kubernetes secrets encrypted with cloud provider KEK stored in HSM)
- **FR-016**: System MUST provide health checks that verify cryptographic backend availability and fail startup if production requirements are not met
- **FR-017**: System MUST support key versioning and rotation with automatic time-based rotation according to configurable policies per environment, and MUST allow administrators to trigger immediate manual rotation for security incidents
- **FR-018**: System MUST integrate with Azure Key Vault, AWS KMS, and GCP Cloud KMS using provider-specific SDKs and best practices
- **FR-019**: System MUST support Docker Desktop and container-based development workflows with appropriate key storage persistence
- **FR-020**: System MUST provide clear warnings and explicit configuration when operating in reduced-security modes (development, software-only)
- **FR-021**: System MUST apply the following backend selection priority when multiple backends are available: (1) Cloud HSM (Azure/AWS/GCP), (2) Kubernetes Secrets with external KMS, (3) OS-native secure storage, (4) Software-only (development mode with warnings). Configuration MAY override this default precedence.
- **FR-022**: System MUST monitor key age and automatically initiate rotation when the configured time interval is reached (e.g., 90 days), with the rotation process transparent to ongoing operations (old key versions remain valid for signature verification)
- **FR-023**: System MUST authenticate to cryptographic backends using managed identities (Azure Managed Identity, AWS IAM Roles with instance profiles) in cloud environments, Kubernetes service accounts in K8s deployments, and OS process identity for local development, eliminating the need for stored credentials or secrets in configuration
- **FR-024**: System MUST allow operators to configure HSM fallback behavior at deployment/environment level (enabled or disabled), with production environments defaulting to disabled (fail-closed) and the configuration applying uniformly to all cryptographic operations in that environment. When enabled, fallback operations MUST be prominently logged with warnings.

### Key Entities *(include if feature involves data)*

- **Cryptographic Backend**: Represents the storage and execution environment for cryptographic operations. Attributes include provider type (Azure, AWS, GCP, Kubernetes, OS, Browser), security level (HSM, TPM, software), availability status, and configuration parameters
- **Key Handle**: Abstract reference to a cryptographic key stored in a secure enclave. Attributes include key identifier, version, algorithm, creation timestamp, last rotation timestamp, next scheduled rotation date, provider-specific URI (e.g., Azure Key Vault key ID), and security properties (exportable/non-exportable, HSM-backed)
- **Environment Configuration**: Defines the cryptographic requirements for different deployment environments. Attributes include environment type (production, staging, development, local), required security level, allowed backend types, backend selection precedence order, key rotation interval, HSM fallback policy (enabled/disabled, applies uniformly to all operations), and audit requirements
- **Cryptographic Operation**: Represents a signing, encryption, or key generation operation. Attributes include operation type, key handle reference, timestamp, requesting service/user, success/failure status, and provider-specific audit trail reference
- **Key Migration Record**: Tracks secure key transfer between providers. Attributes include source provider, destination provider, migration timestamp, key identifiers (old/new), cryptographic proof of integrity, and administrative approval

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Production deployments enforce HSM-backed key storage with 100% of wallet private keys stored exclusively in cloud provider HSMs, verified through provider audit logs showing zero key export operations
- **SC-002**: System supports at least three major cloud providers (Azure, AWS, GCP) with identical security guarantees, verified by deploying to each provider and passing identical security test suites
- **SC-003**: Local development environments successfully create and use wallets without requiring cloud connectivity, verified by disconnecting network and completing full wallet lifecycle (create, sign, verify)
- **SC-004**: Signing operations execute within secure enclave boundaries with zero private key exposure to application memory, verified through memory dump analysis showing no extractable key material
- **SC-005**: System automatically detects environment and selects appropriate cryptographic backend without manual configuration in 95% of common deployment scenarios (Azure App Service, AWS ECS, GCP Cloud Run, Kubernetes, Docker Desktop, local OS)
- **SC-006**: Cryptographic backend failures are handled gracefully with clear error messages and automatic retry, maintaining 99.9% operation success rate under normal conditions (excluding provider outages)
- **SC-007**: All cryptographic operations are audit logged with provider-specific trails, verified by reviewing logs and confirming 100% operation coverage with no sensitive data exposure
- **SC-008**: Development-mode keys are prevented from production deployment with 100% rejection rate, verified by attempting to migrate local keys to production and confirming rejection
- **SC-009**: Developer productivity is maintained with local environment setup completing in under 5 minutes without requiring cloud provider accounts
- **SC-010**: Security policies are enforced at startup with production environments refusing to start if HSM requirements are not met, verified by misconfiguring production deployment and confirming startup failure with clear error message

## Assumptions

- **Cloud Provider Availability**: Production environments are assumed to have reliable connectivity to cloud provider HSM services with published SLA guarantees (typically 99.9%+)
- **Managed Identity Configuration**: Cloud deployments are assumed to have managed identities (Azure Managed Identity, AWS IAM Roles) properly configured with appropriate permissions to access HSM services; Kubernetes deployments have service accounts configured with necessary RBAC permissions
- **Environment Detection**: The system can reliably detect deployment environment through standard environment variables and metadata services (e.g., Azure IMDS, AWS IMDS, Kubernetes downward API)
- **Provider SDK Maturity**: Azure Key Vault SDK, AWS KMS SDK, and GCP Cloud KMS SDK are stable and support required operations (key generation, signing, encryption) with HSM backing
- **Development Environment**: Developers have local OS instances with secure storage capabilities (Windows 10+, macOS 10.12+, Linux with gnome-keyring or similar)
- **Browser Support**: Web Crypto API is available in all supported browsers (Chrome, Firefox, Safari, Edge - all modern versions)
- **Kubernetes Version**: Kubernetes deployments use version 1.20+ with support for encrypted secrets and external KMS integration
- **Security Clearance**: Production HSM usage implies appropriate access controls and compliance requirements are already established by the organization
- **Performance Impact**: HSM operations introduce acceptable latency (<500ms for signing operations) compared to local cryptographic operations
- **Cost Model**: Organizations deploying to production accept cloud provider HSM costs as part of security requirements
- **Key Backup**: For non-HSM scenarios (development), users are responsible for backing up key material; HSM-backed keys are non-exportable by design

## Dependencies

- **Sorcha.Cryptography Library**: Must support abstraction layer for multiple cryptographic backends
- **Sorcha.Wallet.Service**: Requires integration with cryptographic backend selection and configuration
- **Cloud Provider SDKs**: Azure.Security.KeyVault, AWS SDK for .NET (AWSSDK.KeyManagementService), Google.Cloud.Kms.V1
- **Identity and Authentication SDKs**: Azure.Identity (for Managed Identity), AWS SDK credential providers (for IAM Roles), Kubernetes client-go (for service account tokens)
- **OS Cryptographic APIs**: Windows DPAPI (via .NET ProtectedData), macOS Security Framework (via P/Invoke or native interop), Linux Secret Service (via DBus or library)
- **Web Crypto API**: Browser standard (no external dependency, but requires HTTPS context)
- **Kubernetes Client**: For Kubernetes secret integration (KubernetesClient or official client-go bindings)
- **Configuration System**: .NET Configuration framework with environment-specific settings
- **Audit Logging**: Integration with provider-specific audit systems (Azure Monitor, AWS CloudTrail, GCP Cloud Logging)

## Non-Functional Requirements

### Security

- **NFR-001**: Private keys stored in HSM MUST be marked as non-exportable and all operations MUST execute within enclave boundary
- **NFR-002**: All cryptographic operations MUST be audit logged with tamper-evident trails
- **NFR-003**: Development mode MUST include prominent warnings and require explicit acknowledgment of reduced security
- **NFR-004**: Key material MUST be automatically redacted from all logs, error messages, and diagnostic outputs
- **NFR-005**: Memory dumps and crash reports MUST exclude cryptographic key material through secure memory allocation
- **NFR-006**: Access to cryptographic operations MUST be authenticated and authorized using managed identities (Azure Managed Identity, AWS IAM Roles) in cloud environments, Kubernetes service accounts in K8s deployments, and process identity for local development, with authorization based on deployment environment policies

### Performance

- **NFR-007**: HSM signing operations SHOULD complete within 500ms under normal conditions (excluding provider latency)
- **NFR-008**: System SHOULD cache provider connections and reuse them to minimize authentication overhead
- **NFR-009**: Failed operations SHOULD implement exponential backoff with jitter to prevent thundering herd during provider outages

### Reliability

- **NFR-010**: System MUST implement health checks for cryptographic backend availability
- **NFR-011**: Production deployments MUST fail fast during startup if required cryptographic backend is unavailable
- **NFR-012**: Operation retries MUST be idempotent and include appropriate timeout/deadline mechanisms

### Usability

- **NFR-013**: Error messages MUST clearly indicate whether the issue is configuration, connectivity, or provider-related
- **NFR-014**: Development environment setup MUST be documented with step-by-step instructions for each supported OS
- **NFR-015**: Provider selection SHOULD be automatic based on environment detection, with manual override available via configuration

### Compatibility

- **NFR-016**: The same application binary MUST support all cryptographic backends without recompilation
- **NFR-017**: Configuration format MUST be consistent across all providers with provider-specific sections as needed
- **NFR-018**: System MUST support .NET 10 standard cryptographic abstractions and not require breaking changes to existing Sorcha.Cryptography APIs

## Out of Scope

- **Hardware Security Modules (On-Premise)**: This specification focuses on cloud HSMs and OS-level secure storage. Support for on-premise HSM devices (e.g., Thales, SafeNet) is deferred to future iterations
- **Custom Cryptographic Algorithms**: The system will use standard algorithms supported by each provider. Custom or experimental algorithms are out of scope
- **Key Escrow/Recovery Services**: Organizational key escrow (e.g., master key recovery by administrators) is a separate feature and not included in this specification
- **Blockchain-Specific Hardware Wallets**: Integration with consumer hardware wallets (Ledger, Trezor) is out of scope; this specification focuses on server-side and browser-based storage
- **FIPS 140-2/3 Certification**: While cloud providers offer FIPS-validated HSMs, certifying the Sorcha application itself is out of scope
- **Quantum-Resistant Algorithms**: Future-proofing for post-quantum cryptography is important but deferred to a separate specification
- **Key Sharing/Multi-Party Computation**: Advanced key management like Shamir Secret Sharing or MPC is out of scope for this specification

## Related Specifications

- [Sorcha Cryptography Rewrite](../../.specify/specs/sorcha-cryptography-rewrite.md) - Core cryptographic library architecture
- [Sorcha Wallet Service](../../.specify/specs/sorcha-wallet-service.md) - Wallet management service requiring secure key storage
- [Sorcha Transaction Handler](../../.specify/specs/sorcha-transaction-handler.md) - Transaction signing using secure keys

## References

- [Azure Key Vault Documentation](https://learn.microsoft.com/en-us/azure/key-vault/)
- [AWS Key Management Service (KMS)](https://docs.aws.amazon.com/kms/)
- [Google Cloud Key Management](https://cloud.google.com/kms/docs)
- [Web Crypto API Specification](https://www.w3.org/TR/WebCryptoAPI/)
- [Kubernetes Secrets Management](https://kubernetes.io/docs/concepts/configuration/secret/)
- [Windows Data Protection API (DPAPI)](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection)
