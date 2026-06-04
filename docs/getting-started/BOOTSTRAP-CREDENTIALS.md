# Bootstrap Credentials — Development Environment

**Environment:** Development / local Docker only

The Tenant Service **auto-seeds** a default organisation, an administrator user, and the
service principals at startup — there is no manual bootstrap step. With `docker-compose up`
the seeding runs automatically the first time the Tenant database is empty
(`DatabaseInitializer` in `src/Services/Sorcha.Tenant.Service/Data/`).

---

## ⚠️ Security warning

**These are DEVELOPMENT credentials only.**

- ❌ NEVER use these credentials, or the predictable development secrets below, in production.
- ✅ In any environment **other than `Development`**, each service-principal secret is **randomly
  generated** at first seed and written **once** to the Tenant Service startup logs
  (`LogWarning`, "SAVE THIS SECRET — it will not be shown again"). Capture it from the logs.
- ✅ Service-principal secrets are **hashed at rest (Argon2id)**; they are never recoverable from
  the database. Other Tenant at-rest secrets (TOTP, OIDC client secrets, login tokens) are
  encrypted with AES-256-GCM (`ISecretProtectionProvider`, Feature 146).

---

## Default organisation & admin user

| Property | Organisation | Admin user |
|----------|--------------|------------|
| **ID** | `00000000-0000-0000-0000-000000000001` | `00000000-0000-0000-0001-000000000001` |
| **Name** | Sorcha Local | System Administrator |
| **Email / Subdomain** | `sorcha-local` | `admin@sorcha.local` |
| **Password** | — | `Dev_Pass_2025!` |
| **Role / Status** | Active | Administrator / Active |

> Change the admin password immediately after first login — see
> [Admin → Installation & First Run](../admin/installation-first-run.md).

**Login** (through the API Gateway):

```bash
curl -X POST http://localhost/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "email": "admin@sorcha.local", "password": "Dev_Pass_2025!" }'
```

The Gateway listens on `http://localhost` (port 80) by default. You can also reach the Tenant
Service directly at `http://localhost:5450` (Docker) or `https://localhost:7110` (Aspire).

---

## Service principal credentials

Service principals authenticate service-to-service via the OAuth2 **client-credentials** flow.
In the **`Development`** environment the seed uses **predictable secrets** of the form
`{client-id-stem}-service-secret` so docker-compose works out of the box. Outside Development the
secret is random (see the security warning above).

| Service | Client ID | Development secret | Scopes |
|---------|-----------|--------------------|--------|
| Blueprint Service | `service-blueprint` | `blueprint-service-secret` | `blueprints:read blueprints:write wallets:sign register:write` |
| Wallet Service | `service-wallet` | `wallet-service-secret` | `wallets:read wallets:write wallets:sign wallets:encrypt wallets:decrypt registers:read` |
| Register Service | `register-service` | `register-service-secret` | `registers:read registers:write registers:query validator:write wallets:sign` |
| Peer Service | `service-peer` | `peer-service-secret` | `peers:read peers:write registers:read` |
| Validator Service | `validator-service` | `validator-service-secret` | `validator:read validator:write wallets:sign registers:read blueprints:read` |
| Tenant Service | `tenant-service` | `tenant-service-secret` | `wallets:read wallets:sign wallets:verify persona:crypto` |
| HAIP Service | `service-haip` | `haip-service-secret` | `blueprints:read` |

**Get a service token** (example — Blueprint Service):

```bash
curl -X POST http://localhost/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=service-blueprint" \
  -d "client_secret=blueprint-service-secret"
```

---

## Token response & properties

```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 28800,
  "scope": "blueprints:read blueprints:write wallets:sign register:write"
}
```

- **Lifetime:** 8 hours (28,800 s)
- **Type:** Bearer (send as `Authorization: Bearer <token>`)
- **Algorithm:** HS256 (HMAC-SHA256, JWT signing key)
- **Issuer:** `urn:sorcha:{installation}` (default installation `sorcha` ⇒ `urn:sorcha:sorcha`).
  Service tokens carry the `{installation}:service` audience. See
  [JWT Configuration](../guides/JWT-CONFIGURATION.md) and the tiered-audience model (Feature 136).

**Use the token:**

```bash
curl -X GET http://localhost/api/blueprints \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

---

## Resetting bootstrap data

Seeding is idempotent and re-runs only when the Tenant database is empty. To regenerate everything:

```bash
# Stop the stack and DELETE all volumes (wipes Postgres/Mongo/Redis data)
docker-compose down -v

# Bring it back up — the Tenant Service re-seeds the org, admin, and service principals
docker-compose up -d
```

⚠️ `down -v` deletes **all** data, including any organisations, users, wallets, and registers you
created. In a non-Development environment the new service-principal secrets are random — capture
them from the Tenant Service logs.

To reset only the service principals (keeping the org and users):

```bash
docker exec -it sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c 'DELETE FROM public."ServicePrincipals";'
# Restart the Tenant Service to re-seed them
docker-compose restart tenant-service
```

---

## Verification

```bash
# Organisation
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c 'SELECT "Id","Name","Subdomain","Status" FROM public."Organizations";'

# Admin user
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c 'SELECT "Id","Email","DisplayName","Status" FROM public."UserIdentities";'

# Service principals (expect 7: blueprint, wallet, register, peer, validator, tenant, haip)
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c 'SELECT "ServiceName","ClientId","Status" FROM public."ServicePrincipals";'
```

---

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `invalid client credentials` | Wrong client ID/secret. In Development use the predictable secrets above; otherwise read the secret from the Tenant Service startup logs. |
| `token is invalid or has expired` | Service tokens last 8 h — request a new one via `/api/service-auth/token`. |
| No service principals seeded | The Tenant database was not empty at startup (seeding is one-time). Reset with `docker-compose down -v && docker-compose up -d`. |
| Database initialisation failed (Windows/Docker Desktop) | Use `host.docker.internal` as the Postgres host in the connection string. |

---

## Related documentation

- [Infrastructure Setup](INFRASTRUCTURE-SETUP.md)
- [Authentication Setup](../guides/AUTHENTICATION-SETUP.md) · [JWT Configuration](../guides/JWT-CONFIGURATION.md)
- [Port Configuration](PORT-CONFIGURATION.md)
