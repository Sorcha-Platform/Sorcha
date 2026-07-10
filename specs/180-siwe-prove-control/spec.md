# Feature Specification: SIWE / Prove-Control — Ethereum Address & secp256k1 Signing

**Feature Branch**: `180-siwe-prove-control`

**Created**: 2026-07-10

**Status**: Draft

**Input**: User description: "Phase 3 SIWE / prove-control (secp256k1 signing). A Sorcha wallet can expose its Ethereum address, sign an EIP-191 / SIWE (EIP-4361) prove-control message with a recoverable secp256k1 signature, and Sorcha can verify an inbound SIWE. Auxiliary Ethereum identity from the existing HD seed; prove-control only (no transactions)."

## User Scenarios & Testing *(mandatory)*

Phases 1–2b made Sorcha a **verifier** of Ethereum-signed credentials and DIDs. Phase 3 is the first time a Sorcha wallet **acts** with an Ethereum key: it can prove it controls an Ethereum address. This unlocks "Sign-In With Ethereum" (SIWE) style flows — the holder proves control of an address to a relying party — and lets Sorcha itself act as a relying party that accepts such proofs from external wallets. The Ethereum identity is derived from the wallet's existing seed as an **auxiliary** identity; the wallet's primary signing algorithm is unchanged. The Ethereum key is used **only** to prove control (sign challenge / SIWE messages), never to move funds or send transactions.

### User Story 1 - A wallet proves control of its Ethereum address (Priority: P1)

A holder's wallet is asked (by a relying party) to prove it controls an Ethereum address: it is given a challenge (a domain, a URI, and a one-time nonce). The wallet produces a signed Sign-In-With-Ethereum message. Anyone can recover the signer's address from the signature and confirm it matches the wallet's Ethereum address and the expected challenge.

**Why this priority**: This is the core capability — proving control of an Ethereum address is the whole point of Phase 3 and the prerequisite for any SIWE-based interoperability. A single wallet producing a verifiable prove-control signature is a complete, demonstrable slice of value.

**Independent Test**: Ask a wallet for its Ethereum address; request a signed SIWE message for a given domain/URI/nonce; independently recover the signer address from the signature and confirm it equals the wallet's address and the challenge fields match.

**Acceptance Scenarios**:

