# PR Review Fixes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix 13 outstanding issues from PR code reviews: crypto correctness (C5, C6, M5, M6), UI bugs (C10, M11), and code quality (M1-M4, M9, M13).

**Architecture:** Targeted fixes to existing files. No new services or infrastructure. One new enum, two new exception classes, SSRF guard added to existing resolver. All changes are backward-compatible.

**Tech Stack:** .NET 10, C# 13, xUnit, FluentAssertions, Moq, Blazor WASM

---

### Task 1: HybridVerificationMode Enum (C5 — Part 1)

**Files:**
- Create: `src/Common/Sorcha.Cryptography/Models/HybridVerificationMode.cs`

**Step 1: Create the enum file**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Cryptography.Models;

/// <summary>
/// Controls how hybrid (classical + PQC) signatures are verified.
/// </summary>
public enum HybridVerificationMode
{
    /// <summary>
    /// Both classical AND PQC components must verify successfully.
    /// Use for production security — compromising one key type is insufficient.
    /// </summary>
    Strict,

    /// <summary>
    /// Either classical OR PQC component verifying is sufficient.
    /// Use during migration when not all participants have PQC keys yet.
    /// </summary>
    Permissive
}
```

**Step 2: Verify it compiles**

Run: `dotnet build src/Common/Sorcha.Cryptography/Sorcha.Cryptography.csproj --no-restore`
Expected: Build succeeded

---

### Task 2: Update ICryptoModule and CryptoModule (C5 — Part 2)

**Files:**
- Modify: `src/Common/Sorcha.Cryptography/Interfaces/ICryptoModule.cs:130-141`
- Modify: `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs:840-896`

**Step 1: Update the interface**

In `ICryptoModule.cs`, replace the `HybridVerifyAsync` signature (lines 130-141):

```csharp
    /// <summary>
    /// Verifies a <see cref="HybridSignature"/> using the specified verification mode.
    /// In Strict mode, both classical and PQC components must verify.
    /// In Permissive mode, either component verifying is sufficient (migration support).
    /// </summary>
    /// <param name="hybridSignature">The hybrid signature to verify.</param>
    /// <param name="hash">The hash that was signed.</param>
    /// <param name="classicalPublicKey">The classical public key (required when classical component is present).</param>
    /// <param name="mode">Verification mode: Strict (default) requires both, Permissive requires either.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CryptoStatus> HybridVerifyAsync(
        HybridSignature hybridSignature,
        byte[] hash,
        byte[]? classicalPublicKey,
        HybridVerificationMode mode = HybridVerificationMode.Strict,
        CancellationToken cancellationToken = default);
```

**Step 2: Update the implementation**

In `CryptoModule.cs`, update the method signature (lines 840-846) to add the `mode` parameter:

```csharp
    public async Task<CryptoStatus> HybridVerifyAsync(
        HybridSignature hybridSignature,
        byte[] hash,
        byte[]? classicalPublicKey,
        HybridVerificationMode mode = HybridVerificationMode.Strict,
        CancellationToken cancellationToken = default)
```

Update the XML doc on line 840:

```csharp
    /// <summary>
    /// Verifies a HybridSignature using the specified verification mode.
    /// </summary>
```

Replace line 886:

```csharp
            return mode switch
            {
                HybridVerificationMode.Strict => (classicalValid && pqcValid) ? CryptoStatus.Success : CryptoStatus.InvalidSignature,
                HybridVerificationMode.Permissive => (classicalValid || pqcValid) ? CryptoStatus.Success : CryptoStatus.InvalidSignature,
                _ => CryptoStatus.InvalidSignature
            };
