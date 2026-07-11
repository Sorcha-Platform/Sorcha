# Feature Specification: Ethereum Transacting — Phase 4 (Native ETH Transfers)

**Feature Branch**: `182-ethereum-transacting`

**Created**: 2026-07-11

**Status**: Draft

**Input**: User description: "Ethereum transacting — Phase 4 (Feature 182): a Sorcha wallet sends a native ETH transfer (EIP-1559 type-2), signed server-side with its auxiliary secp256k1 key and broadcast over a write-capable EVM RPC. Design source of truth: docs/superpowers/specs/2026-07-11-ethereum-verify-phase4-design.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Send a native ETH transfer from a wallet (Priority: P1)

An authorized caller instructs a Sorcha wallet to send a native ETH transfer (a recipient address and an
amount) on an operator-enabled test network. The platform prepares the transaction, signs it with the
wallet's Ethereum identity, broadcasts it to the network, and returns a transaction hash the caller can use
to track it. The caller never sees or handles the wallet's private key.

**Why this priority**: This is the core capability of the phase — the first time a Sorcha wallet moves
value on Ethereum. Everything else supports, bounds, or observes this action.

**Independent Test**: With an enabled test network configured, submit a valid transfer for a funded wallet
and confirm a transaction hash is returned and the transaction appears on-chain. Delivers the end-to-end
value on its own.

**Acceptance Scenarios**:

1. **Given** a funded wallet on an enabled test network and a valid recipient and amount within the value
   cap, **When** the caller submits the transfer, **Then** the platform returns a transaction hash and a
   "submitted" status without waiting for confirmation.
2. **Given** a submitted transfer, **When** the caller requests its status by transaction hash, **Then** the
   platform reports whether it is still pending, succeeded, or reverted (with block number and gas used once
   mined).
3. **Given** a transfer request, **When** the platform prepares it, **Then** the wallet's private key is
   used only to sign and is never returned to the caller or logged.

---

### User Story 2 - Preview the cost before sending (Priority: P2)

Before committing to an irreversible transfer, the caller requests a read-only preview that reports the
computed network fees, gas, and total cost for the intended transfer — without signing or broadcasting
anything.

**Why this priority**: Value transfers are irreversible; letting the caller see the real cost first
materially reduces the risk of a surprising or mistaken send. It is not required for a transfer to work,
so it ranks below P1.

**Independent Test**: Submit a preview for a valid transfer and confirm it returns nonce, gas, fee, and
total-cost figures and that nothing is broadcast (no transaction hash, no on-chain effect).

**Acceptance Scenarios**:

1. **Given** a valid transfer request, **When** the caller requests a preview, **Then** the platform returns
   the computed fees, gas, amount, and estimated total cost and performs no signing or broadcast.
2. **Given** a preview that the caller then submits as a real transfer, **When** both are compared, **Then**
   the previewed figures reflect the same computation the send would use (subject to live network changes).

---

### User Story 3 - Enforce value-moving guardrails (Priority: P1)

The platform refuses transfers that fall outside operator-configured safety limits: networks that are not
enabled, main networks unless explicitly permitted, amounts above the per-transaction value cap, or fees
above the configured ceiling. Only callers holding the dedicated transacting permission may send.

**Why this priority**: Because the action is irreversible and moves real value, the guardrails are as
critical as the send itself. A transfer that bypasses them is a security incident, not a convenience.

**Independent Test**: Attempt transfers that each violate one guardrail (disabled network, main network
without permission, over-cap amount, over-ceiling fee, missing permission) and confirm each is refused with
a clear reason and nothing is broadcast.

**Acceptance Scenarios**:

1. **Given** a network that is not in the enabled allowlist, **When** a transfer targets it, **Then** the
   platform refuses the transfer and broadcasts nothing.
2. **Given** a main (non-test) network and the main-network master switch disabled, **When** a transfer
   targets it, **Then** the platform refuses the transfer.
3. **Given** a transfer amount above the per-transaction value cap, **When** it is submitted, **Then** the
   platform refuses it.
4. **Given** a computed network fee above the configured ceiling, **When** a transfer would use it, **Then**
   the platform refuses the transfer rather than sending at an unbounded fee.
5. **Given** a caller without the transacting permission, **When** they attempt a send or preview, **Then**
   the platform denies the request.

---

### Edge Cases

- **Network unavailable / RPC error**: If the network cannot be reached or returns an error at any step
  (fee lookup, gas estimation, broadcast), the platform refuses the transfer rather than sending blind — no
  partial or unconfirmed-blind broadcast.
- **Gas estimation fails**: If the transfer's gas cannot be estimated (e.g. the recipient rejects it), the
  platform refuses to send.
- **Insufficient funds**: A transfer whose amount plus fees exceeds the wallet's balance is reported as a
  failure (the network rejects the broadcast); the platform surfaces the failure reason.
- **Malformed recipient / amount**: An invalid recipient address or a non-positive/oversized amount is
  rejected with a validation error before any network call.
- **Duplicate / concurrent sends**: Two transfers submitted for the same wallet at nearly the same time may
  contend for the same transaction position; this phase does not coordinate concurrent sends and one may
  fail — this is a documented limitation, not a supported concurrency guarantee.
- **Status of an unknown or not-yet-mined transaction**: A status request for a transaction that is not yet
  mined returns "pending"; an unknown hash returns "pending"/not-found rather than an error.
