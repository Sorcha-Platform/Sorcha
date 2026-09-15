# CLI Models Reference

## Shared Models (from Sorcha.Register.Models)

The CLI uses shared models from `Sorcha.Register.Models` to ensure consistency with the backend services. These models are referenced via ProjectReference in the CLI project.

### Register Model

```csharp
// From: src/Common/Sorcha.Register.Models/Register.cs
public class Register
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public RegisterStatus Status { get; set; }
    public ulong Height { get; set; }
    public bool Advertise { get; set; }
    public bool IsFullReplica { get; set; }
    public string? Votes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

### TransactionModel

```csharp
// From: src/Common/Sorcha.Register.Models/TransactionModel.cs
public class TransactionModel
{
    public string TxId { get; set; } = string.Empty;
    public string RegisterId { get; set; } = string.Empty;
    public ulong? DocketNumber { get; set; }
    public uint Version { get; set; }
    public string SenderWallet { get; set; } = string.Empty;
    public string[] RecipientsWallets { get; set; } = Array.Empty<string>();
    public string PrevTxId { get; set; } = string.Empty;
    public DateTimeOffset TimeStamp { get; set; }
    public string Signature { get; set; } = string.Empty;
    public TransactionMetaData? MetaData { get; set; }
    public PayloadModel[] Payloads { get; set; } = Array.Empty<PayloadModel>();
    public int PayloadCount => Payloads.Length;
}
```

### TransactionMetaData

```csharp
// From: src/Common/Sorcha.Register.Models/TransactionMetaData.cs
public class TransactionMetaData
{
    public string RegisterId { get; set; } = string.Empty;
    public TransactionType TransactionType { get; set; }
    public string? BlueprintId { get; set; }
    public string? InstanceId { get; set; }
    public uint? ActionId { get; set; }
    public uint? NextActionId { get; set; }
    public SortedList<string, string>? TrackingData { get; set; }
}
```

### PayloadModel

```csharp
// From: src/Common/Sorcha.Register.Models/PayloadModel.cs
public class PayloadModel
{
    public string[] WalletAccess { get; set; } = Array.Empty<string>();
    public ulong PayloadSize { get; set; }
    public string Hash { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public string? PayloadFlags { get; set; }
    public Challenge? IV { get; set; }
    public Challenge[]? Challenges { get; set; }
}
```

### Docket Model

```csharp
// From: src/Common/Sorcha.Register.Models/Docket.cs
public class Docket
{
    public ulong Id { get; set; }
    public string RegisterId { get; set; } = string.Empty;
    public DocketState State { get; set; }
    public string Hash { get; set; } = string.Empty;
    public string? PreviousHash { get; set; }
    public DateTimeOffset TimeStamp { get; set; }
    public string? Votes { get; set; }
    public DocketMetaData? MetaData { get; set; }
    public List<string> TransactionIds { get; set; } = new();
}
```

### Register Creation Models

```csharp
// From: src/Common/Sorcha.Register.Models/RegisterCreationModels.cs

public class InitiateRegisterCreationRequest
{
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<OwnerInfo> Owners { get; set; } = new();
}

public class OwnerInfo
{
    public string UserId { get; set; } = string.Empty;
    public string WalletId { get; set; } = string.Empty;
}

public class InitiateRegisterCreationResponse
{
    public string RegisterId { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public List<AttestationRequest> AttestationsToSign { get; set; } = new();
}

public class AttestationRequest
{
    public string Role { get; set; } = string.Empty;
    public string WalletId { get; set; } = string.Empty;
    public string DataToSign { get; set; } = string.Empty;  // Hex-encoded hash
    public string AttestationData { get; set; } = string.Empty;
}

public class FinalizeRegisterCreationRequest
{
    public string RegisterId { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public List<SignedAttestation> SignedAttestations { get; set; } = new();
}

public class SignedAttestation
{
    public string AttestationData { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public SignatureAlgorithm Algorithm { get; set; }
}

public class FinalizeRegisterCreationResponse
{
    public string RegisterId { get; set; } = string.Empty;
    public string GenesisTransactionId { get; set; } = string.Empty;
    public ulong GenesisDocketId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

## CLI-Specific Models (in Sorcha.Cli)

### Wallet Models

```csharp
// From: src/Apps/Sorcha.Cli/Models/Wallet.cs

public class SignTransactionRequest
{
    [JsonPropertyName("transactionData")]
    public string TransactionData { get; set; } = string.Empty;

    [JsonPropertyName("isPreHashed")]
    public bool IsPreHashed { get; set; }

    [JsonPropertyName("derivationPath")]
    public string? DerivationPath { get; set; }
}

public class SignTransactionResponse
{
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("signedBy")]
    public string SignedBy { get; set; } = string.Empty;

    [JsonPropertyName("signedAt")]
    public DateTimeOffset SignedAt { get; set; }

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;
}

// Wallet access delegation (`sorcha wallet access grant|list|revoke|check`).
public class GrantAccessRequest
{
    [JsonPropertyName("subject")] public string Subject { get; set; } = string.Empty;
    [JsonPropertyName("accessRight")] public string AccessRight { get; set; } = string.Empty;   // Owner | ReadWrite | ReadOnly
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("expiresAt")] public DateTime? ExpiresAt { get; set; }
    // #1643: named Sorcha contexts the grant may sign at (--context, repeatable). REQUIRED when an
    // org Administrator grants on the org's wallet; signing with an org wallet refuses unscoped grants.
    [JsonPropertyName("allowedDerivationContexts")] public List<string>? AllowedDerivationContexts { get; set; }
}

public class WalletAccessGrant
{
    // id, subject, accessRight, grantedBy, reason, grantedAt, expiresAt, isActive, plus:
    [JsonPropertyName("allowedDerivationContexts")] public List<string>? AllowedDerivationContexts { get; set; }  // null = unscoped
}
```

### Refit Client DTOs

```csharp
// From: src/Apps/Sorcha.Cli/Services/IRegisterServiceClient.cs

public class UpdateRegisterRequest
{
    public string? Name { get; set; }
    public string? Status { get; set; }
    public bool? Advertise { get; set; }
}

public class RegisterStatsResponse
{
    public int Count { get; set; }
}

public class SubmitTransactionRequest
{
    public string RegisterId { get; set; } = string.Empty;
    public string TxType { get; set; } = string.Empty;
    public string SenderWallet { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string? PreviousTxId { get; set; }
}

public class SubmitTransactionResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class PagedQueryResponse<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class QueryStatsResponse
{
    public long TotalTransactions { get; set; }
    public int TotalRegisters { get; set; }
    public int TotalDockets { get; set; }
}
```

## Enums

### RegisterStatus

```csharp
// From: src/Common/Sorcha.Register.Models/Enums/RegisterStatus.cs
public enum RegisterStatus
{
    Online,
    Offline,
    Checking,
    Recovery
}
```

### DocketState

```csharp
// From: src/Common/Sorcha.Register.Models/Enums/DocketState.cs
public enum DocketState
{
    Pending,
    Sealed,
    Confirmed,
    Orphaned
}
```

### SignatureAlgorithm

```csharp
// From: src/Common/Sorcha.Register.Models/RegisterControlRecord.cs
public enum SignatureAlgorithm
{
    ED25519,
    NISTP256,
    RSA4096
}
```

## Project References

Add these to `Sorcha.Cli.csproj` to use shared models:

```xml
<ItemGroup>
  <!-- Shared Model Libraries -->
  <ProjectReference Include="..\..\Common\Sorcha.Register.Models\Sorcha.Register.Models.csproj" />
  <ProjectReference Include="..\..\Common\Sorcha.Blueprint.Models\Sorcha.Blueprint.Models.csproj" />
</ItemGroup>
```

## Using Shared Models

```csharp
// Import the namespace
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

// Use fully qualified names when needed to avoid conflicts
Sorcha.Register.Models.Register register = await client.GetRegisterAsync(id, token);
```
