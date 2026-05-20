# Spec 5 — Verifier-DID Resolution

**Date:** 2026-05-20
**Arc:** Strathcarron citizen arc (umbrella: `2026-05-13-strathcarron-citizen-arc.md`)
**Builds on:** F127 (Spec 4, credential-gated second service), F120 (production issuer signature verification + DID resolver), F111 (timebound presentation lifecycle)
**Status:** Scope A (this doc). Scope B (signed request objects) deferred — see §6.

## Problem

`SorchaWalletPresentationConsumer.BuildInitiationAsync` (F127) emits the OID4VP
authorization request with a placeholder verifier identity:

```
openid4vp://?client_id=did:sorcha:org:UNKNOWN&response_type=vp_token&nonce=...&request_id=...
```

`client_id` is the verifier identity — the party requesting the presentation. For
the F127 council-page credential gate that's the council operating the page. The
hardcoded `UNKNOWN` means the citizen's wallet cannot display *who* is asking for
their credential. F127 deferred the real resolution to "Spec 5".

This mirrors the issuer side: F120 made the credential **issuer's** DID resolve;
this makes the **verifier's** DID resolve. Both sides of the trust handshake then
carry real, resolvable identities. (#795 wired F120's DID resolver into Blueprint
Service, so the same resolver path the verifier engine uses can resolve the
verifier DID too.)

## Scope (this doc)

Populate `client_id` with the council's real organisation DID
(`did:sorcha:org:{walletAddress}`), resolved from the published blueprint's
owning organisation. The OID4VP request stays **unsigned** — `client_id` is a
**display identity** the wallet shows to the citizen ("Strathcarron Council is
requesting your Assured Identity credential"), not yet a cryptographically
verified claim.

## Identity source — publishing org DID

The verifier is the organisation that published the blueprint and operates the
council page: `blueprint.OrganizationId` (a GUID). Tenant Service publishes that
org's W3C DID document on the org's first credential issuance (F120 US2), reachable
at `GET /orgs/{orgId}/did.json`; the document's top-level `id` is the canonical
`did:sorcha:org:{walletAddress}`.

**Why the org DID, not a participant wallet:** the org DID is the identifier that
already resolves (via the F120 resolver) to the org's **published signing keys**.
When Scope B adds signed request objects, the wallet verifies the request against
exactly this DID with no rework. A blueprint-participant wallet (e.g.
`licensing-officer`) is locally available on the instance but may have no published
DID document with keys — choosing it would be a false economy that Scope B would
rip out.

## Mechanism

```
PresentationLifecycleService.InitiateAsync  (has the blueprint in hand)
  └─ resolve blueprint.OrganizationId → did:sorcha:org:{addr}
       via IOrgDidDocumentClient.ResolveCanonicalDidAsync (GET /orgs/{id}/did.json, read .id)
  └─ pass result as PresentationInitiationContext.VerifierClientId
       └─ SorchaWalletPresentationConsumer.BuildInitiationAsync
            client_id = context.VerifierClientId ?? "did:sorcha:org:UNKNOWN"
```

The lifecycle service owns resolution (it has the blueprint); the consumer stays a
pure adapter that consumes a thin context. This matches the existing
`PresentationInitiationContext` contract ("only fields the consumer needs").

## Graceful degradation

Resolution is **best-effort** and never fails the gate:

- `blueprint.OrganizationId` null (unpublished / legacy) → `VerifierClientId` null
- org has never issued a credential, so no DID doc published → Tenant returns 404 → null
- transport / parse failure → null

In every null case the consumer falls back to the existing `did:sorcha:org:UNKNOWN`
placeholder. The gate's actual job — verifying the citizen's presented credential —
is unaffected; only the displayed verifier identity degrades. The Strathcarron
council has issued the AssuredIdentity credential, so its DID doc exists and the
happy path resolves.

## Forward-compatibility with Scope B

Scope B (deferred): sign the OID4VP request object as a JWT with the council's
signing key, serve it via a `request_uri`, and have the wallet resolve `client_id`
+ verify the request signature (mutual auth). Scope A is a clean stepping stone —
the `client_id` it populates becomes the DID the wallet resolves in Scope B, and
that DID already resolves to the org's published signing keys via F120. Scope B is
purely additive (signing step + request_uri endpoint + wallet-side verification);
nothing in Scope A is rework.

## Changes

| File | Change |
|------|--------|
| `Sorcha.ServiceClients.Http/OrgDidDocument/IOrgDidDocumentClient.cs` | `+ ResolveCanonicalDidAsync(Guid orgId, ct)` |
| `Sorcha.ServiceClients.Http/OrgDidDocument/OrgDidDocumentClient.cs` | GET `/orgs/{id}/did.json`, parse `id`, null on 404/error |
| `Sorcha.Blueprint.Service/Program.cs` | register `IOrgDidDocumentClient` against Tenant base address |
| `Sorcha.PresentationLifecycle.Abstractions/PresentationInitiationContext.cs` | `+ string? VerifierClientId` |
| `Sorcha.Blueprint.Service/.../PresentationLifecycleService.cs` | resolve org DID in non-HAIP branch, populate context |
| `Sorcha.Blueprint.Service/.../SorchaWalletPresentationConsumer.cs` | `client_id = VerifierClientId ?? UNKNOWN`; drop `TODO(T032)` |

## Testing

- Consumer: `BuildInitiationAsync` puts `VerifierClientId` into `client_id`; falls back to `UNKNOWN` when null.
- Client: `ResolveCanonicalDidAsync` → parsed `id` on 200; `null` on 404.

## Non-goals

- Scope B (signed request objects / mutual auth).
- Verifier-DID resolution for the HAIP consumer (HAIP keeps its own external request flow).
- Any blueprint schema or authoring change.