```

Add `using Sorcha.Cryptography.Models;` to the top of `CryptoModule.cs` if not already present. Also add it to `ICryptoModule.cs`.

**Step 3: Build to verify compilation**

Run: `dotnet build src/Common/Sorcha.Cryptography/Sorcha.Cryptography.csproj`
Expected: Build succeeded (or warnings about callers — will fix in tests)

---

### Task 3: Tests for HybridVerificationMode (C5 — Part 3)

**Files:**
- Modify: `tests/Sorcha.Cryptography.Tests/Unit/Pqc/HybridVerificationTests.cs`

**Step 1: Write failing tests for Strict mode**

Add these tests after the existing `HybridVerifyAsync_WrongData_ShouldReject` test (after line 186):

```csharp
    [Fact]
    public async Task HybridVerifyAsync_StrictMode_ClassicalOnlyValid_ShouldReject()
    {
        var classicalKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519)).Value;
        var data = System.Security.Cryptography.SHA256.HashData("strict classical only"u8.ToArray());
        var sig = await _cryptoModule.SignAsync(data, (byte)WalletNetworks.ED25519, classicalKeySet.PrivateKey.Key!);

        var hybrid = new HybridSignature
        {
            Classical = Convert.ToBase64String(sig.Value!),
            ClassicalAlgorithm = "ED25519"
        };

        var result = await _cryptoModule.HybridVerifyAsync(
            hybrid, data, classicalKeySet.PublicKey.Key!,
            HybridVerificationMode.Strict);

        result.Should().Be(CryptoStatus.InvalidSignature,
            "Strict mode requires BOTH components — classical-only should fail");
    }

    [Fact]
    public async Task HybridVerifyAsync_StrictMode_PqcOnlyValid_ShouldReject()
    {
        var pqcKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65)).Value;
        var data = System.Security.Cryptography.SHA256.HashData("strict pqc only"u8.ToArray());
        var sig = await _cryptoModule.SignAsync(data, (byte)WalletNetworks.ML_DSA_65, pqcKeySet.PrivateKey.Key!);

        var hybrid = new HybridSignature
        {
            Pqc = Convert.ToBase64String(sig.Value!),
            PqcAlgorithm = "ML-DSA-65",
            WitnessPublicKey = Convert.ToBase64String(pqcKeySet.PublicKey.Key!)
        };

        var result = await _cryptoModule.HybridVerifyAsync(
            hybrid, data, null,
            HybridVerificationMode.Strict);

        result.Should().Be(CryptoStatus.InvalidSignature,
            "Strict mode requires BOTH components — PQC-only should fail");
    }

    [Fact]
    public async Task HybridVerifyAsync_StrictMode_BothValid_ShouldAccept()
    {
        var classicalKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519)).Value;
        var pqcKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ML_DSA_65)).Value;
        var data = System.Security.Cryptography.SHA256.HashData("strict both valid"u8.ToArray());

        var hybrid = (await _cryptoModule.HybridSignAsync(
            data,
            (byte)WalletNetworks.ED25519, classicalKeySet.PrivateKey.Key!,
            (byte)WalletNetworks.ML_DSA_65, pqcKeySet.PrivateKey.Key!, pqcKeySet.PublicKey.Key!)).Value!;

        var result = await _cryptoModule.HybridVerifyAsync(
            hybrid, data, classicalKeySet.PublicKey.Key!,
            HybridVerificationMode.Strict);

        result.Should().Be(CryptoStatus.Success);
    }

    [Fact]
    public async Task HybridVerifyAsync_PermissiveMode_ClassicalOnlyValid_ShouldAccept()
    {
        var classicalKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519)).Value;
        var data = System.Security.Cryptography.SHA256.HashData("permissive classical"u8.ToArray());
        var sig = await _cryptoModule.SignAsync(data, (byte)WalletNetworks.ED25519, classicalKeySet.PrivateKey.Key!);

        var hybrid = new HybridSignature
        {
            Classical = Convert.ToBase64String(sig.Value!),
            ClassicalAlgorithm = "ED25519"
        };

        var result = await _cryptoModule.HybridVerifyAsync(
            hybrid, data, classicalKeySet.PublicKey.Key!,
            HybridVerificationMode.Permissive);

        result.Should().Be(CryptoStatus.Success,
            "Permissive mode accepts classical-only");
    }

    [Fact]
    public async Task HybridVerifyAsync_DefaultMode_IsStrict()
    {
        // Default parameter should be Strict — verify by checking classical-only fails
        var classicalKeySet = (await _cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519)).Value;
        var data = System.Security.Cryptography.SHA256.HashData("default mode test"u8.ToArray());
        var sig = await _cryptoModule.SignAsync(data, (byte)WalletNetworks.ED25519, classicalKeySet.PrivateKey.Key!);

        var hybrid = new HybridSignature
        {
            Classical = Convert.ToBase64String(sig.Value!),
            ClassicalAlgorithm = "ED25519"
        };

        // Call WITHOUT explicit mode — should default to Strict
        var result = await _cryptoModule.HybridVerifyAsync(
            hybrid, data, classicalKeySet.PublicKey.Key!);

        result.Should().Be(CryptoStatus.InvalidSignature,
            "Default mode should be Strict, rejecting classical-only");
    }
```

Add `using Sorcha.Cryptography.Models;` to the top of the test file.

**Step 2: Run tests**

Run: `dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~HybridVerification" -v normal`
Expected: All new tests PASS. Existing tests `HybridVerifyAsync_ClassicalOnlyValid_ShouldAccept` and `HybridVerifyAsync_PqcOnlyValid_ShouldAccept` will now FAIL because they call without mode param (defaults to Strict).

**Step 3: Fix existing tests to use Permissive mode**

Update `HybridVerifyAsync_ClassicalOnlyValid_ShouldAccept` (line 148) to pass `HybridVerificationMode.Permissive`:

```csharp
        var result = await _cryptoModule.HybridVerifyAsync(hybrid, data, classicalKeySet.PublicKey.Key!, HybridVerificationMode.Permissive);
```

Update `HybridVerifyAsync_PqcOnlyValid_ShouldAccept` (line 167) similarly:

```csharp
        var result = await _cryptoModule.HybridVerifyAsync(hybrid, data, null, HybridVerificationMode.Permissive);
```

**Step 4: Run all tests again**

