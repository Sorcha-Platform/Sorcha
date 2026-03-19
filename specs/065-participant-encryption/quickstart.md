# Quickstart: 065 Participant Resolution & Field-Level Encryption

## What This Feature Does

Replaces hardcoded wallet addresses in blueprints with dynamic participant resolution. Any user can start a workflow (their wallet binds to the participant role). Organisational participants are resolved from published records on the register. DevMode allows plaintext storage for development; encrypted mode uses field-level envelope encryption.

## Development Setup

1. Fresh Docker environment: `docker-compose down -v && docker-compose up -d`
2. Run E2E test: `dotnet test tests/Sorcha.UI.E2E.Tests/ --filter "Category=LongRunning"`
3. The council credential flow test exercises all four stories

## Key Changes By Service

### Validator Service
- `ValidationEngine.ValidateBlueprintConformanceAsync` — 3-tier participant resolution (starting action → instance binding → register lookup)
- `IBlueprintFetcher` already wired (from validator-pipeline-confirmation fix)

### Blueprint Service
- `ActionExecutionService.ExecuteAsync` — bind sender wallet on starting action, check DevMode before encryption
- `Instance.ParticipantWallets` — populated at execution time, not just publish

### Register Service
- `Register` model — new `DevMode` bool field
- New endpoint: `GET /api/registers/{registerId}/participants/resolve`

### Common Libraries
- `Participant.WalletAddress` — made optional (Blueprint.Models)
- No changes to EncryptionPipelineService, DisclosureProcessor, or DisclosureGroupBuilder

## Testing Strategy

- **Unit**: Validator participant resolution (3-tier), Instance binding immutability
- **Integration**: E2E council credential flow in DevMode
- **Contract**: Register DevMode toggle endpoint
- **Encryption**: Disclosure group optimisation, multi-recipient key wrapping (when DevMode off)
