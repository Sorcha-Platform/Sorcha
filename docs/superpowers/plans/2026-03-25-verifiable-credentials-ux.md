# Verifiable Credentials UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the full VC lifecycle UX — issuance summary, holder acceptance, selective disclosure presentation, verifier trust view, and admin credential management.

**Architecture:** Enhances existing credential UI components (CredentialCard, MyCredentials page, PresentationRequestDialog) with new features. Creates new components for issuance summary, disclosure picker, verification trust view, and admin grid. Integrates credential notifications into existing SignalR hub connections.

**Tech Stack:** Blazor WASM, MudBlazor, SignalR, C# 13, existing `ICredentialApiService` + `IPresentationAdminService`

**Spec:** `docs/superpowers/specs/2026-03-25-verifiable-credentials-ux-design.md`

---

## File Map

### New Files
| File | Responsibility |
|------|---------------|
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/IssuanceSummaryPanel.razor` | Post-action issuance awareness panel (modal) |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialAcceptCard.razor` | Pending credential with Accept/Decline + disclosure indicators |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/DisclosurePicker.razor` | Selective disclosure toggle UI for presentations |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/VerificationTrustView.razor` | Contextual verification display (green/amber/red) |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/IssuedCredentialsGrid.razor` | Admin data grid for org-issued credentials |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/CredentialStatusDialog.razor` | Suspend/revoke/reinstate confirmation with reason |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/IssuedCredentials.razor` | Admin page at `/admin/credentials` |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/IssuanceSummaryViewModel.cs` | VM for issuance summary panel |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/DisclosureClaimViewModel.cs` | VM for disclosure picker claims |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/VerificationResultViewModel.cs` | VM for trust view with escalation level |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/IssuedCredentialListItem.cs` | VM for admin grid rows |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/CredentialNotification.cs` | Notification model for SignalR credential events |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/IIssuedCredentialService.cs` | Service interface for admin issued-credential queries |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/IssuedCredentialService.cs` | HttpClient implementation for issued credential admin API |
| `tests/Sorcha.UI.Core.Tests/Components/Credentials/CredentialAcceptCardTests.cs` | Unit tests for accept/decline logic |
| `tests/Sorcha.UI.Core.Tests/Components/Credentials/DisclosurePickerTests.cs` | Unit tests for disclosure toggle logic |
| `tests/Sorcha.UI.Core.Tests/Components/Credentials/VerificationTrustViewTests.cs` | Unit tests for escalation rules |
| `tests/Sorcha.UI.Core.Tests/Models/Credentials/DisclosureClaimViewModelTests.cs` | Unit tests for claim categorisation |
| `tests/Sorcha.UI.Core.Tests/Models/Credentials/VerificationResultViewModelTests.cs` | Unit tests for green/amber/red logic |

