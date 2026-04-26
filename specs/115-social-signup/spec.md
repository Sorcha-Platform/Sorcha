# Feature Specification: Public Social Signup on n1

**Feature Branch**: `115-social-signup`
**Created**: 2026-04-26
**Status**: Draft
**Input**: User description: "Social signup on n1.sorcha.dev — production-leaning public user signup with Google + GitHub. Source design doc: docs/superpowers/specs/2026-04-26-social-signup-n1-design.md (REQs 1-9 + BACKLOG 1-6)."

## User Scenarios & Testing

### User Story 1 - First-time signup via a social provider (Priority: P1)

A first-time visitor to the public Sorcha demonstrator wants to create an
account without typing a password. They land on the signup page, see one
or more "Continue with…" buttons for the social providers the operator
has enabled, click the one they prefer, complete consent at the
provider, and land back at Sorcha already signed in to a new account.
Soon after, they receive a welcome email confirming the account is
ready.

**Why this priority**: This is the goal of the entire feature. Without
it, n1 has no working public-user signup path; the whole purpose of
opening the demonstrator to organic traffic is unmet.

**Independent Test**: Configure one provider (e.g., Google), open the
signup page in a private browser, click the provider button, complete
the provider consent screen, and confirm the visitor lands signed in
with a new account, the welcome email arrives, and the account is
correctly recorded as a public-organisation consumer. No other story
needs to be implemented for this slice to deliver value.

**Acceptance Scenarios**:

1. **Given** the platform is configured with at least one social
   provider, and the visitor has no Sorcha account, **When** the visitor
   clicks "Continue with `<provider>`" and grants consent at the
   provider, **Then** the visitor is signed in, a public-organisation
   consumer account is created for them, and a welcome email is
   dispatched.
2. **Given** the visitor returns later with the same browser, **When**
   they click "Continue with `<same provider>`" again, **Then** they are
   recognised as the existing user, signed in within seconds, no second
   welcome email is sent, and their display name is refreshed if it
   changed at the provider.
3. **Given** no social providers are configured for the environment,
   **When** the visitor opens the signup page, **Then** no provider
   buttons appear and the visitor sees only the password and passkey
   signup options.

---

### User Story 2 - Account-takeover defence at link time (Priority: P1)

A second person attempts to sign in with a social provider using an
email address that already exists in Sorcha as an unverified password
account created by someone else. The platform must refuse to link the
social identity to the existing unverified account and present a clear,
helpful message explaining what happened. Conversely, a genuine user
whose email is already verified in Sorcha can link a matching social
provider seamlessly.

**Why this priority**: With the citizen wallet feature (114) in flight,
a hijacked account is no longer just a Sorcha login — it is a
verifiable-credential wallet identity. The strict link policy must ship
with the rest of social signup or the demonstrator becomes unsafe to
expose to organic traffic.

**Independent Test**: Create a password account with email
`x@example.com` but do not verify it. From a second browser, attempt
social signup with the same email at a provider that asserts the email
as verified. Confirm the attempt is refused with a clear message and
that no link or new identity is created. Then verify the password
account's email and retry — confirm the link succeeds.

**Acceptance Scenarios**:

1. **Given** an existing Sorcha account whose email is not verified, and
   a social provider that asserts the same email as verified, **When** a
   visitor completes social-provider consent, **Then** the link is
   refused with a message directing the user to sign in with their
   password and verify their email first.
2. **Given** a social provider that does not assert the email as
   verified, **When** a visitor completes social-provider consent,
   **Then** the signup is refused with a message asking them to verify
   the address with the provider first.
3. **Given** an existing Sorcha account whose email is verified, **When**
   the same person signs in through a social provider that asserts the
   same email as verified, **Then** the social provider is linked to the
   existing account and the user is signed in.

---

### User Story 3 - Operator controls which providers are available (Priority: P2)

The operator running the n1 environment can configure which social
providers are enabled by editing environment-scoped configuration.
Adding a new provider requires only configuration changes and a service
restart — no code changes. Provider buttons on the signup page
accurately reflect what is configured.

**Why this priority**: This decouples adding the next provider
(Microsoft, later) from a code release, and ensures local dev and other
environments do not display buttons that lead nowhere. It is P2 because
n1 is shipping with two providers from day one; operator configurability
becomes load-bearing only when the second-wave providers come online.

**Independent Test**: On n1, enable only one provider in configuration.
Confirm the signup page shows that provider's button only. Add a second
provider's configuration and restart the service. Confirm the second
button appears. Remove the first provider's configuration and restart.
Confirm only the second remains.

**Acceptance Scenarios**:

1. **Given** the operator has configured exactly one provider, **When**
   any visitor opens the signup page, **Then** exactly one provider
   button is rendered.
2. **Given** the operator removes a provider's configuration and
   restarts the service, **When** any visitor opens the signup page,
   **Then** the removed provider's button is no longer rendered.
3. **Given** a fresh environment with the public-organisation signup
   setting enabled at first deploy, **When** an operator later
   reconfigures providers, **Then** the public-organisation signup
   setting is preserved (it is set once at first seed and survives
   subsequent restarts).

