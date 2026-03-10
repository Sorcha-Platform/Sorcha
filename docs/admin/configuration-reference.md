# Configuration Reference

Complete reference of all environment variables used by Sorcha services. Variables are set in the `.env` file and distributed to containers via `docker-compose.yml`.

## JWT & Authentication

These variables are shared across all services via the `x-jwt-env` YAML anchor.

| Variable | Default | Description |
|----------|---------|-------------|
| `INSTALLATION_NAME` | `localhost` | JWT issuer and audience name. Set to your domain in production. |
| `JWT_SIGNING_KEY` | (dev key) | Base64-encoded 256-bit HMAC signing key. All services must share the same key. |

**Important:** The default `JWT_SIGNING_KEY` in `docker-compose.yml` is for development only. Generate a unique key for every deployment.

### Per-Service Authentication

Each service has its own service-to-service auth credentials:

| Service | Client ID | Scopes |
|---------|-----------|--------|
| Blueprint | `service-blueprint` | `wallets:sign registers:write blueprints:manage` |
| Wallet | `service-wallet` | `registers:write` |
| Register | `register-service` | `wallets:sign validator:write` |
| Tenant | `tenant-service` | (wallet, register, validator access) |
| Validator | `validator-service` | `registers:write registers:read` |
| Peer | `service-peer` | `registers:write registers:read` |

| Variable | Example | Description |
|----------|---------|-------------|
| `ServiceAuth__ClientId` | `service-blueprint` | OAuth2 client ID for service-to-service auth |
| `ServiceAuth__ClientSecret` | (secret) | OAuth2 client secret |
| `ServiceAuth__Scopes` | `wallets:sign` | Space-separated list of requested scopes |

### Tenant Service JWT Settings

| Variable | Default | Description |
|----------|---------|-------------|
| `JwtSettings__InstallationName` | `localhost` | Derived from `INSTALLATION_NAME` |
| `JwtSettings__SigningKey` | (from env) | Derived from `JWT_SIGNING_KEY` |
| `JwtSettings__SigningKeySource` | `Configuration` | Where to load the signing key from |

## Databases

### PostgreSQL

| Variable | Default | Description |
|----------|---------|-------------|
| `POSTGRES_USER` | `sorcha` | PostgreSQL superuser name |
| `POSTGRES_PASSWORD` | `sorcha_dev_password` | PostgreSQL password. **Change for production.** |
| `POSTGRES_DB` | `sorcha` | Default database name |
| `POSTGRES_PORT` | `5432` | Host port mapping for PostgreSQL |

#### Connection Strings

| Variable | Service | Default |
|----------|---------|---------|
| `ConnectionStrings__wallet-db` | Wallet | `Host=postgres;Database=sorcha_wallet;Username=sorcha;Password=sorcha_dev_password` |
| `ConnectionStrings__TenantDatabase` | Tenant | `Host=postgres;Database=sorcha_tenant;Username=sorcha;Password=sorcha_dev_password` |

### MongoDB

| Variable | Default | Description |
|----------|---------|-------------|
| `MONGO_INITDB_ROOT_USERNAME` | `sorcha` | MongoDB root username |
| `MONGO_INITDB_ROOT_PASSWORD` | `sorcha_dev_password` | MongoDB root password. **Change for production.** |
| `MONGODB_PORT` | `27017` | Host port mapping for MongoDB |

#### Connection Strings

| Variable | Service | Default |
|----------|---------|---------|
| `ConnectionStrings__mongodb` | Blueprint | `mongodb://sorcha:sorcha_dev_password@mongodb:27017` |
| `ConnectionStrings__MongoDB` | Register | `mongodb://sorcha:sorcha_dev_password@mongodb:27017` |
| `MongoDB__ConnectionString` | Peer | `mongodb://sorcha:sorcha_dev_password@mongodb:27017` |
| `MongoDB__DatabaseName` | Peer | `sorcha_system_register` |

### Redis

| Variable | Default | Description |
|----------|---------|-------------|
| `REDIS_PORT` | `16379` | Host port mapping for Redis |
| `REDIS_PASSWORD` | (empty) | Redis password. Set for production. |

#### Connection Strings

| Variable | Service | Default |
|----------|---------|---------|
| `ConnectionStrings__Redis` | Blueprint, Wallet, Register, Peer, Validator | `redis:6379` |
| `Redis__ConnectionString` | Tenant | `redis:6379` |
| `ConnectionStrings__Redis` | API Gateway | `redis:6379` |

