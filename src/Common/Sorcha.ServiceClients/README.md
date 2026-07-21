# Sorcha.ServiceClients

**Consolidated gRPC + HTTP clients for inter-service communication**

## Purpose

Unified client implementations for Sorcha's services so consumers don't hand-roll (and duplicate) their own. Reference this library and call `AddServiceClients(...)` to register everything.

## Two-project layout

- **`Sorcha.ServiceClients`** (this project) — the umbrella. Holds the **gRPC** clients (`IDocketSyncClient`, `IRegisterAddressClient`, `IWalletNotificationClient`), the **Peer** client (`IPeerServiceClient`), the DID resolver wiring, and the single `AddServiceClients(...)` DI entry point.
- **`Sorcha.ServiceClients.Http`** — the **HTTP REST** typed clients. `AddServiceClients` delegates their registration here (`AddHttpServiceClients`). This is where the `I{Service}ServiceClient` interfaces live.

## Supported clients

| Client | Protocol | Where | Interface |
|--------|----------|-------|-----------|
| Wallet | HTTP | `Sorcha.ServiceClients.Http` | `IWalletServiceClient` |
| Register | HTTP | `Sorcha.ServiceClients.Http` | `IRegisterServiceClient` |
| Blueprint | HTTP | `Sorcha.ServiceClients.Http` | `IBlueprintServiceClient` |
| Tenant | HTTP | `Sorcha.ServiceClients.Http` | `ITenantServiceClient` |
| Validator | HTTP | `Sorcha.ServiceClients.Http` | `IValidatorServiceClient` |
| HAIP | HTTP | `Sorcha.ServiceClients.Http` | `IHaipServiceClient` |
| Participant / Passkey / Register-Invitation | HTTP | `Sorcha.ServiceClients.Http` | `IParticipantServiceClient`, `IPasskeyServiceClient`, `IRegisterInvitationServiceClient` |
| Peer | gRPC | `Sorcha.ServiceClients` | `IPeerServiceClient` |
| Docket sync / Register address / Wallet notification | gRPC | `Sorcha.ServiceClients` | `IDocketSyncClient`, `IRegisterAddressClient`, `IWalletNotificationClient` |
| Validator (submit) | gRPC | Validator Service protos | via proto |

> The HTTP clients are current and in use across the platform — not stubs. Service-to-service HTTP calls attach a service-tier bearer via `ServiceAuthMessageHandler`.

## Usage

### 1. Reference the project

```xml
<ProjectReference Include="..\..\Common\Sorcha.ServiceClients\Sorcha.ServiceClients.csproj" />
```

### 2. Register clients in DI

```csharp
// Program.cs — registers the HTTP clients (via Sorcha.ServiceClients.Http),
// the gRPC clients, the Peer client, and the DID resolvers.
builder.Services.AddServiceClients(builder.Configuration);
```

### 3. Inject and use

```csharp
public class MyService(IWalletServiceClient walletClient)
{
    public Task DoWork() => walletClient.GetWalletsByOwnerAsync(ownerId);
}
```

## Configuration

Client addresses resolve via .NET Aspire service discovery when running under the AppHost. Overrides bind from configuration, e.g.:

```json
{
  "ServiceClients": {
    "WalletService":    { "Address": "http://wallet-service:8080" },
    "RegisterService":  { "Address": "http://register-service:8080" },
    "BlueprintService": { "Address": "http://blueprint-service:8080" },
    "ValidatorService": { "Address": "http://validator-service:8080" },
    "PeerService":      { "Address": "http://peer-service:5000" }
  }
}
```

(Container-internal ports are `8080` for HTTP services and `5000` for the Peer gRPC endpoint; host-published ports differ — see `docs/getting-started/PORT-CONFIGURATION.md`.)

## Design principles

1. **Single source of truth** — one client implementation per service; consumers don't re-roll them.
2. **Comprehensive interfaces** — an interface carries all methods any consumer needs.
3. **Service discovery** — Aspire discovery when available, config override otherwise.
4. **Resilience** — retry/backoff on the HTTP handlers.
5. **Protocol-appropriate** — HTTP for REST surfaces, gRPC for peer/replication/notification streams.

## Contributing

When adding a method: add it to the interface, implement it in the client, update this README, and add a test under `tests/Sorcha.ServiceClients.Tests`.

> **Do not re-declare Wallet HTTP DTOs.** The canonical `WalletDto` / `CreateWallet*` / `SignTransaction*` / `WalletAddressDto` / `AddressListResponse` live only in `Sorcha.Wallet.Contracts` (CI-gated by `wallet-contracts-gate`).
