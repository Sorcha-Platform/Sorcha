# Feature Specification: Tiered-Audience JWT Identity Model + Issuer Hardening (Spec A)

**Feature Branch**: `136-jwt-audience-tiers`
**Created**: 2026-05-21
**Status**: Draft
**Input**: Approved design at `docs/superpowers/specs/2026-05-21-tiered-audience-identity-model-design.md`

> Foundational platform-security rework of how Sorcha issues and validates JWT access tokens. A downstream feature (Spec B — PWA authentication & signup parity) depends on the guarantees defined here.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Trust-tier isolation between consumer and platform surfaces (Priority: P1)

A token obtained for one trust tier cannot be used to reach another tier's surfaces. A citizen's wallet token cannot act against admin/org-management surfaces; an org administrator's platform token cannot be replayed against consumer wallet surfaces. The separation holds at the token layer, independent of (and in addition to) role checks.

**Why this priority**: This is the core defense-in-depth value of the feature. Today the audience claim isolates nothing; authorization rests entirely on roles, so a single misconfigured role is the only thing between a consumer token and an admin surface. Tier isolation makes cross-tier access fail by construction.

**Independent Test**: Mint a consumer token and a platform token for the same person; confirm the consumer token is refused at every platform/admin endpoint and the platform token is refused at every consumer endpoint, regardless of the roles each carries.

**Acceptance Scenarios**:

1. **Given** a valid consumer-tier token, **When** it is presented to an admin/org-management surface, **Then** access is refused at the tier-authorization layer.
2. **Given** a valid platform-tier token, **When** it is presented to a consumer wallet surface, **Then** access is refused at the tier-authorization layer.
3. **Given** a consumer-tier token, **When** it is presented to a consumer surface the holder is entitled to, **Then** access is granted.
4. **Given** an authenticated endpoint that has not been explicitly classified to a tier, **When** any human token is presented, **Then** it is treated as requiring the most restrictive human tier (platform) and a consumer token is refused.

---

### User Story 2 - Infrastructure (service) isolation from human tokens (Priority: P1)

Internal / service-to-service surfaces accept only service-tier tokens, and human tokens are refused there at the token layer — not merely by a claim check. Conversely, a service token is refused at human-facing surfaces.

**Why this priority**: Internal endpoints carry the platform's most powerful operations. Today they authenticate any validly-signed token and then check a single claim; promoting the boundary into the audience means a human token cannot even be accepted at an internal endpoint.

**Independent Test**: Present a human (consumer or platform) token to an internal/service endpoint and confirm refusal; present a service-tier token and confirm acceptance; present a service token to a human-facing endpoint and confirm refusal.

**Acceptance Scenarios**:

1. **Given** a human-tier token, **When** it is presented to an internal/service endpoint, **Then** access is refused.
2. **Given** a service-tier token, **When** it is presented to an internal/service endpoint it is scoped for, **Then** access is granted.
3. **Given** a service-tier token, **When** it is presented to a consumer or platform human surface, **Then** access is refused.

---

### User Story 3 - Cross-installation isolation and issuer hardening (Priority: P1)

Tokens minted by one Sorcha installation are never accepted by another, and an installation cannot silently fall back to a shared identity. Each installation declares its own identity; a misconfigured installation refuses to start rather than running with a shared default.

**Why this priority**: A shared default issuer (a domain the platform may not own per-installation) is "insecure by default." Combined with installation-namespaced audiences, this makes installation identity explicit and forces correct configuration.

**Independent Test**: Configure two installations with distinct identities; confirm a token from installation A is rejected by installation B. Start an installation with no explicit issuer and no installation name in a production-like profile; confirm it refuses to start.

**Acceptance Scenarios**:

1. **Given** two installations with distinct identities, **When** a token issued by installation A is presented to installation B, **Then** installation B rejects it.
2. **Given** a production-like installation with neither an explicit issuer nor an installation name configured, **When** it starts up, **Then** startup fails with an actionable configuration error.
3. **Given** an installation configured only with an installation name, **When** it issues tokens, **Then** both the issuer and the audiences are derived from that name with no shared/default domain.
4. **Given** a white-label installation that overrides the installation name, **When** it issues tokens, **Then** the audiences carry that installation's namespace prefix.

---

### User Story 4 - Consumer token works across every sign-in method (Priority: P2)