## Register Storage (MongoDB)

The Register Service uses a dedicated MongoDB configuration for ledger storage:

| Variable | Default | Description |
|----------|---------|-------------|
| `RegisterStorage__Type` | `MongoDB` | Storage backend type |
| `RegisterStorage__MongoDB__ConnectionString` | (see above) | MongoDB connection string |
| `RegisterStorage__MongoDB__DatabaseName` | `sorcha_register_registry` | Registry metadata database |
| `RegisterStorage__MongoDB__DatabaseNamePrefix` | `sorcha_register_` | Prefix for per-register databases |
| `RegisterStorage__MongoDB__UseDatabasePerRegister` | `true` | Isolate each register in its own database |
| `RegisterStorage__MongoDB__RegisterCollectionName` | `registers` | Collection name for register metadata |
| `RegisterStorage__MongoDB__TransactionCollectionName` | `transactions` | Collection name for transactions |
| `RegisterStorage__MongoDB__DocketCollectionName` | `dockets` | Collection name for dockets |
| `RegisterStorage__MongoDB__CreateIndexesOnStartup` | `true` | Auto-create indexes on service start |

The Validator Service uses read-only access to the same MongoDB configuration (with `CreateIndexesOnStartup` set to `false`).

## Wallet Encryption

| Variable | Default | Description |
|----------|---------|-------------|
| `EncryptionProvider__Type` | `LinuxSecretService` | Encryption provider backend |
| `EncryptionProvider__DefaultKeyId` | `wallet-master-key-2025` | Key identifier for the master encryption key |
| `EncryptionProvider__LinuxSecretService__ServiceName` | `sorcha-wallet-service` | Service name for key storage |
| `EncryptionProvider__LinuxSecretService__FallbackKeyStorePath` | `/var/lib/sorcha/wallet-keys` | File-based key fallback path |
| `EncryptionProvider__LinuxSecretService__MachineKeyMaterial` | `sorcha-docker-wallet-stable-key-v1` | Stable key material for container rebuilds |

**Important:** The `MachineKeyMaterial` value ensures wallet encryption keys survive container rebuilds. Change this value only if you want to invalidate all existing encrypted keys.

## OpenTelemetry & Observability

Shared across all services via the `x-otel-env` YAML anchor:

| Variable | Default | Description |
|----------|---------|-------------|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://aspire-dashboard:18889` | OTLP collector endpoint (gRPC) |
| `OTEL_SERVICE_NAME` | (per service) | Service name in traces/metrics |
| `OTEL_RESOURCE_ATTRIBUTES` | `deployment.environment=docker` | Additional resource attributes |

### Aspire Dashboard

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPIRE_UI_PORT` | `18888` | Host port for the Aspire Dashboard UI |
| `OTLP_GRPC_PORT` | `4317` | Host port for OTLP gRPC collector |
| `OTLP_HTTP_PORT` | `4318` | Host port for OTLP HTTP collector |
| `DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS` | `true` | Allow anonymous access to dashboard |
| `DASHBOARD__OTLP__AUTHMODE` | `Unsecured` | OTLP authentication mode |

## ASP.NET Core

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Development` | Runtime environment (`Development`, `Docker`, `Production`) |
| `ASPNETCORE_URLS` | `http://+:8080` | Listening URLs inside containers |

## Feature Flags

| Variable | Default | Description |
|----------|---------|-------------|
| `OPENAPI_REQUIRE_AUTH` | `true` | Require JWT authentication for `/openapi` endpoints |
| `SORCHA_SEED_SYSTEM_REGISTER` | `false` | Auto-create the system register on startup |

## Service URLs (Internal)

These configure how services discover each other inside the Docker network. Typically only changed for custom deployments.

### API Gateway Upstream Routes

| Variable | Default | Description |
|----------|---------|-------------|
| `Services__Blueprint__Url` | `http://blueprint-service:8080` | Blueprint service upstream |
| `Services__Wallet__Url` | `http://wallet-service:8080` | Wallet service upstream |
| `Services__Register__Url` | `http://register-service:8080` | Register service upstream |
| `Services__Tenant__Url` | `http://tenant-service:8080` | Tenant service upstream |
| `Services__Peer__Url` | `http://peer-service:8080` | Peer service upstream |
| `Services__Validator__Url` | `http://validator-service:8080` | Validator service upstream |
| `Services__UI__Url` | `http://sorcha-ui-web:8080` | UI web application upstream |

