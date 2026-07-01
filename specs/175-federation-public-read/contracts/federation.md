# Contract: Federation — anonymous public read + node-identity peer auth (175)

## C1 — Register read/replicate authorization
- **Public register** (`Advertise == true`): read/replicate is **allowed anonymously** — no
  installation token required or validated. Rate-limited.
- **Private register**: unchanged — requires existing register-scoped auth (401/403 to anonymous).
- Gate evaluated **per request** on the register's current public state.

## C2 — Verify-on-replicate (mandatory, fail-closed)
Before a pulled register (or its dockets) is persisted/trusted, the ingesting node MUST verify:
1. Genesis `InitialControlRecord` attestations,
2. `CryptoPolicy` conformance,
3. Docket / validator signatures,
4. Register identity (id/DID) matches what was requested.
Any failure ⇒ **reject, do not persist** (no trust-on-transport). Verification is installation-neutral
(it does not depend on the caller's or a shared installation token).

## C3 — Node-identity peer handshake
- Peer handshake / gossip / sync authenticates with **node identity** (node key / node cert), **not**
  a `{installation}:service` JWT.
- A node from installation A and a node from installation B complete the handshake and exchange
  **public** registers without either presenting an installation-scoped token to the other.
- TLS posture defined here (mTLS with node cert preferred; confirm vs. n1's served endpoint — O4).

## C4 — Unchanged boundaries (must-not-regress)
- **Writes** to any register require the **target register's** governance/participant authority
  (register-scoped). This feature grants **no** cross-installation write.
- **F136** cross-installation rejection for **authenticated** calls is unchanged; the anonymous path
  **bypasses** installation-token validation — it MUST NOT be implemented by accepting foreign tokens.
- Intra-installation service-to-service auth unchanged.

## Test contract
- **Positive**: node A (installation A) reads/replicates B's public SSR anonymously → verifies → holds
  a valid copy; peer link healthy.
- **Verify-fail**: a tampered register is rejected (not persisted).
- **Negative**: anonymous read of a private register on B → refused; write to a B register from A →
  refused.
- **Regression**: intra-installation service auth + F136 authenticated rejection unchanged.
