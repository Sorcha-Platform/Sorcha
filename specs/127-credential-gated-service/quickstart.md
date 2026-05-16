# F127 Quickstart — Blue Badge Walk

**Feature**: F127 / credential-gated second council service (Blue Badge)
**Audience**: developers + demo operators running Spec 4 end-to-end against the local Docker stack.

> **Prerequisite**: Spec 3 (F126) cold-start walkthrough has been run at least once and produced `walkthroughs/Strathcarron/state.json`. Spec 4 chains off that state.

## 1 — Bring up the stack

```pwsh
docker-compose up -d
```

New service in the stack: `strathcarron-portal` at `http://localhost:5400/`. Confirm:

```pwsh
docker-compose ps strathcarron-portal
curl http://localhost:5400/
```

The Strathcarron Council home page renders with two service cards: Driving Licence and Blue Badge.

## 2 — Seed the demo

```pwsh
# Run Spec 3 cold-start first (if you haven't already):
./walkthroughs/Strathcarron/setup-cold-start-demo.ps1

# Then Spec 4 — chains off Spec 3's state.json:
./walkthroughs/Strathcarron/setup-blue-badge-demo.ps1
```

The Spec 4 seeder:
1. Reads `state.json` from Spec 3 (council org id, register id, council wallet, citizen accounts).
2. Publishes the Blue Badge blueprint with `prerequisites.presentationRequests` declared against `AssuredIdentityCredential`.
3. Confirms the returning-Tier-1 citizen account (`returning-<rand>@example.test`) is paired with a device AND has received an `AssuredIdentityCredential` from Spec 3's driving-licence flow.
4. Writes the Blue Badge blueprint id back into `state.json`.

If the returning citizen doesn't yet hold an `AssuredIdentityCredential`, the seeder fails fast with a message telling the operator to walk the Spec 3 returning-Tier-1 journey first.

## 3 — Walk the demo

### Walk 1: Returning Tier 1 — happy path (SC-001, SC-002)

1. Open `http://localhost:5400/services/blue-badge` in a desktop browser.
2. Sign in as `returning-<rand>@example.test` (password = `state.json:citizens.fastPath.password`).
3. The page renders: "To apply for a Blue Badge we need to confirm your identity. Tap the button below — your wallet will ask you which credential to use."
4. Tap **Present from wallet**. The hybrid QR + tap-link + paste affordance appears.
5. Open `http://localhost/wallet/` in the same browser (or scan the QR with a phone).
6. PWA: confirm the consent sheet shows the four disclosed claims. Tap **Confirm**.
7. Council page rerenders: "Verified ✓ — Sarah Example" with the four identity fields pre-populated.
8. Fill the Blue Badge fields (`mobilityCondition`, optionally `previousBadgeNumber`). Submit.
9. "Your application is in. Watch your wallet — your Blue Badge will arrive within a few seconds."
10. PWA: the `BlueBadgeCredential` appears in the home-row stack within 3–5 seconds. No takeover (Sarah is a returning citizen).

**Expected outcome**: End-to-end in under 45 seconds.

### Walk 2: No-credential citizen — error path (SC-003)

1. Open `http://localhost:5400/services/blue-badge` in a fresh incognito.
2. Sign in as `mini-gate-<rand>@example.test` (Spec 3 Tier 2 citizen) — *no `AssuredIdentityCredential` issued yet*.
3. Pair a wallet device (the F126 mini-gate flow takes over briefly).
4. Tap **Present from wallet**. The PWA picker opens — empty.
5. Council page surfaces the no-credential error state: "We need an Assured Identity credential from Strathcarron Council to continue. If you don't have one yet, apply for a driving licence first." Link points at `http://localhost:5400/services/driving-licence`.

**Expected outcome**: No dead end. The citizen has a clear next step.

### Walk 3: Stranger scans QR — friend-scans mitigation (FR-017)

1. Citizen A walks Walk 1 up to the QR step.
2. Citizen B's phone scans the QR.
3. Citizen B's PWA opens and runs the confirmation dialog: "You're about to present `AssuredIdentityCredential` to Strathcarron Council. If that's not what you wanted, cancel."
4. Citizen B taps **Cancel**.
5. Council page (on Citizen A's browser) is unchanged — still waiting for a presentation.

**Expected outcome**: No silent cross-pollination. Citizen B's credential is never disclosed.

### Walk 4: Revoked credential — security path (SC-005, FR-019)

1. As Strathcarron Council admin, revoke the returning citizen's `AssuredIdentityCredential` via the status-list endpoint (F079).
2. Returning citizen walks Walk 1.
3. At the wallet's POST, the server's `Sorcha.Verifier.Engine` returns trust status `Revoked`.
4. Council page surfaces: "This credential has been revoked. Please contact Strathcarron Council."

**Expected outcome**: Application does not advance. Revocation is detected at the moment of presentation, not retrospectively.

## 4 — Spot-check the boundary

After PR-A lands, confirm the boundary is enforced:

```pwsh
# The moved page is gone from src/:
test-path src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/CouncilApplicationDrivingLicence.razor
# False

# Old URL 404s on the platform:
curl -i http://localhost/app/strathcarron/services/driving-licence
# 404

# New URL lands on the sample:
curl -i http://localhost:5400/services/driving-licence
# 200

# CI grep gate catches a forbidden ProjectReference:
./scripts/check-samples-references.ps1
# OK — no forbidden references found

# Deliberately add a forbidden reference to samples/strathcarron-portal/Sorcha.Sample.StrathcarronPortal.csproj,
# then re-run:
./scripts/check-samples-references.ps1
# ERROR — forbidden reference to src/Apps/Sorcha.UI/Sorcha.UI.Core/...
```

## 5 — Tear down

```pwsh
docker-compose down -v
```

Removes the new `strathcarron-portal` container along with the rest. No persistent data is left on the host.
