# Contract — Email Templates

**Date**: 2026-04-24
**Scope**: Authoring contract for the six Scriban template pairs that ship with this feature.

This file documents what each template is responsible for, what model fields it may reference, and what tone and visual conventions it MUST follow. Template copy may be polished during implementation, but these contracts bound what the templates may do.

---

## Shared conventions

- **Field references**: templates use Scriban snake_case to reference PascalCase C# properties (e.g. `{{ display_name }}` → `Model.DisplayName`).
- **Layout inheritance**: every concrete HTML template ends with `{{ capture content }}…{{ end }} {{ include 'base.html' }}`. `base.html` renders the shared frame and interpolates `{{ content }}`.
- **No external CSS, no webfonts, no JavaScript, no inline SVG** — renders the same in Gmail / Outlook / Apple Mail.
- **No `<script>`, no `<iframe>`, no `<link rel="stylesheet">`.**
- **CTA buttons** are anchor tags styled inline with `background: {{ branding.primary_color }}`, white text, 6px radius, 12×24px padding.
- **Subject line** is supplied by `ITransactionalEmailService`, not from the template — templates render body only.
- **Plaintext pair** for each HTML template must convey the same intent, the same CTA as a raw URL, and the same expiry framing.
- **No secret material** — no tokens, no passwords, no recovery phrases, no API keys in any template body.

---

## 1. `base.html` / `base.txt`

**Purpose**: shared frame wrapping every concrete template's content.

**Model access**: any concrete model (must expose `Branding` and a `content` capture block from the child).

**HTML must render**:
- Header: logo (if `branding.logo_url` non-null) OR sender name in `branding.primary_color`.
- Body: interpolated `{{ content }}` from the child template.
- Footer: tagline (if present), plus a reply-to invitation pointing at `branding.reply_to`.

**Plaintext must render**:
- Sign-off `— the {{ branding.sender_name }} team`.
- Footer line `Questions? Write to {{ branding.reply_to }}.`

---

## 2. `verify.html` / `verify.txt`

**Purpose**: confirm a newly-registered email-password user's email address.

**Model**: `VerifyEmailModel { DisplayName, VerifyUrl, ExpiresInHours, Branding }`

**MUST include**:
- Personal greeting by `display_name`.
- A single CTA button ("Confirm my email") linking to `verify_url`.
- Expiry framing referencing `expires_in_hours`.
- Reassurance line for recipients who did not sign up ("just ignore this — no account will be created").

**MUST NOT include**:
- The verification token as text.
- Any instruction to paste the token elsewhere.
- Any fear language or compliance boilerplate.

**Branding**: Sorcha default (non-org).

---

## 3. `invite.html` / `invite.txt`

**Purpose**: invite a new user to join an organisation.

**Model**: `InviteEmailModel { InviterName, OrganizationName, RoleDisplayName, AcceptUrl, ExpiresInDays, Branding }`

**MUST include**:
- Prominent `organization_name` and `role_display_name` — what org, what role.
- The inviter's name (`inviter_name`) so the invitation feels personal.
- CTA button ("Accept invitation") linking to `accept_url`.
- Expiry framing referencing `expires_in_days`.

**MUST NOT include**:
- The invitation token as text.
- Language suggesting the invitation is from Sorcha rather than the organisation.

**Branding**: per-org via `InviteEmailDispatch.InvitingOrganization`. Logo in header (or org name if no logo). Button uses `branding.primary_color`.

---

## 4. `reset.html` / `reset.txt`

**Purpose**: send a password-reset link.

**Model**: `ResetPasswordModel { DisplayName, ResetUrl, ExpiresInMinutes, Branding }`

**MUST include**:
- Personal greeting by `display_name`.
- CTA button ("Reset password") linking to `reset_url`.
- Expiry framing referencing `expires_in_minutes`.
- Reassurance line for recipients who did not request a reset.

**MUST NOT include**:
- The old password or its hash.
- Any suggestion that the user's account is at risk unless the request was initiated.

**Branding**: Sorcha default (non-org).

---

## 5. `welcome-public.html` / `welcome-public.txt`

**Purpose**: first-post-verification greeting for a public (non-org-invited) user. Primes the recipient for the wallet recovery-phrase moment.

**Model**: `WelcomePublicModel { DisplayName, DashboardUrl, BrowseRegistersUrl, DemoWorkflowsUrl, DocsUrl, Branding }`

**MUST include**:
- Warm greeting by `display_name`.
- CTA button linking to `dashboard_url`.
- An "about the recovery phrase" section that:
  - Names the concept ("12-word recovery phrase")
  - States it will be shown once at wallet creation
  - States that Sorcha cannot see it and cannot retrieve it
  - Suggests concrete save mediums (password manager, paper)
  - Frames this calmly, not as a threat
- A "what's next" list referencing `browse_registers_url`, `demo_workflows_url`, `docs_url`.

**MUST NOT include**:
- Any recovery-phrase content (no example mnemonics, no "your phrase is…").
- Any link claiming to reveal a recovery phrase later — no such path exists.
- Panic language ("if you lose this you're doomed").

**Branding**: Sorcha default (non-org).

---

## 6. `welcome-invited.html` / `welcome-invited.txt`

**Purpose**: first-post-join greeting for a user who accepted an organisation invitation.

**Model**: `WelcomeInvitedModel { DisplayName, OrganizationName, RoleDisplayName, DashboardUrl, Branding }`

**MUST include**:
- Greeting naming the organisation and role ("You've joined {organization_name}").
- CTA button linking to `dashboard_url`.
- A pointer to the organisation's administrator team as the primary source of help.

**MUST NOT include**:
- Any recovery-phrase content (org-managed users typically use org infrastructure).
- Any implication that the user's membership grants platform-admin capabilities.

**Branding**: per-org via the user's first-joined standard organisation. Logo in header, button in org primary colour, footer tagline from the org if set.

---

## Snapshot-fixture contract

Every template pair ships a golden rendering fixture at:

```
tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/
  verify.html            verify.txt
  invite.html            invite.txt
  reset.html             reset.txt
  welcome-public.html    welcome-public.txt
  welcome-invited.html   welcome-invited.txt
```

Each fixture is the exact output of rendering the template against a canonical test model (fixed `DisplayName = "Stuart Fraser"`, fixed URLs, fixed `Branding` values). The template snapshot test asserts string equality.

**Updating a fixture**: after a deliberate template or copy change, regenerate all affected fixtures and commit them alongside the template edit in the same change. Reviewers see the before/after in the same diff.