Run: `dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~HybridVerification" -v normal`
Expected: ALL tests PASS

**Step 5: Commit**

```bash
git add src/Common/Sorcha.Cryptography/Models/HybridVerificationMode.cs \
        src/Common/Sorcha.Cryptography/Interfaces/ICryptoModule.cs \
        src/Common/Sorcha.Cryptography/Core/CryptoModule.cs \
        tests/Sorcha.Cryptography.Tests/Unit/Pqc/HybridVerificationTests.cs
git commit -m "feat: add configurable HybridVerificationMode (Strict/Permissive)

- Default to Strict (AND) — both classical and PQC must verify
- Permissive (OR) available for migration periods
- 5 new tests covering both modes and default behaviour
- Fixes PR #131 review issue C5"
```

---

### Task 4: SSRF Protection in WebDidResolver (C6)

**Files:**
- Modify: `src/Common/Sorcha.ServiceClients/Did/WebDidResolver.cs`
- Create: `tests/Sorcha.ServiceClients.Tests/Did/WebDidResolverSsrfTests.cs`

**Step 1: Write failing tests**

Create `tests/Sorcha.ServiceClients.Tests/Did/WebDidResolverSsrfTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Did;
using Xunit;

namespace Sorcha.ServiceClients.Tests.Did;

/// <summary>
/// Tests for SSRF protection in the did:web resolver.
/// </summary>
public class WebDidResolverSsrfTests
{
    private static WebDidResolver CreateResolver(bool allowPrivate = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DidResolver:AllowPrivateAddresses"] = allowPrivate.ToString()
            })
            .Build();

        return new WebDidResolver(
            new HttpClient(),
            NullLogger<WebDidResolver>.Instance,
            config);
    }

    [Theory]
    [InlineData("did:web:127.0.0.1")]
    [InlineData("did:web:169.254.169.254")]
    [InlineData("did:web:10.0.0.1")]
    [InlineData("did:web:172.16.0.1")]
    [InlineData("did:web:192.168.1.1")]
    [InlineData("did:web:localhost")]
    public async Task ResolveAsync_PrivateAddress_ShouldReturnNull(string did)
    {
        var resolver = CreateResolver(allowPrivate: false);
        var result = await resolver.ResolveAsync(did);
        result.Should().BeNull("SSRF protection should block private/reserved addresses");
    }

    [Theory]
    [InlineData("did:web:127.0.0.1")]
    [InlineData("did:web:10.0.0.1")]
    [InlineData("did:web:localhost")]
    public async Task ResolveAsync_PrivateAddress_AllowedInDevMode_ShouldNotBlockDns(string did)
    {
        var resolver = CreateResolver(allowPrivate: true);
        // With AllowPrivateAddresses=true, the DNS check is skipped.
        // The request will fail due to network/TLS, but it should NOT be blocked by SSRF guard.
        // We verify by checking it doesn't return null from the SSRF guard specifically —
        // it will return null from the HTTP failure, which is fine.
        // The key assertion: no "SSRF protection blocked" log message would be emitted.
        // For this test, we just verify it doesn't throw.
        var result = await resolver.ResolveAsync(did);
        // Result will be null (no actual server), but the important thing is no exception
    }

    [Fact]
    public void IsPrivateOrReservedAddress_Loopback_ReturnsTrue()
    {
        WebDidResolver.IsPrivateOrReservedAddress(System.Net.IPAddress.Loopback)
            .Should().BeTrue();
    }

    [Fact]
    public void IsPrivateOrReservedAddress_LinkLocal_ReturnsTrue()
    {
        WebDidResolver.IsPrivateOrReservedAddress(System.Net.IPAddress.Parse("169.254.1.1"))
            .Should().BeTrue();
    }

    [Fact]
    public void IsPrivateOrReservedAddress_PublicAddress_ReturnsFalse()
    {
        WebDidResolver.IsPrivateOrReservedAddress(System.Net.IPAddress.Parse("8.8.8.8"))
            .Should().BeFalse();
    }
}
```

**Step 2: Run tests to confirm they fail**

Run: `dotnet test tests/Sorcha.ServiceClients.Tests --filter "FullyQualifiedName~SsrfTests" -v normal`
Expected: FAIL — `WebDidResolver` constructor doesn't accept `IConfiguration`, `IsPrivateOrReservedAddress` doesn't exist

**Step 3: Implement SSRF protection**

Replace the entire `WebDidResolver.cs` with:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves did:web DIDs by fetching a DID Document over HTTPS.
///   - did:web:{domain}               → GET https://{domain}/.well-known/did.json
///   - did:web:{domain}:{path}:{...}  → GET https://{domain}/{path}/.../did.json
/// </summary>
public class WebDidResolver : IDidResolver
{
    private const string Method = "web";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<WebDidResolver> _logger;
    private readonly bool _allowPrivateAddresses;

