# Feature Specification: Wallet-aware PairingTakeover

**Feature Branch**: `149-pwa-pairing-takeover-wallet-aware`
**Created**: 2026-06-06
**Status**: Approved
**Design**: `docs/superpowers/specs/2026-06-06-pwa-pairing-takeover-wallet-aware-design.md`
**Roadmap**: P0 #1 in `docs/superpowers/specs/2026-06-06-citizen-wallet-companion-roadmap.md`

## Summary

The Citizen Wallet PWA's `PairingTakeover` overlay currently assumes every signed-in citizen
already has a wallet, and offers only "Set up this device". A citizen who reaches the PWA without
a web-created wallet hits a 404 dead-end. This feature makes the takeover **wallet-aware**: it
detects whether the citizen has a wallet and, when they do not, routes them to create one on the
web app (companion-first) instead of dead-ending — while leaving the existing pair flow unchanged
for citizens who already have a wallet.

## User Scenarios & Testing

### US1 — Walletless citizen is routed to web wallet creation (P0)

A citizen signs into the PWA but has never created a wallet on the web. Instead of being offered a
"Set up this device" action that fails with a 404, the takeover shows a **create-wallet** state
that explains the wallet lives on the web and provides a button that takes them to the web
wallet-creation page.

**Acceptance:**
1. **Given** a signed-in citizen with no wallet and no paired device, **When** the takeover
   resolves, **Then** it shows the create-wallet state (headline "Create your wallet first") and
   does **not** show the "Set up this device" enrol button.
2. **Given** the create-wallet state, **When** the citizen taps "Create your wallet", **Then** the
   browser force-loads the web host's `/wallets/create` page at the origin root.
3. **Given** the citizen has created a wallet on the web and returns to the PWA, **When** the
   takeover re-initialises, **Then** it shows the existing pair flow (because a wallet now exists).

### US2 — Citizen with a wallet still gets the pair flow unchanged (P0)

A citizen who already has a wallet but no paired device on this hardware must see exactly today's
pairing experience.

**Acceptance:**
1. **Given** a signed-in citizen with a wallet but no device on this hardware, **When** the takeover
   resolves, **Then** it shows the existing pair state ("Set up this device" + short-code panel),
   unchanged.
2. **Given** a citizen with a paired device on this hardware, **When** the PWA loads, **Then** the
   takeover stays hidden (unchanged).

### US3 — No flashing during detection (P0)

The overlay must never briefly flash the wrong state while server checks are in flight.

**Acceptance:**
1. **Given** the device check or the wallet check is still in flight, **When** the component renders,
   **Then** the overlay is hidden (no partial/incorrect state shown).

### Edge cases

- **Existence check fails (network/transient):** the takeover falls through to the existing pair
  flow (fail-safe). A genuinely walletless citizen would then see the pre-existing enrol 404 message
  — no worse than today; never a false "create a second wallet" prompt for a wallet owner.
- **Short-code pairing in the walletless state:** not offered — redeeming a pairing code also
  requires a wallet, so it is omitted from the create-wallet state.

## Requirements

### Functional

- **FR-001:** The system MUST expose a consumer-tier endpoint that reports, for the signed-in
  citizen, whether a wallet exists, returning an explicit boolean with HTTP 200 (no 401/404
  ambiguity for an authenticated consumer).
- **FR-002:** The PWA MUST determine wallet existence via a single ("one-shot") check; it MUST NOT
  require a live change-notification contract for this signal (walletless is a terminal cold-start
  state that only transitions false→true once).
- **FR-003:** When a signed-in citizen has no paired device on this hardware **and** no wallet, the
  takeover MUST present a create-wallet state that routes to the web wallet-creation page and MUST
  NOT present the device-enrol action.
- **FR-004:** The create-wallet route MUST force-load the web host's `/wallets/create` at the origin
  root (absolute origin), matching the existing web-signup handoff precedent.
- **FR-005:** When a signed-in citizen has no paired device on this hardware **but** has a wallet,
  the takeover MUST present the existing pair flow unchanged.
- **FR-006:** The takeover MUST remain hidden while either the device check or the wallet check is
  in flight (no flashing).
- **FR-007:** On a transient failure of the wallet-existence check, the takeover MUST fall through to
  the pair flow (fail-safe), never falsely routing a wallet owner to create another wallet.
- **FR-008:** All new behaviour MUST follow Sorcha notification routing (no `ISnackbar`; inline
  feedback only) and carry license headers on new files.

### Key entities

- **WalletExistsResponse** — response of the existence endpoint: `{ hasWallet: bool }`.

## Success Criteria

- **SC-001:** A walletless signed-in citizen in the PWA reaches a working "create your wallet on the
  web" path with zero dead-ends, and after creating a wallet can return and pair the device.
- **SC-002:** A citizen who has a wallet but no device on this hardware experiences no change to the
  pair flow.
- **SC-003:** No state flashes during detection; the wrong body is never shown.
- **SC-004:** New behaviour is covered by automated tests (component states + existence-probe unit
  tests), CI is green (`build-and-test` + `claude-review`), and the change ships in a single PR.