---

### Edge Cases

- **Provider returns a successful consent but no email claim**: refused
  as if the email were unverified, with a generic "we could not verify
  your email" message; no account is created.
- **Provider's display-name claim is empty**: the visible display name
  falls back to the email local-part (e.g. `alice@example.com` →
  "alice") so the welcome email and UI greeting are non-empty.
- **Visitor cancels the provider consent screen or the provider returns
  an error**: the visitor lands back on the signup page with a
  non-alarming message ("the sign-in was cancelled or failed") and can
  retry.
- **Visitor signs in via the same provider twice in rapid succession**
  (e.g. double-click): the second attempt is recognised as the same
  returning user and produces no duplicate accounts or duplicate welcome
  emails.
- **Provider changes the visitor's email between sign-ins**: the visitor
  remains identified by the provider's stable subject identifier; the
  Sorcha account email does not change automatically. (Email-change
  reconciliation is out of scope; tracked as backlog.)
- **Demo-environment banner**: visible on every load of the signup and
  login pages in the n1 environment, regardless of whether social signup
  succeeds or fails.
- **Welcome email already sent**: if a user signed up earlier with a
  different method (password, passkey) and now adds a social provider,
  the welcome email is not re-sent; the dispatcher is one-shot per
  user.
- **Public-organisation signup not enabled in the environment**:
  attempts to use social signup return a clear "social sign-in is not
  available on this environment" message; no partial account is
  created.
- **Provider configured but credentials invalid** (typo in client
  secret): the consent flow fails at the provider end and the visitor
  sees the cancellation message; the operator can verify by checking
  the refusal telemetry.

## Requirements

### Functional Requirements

#### Visibility & configuration

- **FR-001**: The signup and login pages MUST present a "Continue with
  `<provider>`" button only for providers that are configured with valid
  credentials in the running environment.
- **FR-002**: The signup and login pages MUST suppress the button
  entirely for any provider that is not configured. Greying out,
  tooltips, or placeholder buttons are not acceptable.
- **FR-003**: Provider configuration MUST be deployable via
  environment-scoped settings rather than committed source so that
  secrets are not stored in version control.
- **FR-004**: The platform MUST support adding a new provider after this
  feature ships through configuration alone, without changes to compiled
  code.

#### Account creation & identity binding

- **FR-005**: On a successful social sign-in for a visitor with no
  pre-existing Sorcha account and no email collision, the platform MUST
  create a new public-user account, mark its email as verified, attach
  the social-provider link, add the user to the public organisation in
  the consumer role, and dispatch the welcome email.
- **FR-006**: The platform MUST persist a stable, durable link between a
  Sorcha user account and a social-provider identity using the
  provider's stable subject identifier so that subsequent sign-ins
  recognise the same user even if their provider email changes later.
- **FR-007**: The platform MUST update the timestamp of last use on the
  social-provider link on every successful sign-in.
- **FR-008**: The platform MUST refresh the visible display name from
  the provider's name claim on every successful sign-in when the claim
  is non-empty.
- **FR-009**: The platform MUST NOT update the Sorcha account's primary
  email address from the provider after first signup. (Email-change
  reconciliation is deferred to a follow-up feature.)

#### Verification trust gates

- **FR-010**: The platform MUST refuse social signup when the provider
  does not assert that the supplied email is verified.
- **FR-011**: The platform MUST refuse to link a social provider to a
  pre-existing Sorcha account whose email has not been verified, even
  when the provider asserts its email as verified.
- **FR-012**: The platform MUST allow linking a social provider to a
  pre-existing Sorcha account when both the provider and the existing
  account have asserted the email is verified.
- **FR-013**: A returning user (provider+subject already linked) MUST
  NOT be re-checked against verification gates on subsequent sign-ins.
  Trust is established at link time.

#### Session & post-signup behaviour

- **FR-014**: The platform MUST issue an authenticated session token to
  the user after a successful social sign-in and direct them to the
  application home.
- **FR-015**: The platform MUST dispatch the welcome email at most once
  per user account, irrespective of which sign-in method triggered the
  first successful authentication.

#### Failure & disclosure

- **FR-016**: On any refusal, the platform MUST present the user a
  message that explains what to do next without exposing internal
  identifiers, hashes, or other implementation detail.
- **FR-017**: The platform MUST tolerate visitor cancellation or
  provider error and return the visitor to the signup page with a
  recoverable message rather than an unhandled error.
- **FR-018**: The platform MUST emit telemetry on each refusal — the
  provider name, refusal reason, and a non-reversible identifier — so
  operators can spot anomalies without exposing personal data in the
  telemetry stream.

#### Operational defaults

- **FR-019**: A fresh deployment of the n1 environment MUST be able to
  reach a state where social signup works without an additional manual
  database edit, by way of an environment-scoped configuration flag
  applied at first seed of platform settings.
- **FR-020**: The signup and login pages on the n1 environment MUST
  display a demonstrator banner so visitors understand the platform may
  be reset and they should not commit personal data.
