# Services

Sorcha is composed of 7 microservices orchestrated by .NET Aspire.

| Service | Purpose | Docs |
|---------|---------|------|
| API Gateway | YARP reverse proxy, unified entry point | [README](../../src/Services/Sorcha.ApiGateway/README.md) |
| Blueprint Service | Workflow management, action execution, SignalR | [README](../../src/Services/Sorcha.Blueprint.Service/README.md) |
| Register Service | Distributed ledger, OData queries, chain integrity | [README](../../src/Services/Sorcha.Register.Service/README.md) |
| Tenant Service | Multi-tenant auth, JWT issuer, participant identity | [README](../../src/Services/Sorcha.Tenant.Service/README.md) |
| Wallet Service | HD wallets, crypto operations, credential management | [README](../../src/Services/Sorcha.Wallet.Service/README.md) |
| Validator Service | Consensus, docket building, transaction validation | [README](../../src/Services/Sorcha.Validator.Service/README.md) |
| Peer Service | P2P networking, gRPC, register replication | [README](../../src/Services/Sorcha.Peer.Service/README.md) |

## Architecture

```
┌─────────────┐     ┌─────────────────┐     ┌──────────────────┐
│  Sorcha UI  │────▶│   API Gateway   │────▶│  Blueprint Svc   │
│  (Blazor)   │     │    (YARP)       │     │  (Workflows)     │
└─────────────┘     └─────────────────┘     └────────┬─────────┘
                            │                         │
                    ┌───────┴───────┐        ┌───────┴────────┐
              ┌─────▼─────┐   ┌─────▼─────┐  │  ┌────────────▼┐
              │  Wallet   │   │ Register  │◀─┘  │  Validator  │
              │  Service  │   │  Service  │     │   Service   │
              └─────┬─────┘   └─────┬─────┘     └─────────────┘
              │PostgreSQL │   │  MongoDB  │     │   Redis     │
```

See the [Architecture Reference](../reference/architecture.md) for detailed diagrams.
