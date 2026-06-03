# Quickstart: Tenant Service At-Rest Secret Protection

## Configuration

**Default (no new config):** the protection key and the 2FA-token signing key derive from the
existing `JwtSettings:SigningKey` that the Tenant Service already requires. Nothing to add.

**Optional override** — to rotate the at-rest protection key independently of the JWT signing key:

```jsonc
// appsettings.{Environment}.json (or env / secret store)
{
  "Tenant": {
    "SecretProtection": {
      "Key": "<base64-encoded 32 random bytes>"   // optional; takes precedence when present
    }
  }
}
```

Generate a key: `openssl rand -base64 32` (or any 32-byte CSPRNG output, base64-encoded).

**Production / Staging:** if neither `JwtSettings:SigningKey` nor `Tenant:SecretProtection:Key`
resolves, the service **fails to start** (fail-closed). This is intended.

## Rollout (pre-release clean break)

1. Deploy the build.
2. **Clear the Tenant database** (the squashed initial migration creates the new column shape).
3. Ensure `JwtSettings:SigningKey` is set (already required in hardened environments).
4. (Optional) set `Tenant:SecretProtection:Key`.

There is **no data migration** and **no decoding of old-format secrets** — old TOTP/OIDC values do
not survive the clear, and re-enrolment / re-entry happens naturally afterwards.

## Verify (maps to Success Criteria)

- **SC-001 (TOTP unreadable):** enrol 2FA, then `SELECT "EncryptedSecret" FROM "TotpConfigurations"` — the bytes are AES-GCM ciphertext, not the Base32 secret and not Base64-decodable to it. A valid code still verifies.
- **SC-002 (OIDC usable):** save an IdP config with a known client secret, drive the OIDC token exchange, confirm the value sent to the provider equals the original secret; the stored column is not the plaintext.
- **SC-003 (cross-replica 2FA):** issue a 2FA intermediate token on one instance, validate it on a second identically-configured instance (and after a restart) — accepted.
- **SC-004 (fail-closed):** start with `ASPNETCORE_ENVIRONMENT=Production` and no resolvable key → host refuses to start with a clear error.
- **SC-005 (no new mandatory config):** an environment that already sets `JwtSettings:SigningKey` runs with no additional configuration.
- **SC-006 (clean break):** repo grep finds no surviving `v1:`-Base64 TOTP path and no SHA-256-based `EncryptSecret` in `Sorcha.Tenant.Service`.

## Run the tests

```bash
dotnet test tests/Sorcha.Tenant.Service.Tests/Sorcha.Tenant.Service.Tests.csproj
```

Key suites: `SoftwareSecretProtectionProviderTests` (round-trip / tamper / too-short),
`TenantSecretKeyResolverTests` (derivation determinism / override precedence / Production fail-closed),
`TotpServiceTests` (setup→validate; stored-not-recoverable), `IdpConfigurationServiceTests`
(store→recover the real secret).