- **FR-021**: The configured social-callback URL MUST match between the
  provider's registered redirect URI and the environment's actual
  callback handler. The platform MUST present a single canonical
  callback path per environment to keep provider registration tractable
  as more providers are added.

### Key Entities

- **Public user account**: An identity record for a person who can sign
  in to Sorcha. Carries the verified-email state, display name, and the
  set of providers linked to the account. New social signups create one
  of these in the public organisation by default.
- **Social-provider link**: The persistent association between a public
  user account and a single external identity provider. Holds the
  provider name, the provider's subject identifier, the email and name
  captured at link time, the link timestamp, and the last-used
  timestamp. A user may link multiple providers; each provider link is
  independent.
- **Public organisation**: The shared bucket all public signups belong
  to. Confers the consumer role to its members. Its existence and
  enabled-for-signup status is gated by a platform-wide setting.
- **Welcome message dispatch**: A one-shot record indicating the
  welcome email has been sent for a given user. Idempotent — once
  recorded, no further welcome emails fire for the same user.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A first-time visitor with a pre-existing provider account
  can go from clicking a "Continue with…" button to landing inside the
  application in under 60 seconds, including the provider's consent
  screen, on a typical home internet connection.
- **SC-002**: A returning user with a previously linked provider can
  sign in again in under 10 seconds end-to-end.
- **SC-003**: 100% of social signup attempts where the provider does
  not assert the email as verified are refused; zero accounts are
  created for such attempts.
- **SC-004**: 100% of social-link attempts to a pre-existing unverified
  account are refused with the documented message; zero links are
  created in those cases.
- **SC-005**: An operator can rotate a provider's client secret on n1
  by editing one configuration file and restarting one service,
  completing the rotation in under 5 minutes including verification.
- **SC-006**: 95% of welcome emails are delivered within 30 seconds of
  the first successful sign-in.
- **SC-007**: After this feature ships, adding a new provider requires
  only configuration changes — no source-code modification — to bring
  the new provider's button live.
- **SC-008**: Zero public-user accounts exist with the verified-email
  state set to false on the user record after this feature ships, on
  the n1 environment.
- **SC-009**: The demonstrator banner is visible on 100% of n1 signup-
  page loads.
- **SC-010**: Operators can identify the cause of a refusal from
  telemetry by counting refusals grouped by provider and reason,
  without reading raw logs.

## Assumptions

- Two providers are in scope at launch: Google and GitHub. Microsoft
  and Apple are deliberately deferred and are NOT part of this feature.
- The operator has administrative access at the chosen providers
  sufficient to register an application and obtain client credentials
  for social sign-in integration.
- DNS for `n1.sorcha.dev` resolves correctly and the environment serves
  HTTPS on a public address that providers' redirect URIs can reach.
- The existing welcome-email infrastructure is functional and
  configured on n1 (transactional email facade established in feature
  112).
- The existing social-login plumbing in the platform is functionally
  present but contains a known callback-URL mismatch and missing
  verification-trust enforcement that this feature will close.
- The citizen-wallet feature (114) downstream of this work assumes a
  trustworthy public-user identity; this feature must close the
  account-takeover gap before n1 starts taking organic traffic.
- Provider client secrets are managed by the operator outside source
  control. A move to a managed-secret backend (e.g. Key Vault) is
  appropriate when the platform graduates from a single-VM deployment
  to a multi-node Kubernetes deployment; until then, host-local
  environment files are sufficient.
- Demonstrator-banner copy is fixed and adequate; no copy work is part
  of this feature.

## Out of Scope

- Microsoft as a provider (deferred — work-vs-personal account policy
  warrants its own design with real signup data in hand).
- Apple as a provider (deferred — requires a JWT-based client-secret
  refactor that is materially more work than the other providers).
- Production-grade secret storage (Key Vault, Secrets Manager, or
  equivalent) — appropriate at first multi-node / Kubernetes
  deployment, not before.
- Email-change reconciliation when a provider's email claim drifts
  after link time.
- Consumer-persona attribute model — what social claims become persona
  attributes, how re-verification works, the surface area for citizen-
  wallet attribute set. Tracked separately.
- Account-recovery flows beyond what already exists for password users.
- Profile-page editing of social-provider links beyond what already
  exists.
- Real-publisher OAuth-app verification at provider consent screens
  (test-mode is adequate for the scale this feature targets).
- Citizen-wallet device-enrolment flows. This feature must not block
  their happy path but does not implement them.

## Dependencies

- Existing Tenant Service public-organisation bootstrap and
  consumer-role provisioning.
- Existing welcome-email facade (feature 112).
- Citizen-wallet feature 114 depends on this — the strict link policy
  here is what makes wallet identity safe to anchor on a public-user
  account.

## Notes for Planning

The design doc at
`docs/superpowers/specs/2026-04-26-social-signup-n1-design.md` is the
implementation reference. It captures the technical decisions (REQs
1-9), the live bug being fixed (redirect URI mismatch), the deploy
procedure on n1, and the test surface. Planning should consume the
design doc directly and produce a tasks list that maps each FR in this
spec to one or more implementation steps in the design.
