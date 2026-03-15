# Quickstart: System Register as Real Ledger

**Feature**: 057-system-register-ledger

## What Changed

The system register is now a **real register** on the distributed ledger, not a separate MongoDB collection. Blueprints are stored as transactions, validated by the validator service, sealed into dockets, and replicated via the peer network.

## How It Works

### Automatic Bootstrap

On first startup, the platform automatically:
1. Creates the system register using the standard two-phase register creation flow
2. Signs the genesis transaction with the system wallet
3. Waits for the genesis docket to seal
4. Publishes seed blueprints (register-creation-v1, register-governance-v1) as transactions

No manual steps required. Subsequent restarts skip bootstrap (idempotent).

### Viewing the System Register

The system register appears in two places:
- **Registers list** (`/registers`) — alongside all other registers
- **Admin System Register page** (`/admin/system-register`) — with blueprint catalog view

### Publishing a Blueprint

```bash
# Via API (authenticated as Administrator)
curl -X POST https://your-host/api/system-register/publish \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "blueprintId": "my-workflow-v1",
    "blueprint": {
      "@context": "https://sorcha.dev/blueprints/v1",
      "id": "my-workflow-v1",
      "title": "My Workflow",
      "version": "1.0.0",
      "actions": [...]
    }
  }'
```

### Querying Blueprints

```bash
# List all blueprints
curl https://your-host/api/system-register/blueprints

# Get a specific blueprint
curl https://your-host/api/system-register/blueprints/register-creation-v1

# Or use the standard register transaction API
curl https://your-host/api/registers/aebf26362e079087571ac0932d4db973/transactions
```

## Migration Notes

- The `SORCHA_SEED_SYSTEM_REGISTER` environment variable is no longer needed
- The `sorcha_system_register_blueprints` MongoDB collection is no longer used
- Any data in the old collection is not migrated (fresh bootstrap creates new data)
- Existing blueprint content is identical — only the storage mechanism changed

## Key Files

| File | Role |
|------|------|
| `Register.Service/Services/SystemRegisterBootstrapper.cs` | Orchestrates bootstrap on startup |
| `Register.Service/Services/SystemRegisterService.cs` | Queries register transactions for blueprints |
| `Register.Service/Endpoints/SystemRegisterEndpoints.cs` | REST API convenience endpoints |
| `Register.Models/Constants/SystemRegisterConstants.cs` | Deterministic ID and display name |