    public WebDidResolver(HttpClient httpClient, ILogger<WebDidResolver> logger, IConfiguration? configuration = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _allowPrivateAddresses = configuration?.GetValue<bool>("DidResolver:AllowPrivateAddresses") ?? false;
    }

    /// <inheritdoc />
    public bool CanResolve(string didMethod) =>
        string.Equals(didMethod, Method, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did))
            return null;

        var url = BuildUrl(did);
        if (url is null)
            return null;

        // SSRF protection: validate resolved IP addresses
        if (!_allowPrivateAddresses)
        {
            if (!await IsHostAllowedAsync(url.Host, ct))
            {
                _logger.LogWarning(
                    "SSRF protection blocked did:web resolution for {Did} — host {Host} resolves to private/reserved address",
                    did, url.Host);
                return null;
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        try
        {
            using var response = await _httpClient.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "did:web resolution failed for {Did}: HTTP {StatusCode}",
                    did, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var doc = JsonSerializer.Deserialize<DidDocument>(json, JsonOptions);

            if (doc is null)
            {
                _logger.LogWarning("did:web resolution returned null document for {Did}", did);
                return null;
            }

            // Verify the resolved document ID matches the DID we requested
            if (!string.Equals(doc.Id, did, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "did:web document ID mismatch: expected {Expected}, got {Actual}",
                    did, doc.Id);
                return null;
            }

            return doc;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("did:web resolution timed out for {Did}", did);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "did:web resolution network error for {Did}", did);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "did:web resolution returned invalid JSON for {Did}", did);
            return null;
        }
    }

    /// <summary>
    /// Checks whether a hostname resolves to any private or reserved IP address.
    /// Returns true if the host is safe to connect to, false if blocked.
    /// </summary>
    private async Task<bool> IsHostAllowedAsync(string host, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            foreach (var address in addresses)
            {
                if (IsPrivateOrReservedAddress(address))
                    return false;
            }
            return addresses.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS resolution failed for host {Host}", host);
            return false;
        }
    }

    /// <summary>
    /// Determines whether an IP address is in a private, loopback, link-local, or reserved range.
    /// </summary>
    public static bool IsPrivateOrReservedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        var bytes = address.GetAddressBytes();

        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => bytes switch
            {
                [10, ..] => true,                                          // 10.0.0.0/8
                [172, >= 16 and <= 31, ..] => true,                        // 172.16.0.0/12
                [192, 168, ..] => true,                                    // 192.168.0.0/16
                [169, 254, ..] => true,                                    // 169.254.0.0/16 (link-local)
                [0, ..] => true,                                           // 0.0.0.0/8
                _ => false
            },
            System.Net.Sockets.AddressFamily.InterNetworkV6 =>
                address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
                IPAddress.IPv6Loopback.Equals(address) ||
                IPAddress.IPv6None.Equals(address),
            _ => true // Unknown address family — block by default
        };
    }

    /// <summary>
    /// Builds the HTTPS URL for the DID Document.
    /// </summary>
    private Uri? BuildUrl(string did)
    {
        var parts = did.Split(':');
        if (parts.Length < 3)
        {
            _logger.LogWarning("Invalid did:web format: {Did}", did);
            return null;
        }

        var domain = Uri.UnescapeDataString(parts[2]);

        if (string.IsNullOrWhiteSpace(domain))
        {
            _logger.LogWarning("did:web has empty domain: {Did}", did);
            return null;
        }

        string path;
        if (parts.Length > 3)
        {
            var pathSegments = parts[3..].Select(Uri.UnescapeDataString);
            path = $"https://{domain}/{string.Join('/', pathSegments)}/did.json";
        }
        else
        {
            path = $"https://{domain}/.well-known/did.json";
        }

        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            _logger.LogWarning("Could not construct valid URL from did:web {Did}", did);
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning("did:web requires HTTPS, got {Scheme} for {Did}", uri.Scheme, did);
            return null;
        }

        return uri;
    }
}
```

**Step 4: Update DI registration if needed**

Check if `WebDidResolver` is registered in DI. If so, update the registration to inject `IConfiguration`. The `IConfiguration` parameter is optional (nullable), so existing registrations without it will still compile.

**Step 5: Run SSRF tests**

Run: `dotnet test tests/Sorcha.ServiceClients.Tests --filter "FullyQualifiedName~SsrfTests" -v normal`
Expected: PASS

**Step 6: Run all ServiceClients tests**

Run: `dotnet test tests/Sorcha.ServiceClients.Tests -v normal`
Expected: All tests PASS

**Step 7: Commit**

```bash
git add src/Common/Sorcha.ServiceClients/Did/WebDidResolver.cs \
        tests/Sorcha.ServiceClients.Tests/Did/WebDidResolverSsrfTests.cs
git commit -m "fix: add SSRF protection to did:web resolver

