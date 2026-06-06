# Quickstart: Wallet-aware PairingTakeover

## What changed

The PWA pairing takeover now distinguishes "no wallet yet" from "no device here":

- **Walletless** signed-in citizen → "Create your wallet first" → button force-loads the web
  `/wallets/create` page. After creating a wallet on the web and returning to the PWA, they get the
  pair flow.
- **Has wallet, no device here** → existing pair flow, unchanged.

## Try it (local Docker stack)

1. Bring up the stack (`docker-compose up -d`) and open the PWA under `/wallet`.
2. **Walletless path:** sign in as a citizen who has an account but no wallet. The takeover shows
   the create-wallet state; tapping "Create your wallet" lands on the web `/wallets/create`.
3. **Has-wallet path:** sign in as a citizen who already created a wallet (web) but hasn't paired
   this browser. The takeover shows "Set up this device" + the short-code panel, as before.

## Verify the endpoint directly

```bash
# With a consumer-tier bearer token for the citizen:
curl -s -H "Authorization: Bearer $CONSUMER_JWT" \
  http://localhost/api/v1/wallet/exists
# → {"hasWallet":false}  (walletless)  or  {"hasWallet":true}
```

## Run the tests

```bash
dotnet test tests/Sorcha.Wallet.Pwa.Tests \
  --filter "FullyQualifiedName~HasWalletProbeTests|FullyQualifiedName~PairingTakeoverTests"
```

## n1 validation (post-merge, post-Docker-Publish)

Use the `n1-deploy` skill: pull + recreate `sorcha-wallet-pwa`, confirm the new build badge version,
then walk both paths (walletless → web create handoff; has-wallet → pair flow).
