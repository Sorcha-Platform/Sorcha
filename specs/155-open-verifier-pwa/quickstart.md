# Quickstart — Open Verifier PWA demo

End-to-end "Age over 18?" demo against the local Docker stack.

## Prerequisites

1. `docker-compose up -d` — full stack (UI `:5400`, gateway `:80`, verifier under `/verify/`, wallet PWA
   under `/wallet/`).
2. The AssuredIdentity issuing org **has an org master key**: `Set-SorchaOrgMasterKey` for that org
   (otherwise the issuer signature is unresolvable — the bare-wallet-`iss` trap). The phase-1 setup
   script ensures this.
3. The verifier is configured with a `ServiceAuth:ClientId` so the DID-backed issuer resolver is active
   (resolves `did:sorcha:org:` for layer-2 verification).
4. A citizen has enrolled a device in the wallet PWA and holds an AssuredIdentity credential issued with
   `age_over_18`, `portrait`, and the `registerAnchor` (registerId) claim.

## Run the demo

1. Open the installed **Verifier** PWA (or `…/verify/`). Confirm the install affordance appears (FR-014);
   install it and relaunch as a standalone app.
2. On the **Ask** screen, tap **"Age over 18?"**. (Under the hood: requests `age_over_18` + `portrait`
   only.) Tap **Start verification** → the QR session screen renders the `openid4vp://` QR.
3. In the wallet PWA, scan the QR, review the consent sheet (only age + portrait requested), approve.
4. The verifier flips to the **Verdict** screen:
   - **Over 18 ✓** with the portrait, `age_over_18 = true` chip, and **Issued by {org}** + DID.
   - **Validation trail** with four steps. Expand **Selective disclosure** → disclosed (`age_over_18`,
     `portrait`) vs withheld (givenName, familyName, dateOfBirth, address).
5. Tap **"verify inclusion proof"** on the **On the public register** step → the verifier calls
   `GET /api/registers/{registerId}/credentials/{credentialId}/anchor`, verifies the Merkle proof, and the
   step flips to **anchored ✓** with docket + sealed time.
6. (Optional) **Export verification bundle** → a portable JSON re-checkable via
   `POST /api/registers/{registerId}/verification-bundles/verify` (anonymous) without the verifier.

## Verifying success criteria

- **SC-001**: time from app-open to verdict < 60 s.
- **SC-002**: only `age_over_18` + `portrait` disclosed; name/DOB/address shown as withheld.
- **SC-003**: all four trail steps present and expand/collapse.
- **SC-004**: anchor confirmed + bundle exports and independently re-verifies.
- **SC-005**: revoke the credential (status-list bit) and re-present → overall "not valid", revocation
  layer Fail, issuer-signature layer still Pass.
- **SC-006**: install + standalone launch on one desktop + one mobile browser.

## Tests

- E2E: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=Verifier"`.
- Unit: `dotnet test tests/Sorcha.Verifier.Tests` and `tests/Sorcha.Register.Service.Tests`.