- **Prove-control key isolation**: The Ethereum identity's existing prove-control message signing continues
  to refuse transaction-shaped payloads; transactions are only ever produced through the dedicated
  transacting path.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST allow an authorized caller to send a native ETH transfer from a specified
  wallet by providing a target network, a recipient address, and an amount.
- **FR-002**: The platform MUST sign the transfer with the wallet's own Ethereum identity, derived on
  demand, and MUST never return, expose, or log the private key.
- **FR-003**: The platform MUST return a transaction hash and a "submitted" status immediately after
  broadcasting, without blocking on network confirmation.
- **FR-004**: The platform MUST provide a way to look up the current status of a submitted transfer by its
  transaction hash, reporting pending, succeeded, or reverted (with block number and gas used once mined).
- **FR-005**: The platform MUST provide a read-only preview that reports the computed fees, gas, amount, and
  estimated total cost for an intended transfer without signing or broadcasting anything.
- **FR-006**: The platform MUST restrict sends to networks on an operator-configured enabled allowlist and
  MUST refuse any network not on it.
- **FR-007**: The platform MUST default to test networks only, refusing main (non-test) networks unless an
  operator explicitly enables a main-network master switch.
- **FR-008**: The platform MUST enforce a per-transaction maximum value cap and refuse any transfer above it.
- **FR-009**: The platform MUST enforce a maximum network-fee ceiling and refuse any transfer whose computed
  fee would exceed it, rather than sending at an unbounded fee.
- **FR-010**: The platform MUST require a dedicated transacting permission (distinct from other wallet
  permissions) to send or preview a transfer, and MUST deny callers without it.
- **FR-011**: The platform MUST refuse the transfer (broadcast nothing) whenever a required network step —
  fee lookup, gas estimation, or broadcast — fails or the network is unreachable.
- **FR-012**: The platform MUST validate the recipient address and amount before making any network call and
  reject malformed input with a clear validation error.
- **FR-013**: The platform MUST confine value-moving signing to the server side; the on-device (wallet PWA)
  surface MUST NOT be able to build, sign, or broadcast a value transaction.
- **FR-014**: The platform MUST NOT alter the wallet's primary identity/algorithm or the existing
  prove-control (message-signing) behavior, which MUST continue to refuse transaction-shaped payloads.
- **FR-015**: The platform MUST record observability signals for submitted transfers and for each category of
  refusal (policy, network error, estimation failure, broadcast failure).
- **FR-016**: Every refusal MUST return a clear, caller-facing reason distinguishing why the transfer was
  not sent.

### Key Entities *(include if feature involves data)*

- **Transfer request**: The caller's intent — target network, recipient address, amount, and which wallet
  identity index to use.
- **Prepared transaction**: The fully-specified transfer ready to sign — network, sender nonce, amount,
  recipient, gas limit, and fee parameters — derived by the platform from the request plus live network
  data.
- **Submitted transfer result**: What the caller receives after broadcast — the transaction hash, sender
  address, network, nonce, and submitted status.
- **Transfer status**: The on-chain outcome of a submitted transfer — pending, succeeded, or reverted, with
  block number and gas used when available.
- **Transfer preview**: The read-only cost projection — nonce, gas, fees, amount, and estimated total cost.
- **Transacting policy**: The operator configuration bounding sends — enabled networks, main-network master
  switch, per-transaction value cap, and fee ceiling.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An authorized caller can send a valid native ETH transfer on an enabled test network and
  receive a transaction hash that resolves to a real on-chain transaction, in a single request.
- **SC-002**: 100% of transfers that violate a guardrail (disabled network, main network without the master
  switch, over-cap amount, over-ceiling fee, or missing permission) are refused with a clear reason and
  result in no broadcast.
- **SC-003**: The private key is never present in any response or log for any transfer, preview, or status
  request (0 occurrences).
- **SC-004**: A caller can preview the cost of an intended transfer and receive fee, gas, and total-cost
  figures with zero on-chain effect (no transaction hash produced).
- **SC-005**: When the network is unreachable or any required network step fails, 100% of affected transfers
  are refused with no broadcast (no blind or partial sends).
- **SC-006**: The on-device (wallet PWA) surface exposes no capability to build, sign, or broadcast a value
  transaction (0 such capabilities present).
- **SC-007**: Existing prove-control message signing and credential/DID verification behavior are unchanged
  (all prior-phase acceptance checks continue to pass).

## Assumptions

- The wallet already has an Ethereum identity derivable from its existing seed (delivered in Phase 3); this
  feature reuses it and does not change the wallet's primary identity or algorithm.
- Operators are responsible for funding the wallet's Ethereum address; the platform does not fund accounts.
- Operators configure at least one write-capable network endpoint; public read-only endpoints that reject
  transaction submission are not sufficient for sending.
- The default enabled networks are the well-known Ethereum test networks (Sepolia and Holešky); main
  networks are opt-in.
- Scope is limited to native ETH transfers (no contract interactions / calldata) and to the modern
  fee-market (EIP-1559 type-2) transaction form; legacy transaction forms and contract writes are out of
  scope for this phase.
- Concurrent sends from the same wallet are not coordinated in this phase; low-volume, sequential use is
  assumed.
- The fee heuristic (base fee headroom plus a priority tip, bounded by the operator ceiling) is acceptable
  for test-network use; sophisticated fee strategies and transaction replacement/speed-up are out of scope.
- Transaction tracking is caller-driven polling of the status endpoint; the platform does not push
  confirmation notifications in this phase.
