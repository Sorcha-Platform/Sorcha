# Strathcarron Council Demo Universe

A shared fictional Scottish council area used across all council-related walkthroughs.
No real council names, utility companies, or identifiable organisations are used.

## Geography

| Place | Type | Postcode Prefix |
|-------|------|-----------------|
| Strathcarron | Council area | SC |
| Carronbridge | Main town (council HQ) | SC4 |
| Dalreoch | Rural village | SC6 |
| Invercarron | Conservation village | SC2 |
| Loch Morach | Scenic loch | — |

## Organisations

| Org | Subdomain | Roles |
|-----|-----------|-------|
| Strathcarron Council | strathcarron | planning-officer, building-standards-officer, building-inspector, building-control, housing-officer |
| Stoniebridge Construction | stoniebridge | contractor |
| Murchison Engineering | murchison | structural-engineer |
| Heatherbank Environmental | heatherbank | ecologist, environmental-assessor |
| Caledonian Water | caledonian-water | utilities-officer |

## Usage

Each walkthrough calls `setup-council.ps1` before its own setup:

```powershell
$councilState = & (Join-Path $PSScriptRoot ".." "council" "setup-council.ps1") -Profile $Profile
```

The script is idempotent — safe to call multiple times or from different walkthroughs.

## Walkthroughs Using This Universe

- **ConstructionPermit** — 4-org construction permit approval
- **SelfBuildHouse** — 6-org self-build with planning + building standards
- **PropertyInspection** — Council property services with photo evidence
