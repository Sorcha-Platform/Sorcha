---
title: Sorcha Security Model
description: How Sorcha produces cryptographic evidence — selective disclosure, post-quantum posture, the classical boundary at HAIP, the trust anchor model, and the gaps named honestly.
standards:
  - ML-DSA (FIPS 204)
  - HAIP 1.0
  - W3C Verifiable Credentials Data Model 2.0
  - OAuth 2.0
last_updated: 2026-05-04
---

# Sorcha Security Model

Sorcha's security posture is best described as: *evidence over assertion, post-quantum where it matters, classical where the standard demands, and honest about the gaps.* This document is the reviewer-facing companion to [`STANDARDS.md`](../STANDARDS.md). The standards file says **what** Sorcha implements. This file says **why** the architecture takes the shape it does and **where** it stops.

## The Core Claim

A Sorcha workflow produces evidence, not assertion. Every action is signed by a key the participant controls. Every register entry is Merkle-chained to the entry before it. Every disclosure is scoped by a per-recipient symmetric key — the platform itself cannot read what it was not given the key for. A reviewer with a copy of the register and the relevant public keys can verify every step independently, without trusting Sorcha or any operator.

This is the architectural property the rest of the document derives from. Selective disclosure, post-quantum cryptography, the trust anchor model — all of them are mechanisms in service of *third-party-verifiable evidence*.

## Selective Disclosure — Architectural, Not Policy

Most platforms enforce data scoping through access control: the data is in a database, the database is open to the platform, the platform allows or denies queries based on a policy table. The data is *technically* accessible to the platform; the platform *chooses* not to expose it.

Sorcha enforces data scoping cryptographically. The mechanism is JSON Pointer paths inside an SD-JWT VC envelope, plus per-disclosure salts plus per-recipient symmetric key wrapping. When an action is submitted, the submitter encrypts each disclosable field with a key derived for the intended recipient. The platform stores the ciphertext, hashes it into the docket, and routes it — but if the platform was not given the key, it *cannot* decrypt the field. This is an architectural property, not a policy one.

The implementation lives in `src/Common/Sorcha.Cryptography/SdJwt/` and conforms to the W3C Verifiable Credentials Data Model 2.0 with the SD-JWT VC profile. JSON Pointer is the disclosure addressing mechanism — paths like `/buyer/contact/email` rather than schema-derived field IDs.

**What this means for a regulator or auditor:** disclosures to you are cryptographic proof, not platform-asserted views. If the platform were compromised tomorrow the disclosures you received yesterday remain valid; the platform never had the keys to forge a different disclosure on your behalf.

**What this does not protect against:** the *aggregate inference* threat — see the next section.

## The Aggregate Inference Threat

Selective disclosure protects individual fields. It does not protect against an adversary who:

1. Receives a series of legitimate disclosures over time, and
2. Cross-references those disclosures with external data sources, and
3. Infers values they were not directly disclosed.

This is a real and named threat. It is not a flaw in the cryptography — it is a property of the use case. A long-running disclosure relationship inevitably leaks information through *which* fields are disclosed, *how often*, and the *side channels* of timestamps and request patterns.

Sorcha's architectural answer is to make these side channels visible — every action is logged, every disclosure has a recipient identifier, every workflow has a counted set of disclosures. A workflow designer can inspect, before going live, what an attacker observing the disclosure surface could infer. This is operational hygiene, not a cryptographic property.

For workflows where the aggregate inference threat is unacceptable (statistical disclosure of individuals from aggregate query results, for example), Sorcha is the wrong tool. Use a system designed for differential privacy.

## Post-Quantum Posture

Sorcha is one of the few production-grade platforms with post-quantum cryptography (PQC) as core, not a branch feature. The two NIST-standardised primary algorithms are implemented:

- **ML-DSA** (NIST FIPS 204, the Module-Lattice Digital Signature Algorithm) — internal signing path. Used for action signatures, docket signatures, and inter-service authentication tokens. Implemented in `src/Common/Sorcha.Cryptography/Core/PqcSignatureProvider.cs`.
- **ML-KEM** (NIST FIPS 203, the Module-Lattice Key Encapsulation Mechanism) — internal key-encapsulation path. Used for symmetric-key agreement on the per-recipient disclosure path. Implemented in `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs`.

The reason the post-quantum posture matters here is the *long product lifetime* of the records Sorcha produces. A Digital Product Passport for a steel beam needs to remain verifiable in 2056. A municipal benefits decision needs to remain auditable in 2050. Today's classical signatures may not be safe over those horizons; the PQC signatures should be.

## The Classical Boundary at HAIP

There is one place Sorcha's signatures are *not* post-quantum: the HAIP wire boundary. HAIP 1.0 (OpenID Foundation, December 2025) standardises classical signature suites at the wallet ecosystem boundary — ES256 and EdDSA. The EU Digital Identity Wallet, the GOV.UK Wallet, and every other HAIP-conformant holder wallet expects classical signatures. Sorcha cannot ship post-quantum at this boundary because the consumers cannot verify it.

The bridge: the HAIP Service derives a *classical co-key* alongside its post-quantum primary key for every issuer identity. The classical co-key signs the wire-format JOSE/COSE envelope; the post-quantum primary signs the internal record. Both keys are tied to the same logical issuer through the BIP44 derivation chain.

