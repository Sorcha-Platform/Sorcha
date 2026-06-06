# Phase 1 Data Model: Wallet-aware PairingTakeover

No persisted entities. No EF/schema change. The feature reads existing wallet data via
`IWalletRepository` through `ResolveCitizenContextAsync` and adds one transport DTO.

## DTO — WalletExistsResponse

Location: `src/Common/Sorcha.CitizenWallet.Abstractions/Models/WalletExistsResponse.cs`
(sibling to `HasAnyDeviceResponse`).

| Field | Type | Description |
|---|---|---|
| `HasWallet` | `bool` | `true` when a wallet resolves for the calling citizen (`walletAddress is not null`), else `false`. |

- Immutable `record` with `init` accessor; XML-documented; license header.
- Deliberately carries **no** wallet address or other PII — boolean only.

## Client transient state (PairingTakeover)

Not persisted; component-local fields driving the 3-state machine:

| Field | Type | Meaning |
|---|---|---|
| `Probe.HasAnyDevice` | `bool?` | existing device probe; `null` = in flight |
| `_hasWallet` | `bool?` | result of the one-shot `HasWalletAsync`; `null` = not yet resolved |

### State transitions (visibility + body)

```
HasAnyDevice == null                      → hidden (device check in flight)
HasAnyDevice == true                      → hidden (device already paired here)
HasAnyDevice == false & _hasWallet == null→ hidden (wallet check in flight)  [no flash]
HasAnyDevice == false & _hasWallet == false→ visible: CREATE-WALLET body
HasAnyDevice == false & _hasWallet == true → visible: PAIR body (unchanged)
```

`_hasWallet` is fetched once, immediately after the device probe resolves to `false`.