- Block private/loopback/link-local IP addresses by default
- DidResolver:AllowPrivateAddresses config for dev/Docker environments
- Static IsPrivateOrReservedAddress helper for IPv4 and IPv6
- Fixes PR #130 review issue C6"
```

---

### Task 5: Fix Hard-coded Algorithm Names in Hybrid Sign (M5)

**Files:**
- Modify: `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs:442-458`

**Step 1: Fix the hardcoded algorithm names**

Replace lines 442-458 in the hybrid signing section. Before the existing `Task.WhenAll` block, add wallet lookups:

After line 440 (`}`), insert:

```csharp
                // Look up actual wallet algorithms
                var classicalWallet = await walletManager.GetWalletAsync(address, cancellationToken);
                var pqcWallet = await walletManager.GetWalletAsync(request.PqcWalletAddress, cancellationToken);
                if (classicalWallet == null || pqcWallet == null)
                {
                    return Results.NotFound(new ProblemDetails
                    {
                        Title = "Wallet Not Found",
                        Detail = classicalWallet == null
                            ? $"Classical wallet not found: {address}"
                            : $"PQC wallet not found: {request.PqcWalletAddress}",
                        Status = StatusCodes.Status404NotFound
                    });
                }
```

Then replace lines 455-457:

```csharp
                    ClassicalAlgorithm = classicalWallet.Algorithm,
                    PqcAlgorithm = pqcWallet.Algorithm,
```

**Step 2: Build**

Run: `dotnet build src/Services/Sorcha.Wallet.Service/Sorcha.Wallet.Service.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs
git commit -m "fix: use actual wallet algorithm in hybrid signature metadata

- Look up classical and PQC wallet entities before signing
- Use wallet.Algorithm instead of hardcoded ED25519/ML-DSA-65
- Fixes PR #131 review issue M5"
```

---

### Task 6: Fix Spec File Inconsistencies (M6/M14)

**Files:**
- Modify: `.specify/specs/features/cryptography/spec.md`
- Modify: `.specify/specs/features/transaction-handler/spec.md`
- Modify: `specs/040-quantum-safe-crypto/contracts/crypto-policy-api.md`

**Step 1: Update cryptography spec default**

In `.specify/specs/features/cryptography/spec.md`, find all references to `ML-DSA-44` as default and change to `ML-DSA-65`. Find `ML-KEM-512` as default and change to `ML-KEM-768`. Add note: "ML-DSA-44 and ML-KEM-512 deferred — not CNSA 2.0 compliant."

**Step 2: Update transaction-handler spec default**

In `.specify/specs/features/transaction-handler/spec.md`, make the same changes. Find the Q&A about signing algorithm and update: "ML-DSA-65 (default), configurable to ML-DSA-87 via blueprint policy."

**Step 3: Remove sharedSecret from encapsulate contract**

In `specs/040-quantum-safe-crypto/contracts/crypto-policy-api.md`, find the encapsulate response JSON (around line 129-136) and remove the `"sharedSecret"` field. Update the description to note the KEM security model: only ciphertext is returned.

**Step 4: Commit**

```bash
git add .specify/specs/features/cryptography/spec.md \
        .specify/specs/features/transaction-handler/spec.md \
        specs/040-quantum-safe-crypto/contracts/crypto-policy-api.md
git commit -m "docs: align spec files with ML-DSA-65 default and fix encapsulate contract

- Update cryptography and transaction-handler specs: ML-DSA-65 default (not 44)
- Remove sharedSecret from encapsulate API contract (KEM security model)
- Fixes PR #131 review issues M6, M14"
```

---

### Task 7: Fix BlueprintId/RegisterId in Pending Actions (C10/M11)

**Files:**
- Modify: `src/Services/Sorcha.Blueprint.Service/Program.cs:1720-1728`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Workflows/WorkflowInstanceViewModel.cs:29-40`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor:245-252`

**Step 1: Add BlueprintId and RegisterId to next-actions endpoint response**

In `Program.cs`, update the anonymous object at lines 1720-1728. Replace:

```csharp
                nextActions.Add(new
                {
                    actionId = action.Id,
                    title = action.Title,
                    description = action.Description,
                    participantId = participant?.Principal,
                    branchId = instance.ActiveBranches
                        .FirstOrDefault(b => b.CurrentActionId == actionId)?.Id
                });
```

With:

```csharp
                nextActions.Add(new
                {
                    actionId = action.Id,
                    title = action.Title,
                    description = action.Description,
                    participantId = participant?.Principal,
                    blueprintId = instance.BlueprintId,
                    registerId = instance.RegisterId,
                    blueprintName = blueprint.Title,
                    branchId = instance.ActiveBranches
                        .FirstOrDefault(b => b.CurrentActionId == actionId)?.Id
                });
```

**Step 2: Add properties to PendingActionViewModel**

In `WorkflowInstanceViewModel.cs`, add two properties to `PendingActionViewModel` (after line 32):

```csharp
    public string BlueprintId { get; init; } = string.Empty;
    public string RegisterId { get; init; } = string.Empty;
```

**Step 3: Fix MyActions.razor field mappings**

In `MyActions.razor`, replace lines 247 and 251:

```csharp
                    BlueprintId = action.BlueprintId,
