# Feature Specification: Credential-Priced RFQ for Invoice Financing

**Feature Branch**: `113-credential-priced-rfq`
**Created**: 2026-04-24
**Status**: Draft
**Input**: User description: Extend the TradeFinance walkthrough with a competitive sealed-bid RFQ for invoice financing where a supplier's stack of attested credentials measurably lowers the rate two competing lenders quote. Two lenders with distinct pricing curves; pseudonymous bidder; reveal-on-win; timebound bid window.
**Design Reference**: `docs/superpowers/specs/2026-04-24-trade-finance-rfq-design.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Competitive RFQ rewards proof (Priority: P1)

A supplier holds attested credentials (verified business registration, trade credit insurance, digital product passport, carbon-tier attestation) in addition to the standard verified invoice. They open a request for quote against an invoice, and two lenders respond with priced offers within a defined window. Each lender values the credentials differently — one is traditional and weights credit insurance heavily; the other is green-mandated and weights sustainability credentials heavily. The supplier sees two distinct quotes side by side, with each rate broken down by which credential drove which discount, picks the better offer, and the financing proceeds with the chosen lender.

**Why this priority**: This is the headline value proposition. The feature is meaningless without it. Demonstrates "market for risk-priced proof" — the single most important sentence.

**Independent Test**: Run the walkthrough end-to-end with a fully-credentialled supplier; verify both lenders submit quotes within the window, the rate differential between lenders is at least 0.4 percentage points, the supplier's chosen quote becomes binding, and the existing invoice-finance flow runs through against the chosen lender.

**Acceptance Scenarios**:

1. **Given** a supplier holds 5 valid credentials and an active verified invoice, **When** the supplier opens an RFQ, **Then** both lenders receive the RFQ and return a priced quote within the bid window.
2. **Given** two lenders with different pricing curves see the same credential stack, **When** they compute their quotes, **Then** the resulting rates differ by at least 0.4 percentage points and reflect each lender's curve weights.
3. **Given** two valid quotes, **When** the supplier accepts one, **Then** the accepted quote is binding, the rejected quote is closed, and the existing invoice-finance flow proceeds with the accepted lender as financier.
4. **Given** a supplier who only holds the verified business registration and verified invoice (no green credentials), **When** they open an RFQ, **Then** the traditional lender quotes a rate roughly 0.6–1.0 percentage points higher than for a fully-credentialled supplier, and the green-focused lender either declines to bid or quotes notably worse than its fully-credentialled-supplier rate.

---

### User Story 2 — Pseudonymous bidder with reveal-on-win (Priority: P2)

The supplier opens the RFQ from a one-time pseudonymous identity that is not linked to their organisation in any externally-readable way. Lenders price the RFQ against the credential stack alone, never knowing which organisation is behind it. When the supplier accepts a quote, they reveal the pseudonym-to-organisation link only to the chosen lender; the losing lender retains no information that would identify the supplier.

**Why this priority**: This is what makes the demo distinctive. Existing RFQ platforms have full visibility into supplier identity throughout. The privacy story lands the "no trusted intermediary" message and is the single most quotable security property.

**Independent Test**: Inspect the public record of the RFQ. Verify the supplier's real organisation identity is absent from every record visible to anyone other than the winning lender. Verify the winning lender, after the reveal step, can cryptographically validate that the pseudonym belongs to a specific real organisation. Verify the losing lender cannot.

**Acceptance Scenarios**:

1. **Given** a supplier opens an RFQ from a pseudonymous identity, **When** lenders inspect the RFQ, **Then** neither lender can determine the supplier's real organisation from any externally-readable record.
2. **Given** the supplier accepts a quote, **When** the reveal step completes, **Then** only the accepted lender can read the link between the pseudonymous identity and the supplier's real organisation.
3. **Given** the losing lender inspects every public record after the RFQ closes, **When** they attempt to identify the supplier or read the winning quote terms, **Then** they obtain neither.

---

### User Story 3 — Timebound sealed-bid window (Priority: P3)

The RFQ has a clearly-bounded bidding window (default 10 minutes). Within the window, lenders may submit quotes; quotes are sealed (each lender's quote readable only by the supplier, not by other lenders). After the window closes, no new quotes can be accepted; the supplier has a short additional window to choose from received quotes. If no quote is accepted, or no quotes are received, the RFQ ends with an explicit terminal status.

**Why this priority**: This is the fairness and lifecycle property that makes the bidding feel like an actual market rather than open-ended negotiation. Borrows directly from existing presentation-lifecycle infrastructure, so the cost is mostly configuration, not new mechanics.

**Independent Test**: Open an RFQ with no lenders willing to bid; verify it abandons cleanly at window expiry. Open an RFQ where one lender bids and the other does not; verify the supplier can still accept the single bid. Submit a bid 1 second after the window closes; verify it is rejected.

**Acceptance Scenarios**:

1. **Given** an open RFQ and a 10-minute window, **When** the window expires with no bids, **Then** the RFQ records an "abandoned, no bids" terminal status.
2. **Given** an open RFQ where exactly one lender bids before the window closes, **When** the supplier accepts that bid within the acceptance window, **Then** financing proceeds with the bidding lender.
3. **Given** the bid window has closed, **When** a lender attempts a late bid, **Then** the bid is rejected with a "window closed" reason and the rejection is recorded.
4. **Given** the supplier has received bids but does not accept any before the acceptance window expires, **When** the acceptance window closes, **Then** the RFQ records an "expired, no acceptance" terminal status and the bids are no longer binding.

---

### Edge Cases

- **No lender bids within the window** — RFQ ends with "abandoned, no bids" terminal status; supplier can open a new RFQ if desired.
- **One lender bids, the other declines explicitly** — supplier may accept the single bid; the explicit decline is recorded with the lender's reason.
- **One lender bids, the other does not respond at all** — supplier may accept the single bid; the silent lender's absence is recorded as "no response within window".
- **Both lenders decline (e.g., supplier credential bundle does not meet either lender's minimum)** — RFQ ends with "all declined" terminal status; supplier sees the declines.
- **Supplier presents an expired or revoked credential** — pricing curve treats the credential as absent; lenders may still bid or decline based on remaining credentials.
- **Late bid submission** — bid is rejected with explicit reason; rejection is observable in the public record.
- **Supplier does not accept any quote before the acceptance window expires** — RFQ ends with "expired, no acceptance"; quotes become non-binding.
- **Supplier opens a duplicate RFQ for the same invoice while one is already active** — second RFQ is rejected with "invoice already under active RFQ".
- **Supplier reveals identity to a lender other than the accepted one** — disallowed by the protocol; reveal is only possible against the accepted quote.
- **Lender attempts to bid on its own RFQ (lender impersonates supplier)** — disallowed by the protocol; bid is rejected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A supplier MUST be able to open a request for quote against a verified invoice they hold.
- **FR-002**: A supplier MUST be able to attach an arbitrary number of attested credentials to an RFQ, with at most one credential per credential type.
- **FR-003**: An RFQ MUST be visible to a defined set of lenders for a defined bid window.
- **FR-004**: Each lender MUST be able to compute and submit a priced quote (rate, advance percentage, fee) based solely on the credentials disclosed in the RFQ.
- **FR-005**: A submitted quote MUST be readable by the supplier who opened the RFQ and unreadable by any other lender.
- **FR-006**: The supplier's real organisation identity MUST NOT be readable from the RFQ or any submitted quote by any party other than the lender whose quote is accepted.
- **FR-007**: The supplier MUST be able to view all received quotes after the bid window closes.
- **FR-008**: The supplier MUST be able to accept exactly one quote within a defined acceptance window after bid window closure.
- **FR-009**: When a quote is accepted, the supplier MUST be able to reveal their real organisation identity to the accepting lender, and to no other party.
- **FR-010**: The system MUST close the bid window after a configured duration (default: 10 minutes) and reject any quote submitted after closure.
- **FR-011**: The system MUST record a terminal status for every RFQ — accepted, abandoned (no bids), all-declined, or expired (bids received but none accepted).
- **FR-012**: Each lender MUST be able to publish an indicative rate card (the discounts applied per credential type) that suppliers can read before opening an RFQ.
- **FR-013**: The disclosed credential claims attached to an RFQ MUST be limited to the claims required by the lender rate cards; claims not required for pricing MUST remain hidden.
- **FR-014**: The accepted quote MUST drive a downstream invoice-finance flow with the accepting lender as the financier.
- **FR-015**: The losing lender MUST NOT be able to read the accepted quote's terms (rate, advance percentage, fee).
- **FR-016**: A duplicate RFQ for the same invoice (when one is already active) MUST be rejected with an explicit reason.
- **FR-017**: A late bid submitted after the bid window has closed MUST be rejected and the rejection MUST be observable in the public record.
- **FR-018**: A lender MUST be able to explicitly decline to bid on an RFQ; declines MUST be recorded with the lender's stated reason.

### Key Entities

- **Request For Quote (RFQ)**: An open auction for invoice financing. Has a bid window, an acceptance window, a credential bundle disclosed to lenders, and a terminal status. Each RFQ is tied to exactly one verified invoice and exactly one supplier (referenced via a pseudonymous identity).
- **Quote / Bid**: A lender's priced offer in response to an RFQ. Contains rate, advance percentage, fee, and expiry. Only readable by the supplier who opened the RFQ.
- **Rate Card**: A lender's published, indicative pricing schedule listing the discount applied per credential type. Readable by anyone with access to the trade finance register.
- **Pseudonymous Bidder**: A one-time identity used by a supplier to open an RFQ. Not linked to the supplier's organisation in any record visible to anyone other than the lender whose quote is accepted.
- **Identity Reveal**: The post-acceptance link between a pseudonymous bidder and the supplier's real organisation. Visible only to the accepted lender.
- **Credential Bundle**: The set of attested credentials a supplier presents in an RFQ. Each credential is selectively disclosed — only claims required by the lender rate cards are visible.

### Assumptions

- The supplier holds all credentials they intend to present in their wallet at the time of opening the RFQ. Credential issuance is a separate concern and is staged before the RFQ flow runs.
- Lenders subscribe to the trade finance register before any RFQ is opened. Lender onboarding is out of scope for this feature.
- The pseudonymous bidder is derived from the supplier organisation's existing key material (so the supplier organisation can prove the link cryptographically when revealing). No external wallet or new key custody system is introduced.
- The default bid window is 10 minutes and the default acceptance window is 5 minutes. Both are configurable per RFQ.
- For the demonstration, exactly two lenders compete on each RFQ. The architecture does not prohibit more lenders, but multi-lender (>2) coordination, scaling, and UX are not exercised by this feature.
- The credential-pricing curve is private to each lender; only the indicative rate card is published. The exact algorithm a lender uses to compute a final binding quote may differ from the indicative card.
- For sustainability claims (e.g. carbon footprint), the credential issuer is trusted to pre-compute coarse tier bands (e.g. "Tier A / B / C / D"). Cryptographic predicate proofs over hidden numeric values are explicitly out of scope.
- Lenders' pricing services are operated by the lenders themselves. For the walkthrough demonstration, they may run as scripted processes alongside the walkthrough runner; in production they would be independent services.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a supplier presenting the full credential bundle, the rate quoted by the green-focused lender is at least 0.4 percentage points lower than the rate quoted by the traditional lender.
- **SC-002**: For the same supplier organisation, the rate quoted by the traditional lender for the full credential bundle is at least 0.6 percentage points lower than the rate quoted for a minimal bundle (verified business registration only).
- **SC-003**: The green-focused lender declines to bid on suppliers who do not hold a sustainability credential at the minimum required tier.
- **SC-004**: 100% of RFQs reach a documented terminal status (accepted / abandoned / all-declined / expired) within the bid window plus the acceptance window plus 60 seconds tolerance.
- **SC-005**: After an RFQ closes, the losing lender cannot recover the supplier's real organisation identity from any record they can read.
- **SC-006**: After an RFQ closes, the losing lender cannot recover the accepted quote's rate, advance percentage, or fee from any record they can read.
- **SC-007**: A reviewer comparing the two lenders' rate cards can predict the relative ordering of their bids for any given credential bundle without running the walkthrough.
- **SC-008**: The end-to-end walkthrough — credential staging, RFQ open, two bids, acceptance, reveal, financing — completes in under 3 minutes per run.
- **SC-009**: A side-by-side rendering of the two received bids shows, for each bid, the base rate, every per-credential discount applied, the final rate, advance percentage, fee, and time remaining in the acceptance window.

### Out of Scope

The following are explicitly deferred to future work and MUST NOT be addressed by this feature:

- Cryptographic predicate proofs over hidden numeric credential values (e.g., proving "carbon below threshold" without revealing the value). The current feature relies on issuer-pre-computed tier bands.
- Multi-hop chain-of-custody credentials (e.g., forester → sawmill → distributor). The DPP credential is treated as atomic.
- More than two competing lenders on a single RFQ.
- Private invitation-only trade finance register. The trade finance register stays public; actor and bid privacy is provided by pseudonymous bidding and per-recipient encryption.
- Dynamic rate card republishing or curve rotation. Rate cards are published once and remain static for the demonstration.
- Credential revocation events affecting active RFQs.
- Secondary markets or loan resale.
- External-wallet flows for the supplier (the pseudonymous bidder is a Sorcha-internal identity).
- Production-grade privacy beyond pseudonym + reveal-on-win (e.g., unlinkable presentations across multiple RFQs).
