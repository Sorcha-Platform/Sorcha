# Services

Sorcha is composed of 7 microservices orchestrated by .NET Aspire.

| Service | Purpose | Docs |
|---------|---------|------|
| [API Gateway](./api-gateway) | YARP reverse proxy, unified entry point | [README](./api-gateway) |
| [Blueprint Service](./blueprint-service) | Workflow management, action execution, SignalR | [README](./blueprint-service) |
| [Register Service](./register-service) | Distributed ledger, OData queries, chain integrity | [README](./register-service) |
| [Tenant Service](./tenant-service) | Multi-tenant auth, JWT issuer, participant identity | [README](./tenant-service) |
| [Wallet Service](./wallet-service) | HD wallets, crypto operations, credential management | [README](./wallet-service) |
| [Validator Service](./validator-service) | Consensus, docket building, transaction validation | [README](./validator-service) |
| [Peer Service](./peer-service) | P2P networking, gRPC, register replication | [README](./peer-service) |

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

See the [Architecture Reference](/reference/architecture) for detailed diagrams.
