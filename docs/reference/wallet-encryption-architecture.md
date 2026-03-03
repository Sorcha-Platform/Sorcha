# Wallet Encryption Architecture

**Document Version:** 1.0
**Last Updated:** 2026-01-11
**Status:** Production-Ready

---

## Table of Contents

1. [Overview](#overview)
2. [Encryption Architecture](#encryption-architecture)
3. [Algorithms and Key Hierarchy](#algorithms-and-key-hierarchy)
4. [Platform-Specific Implementations](#platform-specific-implementations)
5. [Docker/Container Deployment](#dockercontainer-deployment)
6. [Security Properties](#security-properties)
7. [Key Management](#key-management)
8. [Operational Procedures](#operational-procedures)

---

## Overview

The Sorcha Wallet Service implements a **two-layer encryption architecture** to protect cryptographic wallet private keys at rest. This design follows the principle of **envelope encryption** (also called key wrapping or data key encryption), where sensitive data is encrypted with a Data Encryption Key (DEK), and the DEK itself is protected by a Key Encryption Key (KEK).

### Why Two-Layer Encryption?

1. **Separation of Concerns**: Application-layer encryption (DEK) is independent of infrastructure-layer key protection (KEK)
2. **Performance**: DEKs can be cached in memory for fast encryption/decryption
3. **Key Rotation**: KEKs can be rotated without re-encrypting all wallet data
4. **Compliance**: Meets regulatory requirements for cryptographic key protection (PCI-DSS, FIPS 140-2)
5. **Cloud/HSM Integration**: KEK can be managed by hardware security modules or cloud key vaults

---

## Encryption Architecture

### High-Level Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    Sorcha Wallet Service                        │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │              Wallet Private Key (Plaintext)              │  │
│  │            ED25519/P-256/RSA-4096 Private Key            │  │
│  └────────────────────────┬─────────────────────────────────┘  │
│                           │                                     │
│                           │ Encrypt                            │
│                           ▼                                     │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │         Layer 1: Application Encryption (AES-256-GCM)    │  │
│  │                                                          │  │
│  │  Algorithm: AES-256-GCM                                  │  │
│  │  Key: Data Encryption Key (DEK) - 256-bit random        │  │
│  │  Nonce: 96-bit random (per-operation)                   │  │
│  │  Tag: 128-bit authentication tag                        │  │
│  │                                                          │  │
│  │  Output: Encrypted Private Key (Base64)                 │  │
│  └────────────────────────┬─────────────────────────────────┘  │
│                           │                                     │
│                           │ Store in Database                  │
│                           ▼                                     │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │              PostgreSQL Database (wallet-db)             │  │
│  │                                                          │  │
│  │  Table: wallets                                          │  │
│  │  Column: encrypted_private_key (TEXT)                    │  │
│  │  Format: <nonce>:<ciphertext>:<tag> (Base64)            │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                    Encryption Provider Layer                    │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │       Data Encryption Key (DEK) - 256-bit Plaintext      │  │
│  │              (Generated once per wallet-key-id)          │  │
│  └────────────────────────┬─────────────────────────────────┘  │
│                           │                                     │
│                           │ Encrypt                            │
│                           ▼                                     │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │      Layer 2: Infrastructure Key Protection              │  │
│  │                                                          │  │
│  │  Platform:        Algorithm:                             │  │
│  │  ───────────      ────────────                          │  │
│  │  Windows          DPAPI (LocalMachine/CurrentUser)       │  │
│  │  Linux            Secret Service D-Bus API               │  │
│  │  Docker           AES-256-GCM with KEK from entropy      │  │
│  │  Azure            Azure Key Vault (Wrap/Unwrap)          │  │
│  │                                                          │  │
│  │  Output: Encrypted DEK (Base64)                          │  │
│  └────────────────────────┬─────────────────────────────────┘  │
│                           │                                     │
│                           │ Store on Disk                      │
│                           ▼                                     │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │           Persistent Key Storage (Volume Mount)          │  │
│  │                                                          │  │
│  │  Windows: C:\app\keys\<keyid>.key                        │  │
│  │  Linux:   /var/lib/sorcha/wallet-keys/<keyid>.key       │  │
│  │  Docker:  Volume mount (survives container restarts)    │  │
│  │                                                          │  │
│  │  Format: Base64-encoded encrypted DEK                    │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Algorithms and Key Hierarchy

### Layer 1: Application-Level Encryption (Wallet Private Keys)

**Algorithm:** AES-256-GCM (Galois/Counter Mode)

**Purpose:** Encrypt wallet private keys before storing in the database

**Key Material:**
- **Data Encryption Key (DEK)**: 256-bit (32 bytes) random key
- **Nonce/IV**: 96-bit (12 bytes) random value per encryption operation
- **Authentication Tag**: 128-bit (16 bytes) AEAD tag for integrity verification

**Properties:**
- **Authenticated Encryption**: GCM mode provides both confidentiality and integrity
- **Non-Deterministic**: Same plaintext produces different ciphertexts (due to random nonce)
- **Tamper-Proof**: Any modification to ciphertext fails authentication tag verification
- **Standards Compliance**: NIST SP 800-38D approved AEAD mode

**Output Format:**
```
<nonce_base64>:<ciphertext_base64>:<tag_base64>
```

Example:
```
a7J2k9mP4xQ1sT6v:encrypted_private_key_data_here:3rF8nK2mL5xP9cT1
```

**Code Reference:**
- Implementation: `src/Common/Sorcha.Wallet.Core/Encryption/Providers/LocalEncryptionProvider.cs:EncryptAsync()`
- Decryption: `src/Common/Sorcha.Wallet.Core/Encryption/Providers/LocalEncryptionProvider.cs:DecryptAsync()`

---

### Layer 2: Infrastructure-Level Key Protection (DEK Protection)

**Purpose:** Protect DEKs using platform-specific secure storage

#### Windows DPAPI (Data Protection API)

**Algorithm:** DPAPI with AES-256 (CNG-based)

**Scopes:**
- **LocalMachine**: KEK derived from machine-specific entropy (all users on machine can decrypt)
- **CurrentUser**: KEK derived from user-specific entropy (only current user can decrypt)

**Key Derivation:**
- DPAPI internally uses PBKDF2 with SHA-256 and machine/user entropy
- KEK is never exposed to the application
- Encryption/decryption performed by Windows CryptoAPI

**Platform Requirements:**
- Windows 10/11, Windows Server 2016+
- .NET `System.Security.Cryptography.ProtectedData` API

**Code Reference:**
- `src/Common/Sorcha.Wallet.Core/Encryption/Providers/WindowsDpapiEncryptionProvider.cs:ProtectKey()`

---

#### Linux Secret Service (freedesktop.org D-Bus API)

**Algorithm:** Platform-specific keyring encryption (typically AES-256)

**Keyrings:**
- **GNOME Keyring**: User session keyring protected by login password
- **KDE Wallet**: KDE Plasma wallet protected by user password
- **Secret Service Daemon**: Cross-desktop keyring API

**Fallback (Docker/No D-Bus):**
When D-Bus is unavailable (e.g., Docker containers), the provider falls back to file-based encryption with a KEK derived from system entropy (`/dev/urandom`).

**Fallback Algorithm:**
- KEK: 256-bit key derived from `/dev/urandom` (generated once, stored encrypted)
- Algorithm: AES-256-GCM
- Storage: `/var/lib/sorcha/wallet-keys/<keyid>.key`

**Platform Requirements:**
- Linux with D-Bus and Secret Service provider (libsecret)
- Fallback: Any Linux system with `/dev/urandom`

**Code Reference:**
- `src/Common/Sorcha.Wallet.Core/Encryption/Providers/LinuxSecretServiceEncryptionProvider.cs`

---

#### Azure Key Vault (Cloud KMS)

**Algorithm:** Azure Key Vault Managed HSM or Standard vault

**Operations:**
- **Wrap Key**: Encrypt DEK using Azure Key Vault KEK
- **Unwrap Key**: Decrypt DEK using Azure Key Vault KEK

**KEK Storage:**
- HSM-backed keys (FIPS 140-2 Level 2 or Level 3)
- Software-backed keys (Azure-managed encryption)

**Authentication:**
- Managed Identity (recommended for production)
- Service Principal (client credentials)
- Azure CLI credentials (development only)

**Caching:**
- DEKs cached in memory for configurable TTL (default: 60 minutes)
- Stale cache allowed during Azure outages (configurable)

**Platform Requirements:**
- Azure.Security.KeyVault.Keys SDK
- Azure.Identity for authentication

**Code Reference:**
- Implementation: `src/Common/Sorcha.Wallet.Core/Encryption/Providers/AzureKeyVaultEncryptionProvider.cs` (pending)

---

## Platform-Specific Implementations

### Local Development (LocalEncryptionProvider)

**Use Case:** Development and testing only

**Security Level:** ⚠️ **NOT PRODUCTION SAFE** ⚠️

**Algorithm:** AES-256-GCM with in-memory KEK

**Limitations:**
- KEK stored in memory only (lost on service restart)
- All wallets become inaccessible after restart
- No persistent key storage

**Configuration:**
```json
{
  "EncryptionProvider": {
    "Type": "Local",
    "DefaultKeyId": "dev-key-2025"
  }
}
```

**Code Reference:**
- `src/Common/Sorcha.Wallet.Core/Encryption/Providers/LocalEncryptionProvider.cs`

---

### Windows Production (WindowsDpapiEncryptionProvider)

**Use Case:** Windows servers and desktop applications

**Security Level:** ✅ **Production-Ready**

**Algorithm:** DPAPI (CNG-based AES-256)

**Configuration:**
```json
{
  "EncryptionProvider": {
    "Type": "WindowsDpapi",
    "DefaultKeyId": "wallet-key-2025",
    "WindowsDpapi": {
      "KeyStorePath": "C:\\app\\keys",
      "Scope": "LocalMachine"  // or "CurrentUser"
    }
  }
}
```

**Best Practices:**
- Use **LocalMachine** scope for Windows Services running under a service account
- Use **CurrentUser** scope for desktop applications running under user accounts
- Ensure `KeyStorePath` has appropriate ACLs (only service account has access)
- Back up encrypted DEK files from `KeyStorePath` (useless without machine/user context)

**Code Reference:**
- `src/Common/Sorcha.Wallet.Core/Encryption/Providers/WindowsDpapiEncryptionProvider.cs`

---

### Linux Production (LinuxSecretServiceEncryptionProvider)

**Use Case:** Linux servers with Secret Service, headless servers with fallback

**Security Level:** ✅ **Production-Ready**

**Algorithm:**
- Primary: Secret Service D-Bus API (keyring-specific encryption)
- Fallback: AES-256-GCM with entropy-derived KEK

**Configuration:**
```json
{
  "EncryptionProvider": {
    "Type": "LinuxSecretService",
    "DefaultKeyId": "wallet-key-2025",
    "LinuxSecretService": {
      "ServiceName": "sorcha-wallet-service",
      "FallbackKeyStorePath": "/var/lib/sorcha/wallet-keys"
    }
  }
}
```

**Best Practices:**
- Enable Secret Service on desktop Linux systems (GNOME Keyring, KDE Wallet)
- For headless servers (no D-Bus), fallback mode is automatic
- Ensure `FallbackKeyStorePath` has restrictive permissions (chmod 700, owned by service user)
- Mount `FallbackKeyStorePath` as a persistent volume in Docker

**Code Reference:**
- `src/Common/Sorcha.Wallet.Core/Encryption/Providers/LinuxSecretServiceEncryptionProvider.cs`

---

## Docker/Container Deployment

### Architecture in Docker

Docker containers run **Linux** and do **NOT** have access to D-Bus Secret Service. Therefore, `LinuxSecretServiceEncryptionProvider` automatically falls back to **file-based key storage** with entropy-derived KEK.

### Encryption Flow in Docker

1. **First startup**: Service generates a 256-bit KEK from `/dev/urandom`
2. **KEK protection**: KEK is encrypted with a master key derived from `/dev/urandom` (bootstrap process)
3. **DEK generation**: For each wallet, a 256-bit DEK is generated and encrypted with KEK
4. **Persistence**: Encrypted DEKs are stored in `/var/lib/sorcha/wallet-keys/<keyid>.key`
5. **Volume mount**: Docker volume `wallet-encryption-keys` ensures persistence across container restarts

### Docker Compose Configuration

**Volume Declaration:**
```yaml
volumes:
  wallet-encryption-keys:
```

**Wallet Service Configuration:**
```yaml
wallet-service:
  environment:
    ASPNETCORE_ENVIRONMENT: Docker
    EncryptionProvider__Type: LinuxSecretService
    EncryptionProvider__DefaultKeyId: wallet-master-key-2025
    EncryptionProvider__LinuxSecretService__ServiceName: sorcha-wallet-service
    EncryptionProvider__LinuxSecretService__FallbackKeyStorePath: /var/lib/sorcha/wallet-keys
  volumes:
    - wallet-encryption-keys:/var/lib/sorcha/wallet-keys
```

**Dockerfile Preparation:**
```dockerfile
# Create wallet encryption keys directory with proper permissions
RUN mkdir -p /var/lib/sorcha/wallet-keys && \
    chown -R 1654:1654 /var/lib/sorcha

# Copy directory structure to final stage
COPY --from=dependencies --chown=$APP_UID:$APP_UID /var/lib/sorcha /var/lib/sorcha
```

### Security Properties in Docker

✅ **Persistence**: Keys survive container restarts (Docker volume)
✅ **Isolation**: Each container instance has independent key storage
✅ **Non-deterministic encryption**: Random nonce per operation
✅ **Tamper detection**: AES-GCM authentication tag
⚠️ **KEK protection**: KEK derived from system entropy (not hardware-backed)
⚠️ **Backup required**: Volume backup is essential for disaster recovery

### Production Recommendations for Docker

For production Docker/Kubernetes deployments, consider:

1. **Migrate to Azure Key Vault**: Replace file-based KEK with HSM-backed keys
2. **Volume encryption**: Use encrypted Docker volumes (LUKS, Azure Disk Encryption)
3. **Secret management**: Use Kubernetes Secrets or Azure Key Vault for configuration
4. **Backup strategy**: Automate backup of `wallet-encryption-keys` volume
5. **Access control**: Restrict volume access to wallet service container only

---

## Security Properties

### Confidentiality

| Property | Layer 1 (DEK) | Layer 2 (KEK) |
|----------|---------------|---------------|
| Algorithm | AES-256-GCM | Platform-specific (AES-256 or stronger) |
| Key Size | 256 bits | 256 bits (or HSM-backed) |
| Nonce/IV | 96 bits (random per operation) | Platform-managed |
| Key Storage | Encrypted on disk (PostgreSQL) | Platform keyring or file system |
| Key Exposure | Never logged or transmitted | Never exposed to application |

### Integrity

- **AES-GCM Authentication Tag**: 128-bit tag ensures ciphertext integrity
- **Tamper Detection**: Any modification to ciphertext causes decryption failure
- **Non-Repudiation**: Audit logs record all encryption/decryption operations with timestamps

### Availability

- **KEK Loss**: If KEK is lost, all wallet private keys become **permanently inaccessible**
- **Backup Strategy**: Encrypted DEK files must be backed up (useless without KEK)
- **Disaster Recovery**: Document KEK recovery procedures for each platform

### Compliance

- **NIST Compliance**: AES-256-GCM is NIST SP 800-38D approved
- **FIPS 140-2**: Windows DPAPI uses FIPS-certified CNG providers
- **PCI-DSS**: Envelope encryption meets PCI-DSS requirement 3.5 (key encryption)
- **GDPR**: Encrypted data at rest satisfies data protection requirements

---

## Key Management

### Key Lifecycle

```
┌─────────────┐
│ Key Created │ ─────> DEK generated (256-bit random)
└──────┬──────┘       KEK protects DEK
       │              Encrypted DEK stored on disk
       │
       ▼
┌─────────────┐
│ Key Active  │ ─────> DEK cached in memory
└──────┬──────┘       Used for wallet encryption/decryption
       │              Audit logs record usage
       │
       ▼
┌─────────────┐
│ Key Rotated │ ─────> New DEK generated
└──────┬──────┘       Old DEK marked deprecated
       │              Wallets re-encrypted with new DEK
       │
       ▼
┌─────────────┐
│ Key Retired │ ─────> DEK removed from cache
└─────────────┘       Encrypted DEK file archived (not deleted)
```

### Key Rotation

**When to Rotate KEK:**
- Annually (recommended)
- After security incident
- After personnel changes (if KEK is user-scoped)

**When to Rotate DEK:**
- Quarterly (recommended)
- After suspected compromise
- When migrating to new encryption provider

**Rotation Procedure:**
1. Generate new KEK (platform-specific)
2. Decrypt all DEKs with old KEK
3. Re-encrypt DEKs with new KEK
4. Update encrypted DEK files on disk
5. Securely delete old KEK (if applicable)

---

## Operational Procedures

### Initial Setup (Docker)

1. **Start services:**
   ```bash
   docker-compose up -d wallet-service
   ```

2. **Verify encryption provider initialized:**
   ```bash
   docker logs sorcha-wallet-service | grep "Encryption provider initialized"
   ```

3. **Verify volume created:**
   ```bash
   docker volume ls | grep wallet-encryption-keys
   ```

4. **Verify key directory permissions:**
   ```bash
   docker exec sorcha-wallet-service ls -la /var/lib/sorcha/wallet-keys
   # Should show: drwx------ 2 app app ... /var/lib/sorcha/wallet-keys
   ```

### Backup Procedures

**Docker Volume Backup:**
```bash
# Create backup directory
mkdir -p ./backups/wallet-keys

# Backup encryption keys
docker run --rm \
  -v wallet-encryption-keys:/source:ro \
  -v ./backups/wallet-keys:/backup \
  alpine \
  tar czf /backup/wallet-keys-$(date +%Y%m%d-%H%M%S).tar.gz -C /source .
```

**Restore from Backup:**
```bash
# Stop wallet service
docker-compose stop wallet-service

# Restore keys
docker run --rm \
  -v wallet-encryption-keys:/target \
  -v ./backups/wallet-keys:/backup:ro \
  alpine \
  tar xzf /backup/wallet-keys-YYYYMMDD-HHMMSS.tar.gz -C /target

# Restart wallet service
docker-compose start wallet-service
```

### Monitoring and Audit

**Health Check:**
```bash
curl http://localhost:8080/health
# Response should include: "encryption-provider": "Healthy"
```

**Audit Logs:**
All encryption operations are logged with:
- Operation type (Encrypt, Decrypt, CreateKey, KeyExists)
- Key ID
- Duration (milliseconds)
- Status (Success, Failed)
- User context (if available)
- Error type (for failures)

**Log Query Example:**
```bash
docker logs sorcha-wallet-service | grep "Encryption operation"
```

---

## Summary

The Sorcha Wallet Service uses a **two-layer envelope encryption architecture** to protect wallet private keys:

1. **Layer 1 (Application)**: AES-256-GCM encrypts private keys with Data Encryption Keys (DEKs)
2. **Layer 2 (Infrastructure)**: Platform-specific key protection (DPAPI, Secret Service, Key Vault) encrypts DEKs with Key Encryption Keys (KEKs)

This design provides:
- ✅ Strong cryptographic protection (AES-256-GCM)
- ✅ Platform-native key management
- ✅ Compliance with industry standards (NIST, FIPS, PCI-DSS)
- ✅ Docker/container support with persistent storage
- ✅ Cloud-ready architecture (Azure Key Vault integration)

For Docker deployments, ensure:
- Volume mount for `/var/lib/sorcha/wallet-keys`
- Regular backups of encryption keys
- Migration path to Azure Key Vault for production

---

## Related Documentation

- [Wallet Service Specification](../.specify/specs/sorcha-wallet-service.md)
- [Cryptography Library](../src/Common/Sorcha.Cryptography/README.md)
- [Docker Deployment Guide](./docker-deployment.md)
- [Security Best Practices](./security-best-practices.md)

---

**Document Maintainer:** Sorcha Security Team
**Last Review:** 2026-01-11
**Next Review:** 2026-04-11
