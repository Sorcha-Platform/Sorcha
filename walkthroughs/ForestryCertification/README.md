# Forestry Certification — Digital Product Passport

A short standalone walkthrough that issues a verifiable Digital Product Passport for a timber batch. The DPP credential is portable — downstream walkthroughs (Trade Finance, eventually) consume it to apply preferential terms for verifiably-sustainable products.

## The story

Highland Timber Supplies submits a timber batch for forestry certification. An independent auditor at Forestry Certification reviews the chain-of-custody evidence and either issues a `ForestProductDPPCredential` with sustainability claims or declines with a reason.

## Architecture

| Element | Detail |
|---|---|
| Registers | 1 — Forestry Certification Register (DevMode) |
| Organisations | 2 — Forestry Certification (issuer), Highland Timber Supplies (supplier) |
| Participants | 2 — Forestry Auditor (closed), Sales Manager (open / late-bound) |
| Actions | 2 — Submit Batch for Certification, Audit & Issue DPP |
| Issued credential | `ForestProductDPPCredential` (SD-JWT VC, 365-day expiry, 12 selectively-disclosable claims) |

The Sales Manager is an **open participant** — late-bound at runtime to whichever wallet submits Action 1. Any timber supplier can apply; the demo wires Highland Timber's wallet specifically so the credential is held by an organisation whose identity carries through to Trade Finance.

## Running

```powershell
# One-time per stack — provisions both orgs, wallets, register, blueprint
pwsh walkthroughs/ForestryCertification/setup.ps1 -Profile gateway

# Golden path — Sitka Spruce batch from Glen Affric, FSC-certified, score 87
pwsh walkthroughs/ForestryCertification/run.ps1 -Scenario golden-path

# Decline — restricted preservative + expired chain-of-custody cert
pwsh walkthroughs/ForestryCertification/run.ps1 -Scenario decline
```

`-Profile` accepts `gateway` (local Docker via API gateway), `direct` (services exposed individually), `aspire`, or `n1`.

## Issued credential — claim mapping

The credential consolidates evidence from both actions:

| Claim | Source action | Source field |
|---|---|---|
| `batchId`, `species`, `volumeCubicMetres`, `processingFacility` | Action 1 | Sales Manager submission |
| `originCountry`, `forestUnit`, `harvestDate` | Action 1 | nested forest origin |
| `certificationScheme`, `certificationBody` | Action 2 | Auditor decision |
| `embodiedCarbonKgCO2e` | Action 2 | Auditor's **verified** value (replaces supplier's declaration) |
| `sustainabilityScore` | Action 2 | Composite 1–100 score |
| `expiryDate` | Action 2 | DPP validity window |

Nine of these are flagged `disclosable` so a downstream verifier (Trade Finance) can present only what it needs — typically `sustainabilityScore`, `expiryDate`, `certificationScheme`, `embodiedCarbonKgCO2e` — without leaking forest unit names or volumes.

## Cross-walkthrough composition (Trade Finance follow-up)

Once Trade Finance R2 is updated to consume `ForestProductDPPCredential`, running both walkthroughs against the same stack produces the rate-uplift demonstration:

```powershell
pwsh walkthroughs/ForestryCertification/setup.ps1 -Profile gateway
pwsh walkthroughs/ForestryCertification/run.ps1 -Scenario golden-path

pwsh walkthroughs/TradeFinance/setup.ps1 -Profile gateway
pwsh walkthroughs/TradeFinance/run.ps1 -Scenario golden-path  # picks up the DPP, applies uplift
```

Highland Timber's Sales Manager wallet is the same identity in both walkthroughs (idempotent org lookup by `highland-timber` subdomain), so the DPP credential issued here is already in their wallet by the time Trade Finance asks for it.

## Notes

- The blueprint uses standard `format: "date"` rather than the spec'd `formatMaximum`/`formatMinimum` tokens — the validator dialect doesn't ship with that resolver yet (`VAL_SCHEMA_005`). Once the core schema component catalog lands, swap them back in.
- `targetAudience: SorchaInternal` on the credential issuance config produces a publish-time deprecation warning. Migrate to `SorchaLocalWallet` in a follow-up.
- DevMode register stores payloads as plaintext for inspection. Switch to FLE for any production-leaning demo.
