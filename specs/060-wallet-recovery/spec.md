# Feature 060: Wallet Recovery

**Status:** Future | **Priority:** P2 | **Effort:** ~40h

## Problem

Users who lose access to their device lose the ability to sign transactions and participate in workflows. Current recovery requires the BIP39 mnemonic phrase, which most users will not have backed up. Enterprise users need organization-managed recovery. The platform needs multiple recovery paths to match different deployment contexts.

## Clarifications

### Session 2026-03-17

- Q: Which recovery paths are in scope for the first implementation phase? → A: Paths 1 (mnemonic, existing), 3 (org-managed), and 4 (passkey) first. Path 2 (social recovery) deferred to a later phase.
- Q: Should recovery revoke all existing delegation grants? → A: Revoke all by default, but prompt user to selectively preserve specific delegations. Org admins can opt-out of revocation during org-managed recovery.
- Q: How should recovery work when a user has multiple wallets? → A: Recovery restores all wallets tied to the user account in one operation.
- Q: Where is the encrypted recovery key escrowed? → A: Wallet Service PostgreSQL — stored with the wallet record. No separate escrow service needed.
- Q: When is the recovery key generated? → A: At wallet creation only. Recovery key (AES-256) encrypts master key; recovery key itself wrapped to each recovery path's public key (org recovery key for Path 3, passkey public key for Path 4). Stored as RecoveryKeyWraps on wallet record.

## Recovery Paths

### Path 1: Mnemonic Recovery (Exists Today)

- User enters 12/24 word mnemonic
- Server re-derives master key, matches to existing wallet by address
- All addresses, delegations, credentials restored
- **Limitation:** Users lose mnemonics; no organizational fallback

### Path 2: Social Recovery (Deferred — Phase 2)

User pre-designates N trusted contacts with a threshold (e.g., 3-of-5):
- Each contact holds a Shamir's Secret Share of a recovery key
- Recovery process modelled as a Sorcha blueprint (dogfooding the platform)
- Contacts approve recovery via their own wallets (signed attestation)
- On threshold met, shares are combined to reconstruct recovery key
- Recovery key decrypts the wallet's escrowed master key

**Key design decisions:**
- Shares generated client-side, never assembled on server
- Recovery blueprint enforces time-lock (e.g., 48h waiting period) to allow dispute
- Contacts can revoke their share if they suspect compromise
- Share rotation without re-keying the wallet

**Effort:** 16h

### Path 3: Organization-Managed Recovery (New)

For enterprise deployments where the organization controls key policy:
- Org admin can initiate recovery for org members
- Server-side key escrow: wallet recovery key encrypted to org's recovery public key
- Org admin authenticates (MFA required), decrypts recovery key, re-provisions wallet
- Builds on existing delegation model: add `Recovery` access right
- Audit logged with non-repudiation

**Effort:** 8h

### Path 4: Passkey-Bound Recovery (New)

Leverages existing FIDO2/WebAuthn infrastructure in Tenant Service:
- At wallet creation, recovery key encrypted to passkey's public key (already registered via signup)
- Recovery: user authenticates with passkey on new device → proves ownership via challenge signature → server releases encrypted recovery key blob → wallet restored
- No extra UX step — same passkey used for login protects recovery
- Works across devices if passkey synced (iCloud Keychain, Google Password Manager)
- Lowest friction for consumer users

**Effort:** 8h

## Recovery Scope

Recovery restores:
1. Signing capability (master key + derived keys)
2. All pending actions across all registers
3. Workflow participation (other parties unaware of recovery)
4. Credential portfolio (SD-JWT VCs linked to wallet)
5. Delegation grants (both given and received) — all delegations revoked by default; user prompted to selectively preserve specific delegations; org admins can opt-out of revocation
6. Notification preferences and history

## Dependencies

- Sorcha.Cryptography: Shamir's Secret Sharing implementation
- Blueprint Engine: Recovery workflow blueprint template
- Tenant Service: Passkey credential resolution, org admin authorization
- Wallet Service: Key escrow storage (encrypted recovery key stored with wallet record in PostgreSQL), recovery key management

## Open Questions

- Should social recovery shares be stored on-register (transparent) or off-chain (private)?
- ~~Should recovery revoke all existing delegation grants as a security measure?~~ → Resolved: Revoke by default, user can selectively preserve, org admin can opt-out
- ~~How to handle recovery when user has multiple wallets?~~ → Resolved: Recovery restores all wallets tied to the user account in one operation
- Time-lock duration: configurable per org or platform-wide?