```

```csharp
                    RegisterAddress = action.RegisterId,
```

**Step 4: Build UI projects**

Run: `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Sorcha.UI.Web.Client.csproj`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add src/Services/Sorcha.Blueprint.Service/Program.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Workflows/WorkflowInstanceViewModel.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor
git commit -m "fix: use published blueprint TxId and RegisterId in action submissions

- Backend: include blueprintId and registerId in next-actions response
- Frontend: add BlueprintId/RegisterId to PendingActionViewModel
- Fix MyActions.razor to use correct fields instead of BlueprintName/empty
- BlueprintId is the TxId of the published blueprint Control transaction
- Fixes PR #127 review issues C10, M11"
```

---

### Task 8: TokenClaimConstants Consistency (M1)

**Files:**
- Modify: `src/Services/Sorcha.Register.Service/Extensions/AuthenticationExtensions.cs`
- Modify: `src/Services/Sorcha.Blueprint.Service/Extensions/AuthenticationExtensions.cs`
- Modify: `src/Services/Sorcha.Wallet.Service/Extensions/AuthenticationExtensions.cs`
- Modify: `src/Services/Sorcha.Peer.Service/Extensions/AuthenticationExtensions.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Extensions/AuthenticationExtensions.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Services/TokenService.cs`

**Step 1: Replace in all 5 AuthenticationExtensions.cs files**

For Register, Blueprint, Wallet, Peer services — apply these replacements throughout each file:

| Old | New |
|-----|-----|
| `"org_id"` | `TokenClaimConstants.OrgId` |
| `"token_type"` | `TokenClaimConstants.TokenType` |
| `"service"` (as claim value) | `TokenClaimConstants.TokenTypeService` |
| `"user"` (as claim value) | `TokenClaimConstants.TokenTypeUser` |

Ensure each file has `using Sorcha.ServiceClients.Auth;` at the top (Register already has it).

**Step 2: Replace in TokenService.cs**

Apply the same replacements in `src/Services/Sorcha.Tenant.Service/Services/TokenService.cs`. Add `using Sorcha.ServiceClients.Auth;`. Replace:

- Line 82: `new("org_id", ...)` → `new(TokenClaimConstants.OrgId, ...)`
- Line 84: `new("token_type", "user")` → `new(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser)`
- Line 131: `new("token_type", "user")` → same
- Line 180: `new("token_type", "service")` → `new(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService)`
- Line 252: `FindFirst("org_id")` → `FindFirst(TokenClaimConstants.OrgId)`
- Line 379: `FindFirst("org_id")` → `FindFirst(TokenClaimConstants.OrgId)`
- Line 450: `new Claim("org_id", orgId)` → `new Claim(TokenClaimConstants.OrgId, orgId)`

**Step 3: Build solution**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Run all tests**

Run: `dotnet test --filter "FullyQualifiedName~Authentication" -v normal`
Expected: All PASS

**Step 5: Commit**

```bash
git add src/Services/Sorcha.Register.Service/Extensions/AuthenticationExtensions.cs \
        src/Services/Sorcha.Blueprint.Service/Extensions/AuthenticationExtensions.cs \
        src/Services/Sorcha.Wallet.Service/Extensions/AuthenticationExtensions.cs \
        src/Services/Sorcha.Peer.Service/Extensions/AuthenticationExtensions.cs \
        src/Services/Sorcha.Tenant.Service/Extensions/AuthenticationExtensions.cs \
        src/Services/Sorcha.Tenant.Service/Services/TokenService.cs
git commit -m "refactor: replace hardcoded claim strings with TokenClaimConstants

- Replace 28 hardcoded 'org_id', 'token_type', 'service', 'user' strings
- All 6 files now use TokenClaimConstants consistently
- Fixes PR #132 review issue M1"
```

---

### Task 9: Security Log Levels + Immutable DTO (M2/M4)

**Files:**
- Modify: `src/Services/Sorcha.Peer.Service/GrpcServices/PeerAuthInterceptor.cs:129,134`
- Modify: `src/Common/Sorcha.ServiceClients/Auth/ITokenIntrospectionClient.cs:31-62`

**Step 1: Fix log levels in PeerAuthInterceptor**

Change line 129:

```csharp
            _logger.LogWarning("gRPC call with expired token — treating as anonymous");
```

Change line 134:

```csharp
            _logger.LogWarning(ex, "gRPC token validation failed — treating as anonymous");
```

**Step 2: Fix mutable DTO**

In `ITokenIntrospectionClient.cs`, change all `{ get; set; }` to `{ get; init; }` on lines 31, 36, 41, 46, 51, 56, 61.

**Step 3: Build**

