# Quickstart: Validator Consensus Security

**Feature**: 066-validator-consensus-security

## Implementation Order

```
Phase 1: Validator Approval Workflow (4.5)
├── 1a. Add Revoked status, suspend/reactivate/revoke to IValidatorRegistry
├── 1b. MongoDB persistence layer (write-through from Redis)
├── 1c. Suspend/Reactivate/Revoke endpoints + last-validator guard
├── 1d. Audit logging (ValidatorAuditEntry collection)
└── 1e. Admin UI page (ValidatorManagement.razor)

Phase 2: Consensus Vote Verification (4.1)
├── 2a. Define canonical vote signing contract
├── 2b. Add signature fields to ConsensusVote
├── 2c. Implement vote signing in validator (outgoing votes)
├── 2d. Implement vote verification in ConsensusEngine/SignatureCollector
└── 2e. Reject votes from non-Active validators

Phase 3: Transaction Replay Protection (4.2)
├── 3a. Add SequenceNumber to Transaction model
├── 3b. Create WalletSequence MongoDB collection + repository
├── 3c. Add sequence validation to ValidationEngine
├── 3d. Add sequence query endpoint
└── 3e. Update Blueprint Service to include sequence numbers
```

## Key Files to Modify

### Phase 1 (Validator Approval)
- `src/Services/Sorcha.Validator.Service/Services/Interfaces/IValidatorRegistry.cs` — Add suspend/reactivate/revoke, rename Removed→Revoked
- `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs` — MongoDB write-through, new operations
- `src/Services/Sorcha.Validator.Service/Endpoints/ValidatorRegistrationEndpoints.cs` — New endpoints
- `src/Services/Sorcha.Validator.Service/Configuration/ValidatorRegistryConfiguration.cs` — MongoDB connection
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/` — New Blazor pages

### Phase 2 (Vote Verification)
- `src/Services/Sorcha.Validator.Service/Models/ConsensusVote.cs` — Add signature fields
- `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs` — Verify votes
- `src/Services/Sorcha.Validator.Service/Services/SignatureCollector.cs` — Add verification logic
- `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs` — Already has VerifySignatureAsync

### Phase 3 (Replay Protection)
- `src/Services/Sorcha.Validator.Service/Models/Transaction.cs` — Add SequenceNumber
- `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs` — Sequence validation stage
- `src/Services/Sorcha.Validator.Service/Endpoints/ValidationEndpoints.cs` — Sequence query endpoint
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/TransactionBuilderService.cs` — Include sequence number

## Testing Strategy

- **Unit tests**: Each new method in ValidatorRegistry, ConsensusEngine, ValidationEngine
- **Integration tests**: Full approve→vote→verify cycle
- **E2E tests**: Admin UI validator management (Playwright)
- **Target**: >85% coverage on new code