A person can obtain a consumer-tier token through any supported authentication method (email/password, two-factor, social, organisation SSO, or device enrolment), and that token works uniformly on consumer surfaces (web and wallet) while remaining powerless on platform surfaces. This is the dependency contract the downstream PWA auth feature relies on.

**Why this priority**: Without this, the downstream PWA signup/sign-in feature cannot land a usable consumer token after a server-side auth round-trip. It is P2 because it is the enabling contract for B rather than a security boundary in itself.

**Independent Test**: Complete each sign-in method requesting the consumer tier and confirm the resulting token is accepted by consumer/wallet surfaces, carries the holder's stable identifier, and is refused on platform surfaces.

**Acceptance Scenarios**:

1. **Given** any supported sign-in method completed with a consumer request, **When** the token is issued, **Then** it is a consumer-tier token carrying the holder's stable identifier and omitting organisation roles.
2. **Given** an authentication entry that targets a consumer destination, **When** the token is issued, **Then** the consumer tier is selected automatically.
3. **Given** a consumer-tier token, **When** it is presented to the wallet surface, **Then** the wallet accepts it.
4. **Given** a refresh of a consumer-tier token, **When** a new access token is minted, **Then** it remains consumer-tier.

---

### User Story 5 - Dual-role person gets the right tier per context (Priority: P3)

A person who is both a citizen (wallet holder) and an organisation administrator receives a consumer token when acting on consumer surfaces and a platform token when administering their organisation, with the token re-minted to the correct tier when they switch context.

**Why this priority**: An important correctness case for real users, but lower priority because it composes the tier-selection rule already required by US1/US4 rather than adding a new mechanism.

**Independent Test**: As a person with both a wallet and an admin role, obtain a consumer token via a consumer entry, then switch to organisation context and confirm the re-minted token is platform-tier; confirm each token only works on its tier.

**Acceptance Scenarios**:

1. **Given** a person with both consumer and platform entitlements, **When** they authenticate via a consumer entry, **Then** they receive a consumer-tier token.
2. **Given** that person holding a consumer token, **When** they switch to organisation/administration context, **Then** a platform-tier token is re-minted.
3. **Given** a person without any platform entitlement, **When** a platform tier is explicitly requested, **Then** the request is refused (not silently downgraded).

---

### Edge Cases

- **Tier over-request**: a request for a tier the holder is not entitled to is refused, not downgraded to a lower tier.
- **No tier requested**: the lowest-privilege human tier (consumer) is selected by default.
- **Unclassified endpoint**: defaults to requiring the most restrictive human tier (platform), so nothing silently accepts a consumer token.
- **Missing issuer configuration**: production-like profile fails closed at startup; development falls back to a clearly-local, non-domain identifier.
- **Refresh token tier mismatch**: a refresh token can only re-mint an access token of its own tier.
- **One-time enrolment audience**: the enrolment-session token remains a distinct, single-use audience and is not a general access token.
- **Service token with wrong installation namespace**: rejected even if it carries the correct service marker.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST issue access tokens in one of four trust tiers — consumer, platform, service, and enrolment-session — each expressed in the token's audience.
- **FR-002**: Each audience MUST be namespaced by the installation, using an installation name that defaults to a standard value and is overridable per deployment.
- **FR-003**: Audience values MUST be derived from a single source of truth so issuance and validation cannot diverge.
- **FR-004**: Token validation MUST accept only audiences belonging to the validating installation, rejecting tokens whose audience namespace identifies a different installation.
- **FR-005**: Tier enforcement MUST occur per protected surface: consumer surfaces accept only consumer-tier tokens, platform surfaces only platform-tier, internal/service surfaces only service-tier.
- **FR-006**: Internal/service surfaces MUST reject human-tier tokens, and human-facing surfaces MUST reject service-tier tokens.
- **FR-007**: Any protected surface not explicitly classified to a tier MUST default to requiring the most restrictive human tier (platform).
- **FR-008**: At issuance, the minted tier MUST be the intersection of the requested tier and the tiers the holder is entitled to; an un-entitled request MUST be refused rather than downgraded.
- **FR-009**: When no tier is requested, the system MUST default to the lowest-privilege human tier (consumer).
- **FR-010**: The requested tier MUST be derivable from the authentication entry (e.g. the post-authentication destination), so an authentication that targets a consumer destination yields a consumer-tier token.
- **FR-011**: Every server-side token-issuance path — interactive login, two-factor completion, refresh, context switch, signup completion, social sign-in, organisation SSO, and device-enrolment redemption — MUST apply the same tier-selection rule and be able to mint a consumer-tier token.
- **FR-012**: A refresh operation MUST mint an access token of the same tier as the refresh token it consumes.
- **FR-013**: A consumer-tier token MUST carry the holder's stable cross-organisation identifier and MUST NOT carry organisation roles or organisation context, so it is inert against platform surfaces.
- **FR-014**: A platform-tier token MUST carry the holder's organisation context and roles.
- **FR-015**: A service-tier token MUST carry its service identity and scopes.
- **FR-016**: A context switch MUST re-mint the token at the tier appropriate to the new context.
- **FR-017**: The system MUST resolve the token issuer with no shared default: an explicitly configured issuer takes precedence; otherwise the issuer is derived from the installation name as an opaque, non-domain identifier.
- **FR-018**: When neither an explicit issuer nor an installation name is configured, the system MUST fail closed at startup in production-like profiles, and MUST use a clearly-local non-domain identifier in development.
- **FR-019**: The installation name MUST drive both the issuer and the audience namespace, so the two cannot be configured inconsistently.
- **FR-020**: The consumer surfaces of the wallet and the consumer web experience MUST accept the consumer-tier audience (the downstream PWA-auth dependency contract).

