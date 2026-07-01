# Phase 1 Data Model: Federation anonymous public read (175)

No new persistent storage. Entities below are existing domain objects (reused) or in-memory concepts.

## Public register *(existing — register domain)*
An advertised register replicable by any node.
- Public gate: `Advertise == true` (per-request). Private/non-advertised → excluded from anonymous read.
- Self-verifying: `InitialControlRecord` (genesis attestations) + `CryptoPolicy` + sealed dockets +
  validator signatures + register id/DID.

## Node identity *(new or surfaced — peer)*
A node's installation-neutral identity for federation auth.
- A node signing key / certificate (distinct from the `service-peer` installation JWT).
- Used to authenticate the peer handshake/sync; verifiable by any peer regardless of installation.

## Replication verification result *(in-memory)*
Outcome of verifying a pulled register before persistence.
- `Verified` (bool, fail-closed), plus the specific check that failed (genesis / crypto-policy /
  docket-signature / identity mismatch) for diagnostics.
- Never persist/trust a register with `Verified == false`.

## Installation *(existing — F136)*
An authority domain (namespaced issuer/audiences). Installations stay separate; no shared identity is
created by this feature.

## Access decision *(in-memory, per request)*
- Read of a **public** register → **anonymous allowed** (rate-limited, then verify on replicate).
- Read of a **private** register / any **write** → existing register-scoped auth required (unchanged).