### API Gateway Additional Settings

| Variable | Default | Description |
|----------|---------|-------------|
| `OpenApi__RequireAuth` | `true` | Require auth for OpenAPI documentation |
| `Dashboard__AspireDashboardUrl` | `http://localhost:18888` | URL for proxied dashboard access |

### Inter-Service Client Configuration

Each service configures clients for the services it calls:

| Variable Pattern | Example | Description |
|------------------|---------|-------------|
| `ServiceClients__<Service>__Address` | `http://wallet-service:8080` | Base URL for service client |
| `ServiceClients__<Service>__UseGrpc` | `false` | Use gRPC instead of HTTP |

## Peer Network

| Variable | Default | Description |
|----------|---------|-------------|
| `PEER_NODE_ID` | `local-peer.sorcha.dev` | Unique node identifier |
| `PEER_PUBLIC_ADDRESS` | (empty) | Public address for incoming connections |
| `PEER_GRPC_PORT` | `50051` | Host port for gRPC peer connections |
| `PeerService__Port` | `5000` | Internal gRPC listen port |
| `PeerService__EnableTls` | `false` | Enable TLS for gRPC |

### Seed Nodes

| Variable | Default | Description |
|----------|---------|-------------|
| `PeerService__SeedNodes__SeedNodes__0__NodeId` | `n0.sorcha.dev` | Seed node identifier |
| `PeerService__SeedNodes__SeedNodes__0__Hostname` | `n0.sorcha.dev` | Seed node hostname |
| `PeerService__SeedNodes__SeedNodes__0__Port` | `443` | Seed node port |
| `PeerService__SeedNodes__SeedNodes__0__EnableTls` | `true` | Use TLS for seed connection |

Additional seed nodes follow the same pattern with incrementing index (`__1__`, `__2__`, etc.).

## Validator

| Variable | Default | Description |
|----------|---------|-------------|
| `Validator__ValidatorId` | `docker-validator-1` | Unique validator node identifier |
| `Validator__SystemWalletAddress` | (empty) | System wallet address (auto-created if empty) |
| `VALIDATOR_HTTP_PORT` | `5800` | Host port for HTTP REST API |
| `VALIDATOR_GRPC_PORT` | `5801` | Host port for gRPC endpoint |

## AI Provider (Optional)

| Variable | Default | Description |
|----------|---------|-------------|
| `ANTHROPIC_API_KEY` | (empty) | Anthropic Claude API key for AI features in Blueprint Service |
| `AIProvider__Model` | `claude-sonnet-4-5-20250929` | AI model identifier |

## Host Port Overrides

All published ports can be overridden via environment variables:

| Variable | Default | Maps To |
|----------|---------|---------|
| `GATEWAY_HTTP_PORT` | `80` | API Gateway HTTP |
| `GATEWAY_HTTPS_PORT` | `443` | API Gateway HTTPS |
| `BLUEPRINT_PORT` | `5000` | Blueprint Service |
| `REGISTER_PORT` | `5380` | Register Service |
| `TENANT_PORT` | `5450` | Tenant Service |
| `VALIDATOR_HTTP_PORT` | `5800` | Validator HTTP |
| `VALIDATOR_GRPC_PORT` | `5801` | Validator gRPC |
| `PEER_GRPC_PORT` | `50051` | Peer Service gRPC |
| `UI_HTTP_PORT` | `5400` | UI Web HTTP |
| `UI_HTTPS_PORT` | `5401` | UI Web HTTPS |
| `REDIS_PORT` | `16379` | Redis |
| `POSTGRES_PORT` | `5432` | PostgreSQL |
| `MONGODB_PORT` | `27017` | MongoDB |
| `ASPIRE_UI_PORT` | `18888` | Aspire Dashboard |

## Docker Volumes

| Volume | Purpose | Backup Priority |
|--------|---------|-----------------|
| `postgres-data` | PostgreSQL data (wallets, tenants) | Critical |
| `mongodb-data` | MongoDB data (registers, blueprints) | Critical |
| `redis-data` | Redis persistence | Low (cache) |
| `wallet-encryption-keys` | Wallet master encryption keys | Critical |
| `dataprotection-keys` | ASP.NET Data Protection keys | Important |
