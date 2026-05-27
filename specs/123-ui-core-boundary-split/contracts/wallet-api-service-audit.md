# Contract — `IWalletApiService` Audit Verdict

## Verdict

**USER-only. No split required. Folder move only.**

## Evidence

Phase 0 inspection of the interface surface:

```csharp
Task<List<WalletDto>> GetWalletsAsync(CancellationToken ct = default);
Task<WalletDto?> GetWalletAsync(string address, CancellationToken ct = default);
Task<CreateWalletResponse> CreateWalletAsync(CreateWalletRequest request, CancellationToken ct = default);
Task<WalletDto> RecoverWalletAsync(RecoverWalletRequest request, CancellationToken ct = default);
Task<bool> DeleteWalletAsync(string address, CancellationToken ct = default);
Task<SignTransactionResponse> SignDataAsync(string address, SignTransactionRequest request, CancellationToken ct = default);
Task<AddressListResponse> GetAddressesAsync(string address, int page = 1, int pageSize = 50, CancellationToken ct = default);
Task<WalletAddressDto> RegisterAddressAsync(string address, RegisterAddressRequest request, CancellationToken ct = default);
```

All eight methods are wallet-owner operations. A user manages their own wallets — listing, creating, recovering, deleting, signing data, getting addresses, registering new addresses. There is no admin-only method that lets an admin operate on someone else's wallet.

Org-wallet access *grant/revoke* operations live on a separate interface — `IWalletAccessService` — which IS admin-only (org admins grant/revoke access to org-owned wallets) and is moved to `Services/Admin/` under R5.

## Action

| Step | Detail |
|---|---|
| 1 | Move `IWalletApiService.cs` from `Services/Wallet/IWalletApiService.cs` to `Services/User/Wallet/IWalletApiService.cs` |
| 2 | Move `WalletApiService.cs` (concrete) alongside it |
| 3 | DI registration unchanged (same interface, same concrete) — only the file location changes |
| 4 | No consumer updates required — interface name and namespace preserved |

## Verification

1. **Given** a host-app page that today injects `IWalletApiService`, **When** the refactored codebase is rebuilt, **Then** the page builds and runs without changes — the type is found via its preserved namespace.
2. **Given** a developer browsing `Services/User/Wallet/`, **When** they look for wallet-management interfaces, **Then** they see `IWalletApiService` plus its concrete and recognise the audience by folder location.