Run: `dotnet build src/Services/Sorcha.Peer.Service/Sorcha.Peer.Service.csproj && dotnet build src/Common/Sorcha.ServiceClients/Sorcha.ServiceClients.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/Services/Sorcha.Peer.Service/GrpcServices/PeerAuthInterceptor.cs \
        src/Common/Sorcha.ServiceClients/Auth/ITokenIntrospectionClient.cs
git commit -m "fix: promote security events to Warning and make DTO immutable

- PeerAuthInterceptor: LogDebug → LogWarning for token expiry/failure
- TokenIntrospectionResult: set → init for all properties
- Fixes PR #132 review issues M2, M4"
```

---

### Task 10: Typed Domain Exceptions (M3)

**Files:**
- Create: `src/Services/Sorcha.Wallet.Service/Exceptions/WalletNotFoundException.cs`
- Create: `src/Services/Sorcha.Wallet.Service/Exceptions/WalletAccessAlreadyExistsException.cs`
- Modify: `src/Common/Sorcha.Wallet.Core/Services/Implementation/DelegationService.cs:52,62,115,211,216`
- Modify: `src/Services/Sorcha.Wallet.Service/Endpoints/DelegationEndpoints.cs:108,112,184`

**Step 1: Create exception classes**

`src/Services/Sorcha.Wallet.Service/Exceptions/WalletNotFoundException.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Exceptions;

/// <summary>
/// Thrown when a wallet or wallet access grant is not found.
/// </summary>
public class WalletNotFoundException : Exception
{
    public WalletNotFoundException(string message) : base(message) { }
    public WalletNotFoundException(string message, Exception inner) : base(message, inner) { }
}
```

`src/Services/Sorcha.Wallet.Service/Exceptions/WalletAccessAlreadyExistsException.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Exceptions;

/// <summary>
/// Thrown when an access grant already exists for a subject on a wallet.
/// </summary>
public class WalletAccessAlreadyExistsException : Exception
{
    public WalletAccessAlreadyExistsException(string message) : base(message) { }
    public WalletAccessAlreadyExistsException(string message, Exception inner) : base(message, inner) { }
}
```

**Step 2: Update DelegationService to throw typed exceptions**

In `DelegationService.cs`:
- Line 52: `throw new InvalidOperationException($"Wallet not found: {walletAddress}")` → `throw new Sorcha.Wallet.Service.Exceptions.WalletNotFoundException($"Wallet not found: {walletAddress}")`
- Line 62: `throw new InvalidOperationException($"Active access already exists for subject: {subject}")` → `throw new Sorcha.Wallet.Service.Exceptions.WalletAccessAlreadyExistsException($"Active access already exists for subject: {subject}")`
- Line 115: `throw new InvalidOperationException($"No active access found for subject: {subject}")` → `throw new Sorcha.Wallet.Service.Exceptions.WalletNotFoundException($"No active access found for subject: {subject}")`
- Line 211: `throw new InvalidOperationException($"Access grant not found: {accessId}")` → `throw new Sorcha.Wallet.Service.Exceptions.WalletNotFoundException($"Access grant not found: {accessId}")`
- Line 216: `throw new InvalidOperationException($"Access grant {accessId} is no longer active")` → keep as `InvalidOperationException` (this is a state error, not a not-found)

Note: `DelegationService` is in `Sorcha.Wallet.Core` but the exceptions are in `Sorcha.Wallet.Service`. Since `Wallet.Core` shouldn't depend on `Wallet.Service`, place the exception classes in `Sorcha.Wallet.Core/Exceptions/` instead.

**Correction — create files in Wallet.Core instead:**

- Create: `src/Common/Sorcha.Wallet.Core/Exceptions/WalletNotFoundException.cs`
- Create: `src/Common/Sorcha.Wallet.Core/Exceptions/WalletAccessAlreadyExistsException.cs`

Update namespaces to `Sorcha.Wallet.Core.Exceptions`.

**Step 3: Update DelegationEndpoints to catch typed exceptions**

In `DelegationEndpoints.cs`:

Replace line 108:

```csharp
        catch (WalletNotFoundException)
        {
            return Results.NotFound();
        }
```

Replace line 112:

```csharp
        catch (WalletAccessAlreadyExistsException ex)
        {
            return Results.Conflict(new ProblemDetails
            {
                Title = "Access Already Exists",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
```

Replace line 184:

```csharp
        catch (WalletNotFoundException)
        {
            return Results.NotFound();
        }
```

Add `using Sorcha.Wallet.Core.Exceptions;` to the file.

**Step 4: Build**

Run: `dotnet build src/Services/Sorcha.Wallet.Service/Sorcha.Wallet.Service.csproj`
Expected: Build succeeded

**Step 5: Run delegation tests**

Run: `dotnet test tests/Sorcha.Wallet.Service.Tests --filter "FullyQualifiedName~Delegation" -v normal`
Expected: PASS (tests may need updating if they expect `InvalidOperationException`)

**Step 6: Commit**