**The honest framing:** the Sorcha post-quantum posture is real and meaningful for *internal* records — long-lived registers, internal credential issuance, inter-service auth. At the HAIP wire boundary the cryptography is classical and that is what the standard demands. When HAIP profiles a post-quantum suite the platform will adopt it.

## Honest Gaps

Naming what is *not* implemented, with the same precision as what is:

- **SLH-DSA (FIPS 205)** — the stateless hash-based signature, NIST's PQC algorithm-diversity primitive. Not implemented. The case for adding it is CNSA 2.0 alignment and crypto-agility insurance against a future ML-DSA break. `STANDARDS.md` rows it as `planned`.
- **BBS+ Signatures** — IETF draft, the zero-knowledge predicate-proof mechanism for selective disclosure. Not implemented. Today's selective disclosure is *show or hide* (a recipient sees the field or doesn't), not *prove a predicate over a hidden field* (a recipient sees that age > 18 without seeing the age). The case for adding it is HAIP-aligned ZK proofs once the standards land.
- **mTLS at internal service hops** — the constitutional principle is in place. Wire enforcement is on the roadmap. Today, internal hops are JWT-authenticated but not mutually-TLS-authenticated. This is a defence-in-depth gap, not a cryptographic-evidence gap — the wallet signatures and docket signatures remain verifiable regardless of transport.
- **mdoc / ISO 18013-5** — credential format used by mobile driving licences. Not implemented. Roadmap item; SD-JWT VC covers the same ground for HAIP-flow credentials today.
- **DID method registration** — `did:sorcha:org:` and `did:sorcha:holder:` are implemented but not registered with the W3C DID method registry. Cross-platform DID resolution requires bilateral agreement; this is a network-effect gap, not a security one.

## The Trust Anchor Model

Cryptography is only as strong as its root keys. Sorcha's trust anchors are seeded by a *public genesis ceremony* — see spec 099 (System Register Genesis). The ceremony:

1. Generates the initial validator key set in public, with witnesses.
2. Imports those keys into the system register as the founding trust anchors.
3. Publishes the genesis docket with the witness signatures.
4. Locks the register so the genesis docket is the irrevocable cryptographic origin.

A production deployment cannot bypass this — running a Sorcha network without a published genesis ceremony means the validators are anonymous and the trust chain is rooted in nothing third-party-auditable.

The CLI tool that runs the ceremony is `src/Apps/Sorcha.Cli` (`sorcha network genesis`). The n1 deployment was bootstrapped through this ceremony in April 2026 and is the reference for how the process plays out in practice.

Subsequent trust anchors (new validators, new issuing authorities, new participating organisations) are added through register-recorded admission events, each signed by the existing quorum. The trust graph is itself a Sorcha workflow — the platform eats its own dog food.

## Authentication and Authorization

Inside the platform, authentication is OAuth 2.0 / JWT Bearer. The Tenant Service is the JWT issuer; every other service is a resource server that validates tokens against the issuer's public key set. Roles, scopes, and platform-org topology are propagated through JWT claims.

Internal service-to-service calls also use JWTs (with a service identity rather than a user identity) and a delegation token mechanism that propagates the original user's identity through service hops. See [`docs/guides/AUTHENTICATION-SETUP.md`](guides/AUTHENTICATION-SETUP.md) for the full configuration and [`docs/guides/JWT-CONFIGURATION.md`](guides/JWT-CONFIGURATION.md) for token lifetime and signing-key management.

Rate limiting is centralised through the ServiceDefaults `AddRateLimiting()` extension; per-policy limits are bound from `RateLimitSettings`. See `CLAUDE.md` § "Critical Patterns" for the conventions.

## What an Independent Reviewer Should Verify

If you are reviewing Sorcha's security claims and only have an hour, this is the path that produces the most signal:

1. Read the **genesis ceremony** for an actual deployment (n1 is the reference). Confirm the validator key set is publicly recorded and the witnesses are independent.
2. Pick one walkthrough run (`walkthroughs/TradeFinance/run.ps1`) and **verify a single docket independently** — pull it from the register, recompute the Merkle hash, verify the validator quorum signatures against the genesis-recorded keys.
3. Pick one credential issuance and **verify the SD-JWT VC end-to-end** — issuer signature, holder key binding, status list look-up, every disclosure decryption matches the schema.
4. Read [`STANDARDS.md`](../STANDARDS.md) and confirm every standard's `Components` cell points at code that actually implements that standard.
5. Read this document's **Honest Gaps** section and confirm the gaps are accurate — there is nothing claimed here that is not in the codebase, and there is nothing in the codebase that contradicts a claim here.

If any of those five steps fails, the claim being made is wrong and we want to hear about it. Open an issue.

## Pointers

| Source | Purpose |
|---|---|
| [`STANDARDS.md`](../STANDARDS.md) | The standards-compliance source of truth |
| [`docs/architecture.md`](architecture.md) | How the platform produces the evidence this document discusses verifying |
| [`.specify/constitution.md`](../.specify/constitution.md) | Constitutional principles, including Security First |
| `specs/099-system-register-genesis/` | The genesis ceremony specification |
| `specs/079-trust-hardening/` | Trust hardening hardening for inter-tenant register interactions |
| `specs/113-storage-durability-audit/` | Storage durability and fail-fast model for production |
| `.claude/skills/cryptography/SKILL.md` | Implementation patterns for the cryptographic primitives |