### Modified Files
| File | Change |
|------|--------|
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor` | Add Pending/Active/Expired/Revoked tabs, acceptance flow, notification badge |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialCard.razor` | Add 🔒/🔓 disclosure indicators to claims display |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/PresentationRequestDialog.razor` | Replace with DisclosurePicker, add request-matched auto-select |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` | Add "Issued Credentials" admin nav item, credential notification badge |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/ICredentialApiService.cs` | Add `AcceptCredentialAsync`, `DeclineCredentialAsync`, `GetPendingCredentialsAsync` |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs` | Implement new methods |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs` | Register `IIssuedCredentialService` |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ActionsHubConnection.cs` | Add credential notification event handlers |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/CredentialCardViewModel.cs` | Add `DisclosableClaims` list, `IsPending` flag |

---

## Task Breakdown

### Task 1: Credential ViewModels

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/IssuanceSummaryViewModel.cs`
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/DisclosureClaimViewModel.cs`
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/VerificationResultViewModel.cs`
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/IssuedCredentialListItem.cs`
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/CredentialNotification.cs`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/CredentialCardViewModel.cs`
- Create: `tests/Sorcha.UI.Core.Tests/Models/Credentials/DisclosureClaimViewModelTests.cs`
- Create: `tests/Sorcha.UI.Core.Tests/Models/Credentials/VerificationResultViewModelTests.cs`

- [ ] **Step 1: Write tests for DisclosureClaimViewModel**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Tests.Models.Credentials;

using FluentAssertions;
using Sorcha.UI.Core.Models.Credentials;

public class DisclosureClaimViewModelTests
{
    [Fact]
    public void CategoriseClaims_RequiredClaims_MarkedAsRequired()
    {
        var requestedClaims = new[] { "permitType", "siteAddress" };
        var allClaims = new Dictionary<string, object>
        {
            ["permitType"] = "Commercial Build",
            ["siteAddress"] = "42 High Street",
            ["riskLevel"] = "Low"
        };
        var disclosable = new[] { "riskLevel" };

        var result = DisclosureClaimViewModel.CategoriseClaims(allClaims, requestedClaims, disclosable);

        result.Should().HaveCount(3);
        result.First(c => c.ClaimName == "permitType").Category.Should().Be(DisclosureCategory.Required);
        result.First(c => c.ClaimName == "siteAddress").Category.Should().Be(DisclosureCategory.Required);
    }

    [Fact]
    public void CategoriseClaims_DisclosableNotRequested_MarkedAsNotRequested()
    {
        var requestedClaims = new[] { "permitType" };
        var allClaims = new Dictionary<string, object>
        {
            ["permitType"] = "Commercial Build",
            ["riskLevel"] = "Low"
        };
        var disclosable = new[] { "riskLevel" };

        var result = DisclosureClaimViewModel.CategoriseClaims(allClaims, requestedClaims, disclosable);

        result.First(c => c.ClaimName == "riskLevel").Category.Should().Be(DisclosureCategory.NotRequested);
    }

    [Fact]
    public void CategoriseClaims_DisclosableAndRequested_MarkedAsOptional()
    {
        var requestedClaims = new[] { "permitType", "riskLevel" };
        var allClaims = new Dictionary<string, object>
        {
            ["permitType"] = "Commercial Build",
            ["riskLevel"] = "Low"
        };
        var disclosable = new[] { "riskLevel" };

        var result = DisclosureClaimViewModel.CategoriseClaims(allClaims, requestedClaims, disclosable);

        // riskLevel is disclosable AND requested = Optional (holder can toggle)
        // Actually — if the verifier requires it, it's Required even if disclosable
        // Only claims that are disclosable and NOT in requiredClaims are Optional
        result.First(c => c.ClaimName == "riskLevel").Category.Should().Be(DisclosureCategory.Required);
    }

    [Fact]
    public void CategoriseClaims_DisclosableNotInRequired_MarkedAsOptional()
    {
        var requiredClaims = new[] { "permitType" };
        var optionalClaims = new[] { "riskLevel" };
        var allClaims = new Dictionary<string, object>
        {
            ["permitType"] = "Commercial Build",
            ["riskLevel"] = "Low",
            ["assessedValue"] = "450000"
        };
        var disclosable = new[] { "riskLevel", "assessedValue" };

        var result = DisclosureClaimViewModel.CategoriseClaims(
            allClaims, requiredClaims, disclosable, optionalClaims);

        result.First(c => c.ClaimName == "riskLevel").Category.Should().Be(DisclosureCategory.Optional);
        result.First(c => c.ClaimName == "riskLevel").IsSharing.Should().BeFalse();
    }

    [Fact]
    public void OptionalClaims_DefaultToNotSharing()
    {
        var requiredClaims = new[] { "permitType" };
        var optionalClaims = new[] { "riskLevel" };
        var allClaims = new Dictionary<string, object>
        {
            ["permitType"] = "Commercial Build",
            ["riskLevel"] = "Low"
        };
        var disclosable = new[] { "riskLevel" };

        var result = DisclosureClaimViewModel.CategoriseClaims(
            allClaims, requiredClaims, disclosable, optionalClaims);

        result.Where(c => c.Category == DisclosureCategory.Optional)
              .Should().AllSatisfy(c => c.IsSharing.Should().BeFalse());
    }
}
```

