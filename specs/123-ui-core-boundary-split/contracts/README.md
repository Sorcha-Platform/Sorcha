# Contracts — Feature 123

Feature 123 is a refactor, not an API feature. There are no REST/gRPC endpoints to specify. The equivalent of an API contract here is the **interface-split contract** — for each bi-modal C# interface that is split into narrower interfaces, what methods land on which side, what the new interface looks like, and what the migration path is for consumers.

Five contracts cover the splits and extractions in this feature. Plus one general pattern document for shared-DTO extraction that future contributors apply when they discover similar co-located DTOs.

## Index

| Contract | What it covers |
|---|---|
| `register-service-split.md` | `IRegisterService` → `IRegisterReadService` + `IRegisterGovernanceService` |
| `organization-admin-dto-extraction.md` | Extracting `OrganizationDto`, `BrandingDto`, `UserDto`, etc. from `IOrganizationAdminService.cs`, plus the new `IOrganizationReadService` |
| `wallet-api-service-audit.md` | Verdict from Phase 0 audit: `IWalletApiService` is not bi-modal (USER-only). No split. Just a folder move. |
| `register-subscription-audit.md` | Verdict from Phase 0 audit: `IRegisterSubscriptionService` is not bi-modal (USER-only). No split. Just a folder move. |
| `shared-dto-extraction-pattern.md` | General pattern formalising the `SchemaOverlayFieldInfo` and `OrganizationDto` extractions for future use |

## Contract verification

Each contract document includes a **Verification** section listing concrete checks that confirm the contract is honoured after execution. Verification mirrors the Given/When/Then style of acceptance scenarios in `spec.md`.
