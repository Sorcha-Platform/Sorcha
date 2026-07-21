# Sorcha Feature Specs

This directory contains the feature specifications that drove development of the Sorcha platform. Each subdirectory is one feature slice, written in the spec-kit format (`spec.md`, `plan.md`, `tasks.md`, `data-model.md`, `contracts/`, `checklists/`). Numbering is roughly chronological — lower numbers are foundational infrastructure, higher numbers are later features. Completed-feature documentation (endpoints, configuration, cross-cutting patterns) lives in the relevant service READMEs and `docs/`; the specs here are the design-history record, not the operational reference.

A small number of directories carry a `001-` prefix — these are early exploratory spikes where numbering had not yet stabilised. Some numbers are skipped (features that were merged into neighbours or superseded before shipping). The `master/` subdirectory is a legacy artefact and not a feature spec.

For a consolidated platform overview suitable for AI consumption, see [PLATFORM-SPECIFICATION.md](PLATFORM-SPECIFICATION.md).

---

## Index

| Spec directory | Title |
|---|---|
| 001-blueprint-chat | Feature Specification: AI-Assisted Blueprint Design Chat |
| 001-designer-completion | Feature Specification: Blueprint Designer Completion |
| 001-hardware-crypto-enclaves | Feature Specification: Hardware Cryptographic Storage and Execution Enclaves |
| 001-mcp-server | Feature Specification: Sorcha MCP Server |
| 001-participant-identity | Feature Specification: Participant Identity Registry |
| 001-participant-records | Feature Specification: Published Participant Records on Register |
| 001-register-genesis | Feature Specification: Register Creation with Genesis Record and Administrative Control |
| 001-ui-token-refresh | Feature Specification: Sorcha.UI Authentication Token Management and Login UX |
| 001-validator-service-wallet | Feature Specification: Validator Service Wallet Access |
| 002-storage-abstraction | Feature Specification: Multi-Tier Storage Abstraction Layer |
| 002-validator-service | Feature Specification: Validator Service - Distributed Transaction Validation and Consensus |
| 011-admin-dashboard | Feature Specification: Admin Dashboard and Management |
| 012-registers-transactions-ui | Feature Specification: Registers and Transactions UI |
| 013-system-schema-store | Feature Specification: System Schema Store |
| 014-client-zkp | Feature Specification: Browser Cryptographic Capabilities |
| 015-fix-register-crypto | Feature Specification: Fix Register Creation - Fully Functional Cryptographic Register Flow |
| 016-cli-register-update | Feature Specification: CLI Register Commands Update |
| 017-ui-register-management | Feature Specification: UI Register Management |
| 018-blueprint-engine-integration | Feature Specification: Blueprint Engine Integration |
| 019-payload-encryption | Feature Specification: Payload Encryption for DAD Security Model |
| 020-validator-engine-validation | Feature Specification: Validator Engine - Schema & Chain Validation |
| 021-transaction-query-api | Feature Specification: Transaction Query API |
| 022-resolve-runtime-stubs | Feature Specification: Resolve Runtime Stubs and Production-Critical TODOs |
| 023-consolidate-tx-versioning | Feature Specification: Consolidate Transaction Versioning |
| 025-ui-modernization | Feature Specification: Sorcha UI Modernization |
| 026-fix-register-creation-pipeline | Feature Specification: Register Creation Pipeline Fix |
| 027-blueprint-template-library | Feature Specification: Blueprint Template Library & Ping-Pong Blueprint |
| 028-fix-transaction-pipeline | Feature Specification: Fix Transaction Submission Pipeline |
| 029-blueprint-visual-designer | Feature Specification: Blueprint Visual Designer |
| 031-register-governance | Feature Specification: Register Governance — Genesis Blueprint & Decentralized Identity |
| 031-verifiable-credentials | Feature Specification: Verifiable Credentials & eIDAS-Aligned Attestation System |
| 032-action-form-renderer | Feature Specification: Action Form Renderer |
| 033-fix-wallet-dashboard-bugs | Feature Specification: Fix Wallet Dashboard and Navigation Bugs |
| 034-schema-library | Feature Specification: Schema Library |
| 036-unified-transaction-submission | Feature Specification: Unified Transaction Submission & System Wallet Signing Service |
| 037-new-submission-page | Feature Specification: New Submission Page |
| 038-content-type-payload | Feature Specification: Content-Type Aware Payload Encoding |
| 039-verifiable-presentations | Feature Specification: Verifiable Credential Lifecycle & Presentations |
| 040-quantum-safe-crypto | Feature Specification: Quantum-Safe Cryptography Upgrade |
| 041-auth-integration | Feature Specification: Authentication & Authorization Integration |
| 043-ui-cli-modernization | Feature Specification: UI & CLI Modernization |
| 044-codebase-consolidation | Feature Specification: Codebase Cleanup & Consolidation |
| 045-encrypted-payload-integration | Feature Specification: Encrypted Payload Integration |
| 046-ui-polish-designer | Feature Specification: UI Polish & Blueprint Designer |
| 047-inbound-tx-routing | Feature Specification: Inbound Transaction Routing & User Notification |
| 048-register-policy-model | Feature Specification: Unified Register Policy Model & System Register |
| 049-system-admin-tooling | Feature Specification: System Administration Tooling |
| 050-identity-credentials-admin | Feature Specification: Identity & Credentials Admin |
| 051-operations-monitoring-admin | Feature Specification: Operations & Monitoring Admin |
| 052-encryption-integration | Feature Specification: Envelope Encryption Integration |
| 054-org-identity-admin | Feature Specification: Organization Admin & Identity Management |
| 055-passkey-auth | Feature Specification: Passkey (WebAuthn/FIDO2) Authentication |
| 056-dpp-schema-provider | Feature Specification: Digital Product Passport (DPP) Schema Provider |
| 057-system-register-ledger | Feature Specification: System Register as Real Ledger |
| 058-platform-org-topology | Feature Specification: Platform Organisation Topology |
| 059-designer-blueprint-upgrade | Feature Specification: Designer & Blueprint Instructions Upgrade |
| 060-relay-aware-communication | Feature Specification: Relay-Aware Peer Communication |
| 060-wallet-recovery | Feature 060: Wallet Recovery |
| 061-edge-device-integration | Feature 061: Edge Device Integration |
| 062-pending-action-notifications | Feature Specification: Pending Action Notifications & User Communications |
| 063-ai-builder-schemas-vc | Feature Specification: AI Blueprint Builder Enhancement — Schema Library, VC/DPP Integration, and UX Overhaul |
| 064-transaction-explorer | Feature Specification: Transaction Explorer UX Overhaul |
| 065-participant-encryption | Feature Specification: Participant Resolution, Starting Action Binding & Field-Level Encryption |
| 066-validator-consensus-security | Feature Specification: Validator Consensus Security |
| 067-register-security-hardening | Feature Specification: Register TenantId Removal & Security Hardening |
| 068-blueprint-persistence | Feature Specification: Blueprint Service Persistence & Validator Crash Recovery |
| 069-pending-actions-ux | Feature Specification: Pending Actions UX Overhaul & Instance Reference System |
| 069-unified-org-management | Feature Specification: Unified Organisation Management UI |
| 070-ledger-recovery | Feature Specification: Blueprint Service Ledger Recovery & Register Status Sync |
| 071-p2p-register-sync | Feature Specification: P2P Register Replication — End-to-End Transaction Sync |
| 075-fle-crypto-progress-ux | Feature Specification: Field-Level Encryption Completion & Crypto Progress UX |
| 076-register-subscription-sync | Feature Specification: Register Subscription Sync Pipeline |
| 077-participant-autolink-user-provisioning | Feature Specification: Auto-Register Participant & Auto-Link Wallet + PlatformUser Admin Provisioning |
| 078-register-sync-status | Feature Specification: Register Sync Status Lifecycle & UI Improvements |
| 079-trust-hardening | Feature Specification: Transaction Receipts, Merkle Inclusion Proofs & Revocation Transactions |
| 080-cli-modernisation | Feature Specification: CLI Modernisation and Feature Completion |
| 081-trade-finance-walkthrough | Feature Specification: Trade Finance Walkthrough |
| 082-cloud-kms | Feature Specification: Cloud KMS Key Management |
| 083-wallet-key-derivation | Feature Specification: Wallet Key Derivation & UI Transaction Lifecycle |
| 084-mobile-package-infra | Feature Specification: Mobile Package Infrastructure |
| 085-stored-data-transactions | Feature Specification: Stored Data Transactions |
| 086-validator-key-roster | Feature Specification: Validator Key Roster |
| 087-actor-agent | Feature Specification: Autonomous Actor Agent Framework |
| 087-system-register-governance | 087 - System Register Governance |
| 089-signalr-minimal-disclosure | Feature Specification: SignalR Minimal Disclosure & Notification Fix |
| 091-new-submissions-workspace | Feature Specification: New Submissions & Action Workspace |
| 092-consumer-persona | Feature Specification: Consumer Persona and Nav Tidy |
| 093-vc-security-fixes | Feature Specification: Credential & Presentation Security Fixes (HAIP Prep) |
| 094-sdjwt-haip-hardening | Feature Specification: SD-JWT VC HAIP Hardening |
| 095-ietf-token-status-list | Feature Specification: IETF Token Status List (Parallel to W3C) |
| 096-x509-org-trust | Feature Specification: X.509 Organisation Trust Integration |
| 097-openid4vci-issuer | Feature Specification: OpenID4VCI Issuer Endpoint (HAIP) |
| 098-openid4vp-verifier | Feature Specification: OpenID4VP Verifier Endpoint (HAIP) |
| 099-genesis-trust-anchor | Feature Specification: System Register Genesis Trust Anchor |
| 100-resilient-bootstrap | Feature Specification: Resilient System Register Bootstrap |
| 101-haip-walkthroughs | Feature Specification: HAIP Walkthroughs |
| 102-haip-blueprint-integration | Feature Specification: HAIP Blueprint Integration |
| 103-verified-citizen-v2 | Feature Specification: Verified Citizen v2 |
| 104-credential-claim-action | Feature Specification: Credential Claim Action (Feature 103 Wave 14) |
| 106-register-native-credentials | Feature Specification: Register-native credential delivery |
| 107-assured-identity-v1 | Feature Specification: Assured Identity v1 |
| 108-register-local-relationship | Feature Specification: Register State Aggregation & Local Relationship |
| 109-designer-shell-redesign | Feature Specification: AI Designer Unified Shell |
| 110-agent-persona-mode | Feature Specification: Agent Persona Mode |
| 111-presentation-lifecycle | Feature Specification: Timebound Presentation Lifecycle |
| 112-email-sweep | Feature Specification: Transactional Email & Verification Sweep |
| 113-storage-durability-audit | Feature Specification: Storage Provider Audit and Validator Mempool Durability |
| 114-citizen-wallet-pwa | Feature Specification: Citizen Wallet PWA |
| 115-social-signup | Feature Specification: Public Social Signup on n1 |
| 116-account-linking | Feature Specification: Account Linking & Auth-Method Management |
| 117-ai-discoverability | Feature Specification: AI Discoverability & Machine-Readable Marketing |
| 118-notifications-architecture | Feature Specification: Notifications & Realtime Architecture |
| 119-presentation-seal-ordering | Feature Specification: Presentation Lifecycle Chain-Race Resolution via Seal-Aware Ordering |
| 120-production-issuer-signature-verification | Feature Specification: Production Issuer Signature Verification |
| 121-programmable-validation-rules | Feature Specification: Programmable Validation Rule Set (Genesis-Embedded, Governance-Updateable) |
| 122-shared-user-components | Feature Specification: Shared User-Facing UI Component Library |
| 123-ui-core-boundary-split | Feature Specification: UI.Core User/Admin Type-Level Boundary Refactor |
| 124-assured-identity-pwa | Feature Specification: AssuredIdentity on the PWA |
| 125-sorcha-wallet-user-agent | Feature Specification: Sorcha Wallet (Full User-Agent v1) |
| 126-enrol-inside-wizard | Feature Specification: Sorcha Wallet enrolment inside a council application wizard |
| 127-credential-gated-service | Feature Specification: Credential-gated second council service (Blue Badge) |
| 128-cold-start-onboarding | Feature Specification: Cold-start onboarding and device pairing UX |
| 131-dashboard-org-scoping | Specification — Dashboard org-scoping (UX-005) |
| 132-cross-device-qr-scan | Specification — Cross-device QR scan (wallet → council page) |
| 133-cli-api-coverage | Feature Specification: CLI API Surface Catch-Up |
| 134-presentation-history | Feature Specification: Cross-Device Citizen Presentation History |
| 135-eudi-credential-format-trust | Feature Specification: EUDI Credential Format & Unified Trust |
| 136-jwt-audience-tiers | Feature Specification: Tiered-Audience JWT Identity Model + Issuer Hardening (Spec A) |
| 137-cross-node-submission | Feature Specification: Cross-node submission round-trip (Stage 5) |
| 138-federation-trust-hardening | Feature Specification: Federation Trust Hardening |
| 139-mcp-foundation | Feature Specification: MCP Server Foundation |
| 140-mcp-capabilities | Feature Specification: MCP Server Capability Gap Closure |
| 141-wallet-home-redesign | Feature Specification: Citizen Wallet Home — "Bolder" Visual Reskin |
| 142-blueprint-lifecycle | Feature Specification: Blueprint Design Lifecycle Overhaul |
| 143-peer-nat-traversal | Feature Specification: Peer NAT Traversal (Reverse-Stream Rendezvous) |
| 144-assured-identity-demo | Feature Specification: Assured Identity Demo Environment |
| 145-ledger-derived-instances | Feature Specification: Ledger-Derived Workflow Instances |
| 146-tenant-secret-protection | Feature Specification: Tenant Service At-Rest Secret Protection |
| 147-authorization-gap-closure | Feature Specification: Authorization-gap closure |
| 148-verification-correctness | Feature Specification: Verification-correctness |
| 149-pwa-pairing-takeover-wallet-aware | Feature Specification: Wallet-aware PairingTakeover |

---

## Related Documents

- [.specify/constitution.md](../.specify/constitution.md) — Architectural principles
- [.specify/MASTER-TASKS.md](../.specify/MASTER-TASKS.md) — Task tracking
- [docs/architecture.md](../docs/architecture.md) — System architecture
- [docs/reference/API-DOCUMENTATION.md](../docs/reference/API-DOCUMENTATION.md) — API reference