1. **Given** a wallet, **When** its Ethereum address is requested, **Then** a checksummed Ethereum address is returned, and it is stable across requests (deterministic from the wallet's seed).
2. **Given** a challenge (domain, URI, one-time nonce), **When** the wallet signs a SIWE prove-control message, **Then** the signer address recovered from the signature equals the wallet's Ethereum address.
3. **Given** the signed SIWE message, **When** a relying party checks it, **Then** the domain, URI, and nonce in the message match the challenge that was issued.
4. **Given** a request to sign, **When** the wallet signs, **Then** the wallet's Ethereum **private key is never returned** in any response.

### User Story 2 - Sorcha verifies an inbound SIWE proof (Priority: P1)

An external wallet (not Sorcha) presents a SIWE message and signature to prove it controls an Ethereum address. Sorcha verifies the proof: it confirms the signature was produced by the address named in the message, and that the message is fresh and intended for Sorcha (expected nonce, domain, and validity window).

**Why this priority**: Verification is the dual of Story 1 and turns prove-control into an actual authentication that Sorcha can rely on. Without it, Sorcha can only produce proofs, not consume them. It is P1 because it is the "so what" — Sorcha accepting an Ethereum sign-in.

**Independent Test**: Submit a valid external SIWE message + signature with the expected nonce/domain → accepted; submit ones with a tampered signature, a mismatched address, an expired window, or a wrong nonce/domain → rejected.

**Acceptance Scenarios**:

1. **Given** a SIWE message and a signature genuinely produced by the message's address, with the expected nonce and domain and within its validity window, **When** Sorcha verifies it, **Then** it is accepted and the confirmed address is returned.
2. **Given** the same message with a tampered signature, or a signature by a different address, **When** verified, **Then** it is rejected.
3. **Given** a SIWE message whose validity window has expired (or has not yet started), **When** verified, **Then** it is rejected.
4. **Given** a SIWE message whose nonce or domain does not match what Sorcha expected, **When** verified, **Then** it is rejected.

### User Story 3 - The Ethereum key is confined to prove-control (Priority: P2)

The Ethereum key must not be usable to authorise value transfer. Any attempt to have the wallet sign something that is actually a blockchain transaction (rather than a prove-control message) is refused, and the primary wallet identity/algorithm is unaffected.

**Why this priority**: A signing capability is a security boundary. Confining the key to prove-control (and leaving the primary wallet untouched) is essential for safe rollout, but it refines the happy path, hence P2.

**Independent Test**: Attempt to sign a payload that is actually a blockchain transaction → refused. Confirm the wallet's primary signing/identity behaviour is unchanged before and after the feature.

**Acceptance Scenarios**:

1. **Given** a payload that decodes as a blockchain transaction, **When** signing is attempted through the prove-control surface, **Then** it is refused.
2. **Given** a wallet, **When** the Ethereum identity is derived and used, **Then** the wallet's primary signing algorithm, address, and behaviour are unchanged.
3. **Given** the prove-control surface, **When** any request is made, **Then** there is no way to sign an arbitrary raw digest or to export the Ethereum private key.

### Edge Cases

- A nonce presented for verification that Sorcha did not issue (or has already consumed) MUST be rejected (replay protection is the relying party's responsibility; Sorcha checks the expected nonce it was given).
- A SIWE message with a missing or malformed required field MUST fail verification rather than be interpreted loosely.
- The Ethereum address MUST be reproducible from the same wallet seed and derivation index every time.
- Producing a signature MUST NOT weaken or alter the wallet's existing (primary-algorithm) keys or signatures.
- The Ethereum private key MUST never appear in a response, log, or exported artefact.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A wallet MUST expose an **Ethereum address** (checksummed) derived deterministically from the wallet's existing seed at a fixed Ethereum derivation path, as an **auxiliary** identity that does not change the wallet's primary signing algorithm.
- **FR-002**: A wallet MUST be able to **sign** a prove-control message (an Ethereum personal-sign challenge and a SIWE / EIP-4361 message) with a **recoverable** secp256k1 signature, such that the signer's address can be recovered from the signature.
- **FR-003**: Signatures MUST be produced deterministically (no reliance on a random nonce that, if repeated, would leak the key) and in the canonical low-s form accepted by the Ethereum ecosystem.
- **FR-004**: Sorcha MUST be able to **verify** an inbound SIWE message + signature: recover the signer address, confirm it equals the address named in the message, and validate the message's nonce, domain, and validity window against caller-supplied expectations. Any failure MUST reject (fail closed).
- **FR-005**: The Ethereum **private key MUST never be returned, exported, or logged**; it is derived on demand from the wallet's encrypted seed, used, and discarded.
- **FR-006**: Signing MUST require the **same authorisation** as any other wallet operation.
- **FR-007**: The prove-control surface MUST **refuse to sign any payload that decodes as a blockchain transaction**, and MUST NOT offer signing of an arbitrary raw digest — only personal-sign / SIWE messages.
- **FR-008**: The feature MUST NOT change the wallet's **primary signing algorithm** model, its existing keys, addresses, or signatures.
- **FR-009**: SIWE message text MUST be produced and parsed in the standard EIP-4361 form so that external wallets and relying parties interoperate.
- **FR-010**: The signing/verification primitives MUST be usable in the browser-hosted wallet (no server-only or native dependency), since the wallet may need to prove control on-device.
- **FR-011**: The feature MUST NOT introduce any new third-party dependency and MUST NOT enable transactions, on-chain writes, or value transfer (deferred to a later phase).
- **FR-012**: The Ethereum address and prove-control operations MUST be exposed through a small, authorised API surface (get address, sign SIWE, verify SIWE).

### Key Entities

- **Auxiliary Ethereum identity**: The secp256k1 key + checksummed address derived from a wallet's existing seed at the Ethereum derivation path; used only for prove-control.
- **Prove-control message**: An Ethereum personal-sign challenge or SIWE (EIP-4361) message the wallet signs to demonstrate control of its Ethereum address.
- **SIWE verification result**: The outcome (accepted / rejected) plus the confirmed address, produced when Sorcha checks an inbound SIWE proof.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A wallet's Ethereum address is deterministic — the same wallet returns the identical checksummed address on every request.
- **SC-002**: For 100% of prove-control signatures a wallet produces, the signer address recovered from the signature equals the wallet's Ethereum address, and an independent (external) verifier accepts the signature.
- **SC-003**: Sorcha accepts a genuine, fresh, correctly-addressed SIWE proof and rejects — in 100% of cases — proofs that are tampered, wrong-address, expired/not-yet-valid, or carry the wrong nonce/domain.
- **SC-004**: 100% of attempts to sign a blockchain-transaction payload through the prove-control surface are refused, and no API path returns or exports the Ethereum private key.
- **SC-005**: The wallet's primary-algorithm signing, address, and existing signatures are byte-for-byte unchanged by this feature (verified by regression).
- **SC-006**: Interoperability is demonstrated against at least one published Ethereum personal-sign / SIWE reference vector (known key → known signature verifies both ways).
- **SC-007**: New signing/SIWE logic is covered by tests to at least the project's required threshold (>85% of new code).

## Assumptions

- The wallet already has an encrypted master seed and an authorised path to use it for signing; the Ethereum key is derived from that same seed at the standard Ethereum path (`m/44'/60'/0'/0/{index}`), reusing the existing custody model.
- "Prove-control" covers Ethereum personal-sign (EIP-191) challenges and SIWE (EIP-4361) messages; EIP-712 typed-data and transaction signing are out of scope.
- Nonce issuance and replay tracking belong to the relying party; Sorcha's verification checks the signature, address, validity window, and the expected nonce/domain it is given.
- The signing primitives are pure-managed so they run identically on the server and in the browser-hosted wallet.
- The published reference vector used for interoperability testing is a well-known Ethereum personal-sign / SIWE example (independent of Sorcha's implementation).
- Derivation index defaults to 0 (the first Ethereum account) unless a caller specifies another.
