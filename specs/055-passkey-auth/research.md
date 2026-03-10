# Research: Passkey (WebAuthn/FIDO2) Authentication

**Feature**: 055-passkey-auth
**Date**: 2026-03-10

## Decision 1: WebAuthn Library

**Decision**: Fido2NetLib (Fido2.AspNet 4.0.0) — already a dependency in Tenant Service.
**Rationale**: Already referenced in `Sorcha.Tenant.Service.csproj` and `Directory.Packages.props`. Open-source, well-maintained, purpose-built for .NET FIDO2 server implementation. Handles attestation/assertion ceremony verification, COSE key parsing, and signature counter validation.
**Alternatives considered**: Microsoft.AspNetCore.Identity (too coupled to existing custom auth), custom implementation (too risky for cryptographic operations), managed passkey services (conflicts with self-hosted trust model).

## Decision 2: Credential Storage Architecture

**Decision**: Separate `PasskeyCredential` entity in public schema with owner type discriminator.
**Rationale**: Current `PublicIdentity` model stores a single credential (1:1). Multi-credential support requires a normalized `PasskeyCredential` table. Public schema placement enables fast credential lookup by credential ID during authentication without schema resolution. Owner type discriminator (org user vs public identity) with owner ID supports both user types from a single table.
**Alternatives considered**: JSON array on PublicIdentity (poor indexing, hard to manage individual credentials), per-org schema storage (requires cross-schema lookup during authentication).

## Decision 3: Challenge Storage

**Decision**: Distributed cache (Redis) with 5-minute TTL, matching existing OIDC state pattern.
**Rationale**: The OIDC exchange service already stores flow state in Redis with `oidc:state:{state}` keys and short TTL. Passkey challenges follow the same pattern: `passkey:challenge:{transactionId}` with 5-minute TTL. One-time use (deleted after verification). Consistent with existing infrastructure.
**Alternatives considered**: Database storage (unnecessary persistence for ephemeral challenges), in-memory cache (lost on restart, not distributed).

## Decision 4: Attestation Conveyance

**Decision**: `AttestationConveyancePreference.None` (no attestation verification).
**Rationale**: "None" is the most compatible option for consumer-facing applications. It works with all authenticators including platform authenticators (Windows Hello, Touch ID, Face ID) and roaming authenticators (YubiKey). Hardware attestation verification adds complexity and limits device compatibility. Can be upgraded per-deployment if needed.
**Alternatives considered**: Direct attestation (limits compatible devices), indirect attestation (complex trust chain management).

## Decision 5: Login Flow Integration

**Decision**: Passkey as alternative 2FA method for org users, slotting into existing TwoFactorLoginResponse flow.
**Rationale**: The current login flow issues a short-lived `loginToken` when 2FA is required. The passkey assertion can be triggered from the same 2FA step, using the `loginToken` to identify the user and returning a full `TokenResponse` on success. This avoids modifying the primary password verification flow.
**Alternatives considered**: Passkey-only login for org users (too disruptive, password remains primary factor), separate login endpoint (fragments the auth flow).

## Decision 6: Public User Social Login

**Decision**: Reuse existing OIDC infrastructure (OidcExchangeService, OidcProvisioningService) with a new "public user" flow path.
**Rationale**: The OIDC exchange, token parsing, and claim extraction code is already production-tested for org users. Public user social login follows the same OAuth2 flow but creates/links a `PublicIdentity` instead of a `UserIdentity`. The `IdentityProviderConfiguration` model already supports Google, Microsoft, GitHub, and Apple provider types.
**Alternatives considered**: Separate social login service (code duplication), third-party auth service (conflicts with trust model).

## Decision 7: Blazor WebAuthn Integration

**Decision**: JavaScript interop via `IJSRuntime` for `navigator.credentials.create()` and `navigator.credentials.get()`.
**Rationale**: WebAuthn API is browser-native JavaScript. Blazor WASM can call it via JS interop. A small JavaScript module handles the credential creation/assertion ceremonies and returns results to C# for server verification. This is the standard pattern for Blazor + WebAuthn.
**Alternatives considered**: WebAssembly-native WebAuthn (not supported by browsers, API is JS-only).

## Existing Infrastructure Summary

| Component | Status | Notes |
|-----------|--------|-------|
| Fido2.AspNet NuGet | Already referenced | Version 4.0.0 in Directory.Packages.props |
| PublicIdentity model | Exists, needs refactor | Single-credential fields → separate PasskeyCredential entity |
| TokenService.GeneratePublicUserTokenAsync | Ready | Issues JWT with `auth_method: "passkey"` claim |
| OIDC exchange service | Ready | Reusable for public user social login |
| IdentityProviderType enum | Ready | Google, Microsoft, GitHub, Apple defined |
| Distributed cache (Redis) | Ready | Pattern established by OIDC state storage |
| TenantDbContext public schema | Ready | Pattern for adding new entities documented |
| TOTP 2FA flow | Reference pattern | Passkey 2FA follows same loginToken → verify → JWT pattern |
| Rate limiting | Reference pattern | TOTP rate limiter pattern reusable for passkey endpoints |