### Key Entities *(include if feature involves data)*

- **Trust tier**: One of consumer, platform, service, enrolment-session — the principal class a token belongs to. Determines which surfaces accept it.
- **Tier audience**: The token's intended-recipient value, composed of an installation namespace and a tier (e.g. `{installation}:consumer`). The unit both issuance stamps and validation checks.
- **Installation identity**: The per-deployment identity (name → issuer + audience namespace). Distinct per installation; never shared by default.
- **Access token (per tier)**: A signed token whose claim set is determined by its tier — consumer (holder identifier only), platform (organisation context + roles), service (service identity + scopes), enrolment-session (one-time pairing).
- **Tier entitlement**: The set of tiers a given holder may obtain, derived from their roles in the active context (everyone may obtain consumer; platform requires a platform role).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of token-issuance paths emit a tier-scoped, installation-namespaced audience (no path emits a shared or untiered audience).
- **SC-002**: A consumer-tier token is refused at 100% of platform and internal surfaces; a platform-tier token is refused at 100% of consumer and internal surfaces; a service-tier token is refused at 100% of human surfaces.
- **SC-003**: A token issued by one installation is rejected by a second installation in 100% of cases.
- **SC-004**: An installation started in a production-like profile with neither an explicit issuer nor an installation name fails to start and emits an actionable configuration error 100% of the time.
- **SC-005**: A person can obtain a consumer-tier token through every supported sign-in method, and that token is accepted by the wallet surface in each case.
- **SC-006**: A request for a tier the holder is not entitled to is refused (never silently downgraded) in 100% of cases.
- **SC-007**: No protected endpoint accepts a token whose tier does not match the endpoint's classification, including endpoints left unclassified (which default to platform).

## Assumptions

- **Symmetric signing is retained.** Moving to asymmetric signing (so only the issuer can mint) is a separate future initiative and is out of scope here.
- **No migration / back-compat.** This is pre-release; the canonical demo environment is non-production. Rollout is a coordinated configuration change in which existing tokens simply expire and holders re-authenticate. No compatibility shims are required.
- **Requested-tier transport.** The requested tier is derived from the authentication entry's post-authentication destination, with an optional explicit override; a mandatory tier parameter is not required.
- **Issuer default format.** When derived from the installation name, the issuer is an opaque, non-domain identifier (so a misconfiguration never implies a domain the platform does not own).
- **Per-service audiences are out of scope.** Distinguishing one back-end service from another by audience is a separate axis and a separate initiative.

## Dependencies

- **Consumed by Spec B (PWA authentication & signup parity).** Spec B relies on FR-011, FR-013, and FR-020: every server-side auth flow can mint a consumer-tier token; a consumer destination yields the consumer tier; the consumer token carries the holder identifier; and the wallet + consumer web surfaces validate the consumer audience.
- No upstream feature dependencies.

## Out of Scope

- The PWA sign-in/sign-up user experience, the return-to-app token hand-off, and the startup auth-gate decision (all Spec B).
- Asymmetric token signing.
- Per-service (per-API) audiences.
- Any data migration or dual-audience compatibility period.