- [ ] **Step 2: Write tests for VerificationResultViewModel**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Tests.Models.Credentials;

using FluentAssertions;
using Sorcha.UI.Core.Models.Credentials;

public class VerificationResultViewModelTests
{
    [Fact]
    public void EscalationLevel_AllChecksPassed_ReturnsGreen()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = []
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Green);
    }

    [Fact]
    public void EscalationLevel_FailOpenWarning_ReturnsAmber()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = [new VerificationWarning("RevocationCheckUnavailable", "FailOpen policy applied")]
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Amber);
    }

    [Fact]
    public void EscalationLevel_CredentialExpired_ReturnsRed()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = true,
            NotExpired = false,
            RequiredClaimsPresent = true,
            Warnings = []
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    [Fact]
    public void EscalationLevel_CredentialRevoked_ReturnsRed()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = false,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = []
        };

        vm.EscalationLevel.Should().Be(TrustEscalation.Red);
    }

    [Fact]
    public void PassedCheckCount_ReturnsCorrectCount()
    {
        var vm = new VerificationResultViewModel
        {
            SignatureValid = true,
            IssuerTrusted = true,
            NotRevoked = false,
            NotExpired = true,
            RequiredClaimsPresent = true,
            Warnings = []
        };

        vm.PassedCheckCount.Should().Be(4);
        vm.TotalCheckCount.Should().Be(5);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/ --filter "FullyQualifiedName~DisclosureClaimViewModelTests|FullyQualifiedName~VerificationResultViewModelTests" -v m`
Expected: FAIL — types do not exist yet

- [ ] **Step 4: Implement IssuanceSummaryViewModel**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

public class IssuanceSummaryViewModel
{
    public required string CredentialType { get; init; }
    public required string IssuedToDid { get; init; }
    public required string IssuedToName { get; init; }
    public required string SignedByOrg { get; init; }
    public required string ProcessedByName { get; init; }
    public required string ProcessedByRole { get; init; }
    public required int TotalClaims { get; init; }
    public required int DisclosableClaims { get; init; }
    public required string UsagePolicy { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string BlueprintName { get; init; }
    public required string ActionName { get; init; }
}
```

- [ ] **Step 5: Implement DisclosureClaimViewModel**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

public enum DisclosureCategory
{
    Required,
    Optional,
    NotRequested
}

public class DisclosureClaimViewModel
{
    public required string ClaimName { get; init; }
    public required object ClaimValue { get; init; }
    public required DisclosureCategory Category { get; init; }
    public bool IsSharing { get; set; }

    public static List<DisclosureClaimViewModel> CategoriseClaims(
        Dictionary<string, object> allClaims,
        IEnumerable<string> requiredClaims,
        IEnumerable<string>? disclosable,
        IEnumerable<string>? optionalClaims = null)
    {
        var requiredSet = new HashSet<string>(requiredClaims, StringComparer.OrdinalIgnoreCase);
        var disclosableSet = new HashSet<string>(disclosable ?? [], StringComparer.OrdinalIgnoreCase);
        var optionalSet = new HashSet<string>(optionalClaims ?? [], StringComparer.OrdinalIgnoreCase);

        return allClaims.Select(kvp =>
        {
            var category = requiredSet.Contains(kvp.Key)
                ? DisclosureCategory.Required
                : optionalSet.Contains(kvp.Key)
                    ? DisclosureCategory.Optional
                    : disclosableSet.Contains(kvp.Key) && !requiredSet.Contains(kvp.Key)
                        ? DisclosureCategory.NotRequested
                        : DisclosureCategory.NotRequested;

            return new DisclosureClaimViewModel
            {
                ClaimName = kvp.Key,
                ClaimValue = kvp.Value,
                Category = category,
                IsSharing = category == DisclosureCategory.Required
            };
        }).OrderBy(c => c.Category).ToList();
    }
}
```

- [ ] **Step 6: Implement VerificationResultViewModel**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

public enum TrustEscalation
{
    Green,
    Amber,
    Red
}

public record VerificationWarning(string Code, string Message);

public class VerificationResultViewModel
{
    public required bool SignatureValid { get; init; }
    public required bool IssuerTrusted { get; init; }
    public required bool NotRevoked { get; init; }
    public required bool NotExpired { get; init; }
    public required bool RequiredClaimsPresent { get; init; }
    public required List<VerificationWarning> Warnings { get; init; }

    public string? IssuerName { get; init; }
    public string? CredentialType { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int RequiredClaimCount { get; init; }
    public int PresentClaimCount { get; init; }
    public Dictionary<string, object>? DisclosedClaims { get; init; }

    public TrustEscalation EscalationLevel =>
        !SignatureValid || !IssuerTrusted || !NotRevoked || !NotExpired || !RequiredClaimsPresent
            ? TrustEscalation.Red
            : Warnings.Count > 0
                ? TrustEscalation.Amber
                : TrustEscalation.Green;

    public int PassedCheckCount =>
        (SignatureValid ? 1 : 0) + (IssuerTrusted ? 1 : 0) +
        (NotRevoked ? 1 : 0) + (NotExpired ? 1 : 0) +
        (RequiredClaimsPresent ? 1 : 0);

    public int TotalCheckCount => 5;
}
```

- [ ] **Step 7: Implement IssuedCredentialListItem and CredentialNotification**

```csharp
// IssuedCredentialListItem.cs
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

public class IssuedCredentialListItem
{
    public required string CredentialId { get; init; }
    public required string Type { get; init; }
    public required string IssuedToName { get; init; }
    public required string IssuedToDid { get; init; }
    public string? BlueprintName { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string Status { get; init; }
    public required int PresentationCount { get; init; }

    public bool IsExpiringSoon => ExpiresAt.HasValue &&
        ExpiresAt.Value < DateTimeOffset.UtcNow.AddDays(30) &&
        ExpiresAt.Value > DateTimeOffset.UtcNow;

    public bool IsExpired => ExpiresAt.HasValue &&
        ExpiresAt.Value <= DateTimeOffset.UtcNow;
}
```

```csharp
// CredentialNotification.cs
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

public class CredentialNotification
{
    public required string Type { get; init; } // Issued, Accepted, Declined, Suspended, Revoked, PresentationRequested
    public required string CredentialId { get; init; }
    public required string CredentialType { get; init; }
    public string? IssuerName { get; init; }
    public string? RecipientName { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? Message { get; init; }
}
```

- [ ] **Step 8: Update CredentialCardViewModel with pending/disclosure fields**

Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/CredentialCardViewModel.cs`

Add to `CredentialCardViewModel`:
```csharp
public List<string> DisclosableClaims { get; init; } = [];
public bool IsPending { get; init; }
public string? OriginatingBlueprintName { get; init; }
public string? IssuerOrgName { get; init; }
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/ --filter "FullyQualifiedName~DisclosureClaimViewModelTests|FullyQualifiedName~VerificationResultViewModelTests" -v m`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/ tests/Sorcha.UI.Core.Tests/Models/Credentials/
git commit -m "feat: add VC lifecycle view models with disclosure categorisation and trust escalation"
```

---

### Task 2: Credential Accept Card Component

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialAcceptCard.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialCard.razor` — add disclosure indicators

- [ ] **Step 1: Read existing CredentialCard.razor to understand structure**

Read: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialCard.razor`

- [ ] **Step 2: Add disclosure indicators to CredentialCard.razor**

Add 🔒/🔓 icons next to claims in the claims preview section. Claims in `DisclosableClaims` get 🔓, others get 🔒. Only show indicators when `DisclosableClaims` is non-empty.

- [ ] **Step 3: Create CredentialAcceptCard.razor**

Material-style card component for pending credentials. Parameters:
- `CredentialCardViewModel Credential`
- `EventCallback OnAccept`
- `EventCallback OnDecline`
- `bool IsLoading`

Layout per mockup:
- Orange left border (`border-left: 4px solid var(--mud-palette-warning)`)
- Icon + type + issuer + blueprint name header
- Claims grid with 🔒/🔓 indicators
- Metadata row: issued, expires, usage policy, SD-JWT details
- Accept/Decline `MudButton` pair

Uses `@skill blazor` patterns: `MudCard`, `MudCardContent`, `MudGrid`, `MudChip`, `MudButton`.

- [ ] **Step 4: Run build to verify component compiles**

Run: `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Core/`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialAcceptCard.razor src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialCard.razor
git commit -m "feat: add CredentialAcceptCard with disclosure indicators"
```

---

### Task 3: Issuance Summary Panel

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/IssuanceSummaryPanel.razor`

- [ ] **Step 1: Create IssuanceSummaryPanel.razor**

MudBlazor dialog component. Parameters:
- `IssuanceSummaryViewModel Summary`

Layout per design:
- `MudDialog` with title "Credential Issued"
- Structured table: credential type, issued to, signed by, processed by, claims count, usage policy, expiry
- "Done" `MudButton` to close (`MudDialog.Close()`)
- Informational only — not a blocking gate

- [ ] **Step 2: Run build**

Run: `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Core/`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/IssuanceSummaryPanel.razor
git commit -m "feat: add IssuanceSummaryPanel for post-action issuance awareness"
```

---

### Task 4: Disclosure Picker Component

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/DisclosurePicker.razor`
- Create: `tests/Sorcha.UI.Core.Tests/Components/Credentials/DisclosurePickerTests.cs`

- [ ] **Step 1: Write tests for DisclosurePicker logic**

Test that:
- Required claims are always included in disclosed output
- Optional claims toggled ON are included
- Optional claims toggled OFF are excluded
- NotRequested claims are never included
- Summary text updates correctly ("Sharing 3 of 4 claims")

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/ --filter "FullyQualifiedName~DisclosurePickerTests" -v m`
Expected: FAIL

- [ ] **Step 3: Create DisclosurePicker.razor**

Parameters:
- `string VerifierIdentity`
- `string BlueprintContext`
- `CredentialCardViewModel MatchedCredential`
- `List<DisclosureClaimViewModel> Claims`
- `string? UsageWarning`
- `EventCallback<List<string>> OnPresent` — returns list of disclosed claim names
- `EventCallback OnDeny`

Layout per mockup (max-width 540px):
- Header: verifier identity + blueprint context
- Matched credential compact card with "Matched" badge
- Three sections with section headers:
  - "Required by verifier" — blue checkmark, locked
  - "Optional — you choose" — `MudSwitch` toggles, default OFF
  - "Not requested" — greyed out, dash icon
- Usage warning `MudAlert` if applicable
- Summary bar: "Sharing N of M claims" + Present/Deny buttons

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/ --filter "FullyQualifiedName~DisclosurePickerTests" -v m`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/DisclosurePicker.razor tests/Sorcha.UI.Core.Tests/Components/Credentials/DisclosurePickerTests.cs
git commit -m "feat: add DisclosurePicker with request-matched auto-select"
```

---

### Task 5: Verification Trust View Component

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/VerificationTrustView.razor`
- Create: `tests/Sorcha.UI.Core.Tests/Components/Credentials/VerificationTrustViewTests.cs`

- [ ] **Step 1: Write tests for VerificationTrustView**

Test rendering for each escalation level:
- Green: banner shows "Verified credential", details collapsed
- Amber: banner shows "Verification Warning", checklist auto-expanded
- Red: banner shows "Verification Failed", checklist expanded

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/ --filter "FullyQualifiedName~VerificationTrustViewTests" -v m`
Expected: FAIL

- [ ] **Step 3: Create VerificationTrustView.razor**

Parameters:
- `VerificationResultViewModel Result`

Layout per design:
- Banner: `MudAlert` with colour based on `EscalationLevel` (Green=Success, Amber=Warning, Red=Error)
- Verification checklist: 5 rows with ✓/⚠/✗ icons + descriptions
  - Collapsed by default for Green (use `MudExpansionPanel`)
  - Auto-expanded for Amber and Red
- Warning rows highlighted with amber background
- Failure rows highlighted with red background
- Disclosed claims grid below (read-only key-value pairs)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/ --filter "FullyQualifiedName~VerificationTrustViewTests" -v m`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/VerificationTrustView.razor tests/Sorcha.UI.Core.Tests/Components/Credentials/VerificationTrustViewTests.cs
git commit -m "feat: add VerificationTrustView with contextual depth escalation"
```

---

### Task 6: Enhance MyCredentials Page

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/ICredentialApiService.cs`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs`

- [ ] **Step 1: Read existing MyCredentials.razor**

Read: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor`

- [ ] **Step 2: Add pending credential methods to ICredentialApiService**

Add to interface:
```csharp
Task<List<CredentialCardViewModel>> GetPendingCredentialsAsync(string walletAddress);
Task<bool> AcceptCredentialAsync(string walletAddress, string credentialId);
Task<bool> DeclineCredentialAsync(string walletAddress, string credentialId);
```

- [ ] **Step 3: Implement new methods in CredentialApiService**

Read existing implementation first, then add:
- `GetPendingCredentialsAsync` — GET `/api/v1/wallets/{walletAddress}/credentials?status=Pending`
- `AcceptCredentialAsync` — PATCH `/api/v1/wallets/{walletAddress}/credentials/{credentialId}/status` with `{"Status": "Active"}`
- `DeclineCredentialAsync` — PATCH `/api/v1/wallets/{walletAddress}/credentials/{credentialId}/status` with `{"Status": "Declined"}`

- [ ] **Step 4: Enhance MyCredentials.razor with lifecycle tabs**

Replace existing two-tab layout with four tabs:
- **Pending** (with badge count from `_pendingCredentials.Count`) — shows `CredentialAcceptCard` components
- **Active** — existing `CredentialCardList` filtered to Active
- **Expired** — existing `CredentialCardList` filtered to Expired
- **Revoked** — existing `CredentialCardList` filtered to Revoked

Keep the "Presentation Inbox" as a secondary section or fifth tab.

Wire up Accept/Decline handlers on `CredentialAcceptCard`:
- Accept → call `AcceptCredentialAsync` → move to Active tab, show success snackbar
- Decline → confirm dialog → call `DeclineCredentialAsync` → remove from list, show info snackbar

- [ ] **Step 5: Run build**

Run: `dotnet build src/Apps/Sorcha.UI/`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/
git commit -m "feat: enhance MyCredentials page with lifecycle tabs and acceptance flow"
```

---

### Task 7: Issued Credentials Admin Page

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/IIssuedCredentialService.cs`
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/IssuedCredentialService.cs`
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/IssuedCredentialsGrid.razor`
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/CredentialStatusDialog.razor`
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/IssuedCredentials.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Create IIssuedCredentialService**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Credentials;

using Sorcha.UI.Core.Models.Credentials;

public interface IIssuedCredentialService
{
    Task<List<IssuedCredentialListItem>> GetIssuedCredentialsAsync(
        string? statusFilter = null, string? typeFilter = null);
    Task<CredentialOperationResult> SuspendCredentialAsync(
        string credentialId, string reason);
    Task<CredentialOperationResult> RevokeCredentialAsync(
        string credentialId, string reason);
    Task<CredentialOperationResult> ReinstateCredentialAsync(string credentialId);
    Task<CredentialOperationResult> RefreshCredentialAsync(string credentialId);
}
```

- [ ] **Step 2: Implement IssuedCredentialService**

HttpClient-based. Read existing `CredentialApiService.cs` for patterns.
- `GetIssuedCredentialsAsync` — GET `/api/v1/credentials/issued?status={status}&type={type}`
- Lifecycle operations call existing Blueprint Service endpoints: `/api/v1/credentials/{id}/suspend|revoke|reinstate|refresh`
- Uses `LifecycleCredentialRequest` / `RevokeCredentialRequest` / `RefreshCredentialRequest` bodies
- Gets issuer wallet from auth context (JWT claims)

- [ ] **Step 3: Create CredentialStatusDialog.razor**

MudBlazor dialog for status change confirmation. Parameters:
- `string CredentialId`
- `string Action` — "Suspend", "Revoke", "Reinstate", "Refresh"
- `EventCallback<string?> OnConfirm` — returns reason text (null for reinstate/refresh)

Layout:
- Title: "{Action} Credential"
- Warning text per action (from design spec)
- `MudTextField` for reason (required for Suspend/Revoke)
- Red styling for Revoke action
- Confirm/Cancel buttons

- [ ] **Step 4: Create IssuedCredentialsGrid.razor**

Component wrapping `MudDataGrid<IssuedCredentialListItem>`. Parameters:
- `List<IssuedCredentialListItem> Credentials`
- `EventCallback<(string CredentialId, string Action)> OnStatusAction`

Columns per design: Type, Issued To, Via Blueprint, Issued Date, Expires (with colour coding), Status (MudChip), Presentations, Actions (MudMenu).

- [ ] **Step 5: Create IssuedCredentials.razor page**

```razor
@page "/admin/credentials"
@rendermode InteractiveWebAssemblyRenderMode
@attribute [Authorize(Roles = "Administrator,SystemAdmin")]
```

Standard admin page pattern (per existing UserManagement.razor):
- Page title + breadcrumbs
- Filter panel: status dropdown, type text field, date range
- `IssuedCredentialsGrid` component
- Status action handler: opens `CredentialStatusDialog`, calls `IIssuedCredentialService`, refreshes grid

- [ ] **Step 6: Register IIssuedCredentialService in ServiceCollectionExtensions**

Read: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`

Add registration following existing pattern (around line 228-241):
```csharp
services.AddScoped<IIssuedCredentialService>(sp =>
{
    var handler = sp.GetRequiredService<AuthenticatedHttpMessageHandler>();
    handler.InnerHandler = new HttpClientHandler();
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
    var logger = sp.GetRequiredService<ILogger<IssuedCredentialService>>();
    return new IssuedCredentialService(httpClient, logger);
});
```

- [ ] **Step 7: Run build**

Run: `dotnet build src/Apps/Sorcha.UI/`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/IIssuedCredentialService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/IssuedCredentialService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/ src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/IssuedCredentials.razor src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: add Issued Credentials admin page with lifecycle management"
```

---

### Task 8: Navigation & SignalR Notifications

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ActionsHubConnection.cs`

- [ ] **Step 1: Read MainLayout.razor nav structure**

Read: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` (lines 114-211 for Admin section)

- [ ] **Step 2: Add "Issued Credentials" nav item under Administration**

Add nav item after Participants (around line 140):
```razor
<MudNavLink Href="admin/credentials" Icon="@Icons.Material.Filled.VerifiedUser">
    Issued Credentials
</MudNavLink>
```

- [ ] **Step 3: Add credential notification badge to "My Credentials" nav item**

Read existing pending actions badge pattern (lines 32-38), replicate for credential pending count. Add `_pendingCredentialCount` field, update via SignalR event.

- [ ] **Step 4: Read ActionsHubConnection.cs**

Read: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ActionsHubConnection.cs`

- [ ] **Step 5: Add credential notification events to hub connection**

Add event handlers:
```csharp
public event Action<CredentialNotification>? OnCredentialReceived;
public event Action<CredentialNotification>? OnCredentialStatusChanged;
public event Action<int>? OnPendingCredentialCountUpdated;
```

Register in connection setup:
```csharp
_connection.On<CredentialNotification>("CredentialReceived", notification =>
    OnCredentialReceived?.Invoke(notification));
_connection.On<CredentialNotification>("CredentialStatusChanged", notification =>
    OnCredentialStatusChanged?.Invoke(notification));
_connection.On<int>("PendingCredentialCountUpdated", count =>
    OnPendingCredentialCountUpdated?.Invoke(count));
```

- [ ] **Step 6: Wire notification events in MainLayout.razor**

In `OnInitializedAsync`, subscribe:
```csharp
ActionsHub.OnCredentialReceived += OnCredentialReceived;
ActionsHub.OnPendingCredentialCountUpdated += OnPendingCredentialCountUpdated;
```

Show snackbar on `OnCredentialReceived`. Update badge count on `OnPendingCredentialCountUpdated`.

Unsubscribe in `Dispose`.

- [ ] **Step 7: Run build**

Run: `dotnet build src/Apps/Sorcha.UI/`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ActionsHubConnection.cs
git commit -m "feat: add credential nav items and SignalR notification integration"
```

---

### Task 9: Integrate Components into Workflow Actions

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` (or action detail page)
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/PresentationRequestDialog.razor`

- [ ] **Step 1: Read MyActions.razor and action detail flow**

Read: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor`

Understand how action completion is handled to determine where to inject:
- `IssuanceSummaryPanel` after action submit succeeds (if action has credential issuance)
- `VerificationTrustView` in action view when credential presentation is received
- `DisclosurePicker` replacing the existing `PresentationRequestDialog` content

- [ ] **Step 2: Read existing PresentationRequestDialog.razor**

Read: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/PresentationRequestDialog.razor`

- [ ] **Step 3: Enhance PresentationRequestDialog with DisclosurePicker**

Replace the existing checkboxes/radio group approach with the `DisclosurePicker` component. Map the existing `PresentationRequestViewModel` data to `DisclosureClaimViewModel` list using `CategoriseClaims`.

- [ ] **Step 4: Add IssuanceSummaryPanel trigger in action completion**

After `SubmitActionExecuteAsync` succeeds, check if the response includes credential issuance data. If so, open `IssuanceSummaryPanel` as a dialog:
```csharp
if (result?.IssuedCredential is not null)
{
    var parameters = new DialogParameters<IssuanceSummaryPanel>
    {
        { x => x.Summary, MapToSummary(result.IssuedCredential) }
    };
    await DialogService.ShowAsync<IssuanceSummaryPanel>("Credential Issued", parameters);
}
```

- [ ] **Step 5: Add VerificationTrustView in action view for received presentations**

When an action has a received credential presentation, render `VerificationTrustView` inline in the action card. Map the verification result from the action data to `VerificationResultViewModel`.

- [ ] **Step 6: Run build**

Run: `dotnet build src/Apps/Sorcha.UI/`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/PresentationRequestDialog.razor
git commit -m "feat: integrate VC components into blueprint action workflow"
```

---

### Task 10: Final Integration & Build Verification

- [ ] **Step 1: Run full solution build**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Run all UI tests**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/ -v m`
Expected: All tests pass

- [ ] **Step 3: Run existing credential-related tests**

Run: `dotnet test --filter "FullyQualifiedName~Credential" -v m`
Expected: All tests pass (no regressions)

- [ ] **Step 4: Verify no build warnings in new files**

Run: `dotnet build src/Apps/Sorcha.UI/ -warnaserror`
Expected: Build succeeded

- [ ] **Step 5: Commit any final fixes**

```bash
git add -A
git commit -m "chore: final integration fixes for VC UX feature"
```

---

## Backlog Items (Not in this plan)

Captured for future work:
- Credential card theming (certificate style, identity card style)
- Register publication of credential status
- QR code presentation flow for in-person verification
- Credential holder consent preferences (auto-accept rules)
- OID4VC issuance endpoint
- Cross-org trusted issuer registry
