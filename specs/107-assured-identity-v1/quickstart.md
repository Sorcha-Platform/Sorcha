# Quickstart: Assured Identity v1

**Feature**: 107-assured-identity-v1
**Audience**: developers implementing or extending this feature

## Run the walkthrough end-to-end (single peer)

```bash
# Bring up Sorcha (existing compose file)
docker compose up -d

# Set up Government org, DLA org, citizen public account, both blueprints
pwsh walkthroughs/AssuredIdentity/setup.ps1

# Run Phase 1 (Assured Identity issuance) + Phase 2 (Driving Licence chain) end-to-end
pwsh walkthroughs/AssuredIdentity/run.ps1
```

Expected output: two credentials in the citizen's HAIP wallet directory, both verifiable via `sorcha-agent haip verify`.

## Run only one phase

```bash
pwsh walkthroughs/AssuredIdentity/run-phase1-identity.ps1   # Phase 1 only
pwsh walkthroughs/AssuredIdentity/run-phase2-licence.ps1    # Phase 2 only (requires Phase 1 to have run)
```

## Run the cross-peer smoke test

```bash
# Different compose file — two peer stacks
docker compose -f docker-compose.federation.yml up -d

# Wait for both peers healthy (~90s)
docker compose -f docker-compose.federation.yml ps

# Run the smoke test
pwsh walkthroughs/AssuredIdentity/run-multi-peer.ps1

# Inspect findings
cat walkthroughs/AssuredIdentity/multi-peer-findings.md

# Tear down
docker compose -f docker-compose.federation.yml down -v
```

The findings document records pass / degraded-pass / fail / env-failure with timings and any anomalies. **Failures do not block release** — they become tickets for the peer-replication subsystem owner.

## Add a new credential preview using `x-review`

To produce an ID-card-style review screen for any future credential issuance blueprint:

1. Add a final page to your blueprint's `x-pages` list with `x-review` declared:

```jsonc
{
  "title": "Review your application",
  "x-review": {
    "layout": "id-card",
    "editable": true,
    "header": {
      "issuerName": "Your Issuing Organisation",
      "credentialName": "Your Credential Type",
      "colourTheme": "identity-navy"
    }
  }
}
```

2. The renderer pulls all submitted values from prior pages automatically. You don't list them manually.

3. To use a new colour theme, add a CSS block in `Sorcha.UI.Core/Components/Forms/Layouts/IdCardLayout.razor.css`:

```css
.id-card[data-theme="your-theme"] {
  --card-bg-start: #...;
  --card-bg-mid: #...;
  --card-bg-end: #...;
  --card-accent: #...;
  --card-label: #...;
}
```

4. Add the enum value to `XReviewColourTheme` in `Sorcha.Blueprint.Models`. Done.

## Add an optional photo field to a blueprint

```jsonc
{
  "portrait": {
    "type": "string",
    "format": "file-reference",
    "x-file": {
      "accept": ["image/jpeg"],
      "maxSizePerFile": "5MB",
      "maxChunks": 1,
      "capture": "user",
      "embedAs": "image-token-jpeg-240x320"
    }
  }
}
```

In the credential's `claimMappings`:

```jsonc
{ "claimName": "portrait", "sourceField": "/portrait/tokenImageBase64" }
```

The renderer handles capture, ICAO advisory tips, client-side resize. The issuance step validates size and embeds the token. If the citizen skips the photo, the credential is issued without the `portrait` claim.

## Switch the assessor from rules-mode to AI-mode (deferred to v1.1)

Today the assessor actor uses rules mode:

```jsonc
{
  "mode": "rules",
  "rules": [
    {
      "actionName": "Verify Assured Identity Application",
      "decision": "approve"
    }
  ]
}
```

When AI-mode lands (v1.1), swap to:

```jsonc
{
  "mode": "ai",
  "ai": {
    "promptFile": "./prompts/identity-assessor.md",
    "model": "claude-sonnet-4-6",
    "temperature": 0.3
  }
}
```

The rest of the walkthrough is unchanged. The blueprint, the form, the assessor UI, the citizen experience all stay the same. **No platform or blueprint changes** — the agent framework already supports this.

## Debug: photo not embedded in issued credential

Symptom: Citizen submitted with a photo, the credential lacks the `portrait` claim.

Causes (in priority order):

1. **Token oversize** — check the issuance log for `WARN_CRED_PORTRAIT_OVERSIZE_001`. The client-side resize should keep the token ≤27KB base64; if a citizen's photo trips this (very rare), the credential is intentionally issued without portrait.
2. **Resize bypassed** — programmatic submission (test fixture, browser extension) might skip the client-side resize. Check the action payload at `/portrait/tokenImageBase64`. If absent, the client never wrote the token. If present but oversize, see (1).
3. **Schema mapping wrong** — confirm `claimMappings` references `/portrait/tokenImageBase64`, not `/portrait` or `/portrait/fullOriginalChunkIds`.

Log location: `dotnet logs sorcha-blueprint-svc | grep -i portrait`

## Debug: cross-peer credential never arrives on peer B

Symptom: `run-multi-peer.ps1` times out waiting for the credential to land in peer B's MyCredentials PENDING.

Steps:

1. Check the findings document — outcome should be `fail` with anomalies populated
2. Confirm peer B is subscribed to the register: `docker exec peer-b-register-svc curl localhost/api/registers/<id>/subscriptions`
3. Confirm the docket sealed on peer A: `docker exec peer-a-register-svc curl localhost/api/registers/<id>/dockets`
4. Confirm peer-to-peer replication is healthy: check `peer-svc-a` and `peer-svc-b` logs for sync errors
5. Confirm `InboundCredentialDetector` is running on peer B: it subscribes to `docket:confirmed` on Redis; check `wallet-svc-b` logs

If the docket sealed but never replicated, the issue is in the peer subsystem — file an issue, do not block release.

## Migrate a downstream consumer from `VerifiedCitizenCredential` to `AssuredIdentityCredential`

Old:

```jsonc
{
  "credentialRequirements": [
    {
      "type": "VerifiedCitizenCredential",
      "presentationSource": "HaipExternalWallet",
      "requiredClaims": [
        { "claimName": "givenName" },
        { "claimName": "familyName" },
        { "claimName": "dateOfBirth" }
      ]
    }
  ]
}
```

New (drop-in replacement):

```jsonc
{
  "credentialRequirements": [
    {
      "type": "AssuredIdentityCredential",
      "presentationSource": "HaipExternalWallet",
      "requiredClaims": [
        { "claimName": "givenName" },
        { "claimName": "familyName" },
        { "claimName": "dateOfBirth" }
      ]
    }
  ]
}
```

Same claim names, same shapes, same optional disclosure semantics. Only the credential type name changes. Search-and-replace across blueprint JSON files; re-publish blueprints that reference the old name.

## Reference

- Spec: [`spec.md`](spec.md)
- Plan: [`plan.md`](plan.md)
- Design rationale: [`../../docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md`](../../docs/superpowers/specs/2026-04-20-assured-identity-v1-design.md)
- Contracts: [`contracts/`](contracts/)
- Data model: [`data-model.md`](data-model.md)
- Research: [`research.md`](research.md)
