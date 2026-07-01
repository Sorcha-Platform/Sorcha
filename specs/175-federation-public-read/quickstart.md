# Quickstart: Verify cross-installation public-register federation (175)

Two installations, different names. Node A pulls node B's **public** SSR **anonymously**.

## Setup
- **Node B** (the authority holding the public register): e.g. `sorcha` on n1 (or a second local
  installation), with an advertised, sealed System Register.
- **Node A** (the puller): a stack with `InstallationName` **different** from B (e.g. `Phaethon`),
  seeded with B's peer endpoint.

## Steps
1. Bring up A seeded with B. Confirm the **peer link is healthy** (no `failure count` / `0/1 alive`),
   authenticated by **node identity** (no `{installation}:service` token presented to B).
2. A reads/replicates B's **public** SSR **anonymously** (no B credential, no A installation token to
   B). Confirm A receives it.
3. A **verifies** the register crypto (genesis attestations + policy + docket/validator sigs) and
   **persists only on success**. Tamper the bytes in a test → A **rejects**.
4. Confirm A now holds a valid SSR copy and its **local sealing proceeds** (registers advance past
   height 0); the AIAS demo then provisions end-to-end.

## Negative checks
- A (no B credential) reads a **private** register on B → **refused** (401/403).
- A attempts a **write** to a B register → **refused** (not a participant).
- Intra-installation service calls on each node → unchanged (installation service auth + F136 authz
  rejection intact).

## Success = SC-001..SC-006
Anonymous public read works across installations, verification is mandatory/fail-closed, the
previously-refused peer link is healthy, private/writes stay refused, and the two installations remain
distinct — with the SSR-sync unblock demonstrable end-to-end.
