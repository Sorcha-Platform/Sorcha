# Scaling & High Availability

This guide covers horizontal scaling, database replication, and high availability patterns for Sorcha deployments.

## Service Classification

### Stateless Services (Scale Freely)

These services hold no local state and can be scaled horizontally without coordination:

| Service | Notes |
|---------|-------|
| API Gateway | YARP reverse proxy. Scale behind a load balancer. |
| Blueprint Service | Workflow engine. SignalR uses Redis backplane for multi-instance. |
| Tenant Service | Auth and JWT issuance. Tokens are self-contained. |
| Validator Service | Chain validation. Read-only MongoDB access for governance data. |
| Peer Service | P2P networking. Each instance needs a unique `PeerService__NodeId`. |

### Stateful Services (Scale with Care)

| Service | State | Scaling Considerations |
|---------|-------|----------------------|
| Wallet Service | Encryption keys stored in volume | Keys must be accessible to all instances. Use shared volume or external key vault. |
| Register Service | MongoDB per-register databases | Multiple instances can share MongoDB. Ensure write coordination. |

## Horizontal Scaling

### Docker Compose Scaling

Scale stateless services using Docker Compose:

```bash
# Scale Blueprint Service to 3 instances
docker-compose up -d --scale blueprint-service=3

# Scale multiple services
docker-compose up -d --scale blueprint-service=3 --scale tenant-service=2
```

When scaling services, the API Gateway load-balances across instances automatically using Docker DNS round-robin.

### Scaling Architecture

```
                    ┌──────────────────┐
                    │  Load Balancer   │
                    │  (external)      │
                    └────────┬─────────┘
                             │
              ┌──────────────┼──────────────┐
              v              v              v
        ┌───────────┐ ┌───────────┐ ┌───────────┐
        │ API GW #1 │ │ API GW #2 │ │ API GW #3 │
        └─────┬─────┘ └─────┬─────┘ └─────┬─────┘
              │              │              │
              └──────────────┼──────────────┘
                             │  Docker DNS
              ┌──────────────┼──────────────┐
              v              v              v
        ┌───────────┐ ┌───────────┐ ┌───────────┐
        │Blueprint#1│ │Blueprint#2│ │Blueprint#3│
        └─────┬─────┘ └─────┬─────┘ └─────┬─────┘
              │              │              │
              └──────────────┼──────────────┘
                             │
                    ┌────────v─────────┐
                    │ MongoDB / Redis  │
                    │  (shared state)  │
                    └──────────────────┘
```

### Per-Service Scaling Notes

**API Gateway:**
- Fully stateless. Scale as needed behind an external load balancer.
- Each instance maintains its own YARP routing table (loaded from configuration).
- Redis is used for shared DataProtection keys.

**Blueprint Service:**
- SignalR connections use Redis as a backplane, so clients can connect to any instance.
- Blueprint execution is stateless -- workflow state is stored in MongoDB.

**Tenant Service:**
- JWT tokens are self-contained and validated by any service with the signing key.
- Session state (if any) is stored in Redis.
- Database migrations should only run on one instance at startup.

**Validator Service:**
- Read-only access to Register MongoDB. Safe to scale.
- Each instance needs a unique `Validator__ValidatorId`.

**Wallet Service:**
- The `wallet-encryption-keys` volume must be shared across instances.
- Consider migrating to Azure Key Vault or HashiCorp Vault for multi-instance deployments.
- PostgreSQL handles concurrent access natively.

## Database Replication

### PostgreSQL Streaming Replication

For high availability, configure PostgreSQL with streaming replication:

```yaml
# Primary (in docker-compose.override.yml)
postgres-primary:
  image: postgres:17-alpine
  environment:
    POSTGRES_USER: sorcha
    POSTGRES_PASSWORD: <secure-password>
    POSTGRES_DB: sorcha
  command: >
    postgres
    -c wal_level=replica
    -c max_wal_senders=3
    -c max_replication_slots=3

# Replica
postgres-replica:
  image: postgres:17-alpine
  environment:
    PGUSER: replicator
    PGPASSWORD: <replication-password>
  command: >
    bash -c "
    pg_basebackup -h postgres-primary -D /var/lib/postgresql/data -U replicator -v -P -W
    && postgres
    "
```

For production, consider managed PostgreSQL services (Azure Database for PostgreSQL, AWS RDS, etc.) that handle replication automatically.

### MongoDB Replica Sets

MongoDB replica sets provide automatic failover:

```yaml
# In docker-compose.override.yml
mongodb:
  image: mongo:8
  command: ["--replSet", "rs0"]
  environment:
    MONGO_INITDB_ROOT_USERNAME: sorcha
    MONGO_INITDB_ROOT_PASSWORD: <secure-password>

mongodb-secondary:
  image: mongo:8
  command: ["--replSet", "rs0"]
  depends_on:
    - mongodb
```

Initialize the replica set:

```bash
docker exec sorcha-mongodb mongosh -u sorcha -p <password> --eval '
rs.initiate({
  _id: "rs0",
  members: [
    { _id: 0, host: "mongodb:27017", priority: 2 },
    { _id: 1, host: "mongodb-secondary:27017", priority: 1 }
  ]
})'
```

Update connection strings to include replica set:
```
mongodb://sorcha:<password>@mongodb:27017,mongodb-secondary:27017/?replicaSet=rs0
```

### Redis Clustering

For high availability Redis:

```yaml
# Redis with persistence and replication
redis:
  image: redis:8-alpine
  command: >
    redis-server
    --appendonly yes
    --requirepass <redis-password>

redis-replica:
  image: redis:8-alpine
  command: >
    redis-server
    --replicaof redis 6379
    --requirepass <redis-password>
    --masterauth <redis-password>
```

For production, consider Redis Sentinel or Redis Cluster for automatic failover, or a managed service (Azure Cache for Redis, AWS ElastiCache).

## Load Balancer Patterns

### External Load Balancer

Place an external load balancer (nginx, HAProxy, Azure Application Gateway, AWS ALB) in front of multiple API Gateway instances:

```
Internet --> Load Balancer (TLS termination)
                --> API Gateway #1 (HTTP)
                --> API Gateway #2 (HTTP)
                --> API Gateway #3 (HTTP)
```

**nginx example:**

```nginx
upstream sorcha_gateway {
    server gateway-1:80;
    server gateway-2:80;
    server gateway-3:80;
}

server {
    listen 443 ssl;
    server_name sorcha.example.com;

    ssl_certificate     /etc/ssl/sorcha.crt;
    ssl_certificate_key /etc/ssl/sorcha.key;

    location / {
        proxy_pass http://sorcha_gateway;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # WebSocket support for SignalR
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }
}
```

### Session Affinity

SignalR long-polling connections require session affinity (sticky sessions) if not using WebSockets. The Redis backplane handles WebSocket connections across instances without affinity.

## Production Deployment Recommendations

| Component | Development | Small Production | Large Production |
|-----------|-------------|-----------------|-----------------|
| API Gateway | 1 instance | 2 instances | 3+ instances |
| Blueprint | 1 instance | 1-2 instances | 3+ instances |
| Tenant | 1 instance | 2 instances | 2+ instances |
| Wallet | 1 instance | 1 instance | 2 (with shared key vault) |
| Register | 1 instance | 1-2 instances | 3+ instances |
| Validator | 1 instance | 1 instance | 2+ instances |
| Peer | 1 instance | 1 instance | Per-network topology |
| PostgreSQL | Single | Primary + replica | Managed service |
| MongoDB | Single | Replica set (3 nodes) | Managed service |
| Redis | Single | Sentinel (3 nodes) | Managed service / Cluster |
