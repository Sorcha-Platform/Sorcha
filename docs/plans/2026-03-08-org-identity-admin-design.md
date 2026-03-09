# Design: Organization Admin & Identity Management

**Date**: 2026-03-08
**Branch**: `054-org-identity-admin`
**Status**: Approved (brainstorming complete)

## Design Decisions

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 1 | Org creation model | Hybrid — self-service + platform-provisioned | Low friction for small orgs, controlled setup for enterprise |
| 2 | User authentication | Social login + optional email/password + 2FA | Social login as primary (low friction), local as fallback |
| 3 | IDP configuration UX | Discovery-first with top-5 provider shortcuts | No stale wizard screenshots; auto-discovery is reliable |
| 4 | User provisioning | Auto-provision on first OIDC login, no restrictions by default | Start open, admins tighten later with domain restrictions |
| 5 | Role model | 5 roles (SystemAdmin, Administrator, Designer, Auditor, Member) | Consolidated from 8; removed User/Consumer/Developer as redundant |
| 6 | Default role | Member for all auto-provisioned users | Least-privilege; admins promote as needed |
| 7 | Token strategy | Full exchange — external token to Sorcha JWT | Keeps external identity as Tenant Service concern only |
| 8 | URL resolution | 3-tier hybrid — path / subdomain / custom domain | Progressive branding: works immediately, improves with config |
| 9 | OIDC callback | Standardised `/auth/callback/{orgSubdomain}` | Predictable redirect URI regardless of entry URL tier |
| 10 | Claim handling | Require email + name; ask if missing; verify email always | Every user gets a verified email for recovery |
| 11 | Admin console | Full dashboard with user stats, IDP status, audit log | Built in parallel with backend |

## Architecture

```
                    External OIDC Providers
          Microsoft  Google  Okta  Apple  AWS Cognito
                         |
                  Authorization Code
                         |
              +----------v-----------+
              |    Tenant Service     |
              |  +------------------+ |
              |  | OIDC Exchange    | |
              |  | - Discovery      | |
              |  | - Code exchange  | |
              |  | - Claim mapping  | |
              |  | - Provisioning   | |
              |  +--------+---------+ |
              |           |           |
              |  +--------v---------+ |
              |  | Token Service    | |
              |  | - Sorcha JWT     | |
              |  | - Refresh tokens | |
              |  | - 2FA            | |
              |  +------------------+ |
              +----------+-----------+
                         |
                    Sorcha JWT
                         |
              +----------v-----------+
              | All Services          |
              | (unchanged)           |
              +----------------------+
```

## Multi-Tenant URL Resolution

```
Request arrives at API Gateway
    |
    +-- Custom domain? (login.acmestores.com)
    |     -> Lookup CustomDomainMapping table -> resolve org
    |
    +-- Subdomain? (acmestores.sorcha.io)
    |     -> Extract subdomain -> lookup Organization.Subdomain
    |
    +-- Path? (app.sorcha.io/org/acmestores)
          -> Extract slug -> lookup Organization.Subdomain
```

All three tiers resolve to the same org and render the same experience.

## Top 5 OIDC Providers

| Provider | Issuer URL | Notes |
|----------|-----------|-------|
| Microsoft Entra ID | `https://login.microsoftonline.com/{tenant}/v2.0` | Enterprise primary |
| Google | `https://accounts.google.com` | Consumer + Workspace |
| Okta (incl. Auth0) | `https://{domain}.okta.com` | Enterprise SSO |
| Apple | `https://appleid.apple.com` | Consumer; email/name only on first auth |
| Amazon Cognito | `https://cognito-idp.{region}.amazonaws.com/{poolId}` | AWS-centric |

## Data Model Changes

**Modified entities:**
- Organization: +CustomDomain, +CustomDomainStatus, +AllowedEmailDomains, +OrgType, +SelfRegistrationEnabled
- IdentityProviderConfiguration: +IsEnabled, +DisplayName, +ProviderPreset, +DiscoveryDocument
- UserIdentity: -User/Consumer/Developer roles, +ExternalIdpSubject (rename), +ProvisionedVia, +InvitedByUserId, +EmailVerified, +EmailVerifiedAt, +VerificationToken, +ProfileCompleted

**New entities:**
- CustomDomainMapping: domain -> org lookup for API Gateway
- OrgInvitation: time-limited invite with email, role, expiry, status
- AuditEvent: security event log per organization

## Claim Collection Strategy

```
OIDC token received
    |
    +-- Extract email (try: email -> preferred_username -> upn)
    |
    +-- Email found?
    |   +-- Yes + email_verified=true -> Store, skip verification
    |   +-- Yes + no verified flag -> Store, send verification email
    |   +-- No -> Redirect to "Complete your profile" page
    |
    +-- Extract name (try: name -> given_name+family_name)
    |   +-- Missing -> "Complete your profile" page
    |
    +-- Profile complete + email verified -> Issue Sorcha JWT
```