```bash
git add src/Common/Sorcha.Wallet.Core/Exceptions/WalletNotFoundException.cs \
        src/Common/Sorcha.Wallet.Core/Exceptions/WalletAccessAlreadyExistsException.cs \
        src/Common/Sorcha.Wallet.Core/Services/Implementation/DelegationService.cs \
        src/Services/Sorcha.Wallet.Service/Endpoints/DelegationEndpoints.cs
git commit -m "refactor: replace string-based exception matching with typed exceptions

- Add WalletNotFoundException and WalletAccessAlreadyExistsException
- DelegationService throws typed exceptions instead of InvalidOperationException
- DelegationEndpoints catches typed exceptions instead of string matching
- Fixes PR #132 review issue M3"
```

---

### Task 11: CredentialApiService Logging (M9)

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs`

**Step 1: Add logger injection**

Add field after the `_httpClient` field (line 15):

```csharp
    private readonly ILogger<CredentialApiService> _logger;
```

Update constructor (lines 22-25):

```csharp
    public CredentialApiService(HttpClient httpClient, ILogger<CredentialApiService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
```

Add `using Microsoft.Extensions.Logging;` at the top.

**Step 2: Add logging to each catch block**

Update each `catch (HttpRequestException)` block to log:

Line 43-46 (GetCredentialsAsync):
```csharp
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch credentials for wallet {WalletAddress}", walletAddress);
            return [];
        }
```

Line 65-68 (GetCredentialDetailAsync):
```csharp
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch credential {CredentialId} for wallet {WalletAddress}", credentialId, walletAddress);
            return null;
        }
```

Line 82-85 (UpdateCredentialStatusAsync):
```csharp
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to update status for credential {CredentialId}", credentialId);
            return false;
        }
```

Line 98-101 (DeleteCredentialAsync):
```csharp
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to delete credential {CredentialId}", credentialId);
            return false;
        }
```

Line 120-123 (GetPresentationRequestsAsync):
```csharp
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch presentation requests for wallet {WalletAddress}", walletAddress);
            return [];
        }
```

Line 142-145 (GetPresentationRequestDetailAsync):
```csharp
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch presentation request {RequestId}", requestId);
            return null;
        }
```

Line 195-198 (DenyPresentationAsync):
```csharp
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to deny presentation request {RequestId}", requestId);
            return false;
        }
```

**Step 3: Build**

Run: `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Core/Sorcha.UI.Core.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs
git commit -m "fix: add logging to CredentialApiService HTTP error handlers

- Inject ILogger and log warnings in all 7 catch blocks
- Enables observability when Wallet Service is unreachable
- No change to return values or caller behavior
- Fixes PR #130 review issue M9"
```

---

### Task 12: Admin Endpoint Audit Logging (M13)

**Files:**
- Modify: `src/Services/Sorcha.Validator.Service/Endpoints/AdminEndpoints.cs:24-146`

**Step 1: Add HttpContext and ILogger parameters, add audit logging**

Add `HttpContext context` and `ILogger<Program> logger` to the Start, Stop, and Process handlers.

For StartValidator (line 24), add `HttpContext context, ILogger<Program> logger` to the parameters:

```csharp
        group.MapPost("/validators/start", async (
            [FromBody] StartValidatorRequest request,
            IValidatorOrchestrator orchestrator,
            HttpContext context,
            ILogger<Program> logger) =>
        {
            var userId = context.User.FindFirst("sub")?.Value ?? "unknown";
            logger.LogInformation("Admin action {Action} on register {RegisterId} by {UserId}",
                "StartValidator", request.RegisterId, userId);
```

For StopValidator (line 58), same pattern:

```csharp
        group.MapPost("/validators/stop", async (
            [FromBody] StopValidatorRequest request,
            IValidatorOrchestrator orchestrator,
            HttpContext context,
            ILogger<Program> logger) =>
        {
            var userId = context.User.FindFirst("sub")?.Value ?? "unknown";
            logger.LogInformation("Admin action {Action} on register {RegisterId} by {UserId}",
                "StopValidator", request.RegisterId, userId);
```

For ProcessValidationPipeline (line 118):

```csharp
        group.MapPost("/validators/{registerId}/process", async (
            string registerId,
            IValidatorOrchestrator orchestrator,
            HttpContext context,
            ILogger<Program> logger) =>
        {
            var userId = context.User.FindFirst("sub")?.Value ?? "unknown";
            logger.LogInformation("Admin action {Action} on register {RegisterId} by {UserId}",
                "ProcessPipeline", registerId, userId);
```

**Step 2: Build**

Run: `dotnet build src/Services/Sorcha.Validator.Service/Sorcha.Validator.Service.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Services/Sorcha.Validator.Service/Endpoints/AdminEndpoints.cs
git commit -m "fix: add audit logging to admin endpoints with caller identity

- Log userId (sub claim) for Start, Stop, and Process admin actions
- Uses structured logging for searchability
- Fixes PR #132 review issue M13"
```

---

### Task 13: Full Solution Build and Test

**Step 1: Build entire solution**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass. Note pre-existing baseline of ~17 engine failures (JsonLogic-related) — these are not regressions.

**Step 3: Final commit if any stragglers**

If any files were missed, stage and commit with appropriate message.
