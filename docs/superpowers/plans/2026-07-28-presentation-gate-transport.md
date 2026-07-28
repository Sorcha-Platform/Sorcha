# Presentation Gate Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route SorchaWallet credential gates through Blueprint's F127 lifecycle instead of HAIP, behind a single presentation card with a transport seam.

**Architecture:** One `PresentationRequestCard` owns the QR / state / claims chrome. An `IPresentationGateTransport` seam owns protocol, with two implementations — `SorchaWalletGateTransport` (wraps the existing `IPresentationSignal`, which already races the BlueprintHub signal against a 3s status poll) and `HaipGateTransport` (wraps `IHaipOfferService`'s result poll). The server contract gains a `Source` discriminator and the `ClaimsFetchToken` it currently discards.

**Tech Stack:** .NET 10, Blazor WASM, MudBlazor 9.5, xUnit v3 + FluentAssertions + bUnit 2.7, Moq, SignalR.

**Spec:** `docs/superpowers/specs/2026-07-28-presentation-gate-transport-design.md`

## Global Constraints

- License header on every new file: `// SPDX-License-Identifier: MIT` then `// Copyright (c) 2026 Sorcha Contributors`.
- File-scoped namespaces; `_camelCase` private fields; `Async` suffix on async methods; XML `<summary>` on every public member.
- All `System.Text.Json` HTTP reads in UI code pass `JsonDefaults.Api` (`Sorcha.UI.Core.Extensions`) explicitly — enforced by CI gate `json-options-gate`.
- Shared user-facing components live in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User`, whose RootNamespace is `Sorcha.UI.Core`. The F127 stack uses audience-in-namespace here: `Sorcha.UI.Core.Services.User.Presentation` and `Sorcha.UI.Core.Models.User.Presentation`. Follow that, not the folder-only convention.
- Do NOT inject `ISnackbar` — enforced by CI gate `no-new-snackbar`.
- Claims are `IReadOnlyDictionary<string, object?>` everywhere in this feature (matches the existing `DisclosedClaimsResponse.Claims`).
- `GateOutcome` members: `Pending`, `Submitted`, `Success`, `Declined`, `Expired`, `Abandoned`, `Unreachable`.
- **The F127 status endpoint returns `{"state": "..."}` — the JSON property is `state`, not `status`.** See `PresentationSignal.StatusProbeShape`. Getting this wrong is silent: the poll deserialises to null forever and the gate hangs.
- Branch: `feature/presentation-gate-transport`. One PR.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs` | `HaipPresentationRequestResponse` → `PresentationRequestResponse`; add `Source`, `ClaimsFetchToken` |
| `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` | Stop discarding source + token |
| `…/Sorcha.UI.Components.User/Models/User/Workflows/ActionSubmissionResultViewModel.cs` | Client mirror of the response |
| `…/Sorcha.UI.Components.User/Services/User/Presentation/IPresentationGateTransport.cs` | Seam + `GateOutcome` + `GateOutcomes.IsTerminal` |
| `…/Services/User/Presentation/IPresentationSignal.cs` + `PresentationSignal.cs` | Add `OnRequestUnreachable` (404 streak) |
| `…/Services/User/Presentation/SorchaWalletGateTransport.cs` | F127 impl over `IPresentationSignal` + token-bound claims fetch |
| `…/Services/User/Presentation/HaipGateTransport.cs` | HAIP impl over `IHaipOfferService`; claims inline |
| `…/Services/User/Presentation/PresentationServiceCollectionExtensions.cs` | Register both transports |
| `…/Components/Presentation/PresentationRequestCard.razor` | The single control |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs` | Call `AddSorchaPresentationGate` (currently only the sample does) |
| `…/Sorcha.UI.Web.Client/Components/Credentials/PresentationRequestQrDialog.razor` | Forward `Source` + token to the card |
| `…/Sorcha.UI.Web.Client/Pages/NewSubmissionWorkspace.razor`, `Pages/MyActions.razor` | Pass `Source` + token through |
| `samples/strathcarron-portal/Pages/*.razor` | Migrate off `CredentialGateComponent` |

**Deleted at the end:** `…/Components/CredentialGate/CredentialGateComponent.razor`, `…/Sorcha.UI.Web.Client/Components/Credentials/PresentationRequestQrCard.razor`, and `tests/Sorcha.UI.Core.Tests/Components/CredentialGate/CredentialGateComponentTests.cs`.

---

### Task 1: Contract carries source and claims token

`PresentationInitiationResult` already carries a `ClaimsFetchToken`; the response DTO had no field for it, so `ActionExecutionService` dropped it during mapping and the web app could not route a SorchaWallet gate. Same defect class as #1314 and #1318 — a hand-maintained mapping losing a field with nothing verifying the join.

**Files:**
- Modify: `src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs` (record `HaipPresentationRequestResponse`, ~line 187)
- Modify: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:358-373`
- Test: `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionPresentationMappingTests.cs`

**Interfaces:**
- Produces: `PresentationRequestResponse { Guid RequestId; string PresentationRequestUri; string CredentialType; List<string>? RequestedClaims; DateTimeOffset ExpiresAt; PresentationSource Source; string? ClaimsFetchToken }` in namespace `Sorcha.Blueprint.Service.Models.Responses`

- [ ] **Step 1: Read the mapping before changing it**

Read `ActionExecutionService.cs:330-390` and note the exact local variable names, the requirement type that carries `PresentationSource`, and the property on `PresentationInitiationResult` holding the token. The code below uses `presentationRequirement` / `lifecycleResult`; if the existing locals differ, keep the existing names and adapt.

- [ ] **Step 2: Write the failing test**

```csharp
// tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionPresentationMappingTests.cs
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Models.Responses;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// The response DTO is the only channel by which the web app learns which lifecycle owns a
/// presentation request. A missing member here does not fail to compile and does not throw — the
/// client silently polls the wrong service.
/// </summary>
public class ActionExecutionPresentationMappingTests
{
    [Fact]
    public void ResponseCarriesSourceAndClaimsToken()
    {
        var response = new PresentationRequestResponse
        {
            RequestId = Guid.NewGuid(),
            PresentationRequestUri = "openid4vp://authorize?x=1",
            CredentialType = "https://sorcha.dev/vc/assured-identity/v1",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            Source = PresentationSource.SorchaWallet,
            ClaimsFetchToken = "tok-abc"
        };

        response.Source.Should().Be(PresentationSource.SorchaWallet);
        response.ClaimsFetchToken.Should().Be("tok-abc",
            "the web app cannot fetch disclosed claims without it");
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet build tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj --nologo`
Expected: FAIL — `The type or namespace name 'PresentationRequestResponse' could not be found`

- [ ] **Step 4: Rename the record and add the fields**

In `ActionSubmissionResponse.cs`, rename `HaipPresentationRequestResponse` to `PresentationRequestResponse`, update its XML doc (it was never HAIP-specific), and add:

```csharp
    /// <summary>Which lifecycle owns this request — decides the client transport.</summary>
    public required PresentationSource Source { get; init; }

    /// <summary>
    /// Feature 127 single-use token bound to <see cref="RequestId"/>, presented on
    /// <c>GET /api/presentations/{id}/disclosed-claims</c>. Null for HAIP, which returns claims
    /// inline with the verification result.
    /// </summary>
    public string? ClaimsFetchToken { get; init; }
```

Add `using Sorcha.Blueprint.Models.Credentials;` if absent, and update the property on `ActionSubmissionResponse` that references the old type name.

- [ ] **Step 5: Populate them instead of discarding**

In `ActionExecutionService.cs`, rename the local `haipRequirement` to `presentationRequirement` (it is not HAIP-specific) and extend the initialiser:

```csharp
                    PresentationRequest = new PresentationRequestResponse
                    {
                        RequestId = lifecycleResult.PresentationRequestId,
                        PresentationRequestUri = lifecycleResult.AuthorizationRequestUri,
                        CredentialType = presentationRequirement.Type,
                        RequestedClaims = presentationRequirement.RequiredClaims?
                            .Select(c => c.ClaimName).ToList(),
                        ExpiresAt = lifecycleResult.ExpiresAt,
                        Source = presentationRequirement.PresentationSource,
                        ClaimsFetchToken = lifecycleResult.ClaimsFetchToken
                    }
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj --nologo`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs \
        src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs \
        tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionPresentationMappingTests.cs
git commit -m "feat: [F127] - presentation response carries source and claims token"
```

---

### Task 2: Client mirror of the contract

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Workflows/ActionSubmissionResultViewModel.cs:116-125` (record `HaipPresentationRequestInfo`)
- Test: `tests/Sorcha.UI.ContractTests/PresentationRequestContractTests.cs`

**Interfaces:**
- Consumes: Task 1's `PresentationRequestResponse`
- Produces: `PresentationRequestInfo` with the same seven members

- [ ] **Step 1: Confirm the client record's real namespace**

Read the top of `ActionSubmissionResultViewModel.cs` and use its declared namespace in the test below rather than assuming `Sorcha.UI.Core.Models.Workflows`.

- [ ] **Step 2: Write the failing test**

```csharp
// tests/Sorcha.UI.ContractTests/PresentationRequestContractTests.cs
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using FluentAssertions;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.UI.Core.Models.Workflows;
using Xunit;

namespace Sorcha.UI.ContractTests;

public class PresentationRequestContractTests
{
    [Fact]
    public void ClientMirrorHasEveryServerMember()
    {
        var server = typeof(PresentationRequestResponse)
            .GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        var client = typeof(PresentationRequestInfo)
            .GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        client.Should().BeEquivalentTo(server,
            "a member present on one side only is a field the mapping will silently drop");
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet build tests/Sorcha.UI.ContractTests/Sorcha.UI.ContractTests.csproj --nologo`
Expected: FAIL — `PresentationRequestInfo` not found

- [ ] **Step 4: Rename and extend the client record**

Rename `HaipPresentationRequestInfo` to `PresentationRequestInfo` and add:

```csharp
    /// <summary>Which lifecycle owns this request — selects the client transport.</summary>
    public PresentationSource Source { get; init; }

    /// <summary>Single-use F127 claims-fetch token; null for HAIP.</summary>
    public string? ClaimsFetchToken { get; init; }
```

Add `using Sorcha.Blueprint.Models.Credentials;`. Update the `PresentationRequest` property on `ActionSubmissionResultViewModel` and every reference to the old type name (`grep -rn "HaipPresentationRequestInfo" src/`).

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Sorcha.UI.ContractTests/Sorcha.UI.ContractTests.csproj --nologo`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Workflows/ActionSubmissionResultViewModel.cs \
        tests/Sorcha.UI.ContractTests/PresentationRequestContractTests.cs
git commit -m "feat: [F127] - client mirrors the presentation request contract"
```

---

### Task 3: The transport seam

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/IPresentationGateTransport.cs`
- Test: `tests/Sorcha.UI.Core.Tests/Services/Presentation/GateOutcomeTests.cs`

**Interfaces:**
- Produces: `enum GateOutcome`; `static class GateOutcomes { bool IsTerminal(GateOutcome) }`; `interface IPresentationGateTransport { PresentationSource Source; Task<GateOutcome> WaitForOutcomeAsync(Guid, IProgress<GateOutcome>?, CancellationToken); Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(Guid, string?, CancellationToken) }`

`WaitForOutcomeAsync` **awaits a terminal outcome** — it does not return per-tick state. Each transport owns its own waiting mechanism (F127 subscribes to a signal; HAIP loops a poll), so the card holds no protocol loop at all. Non-terminal transitions are reported through the optional `IProgress<GateOutcome>` so the card can still show "Verifying…".

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Sorcha.UI.Core.Tests/Services/Presentation/GateOutcomeTests.cs
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Presentation;

public class GateOutcomeTests
{
    [Theory]
    [InlineData(GateOutcome.Success)]
    [InlineData(GateOutcome.Declined)]
    [InlineData(GateOutcome.Expired)]
    [InlineData(GateOutcome.Abandoned)]
    [InlineData(GateOutcome.Unreachable)]
    public void TerminalOutcomesEndTheWait(GateOutcome outcome)
        => GateOutcomes.IsTerminal(outcome).Should().BeTrue();

    [Theory]
    [InlineData(GateOutcome.Pending)]
    [InlineData(GateOutcome.Submitted)]
    public void NonTerminalOutcomesDoNot(GateOutcome outcome)
        => GateOutcomes.IsTerminal(outcome).Should().BeFalse();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: FAIL — `GateOutcome` not found

- [ ] **Step 3: Create the seam**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>Transport-neutral state of a credential-gate presentation.</summary>
public enum GateOutcome
{
    /// <summary>No wallet interaction observed yet.</summary>
    Pending,

    /// <summary>A presentation arrived and is being verified.</summary>
    Submitted,

    /// <summary>Verified. Disclosed claims may now be fetched.</summary>
    Success,

    /// <summary>The holder refused, or verification failed.</summary>
    Declined,

    /// <summary>The request's validity window elapsed.</summary>
    Expired,

    /// <summary>The holder walked away without completing.</summary>
    Abandoned,

    /// <summary>
    /// The lifecycle has no such request, so waiting can never succeed. Distinct from
    /// <see cref="Expired"/> on purpose: reporting an unreachable request as expired sends the
    /// citizen to inspect their wallet instead of telling them the problem is ours (#1325).
    /// </summary>
    Unreachable
}

/// <summary>Helpers over <see cref="GateOutcome"/>.</summary>
public static class GateOutcomes
{
    /// <summary>Whether no further transition is expected, so the wait must end.</summary>
    public static bool IsTerminal(GateOutcome outcome) => outcome
        is GateOutcome.Success or GateOutcome.Declined or GateOutcome.Expired
        or GateOutcome.Abandoned or GateOutcome.Unreachable;
}

/// <summary>
/// Protocol seam for a credential gate. The card owns chrome; implementations own the lifecycle.
/// </summary>
/// <remarks>
/// Deliberately NOT a reuse of <c>IVerificationTransport</c>, which is verdict-shaped: it returns a
/// vp_token plus a verdict for client-side computation. A gate wants disclosed claims for form
/// prefill, obtained through a single-use token — a third master on that interface, not a fit.
/// </remarks>
public interface IPresentationGateTransport
{
    /// <summary>Which source this transport serves. The card selects on this.</summary>
    PresentationSource Source { get; }

    /// <summary>
    /// Waits until the presentation reaches a terminal outcome, reporting non-terminal
    /// transitions through <paramref name="progress"/> along the way.
    /// </summary>
    /// <remarks>
    /// Implementations MUST return <see cref="GateOutcome.Unreachable"/> rather than
    /// <see cref="GateOutcome.Expired"/> when the lifecycle has no such request, and MUST NOT
    /// throw on transport failure — return a terminal outcome instead.
    /// </remarks>
    Task<GateOutcome> WaitForOutcomeAsync(
        Guid requestId, IProgress<GateOutcome>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Fetches disclosed claims after a successful outcome, or null when none are available.
    /// </summary>
    /// <remarks>
    /// Implementations that use a single-use token MUST reuse the same token across retries — the
    /// F127 endpoint consumes it even on a <c>pending</c> response.
    /// </remarks>
    Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
        Guid requestId, string? claimsFetchToken, CancellationToken ct = default);
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/IPresentationGateTransport.cs \
        tests/Sorcha.UI.Core.Tests/Services/Presentation/GateOutcomeTests.cs
git commit -m "feat: [F127] - add the presentation gate transport seam"
```

---

### Task 4: `IPresentationSignal` reports an unreachable request

`PresentationSignal.FetchOutcomeKindAsync` returns null for a 404 and for a transient failure alike, so a request that does not exist polls silently until the 60s manual-recovery window. That is the same collapse #1325 fixed on the HAIP side.

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/IPresentationSignal.cs`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/PresentationSignal.cs`
- Test: `tests/Sorcha.UI.Core.Tests/Services/Presentation/PresentationSignalNotFoundTests.cs`

**Interfaces:**
- Produces: `event Action? OnRequestUnreachable` on `IPresentationSignal`, raised after `UnreachableThreshold` (3) consecutive 404s from the status endpoint. Raised at most once, and never after a terminal signal.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Sorcha.UI.Core.Tests/Services/Presentation/PresentationSignalNotFoundTests.cs
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Presentation;

public class PresentationSignalNotFoundTests
{
    private sealed class AlwaysHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private static PresentationSignal Build(HttpStatusCode status, FakeTimeProvider time)
    {
        var http = new HttpClient(new AlwaysHandler(status, "{}"))
        {
            BaseAddress = new Uri("https://n1.test/")
        };
        var hub = new PresentationHubConnection("https://n1.test", null,
            NullLogger<PresentationHubConnection>.Instance);
        return new PresentationSignal(hub, http, time, NullLogger<PresentationSignal>.Instance);
    }

    [Fact]
    public async Task RaisesUnreachableAfterConsecutiveNotFounds()
    {
        var time = new FakeTimeProvider();
        var sut = Build(HttpStatusCode.NotFound, time);
        var unreachable = false;
        sut.OnRequestUnreachable += () => unreachable = true;

        await sut.StartAsync(Guid.NewGuid(), CancellationToken.None);
        for (var i = 0; i < 5; i++)
        {
            time.Advance(TimeSpan.FromSeconds(3));
            await Task.Yield();
        }

        unreachable.Should().BeTrue(
            "a request the lifecycle has never heard of cannot be waited for");
        await sut.StopAsync();
    }

    [Fact]
    public async Task DoesNotRaiseUnreachableOnServerError()
    {
        var time = new FakeTimeProvider();
        var sut = Build(HttpStatusCode.InternalServerError, time);
        var unreachable = false;
        sut.OnRequestUnreachable += () => unreachable = true;

        await sut.StartAsync(Guid.NewGuid(), CancellationToken.None);
        for (var i = 0; i < 5; i++)
        {
            time.Advance(TimeSpan.FromSeconds(3));
            await Task.Yield();
        }

        unreachable.Should().BeFalse("a 500 may succeed on the next tick");
        await sut.StopAsync();
    }
}
```

If `Microsoft.Extensions.TimeProvider.Testing` is not already referenced by `Sorcha.UI.Core.Tests`, add it:
`dotnet add tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj package Microsoft.Extensions.TimeProvider.Testing`

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: FAIL — `OnRequestUnreachable` is not a member of `PresentationSignal`

- [ ] **Step 3: Add the event to the interface**

In `IPresentationSignal.cs`, after `OnManualRecoveryRequired`:

```csharp
    /// <summary>
    /// Fires when the lifecycle reports it holds no such request (repeated 404s from the status
    /// endpoint). Permanent — no amount of further waiting can succeed, so the consumer should
    /// stop and say so rather than let the request time out as "expired".
    /// </summary>
    event Action? OnRequestUnreachable;
```

- [ ] **Step 4: Implement the 404 streak**

In `PresentationSignal.cs`, add beside the existing fields:

```csharp
    /// <summary>Consecutive 404s before the request is declared unreachable.</summary>
    private const int UnreachableThreshold = 3;

    private int _notFoundStreak;
    private bool _unreachableRaised;

    public event Action? OnRequestUnreachable;
```

Reset `_notFoundStreak = 0; _unreachableRaised = false;` in `StartAsync` beside `_signalReceived = false;`.

Then change `FetchOutcomeKindAsync` so the 404 is no longer collapsed into the generic null:

```csharp
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (++_notFoundStreak >= UnreachableThreshold && !_unreachableRaised && !_signalReceived)
                {
                    _unreachableRaised = true;
                    _logger.LogError(
                        "Presentation lifecycle has no request {RequestId} after {Streak} "
                        + "consecutive 404s — declaring it unreachable.",
                        _presentationRequestId, _notFoundStreak);
                    OnRequestUnreachable?.Invoke();
                }
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            _notFoundStreak = 0;
```

(The reset goes after the success check, before reading the body.)

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/IPresentationSignal.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/PresentationSignal.cs \
        tests/Sorcha.UI.Core.Tests/Services/Presentation/PresentationSignalNotFoundTests.cs
git commit -m "fix: [F127] - distinguish an unreachable presentation request from a transient failure"
```

---

### Task 5: SorchaWallet transport

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/SorchaWalletGateTransport.cs`
- Test: `tests/Sorcha.UI.Core.Tests/Services/Presentation/SorchaWalletGateTransportTests.cs`

**Interfaces:**
- Consumes: Task 3's seam; Task 4's `OnRequestUnreachable`; `IPresentationSignal` (`OnOutcomeReady(PresentationSignalOutcome{PresentationRequestId, Kind})`, `OnFallbackEngaged`, `OnManualRecoveryRequired`, `StartAsync(Guid, CancellationToken)`, `StopAsync()`); `DisclosedClaimsResponse { Guid PresentationRequestId; string Status; IReadOnlyDictionary<string, object?>? Claims; string? SubjectDisplayName }`
- Produces: `SorchaWalletGateTransport(IPresentationSignal signal, HttpClient http, ILogger<SorchaWalletGateTransport> logger)`

Kind strings come from `PresentationSignal.TerminalStates`: `success`, `decline`, `abandoned`, `abandoned-with-late-outcome`, `expired`. Claims endpoint: `GET /api/presentations/{id:D}/disclosed-claims?token={token}`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Sorcha.UI.Core.Tests/Services/Presentation/SorchaWalletGateTransportTests.cs
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Presentation;

public class SorchaWalletGateTransportTests
{
    /// <summary>Drives the transport by raising signal events on demand.</summary>
    private sealed class FakeSignal : IPresentationSignal
    {
        public event Func<PresentationSignalOutcome, Task>? OnOutcomeReady;
        public event Action? OnFallbackEngaged;
        public event Action? OnManualRecoveryRequired;
        public event Action? OnRequestUnreachable;

        public Guid Started { get; private set; }
        public bool Stopped { get; private set; }

        public Task StartAsync(Guid id, CancellationToken ct) { Started = id; return Task.CompletedTask; }
        public Task StopAsync() { Stopped = true; return Task.CompletedTask; }

        public Task RaiseOutcome(Guid id, string kind)
            => OnOutcomeReady?.Invoke(new PresentationSignalOutcome(id, kind)) ?? Task.CompletedTask;
        public void RaiseUnreachable() => OnRequestUnreachable?.Invoke();
        public void RaiseManualRecovery() => OnManualRecoveryRequired?.Invoke();
        public void RaiseFallback() => OnFallbackEngaged?.Invoke();
    }

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            LastUri = r.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private static SorchaWalletGateTransport Build(IPresentationSignal signal, Stub? stub = null)
        => new(signal,
               new HttpClient(stub ?? new Stub(HttpStatusCode.OK, "{}"))
               { BaseAddress = new Uri("https://n1.test/") },
               NullLogger<SorchaWalletGateTransport>.Instance);

    [Theory]
    [InlineData("success", GateOutcome.Success)]
    [InlineData("abandoned-with-late-outcome", GateOutcome.Success)]
    [InlineData("decline", GateOutcome.Declined)]
    [InlineData("expired", GateOutcome.Expired)]
    [InlineData("abandoned", GateOutcome.Abandoned)]
    public async Task SignalKindMapsOntoGateOutcome(string kind, GateOutcome expected)
    {
        var signal = new FakeSignal();
        var sut = Build(signal);
        var id = Guid.NewGuid();

        var waiting = sut.WaitForOutcomeAsync(id);
        await signal.RaiseOutcome(id, kind);

        (await waiting).Should().Be(expected);
    }

    [Fact]
    public async Task UnreachableSignalEndsTheWait()
    {
        var signal = new FakeSignal();
        var sut = Build(signal);

        var waiting = sut.WaitForOutcomeAsync(Guid.NewGuid());
        signal.RaiseUnreachable();

        (await waiting).Should().Be(GateOutcome.Unreachable);
    }

    [Fact]
    public async Task ManualRecoveryEndsTheWaitAsUnreachable()
    {
        var signal = new FakeSignal();
        var sut = Build(signal);

        var waiting = sut.WaitForOutcomeAsync(Guid.NewGuid());
        signal.RaiseManualRecovery();

        (await waiting).Should().Be(GateOutcome.Unreachable,
            "60s with no signal from either transport is not the same as the holder declining");
    }

    [Fact]
    public async Task CancellationEndsTheWaitAsAbandoned()
    {
        var signal = new FakeSignal();
        var sut = Build(signal);
        using var cts = new CancellationTokenSource();

        var waiting = sut.WaitForOutcomeAsync(Guid.NewGuid(), null, cts.Token);
        await cts.CancelAsync();

        (await waiting).Should().Be(GateOutcome.Abandoned);
        signal.Stopped.Should().BeTrue("the signal must not outlive the wait");
    }

    [Fact]
    public async Task ClaimsFetchWithoutTokenDoesNotCallTheEndpoint()
    {
        // The token is single-use and consumed even on a pending response, so a call without one
        // can never succeed.
        var stub = new Stub(HttpStatusCode.OK, "{}");
        var sut = Build(new FakeSignal(), stub);

        var claims = await sut.FetchClaimsAsync(Guid.NewGuid(), claimsFetchToken: null);

        claims.Should().BeNull();
        stub.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ClaimsFetchReturnsClaimsOnSuccess()
    {
        var id = Guid.NewGuid();
        var stub = new Stub(HttpStatusCode.OK,
            $$"""{"presentationRequestId":"{{id}}","status":"success","claims":{"givenName":"Stuart","age_over_18":true}}""");
        var sut = Build(new FakeSignal(), stub);

        var claims = await sut.FetchClaimsAsync(id, "tok-abc");

        claims.Should().NotBeNull();
        claims!.Should().ContainKey("givenName");
        stub.LastUri.Should().Contain("tok-abc");
    }

    [Fact]
    public async Task ClaimsFetchReturnsNullWhenStatusIsNotSuccess()
    {
        var id = Guid.NewGuid();
        var stub = new Stub(HttpStatusCode.OK,
            $$"""{"presentationRequestId":"{{id}}","status":"pending"}""");
        var sut = Build(new FakeSignal(), stub);

        (await sut.FetchClaimsAsync(id, "tok-abc")).Should().BeNull();
    }

    [Fact]
    public void SourceIsSorchaWallet()
        => Build(new FakeSignal()).Source.Should().Be(PresentationSource.SorchaWallet);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: FAIL — `SorchaWalletGateTransport` not found

- [ ] **Step 3: Implement**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.User.Presentation;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// Feature 127 gate transport. Delegates waiting to <see cref="IPresentationSignal"/>, which
/// already races the BlueprintHub <c>PresentationOutcomeReady</c> event against a 3s poll of
/// <c>/api/presentations/{id}/status</c>, and fetches disclosed claims with the single-use
/// ClaimsFetchToken.
/// </summary>
/// <remarks>
/// The hub is a latency optimisation, not a guarantee — F119's deferred-outcome path does not
/// publish <c>PresentationOutcomeReady</c> yet, so the signal's poll is load-bearing. That race
/// lives in <see cref="PresentationSignal"/>; this class deliberately does not duplicate it.
/// </remarks>
public sealed class SorchaWalletGateTransport(
    IPresentationSignal signal,
    HttpClient http,
    ILogger<SorchaWalletGateTransport> logger) : IPresentationGateTransport
{
    /// <inheritdoc />
    public PresentationSource Source => PresentationSource.SorchaWallet;

    /// <inheritdoc />
    public async Task<GateOutcome> WaitForOutcomeAsync(
        Guid requestId, IProgress<GateOutcome>? progress = null, CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource<GateOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task OnOutcome(PresentationSignalOutcome outcome)
        {
            if (outcome.PresentationRequestId == requestId)
            {
                completion.TrySetResult(MapKind(outcome.Kind));
            }
            return Task.CompletedTask;
        }

        void OnUnreachable() => completion.TrySetResult(GateOutcome.Unreachable);

        // 60s with neither transport surfacing anything is an infrastructure problem, not a
        // holder decision — say so rather than blaming the citizen's wallet.
        void OnManualRecovery() => completion.TrySetResult(GateOutcome.Unreachable);

        void OnFallback() => progress?.Report(GateOutcome.Pending);

        signal.OnOutcomeReady += OnOutcome;
        signal.OnRequestUnreachable += OnUnreachable;
        signal.OnManualRecoveryRequired += OnManualRecovery;
        signal.OnFallbackEngaged += OnFallback;

        using var registration = ct.Register(
            () => completion.TrySetResult(GateOutcome.Abandoned));

        try
        {
            await signal.StartAsync(requestId, ct).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Presentation signal failed to start for {RequestId}", requestId);
            return GateOutcome.Unreachable;
        }
        finally
        {
            signal.OnOutcomeReady -= OnOutcome;
            signal.OnRequestUnreachable -= OnUnreachable;
            signal.OnManualRecoveryRequired -= OnManualRecovery;
            signal.OnFallbackEngaged -= OnFallback;
            try { await signal.StopAsync().ConfigureAwait(false); } catch { /* non-fatal */ }
        }
    }

    /// <summary>
    /// Maps an F111 lifecycle state onto the transport-neutral outcome.
    /// <c>abandoned-with-late-outcome</c> is a success: the presentation did arrive, just after
    /// abandonment was recorded.
    /// </summary>
    private static GateOutcome MapKind(string kind) => kind switch
    {
        "success" => GateOutcome.Success,
        "abandoned-with-late-outcome" => GateOutcome.Success,
        "decline" => GateOutcome.Declined,
        "expired" => GateOutcome.Expired,
        "abandoned" => GateOutcome.Abandoned,
        _ => GateOutcome.Pending
    };

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
        Guid requestId, string? claimsFetchToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(claimsFetchToken))
        {
            logger.LogWarning(
                "No ClaimsFetchToken for {RequestId}; disclosed claims cannot be fetched", requestId);
            return null;
        }

        try
        {
            var url = $"/api/presentations/{requestId:D}/disclosed-claims"
                    + $"?token={Uri.EscapeDataString(claimsFetchToken)}";
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Disclosed-claims fetch for {RequestId} returned {Status}",
                    requestId, response.StatusCode);
                return null;
            }

            var body = await response.Content
                .ReadFromJsonAsync<DisclosedClaimsResponse>(JsonDefaults.Api, ct)
                .ConfigureAwait(false);

            if (body is null || !string.Equals(body.Status, "success", StringComparison.Ordinal))
            {
                logger.LogWarning("Disclosed-claims for {RequestId} came back as {Status}",
                    requestId, body?.Status ?? "(null)");
                return null;
            }

            return body.Claims;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Disclosed-claims fetch failed for {RequestId}", requestId);
            return null;
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/SorchaWalletGateTransport.cs \
        tests/Sorcha.UI.Core.Tests/Services/Presentation/SorchaWalletGateTransportTests.cs
git commit -m "feat: [F127] - SorchaWallet gate transport over the presentation signal"
```

---

### Task 6: HAIP transport

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/HaipGateTransport.cs`
- Test: `tests/Sorcha.UI.Core.Tests/Services/Presentation/HaipGateTransportTests.cs`

**Interfaces:**
- Consumes: Task 3's seam; `IHaipOfferService.PollVerificationResultAsync(Guid, CancellationToken) → HaipPollOutcome(HaipVerificationResult? Result, bool RequestNotFound)`; `HaipVerificationResult(Guid RequestId, string State, bool? IsValid, Dictionary<string, JsonElement>? VerifiedClaims, IReadOnlyList<string>? Errors)`; `HaipPollingDefaults.{PollInterval, MaxPollTicks, MaxConsecutiveNotFound}`
- Produces: `HaipGateTransport(IHaipOfferService haip, TimeProvider time, ILogger<HaipGateTransport> logger)`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Sorcha.UI.Core.Tests/Services/Presentation/HaipGateTransportTests.cs
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Presentation;

public class HaipGateTransportTests
{
    // Drives the poll delay off a fake clock so the tests don't sit through the real cadence.
    private readonly FakeTimeProvider _time = new();

    private HaipGateTransport Build(Mock<IHaipOfferService> haip)
        => new(haip.Object, _time, NullLogger<HaipGateTransport>.Instance);

    /// <summary>Runs the wait while advancing the fake clock so the poll loop makes progress.</summary>
    private async Task<GateOutcome> RunAsync(HaipGateTransport sut, IProgress<GateOutcome>? progress = null)
    {
        var waiting = sut.WaitForOutcomeAsync(Guid.NewGuid(), progress);
        while (!waiting.IsCompleted)
        {
            await Task.Yield();
            _time.Advance(HaipPollingDefaults.PollInterval);
        }
        return await waiting;
    }

    private static Mock<IHaipOfferService> Returning(params HaipPollOutcome[] sequence)
    {
        var haip = new Mock<IHaipOfferService>();
        var index = 0;
        haip.Setup(h => h.PollVerificationResultAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sequence[Math.Min(index++, sequence.Length - 1)]);
        return haip;
    }

    private static HaipPollOutcome Result(string state, Dictionary<string, JsonElement>? claims = null)
        => new(new HaipVerificationResult(Guid.NewGuid(), state, true, claims, null), false);

    [Fact]
    public async Task RepeatedNotFoundEndsTheWaitAsUnreachable()
    {
        var outcome = await RunAsync(Build(Returning(HaipPollOutcome.NotFound)));

        outcome.Should().Be(GateOutcome.Unreachable,
            "the verifier holding no such request is permanent, not an expiry");
    }

    [Theory]
    [InlineData(HaipVerificationStates.Verified, GateOutcome.Success)]
    [InlineData(HaipVerificationStates.Denied, GateOutcome.Declined)]
    [InlineData(HaipVerificationStates.Expired, GateOutcome.Expired)]
    [InlineData(HaipVerificationStates.Cancelled, GateOutcome.Abandoned)]
    public async Task TerminalStateMapsOntoGateOutcome(string state, GateOutcome expected)
        => (await RunAsync(Build(Returning(Result(state))))).Should().Be(expected);

    [Fact]
    public async Task SubmittedIsReportedAsProgressNotAsTheResult()
    {
        var seen = new List<GateOutcome>();
        var sut = Build(Returning(Result(HaipVerificationStates.Submitted),
                                  Result(HaipVerificationStates.Verified)));

        var outcome = await RunAsync(sut, new Progress<GateOutcome>(seen.Add));

        outcome.Should().Be(GateOutcome.Success);
    }

    [Fact]
    public async Task TransientFailureIsRetriedNotTreatedAsUnreachable()
    {
        var sut = Build(Returning(HaipPollOutcome.Transient,
                                  HaipPollOutcome.Transient,
                                  HaipPollOutcome.Transient,
                                  HaipPollOutcome.Transient,
                                  Result(HaipVerificationStates.Verified)));

        (await RunAsync(sut)).Should().Be(GateOutcome.Success);
    }

    [Fact]
    public async Task ClaimsComeFromTheOutcomeWithoutASecondCall()
    {
        var claims = new Dictionary<string, JsonElement>
        {
            ["givenName"] = JsonSerializer.Deserialize<JsonElement>("\"Stuart\"")
        };
        var sut = Build(Returning(Result(HaipVerificationStates.Verified, claims)));

        await RunAsync(sut);
        var fetched = await sut.FetchClaimsAsync(Guid.NewGuid(), claimsFetchToken: null);

        fetched.Should().NotBeNull();
        fetched!.Should().ContainKey("givenName");
    }

    [Fact]
    public void SourceIsHaipExternalWallet()
        => Build(Returning(HaipPollOutcome.Transient)).Source
            .Should().Be(PresentationSource.HaipExternalWallet);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: FAIL — `HaipGateTransport` not found

- [ ] **Step 3: Implement**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Services.Credentials;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// HAIP gate transport — polls the verifier result endpoint. Claims arrive inline with the
/// outcome, so <see cref="FetchClaimsAsync"/> replays what the last poll carried rather than
/// making a second call.
/// </summary>
public sealed class HaipGateTransport(
    IHaipOfferService haip,
    TimeProvider time,
    ILogger<HaipGateTransport> logger) : IPresentationGateTransport
{
    private IReadOnlyDictionary<string, object?>? _lastClaims;

    /// <inheritdoc />
    public PresentationSource Source => PresentationSource.HaipExternalWallet;

    /// <inheritdoc />
    public async Task<GateOutcome> WaitForOutcomeAsync(
        Guid requestId, IProgress<GateOutcome>? progress = null, CancellationToken ct = default)
    {
        var notFoundStreak = 0;
        var lastReported = GateOutcome.Pending;

        for (var tick = 0; tick < HaipPollingDefaults.MaxPollTicks; tick++)
        {
            if (ct.IsCancellationRequested) return GateOutcome.Abandoned;

            var poll = await haip.PollVerificationResultAsync(requestId, ct).ConfigureAwait(false);

            // A 404 is permanent — this verifier has no such request. Tolerate a couple (a
            // just-created request can 404 briefly), then stop and say so. Collapsing it into
            // "not scanned yet" is what let a doomed request poll for five minutes and then get
            // misreported as Expired (#1325).
            if (poll.RequestNotFound)
            {
                if (++notFoundStreak >= HaipPollingDefaults.MaxConsecutiveNotFound)
                {
                    logger.LogError(
                        "Verifier has no request {RequestId} after {Streak} consecutive 404s. If "
                        + "this gate is presentationSource 'SorchaWallet', its request lives in "
                        + "the Blueprint presentation lifecycle, not HAIP.",
                        requestId, notFoundStreak);
                    return GateOutcome.Unreachable;
                }
            }
            else
            {
                notFoundStreak = 0;

                if (poll.Result is { } result)
                {
                    _lastClaims = result.VerifiedClaims?
                        .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);

                    var outcome = MapState(result.State);
                    if (GateOutcomes.IsTerminal(outcome)) return outcome;

                    if (outcome != lastReported)
                    {
                        lastReported = outcome;
                        progress?.Report(outcome);
                    }
                }
            }

            try
            {
                await Task.Delay(HaipPollingDefaults.PollInterval, time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return GateOutcome.Abandoned;
            }
        }

        logger.LogInformation("Poll budget exhausted for {RequestId}", requestId);
        return GateOutcome.Expired;
    }

    /// <summary>Maps a HAIP verification state onto the transport-neutral outcome.</summary>
    private static GateOutcome MapState(string state) => state switch
    {
        HaipVerificationStates.Verified => GateOutcome.Success,
        HaipVerificationStates.Denied => GateOutcome.Declined,
        HaipVerificationStates.Expired => GateOutcome.Expired,
        HaipVerificationStates.Cancelled => GateOutcome.Abandoned,
        HaipVerificationStates.Submitted => GateOutcome.Submitted,
        _ => GateOutcome.Pending
    };

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
        Guid requestId, string? claimsFetchToken, CancellationToken ct = default)
        => Task.FromResult(_lastClaims);
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/HaipGateTransport.cs \
        tests/Sorcha.UI.Core.Tests/Services/Presentation/HaipGateTransportTests.cs
git commit -m "feat: [F127] - HAIP gate transport"
```

---

### Task 7: The unified card

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/PresentationRequestCard.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/PresentationServiceCollectionExtensions.cs`
- Test: `tests/Sorcha.UI.Core.Tests/Components/Presentation/PresentationRequestCardTests.cs`

**Interfaces:**
- Consumes: Tasks 3–6
- Produces: `PresentationRequestCard` with parameters `PresentationRequestUri`, `RequestId`, `Source`, `ClaimsFetchToken`, `CredentialType`, `RequestedClaims`, `ExpiresAt`, `LinkBackUrl`, `NameOfMissingCredentialType`, `EventCallback<GateOutcome> OnOutcome`, `EventCallback<IReadOnlyDictionary<string, object?>?> OnClaims`, `RenderFragment? ChildContent`

`ChildContent` renders only after `Success` — that is how the sample portal's gate-then-form composition survives the retirement of `CredentialGateComponent`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Sorcha.UI.Core.Tests/Components/Presentation/PresentationRequestCardTests.cs
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Components.Presentation;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Presentation;

public class PresentationRequestCardTests : BunitContext
{
    private sealed class FakeTransport(PresentationSource source, GateOutcome outcome)
        : IPresentationGateTransport
    {
        public PresentationSource Source => source;
        public bool WasUsed { get; private set; }

        public Task<GateOutcome> WaitForOutcomeAsync(
            Guid id, IProgress<GateOutcome>? progress = null, CancellationToken ct = default)
        {
            WasUsed = true;
            return Task.FromResult(outcome);
        }

        public Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
            Guid id, string? token, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>?>(
                new Dictionary<string, object?> { ["givenName"] = "Stuart" });
    }

    private void Arrange(params IPresentationGateTransport[] transports)
    {
        Services.AddMudServices();
        Services.AddSingleton<IQrPresentationService, QrPresentationService>();
        foreach (var t in transports) Services.AddSingleton(t);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void SelectsTheTransportMatchingTheSource()
    {
        var haip = new FakeTransport(PresentationSource.HaipExternalWallet, GateOutcome.Success);
        var wallet = new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Success);
        Arrange(haip, wallet);

        Render<PresentationRequestCard>(p => p
            .Add(c => c.RequestId, Guid.NewGuid())
            .Add(c => c.PresentationRequestUri, "openid4vp://authorize?x=1")
            .Add(c => c.Source, PresentationSource.SorchaWallet));

        wallet.WasUsed.Should().BeTrue("a SorchaWallet gate must not be polled against HAIP");
        haip.WasUsed.Should().BeFalse();
    }

    [Fact]
    public void RendersAnHonestMessageWhenNoTransportIsRegistered()
    {
        Arrange(new FakeTransport(PresentationSource.HaipExternalWallet, GateOutcome.Success));

        var cut = Render<PresentationRequestCard>(p => p
            .Add(c => c.RequestId, Guid.NewGuid())
            .Add(c => c.PresentationRequestUri, "openid4vp://authorize?x=1")
            .Add(c => c.Source, PresentationSource.SorchaWallet));

        cut.Markup.Should().Contain("couldn't start",
            "silence would look like an unscanned QR code, forever");
    }

    [Fact]
    public void UnreachableSaysSoRatherThanClaimingExpiry()
    {
        Arrange(new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Unreachable));

        var cut = Render<PresentationRequestCard>(p => p
            .Add(c => c.RequestId, Guid.NewGuid())
            .Add(c => c.PresentationRequestUri, "openid4vp://authorize?x=1")
            .Add(c => c.Source, PresentationSource.SorchaWallet));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Nothing was sent from your wallet");
            cut.Markup.Should().NotContain("expired");
        });
    }

    [Fact]
    public void SuccessSurfacesClaimsAndRendersChildContent()
    {
        Arrange(new FakeTransport(PresentationSource.SorchaWallet, GateOutcome.Success));
        IReadOnlyDictionary<string, object?>? received = null;

        var cut = Render<PresentationRequestCard>(p => p
            .Add(c => c.RequestId, Guid.NewGuid())
            .Add(c => c.PresentationRequestUri, "openid4vp://authorize?x=1")
            .Add(c => c.Source, PresentationSource.SorchaWallet)
            .Add(c => c.ClaimsFetchToken, "tok-abc")
            .Add(c => c.OnClaims, (IReadOnlyDictionary<string, object?>? claims) => received = claims)
            .AddChildContent("<p data-testid=\"form\">the form</p>"));

        cut.WaitForAssertion(() =>
        {
            received.Should().NotBeNull();
            cut.Markup.Should().Contain("data-testid=\"form\"");
        });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: FAIL — `PresentationRequestCard` not found

- [ ] **Step 3: Create the card**

Port the markup from `Sorcha.UI.Web.Client/Components/Credentials/PresentationRequestQrCard.razor` — the header avatar/title switch, the QR container, the claims table, the requested-claims / expiry `MudSimpleTable`, and the `Unreachable` alert added in #1325 — replacing every `HaipVerificationStates` comparison with `GateOutcome`. Keep `FormatClaimValue`'s behaviour but widen it to `object?`: a `JsonElement` still routes to `CredentialClaimDisplayFormatter.FormatJsonElementForDetailDisplay`, null renders as an em dash, anything else as `ToString()`. Never render a raw JSON object — that is how an unresolved SD-JWT `{"_sd":[…]}` digest array once reached a citizen's card.

Code block:

```razor
@using Sorcha.Blueprint.Models.Credentials
@using Sorcha.UI.Core.Services
@using Sorcha.UI.Core.Services.User.Presentation
@implements IDisposable
@inject IEnumerable<IPresentationGateTransport> Transports
@inject IQrPresentationService QrService
@inject ILogger<PresentationRequestCard> Logger

@code {
    /// <summary>The openid4vp://authorize?… URI to render as a QR code.</summary>
    [Parameter, EditorRequired] public string PresentationRequestUri { get; set; } = string.Empty;

    /// <summary>Identifier of the presentation request being awaited.</summary>
    [Parameter, EditorRequired] public Guid RequestId { get; set; }

    /// <summary>Which lifecycle owns the request — selects the transport.</summary>
    [Parameter] public PresentationSource Source { get; set; } = PresentationSource.HaipExternalWallet;

    /// <summary>F127 single-use claims-fetch token; null for HAIP.</summary>
    [Parameter] public string? ClaimsFetchToken { get; set; }

    /// <summary>The credential type being requested, for display.</summary>
    [Parameter] public string CredentialType { get; set; } = string.Empty;

    /// <summary>Claims requested for disclosure, for display.</summary>
    [Parameter] public List<string>? RequestedClaims { get; set; }

    /// <summary>When the request expires, for display.</summary>
    [Parameter] public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Where to send a citizen who has no matching credential.</summary>
    [Parameter] public string? LinkBackUrl { get; set; }

    /// <summary>Human-readable name of the gating credential, e.g. "Assured Identity".</summary>
    [Parameter] public string NameOfMissingCredentialType { get; set; } = "credential";

    /// <summary>Fires on every outcome, terminal or not.</summary>
    [Parameter] public EventCallback<GateOutcome> OnOutcome { get; set; }

    /// <summary>Fires once on success with the disclosed claims (null if none were available).</summary>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, object?>?> OnClaims { get; set; }

    /// <summary>Renders only after the gate clears — typically the form to prefill.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private GateOutcome _outcome = GateOutcome.Pending;
    private IReadOnlyDictionary<string, object?>? _claims;
    private string? _svgContent;
    private bool _claimsFetchFailed;
    private bool _noTransport;
    private Guid _awaitedRequestId;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    protected override void OnParametersSet()
    {
        if (RequestId == Guid.Empty || RequestId == _awaitedRequestId)
        {
            if (string.IsNullOrEmpty(_svgContent) && !string.IsNullOrEmpty(PresentationRequestUri))
            {
                _svgContent = QrService.GenerateSvgFromUri(PresentationRequestUri);
            }
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _awaitedRequestId = RequestId;
        _outcome = GateOutcome.Pending;
        _claims = null;
        _claimsFetchFailed = false;
        _svgContent = string.IsNullOrEmpty(PresentationRequestUri)
            ? null
            : QrService.GenerateSvgFromUri(PresentationRequestUri);

        var transport = Transports.FirstOrDefault(t => t.Source == Source);
        if (transport is null)
        {
            // Silence here would look exactly like an unscanned QR code, forever.
            Logger.LogError(
                "No IPresentationGateTransport registered for {Source}; the host must register one",
                Source);
            _noTransport = true;
            return;
        }

        _noTransport = false;
        _cts = new CancellationTokenSource();
        _ = AwaitOutcomeAsync(transport, RequestId, _cts.Token);
    }

    private async Task AwaitOutcomeAsync(
        IPresentationGateTransport transport, Guid requestId, CancellationToken ct)
    {
        var progress = new Progress<GateOutcome>(o => _ = InvokeAsync(() =>
        {
            if (_disposed || GateOutcomes.IsTerminal(_outcome)) return;
            _outcome = o;
            StateHasChanged();
        }));

        GateOutcome outcome;
        try
        {
            outcome = await transport.WaitForOutcomeAsync(requestId, progress, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Gate transport threw awaiting {RequestId}", requestId);
            outcome = GateOutcome.Unreachable;
        }

        // The presentation succeeded even if the claims fetch then fails. Reporting the whole
        // gate as failed would repeat the "no matching credential" lie (#1324) — surface the
        // claims problem as its own state.
        if (outcome == GateOutcome.Success)
        {
            _claims = await transport.FetchClaimsAsync(requestId, ClaimsFetchToken, ct);
            _claimsFetchFailed = _claims is null;
        }

        await InvokeAsync(async () =>
        {
            if (_disposed) return;
            _outcome = outcome;
            StateHasChanged();

            if (OnOutcome.HasDelegate) await OnOutcome.InvokeAsync(outcome);
            if (outcome == GateOutcome.Success && OnClaims.HasDelegate)
                await OnClaims.InvokeAsync(_claims);
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
```

Markup requirements — these are the states that must render:

| State | Must render |
|---|---|
| `Pending` | QR + `MudProgressLinear` + "Waiting for wallet to scan…" |
| `Submitted` | `MudProgressCircular` + "Verifying presented credential…" |
| `Success` | success alert, the disclosed-claims table, then `ChildContent` |
| `Success` + `_claimsFetchFailed` | success alert plus a warning that the details couldn't be retrieved — **not** an error implying the presentation failed |
| `Declined` | error alert — the holder denied it |
| `Expired` | warning alert — a new request is needed |
| `Abandoned` | warning alert — the wallet didn't respond in time |
| `Unreachable` | error alert containing "Nothing was sent from your wallet", plus `LinkBackUrl` if set |
| `_noTransport` | error alert: "We couldn't start this verification." |

- [ ] **Step 4: Register both transports**

In `PresentationServiceCollectionExtensions.AddSorchaPresentationGate`, after the existing `AddHttpClient<IPresentationSignal, PresentationSignal>` block:

```csharp
        services.AddHttpClient<SorchaWalletGateTransport>(client =>
        {
            client.BaseAddress = new Uri(baseAddress);
        });
        services.AddTransient<IPresentationGateTransport>(
            sp => sp.GetRequiredService<SorchaWalletGateTransport>());

        // HAIP's transport is registered only where IHaipOfferService is — the card falls back to
        // an honest error when a host asks for a source it hasn't wired.
        services.AddTransient<IPresentationGateTransport>(sp => new HaipGateTransport(
            sp.GetRequiredService<IHaipOfferService>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<HaipGateTransport>>()));
```

Add the `Sorcha.UI.Core.Services.Credentials` using. Note `IPresentationSignal` is a transient typed client, so each `SorchaWalletGateTransport` gets its own — correct, since a signal instance tracks one request.

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/PresentationRequestCard.razor \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/PresentationServiceCollectionExtensions.cs \
        tests/Sorcha.UI.Core.Tests/Components/Presentation/PresentationRequestCardTests.cs
git commit -m "feat: [F127] - one presentation card over two gate transports"
```

---

### Task 8: Wire the web app and retire the old surfaces

The web app never called `AddSorchaPresentationGate` — only `samples/strathcarron-portal/Program.cs:66` does. Without this step the SorchaWallet transport resolves to nothing and the card renders its no-transport error, which is honest but still broken.

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/PresentationRequestQrDialog.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/NewSubmissionWorkspace.razor:235-247`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor:529-540`
- Modify: `samples/strathcarron-portal/Pages/*.razor` (every `CredentialGateComponent` use)
- Delete: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/CredentialGate/CredentialGateComponent.razor`
- Delete: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/PresentationRequestQrCard.razor`
- Delete: `tests/Sorcha.UI.Core.Tests/Components/CredentialGate/CredentialGateComponentTests.cs`

- [ ] **Step 1: Register the gate stack in the web client**

In `Sorcha.UI.Web.Client/Program.cs`, alongside the other `Sorcha.UI.Core` registrations:

```csharp
builder.Services.AddSorchaPresentationGate(builder.HostEnvironment.BaseAddress);
```

The value must be the origin serving `/api/presentations/*` and `/hubs/blueprint`. If the app routes through a gateway origin held in configuration rather than `HostEnvironment.BaseAddress`, pass that instead.

- [ ] **Step 2: Pass source and token from both call sites**

In `NewSubmissionWorkspace.razor` and `MyActions.razor`, add to the `DialogParameters` that open `PresentationRequestQrDialog`:

```csharp
                    { x => x.Source, presentation.Source },
                    { x => x.ClaimsFetchToken, presentation.ClaimsFetchToken },
```

Use the existing local's name in place of `presentation` — read the surrounding lines rather than assuming.

- [ ] **Step 3: Forward them through the dialog to the card**

In `PresentationRequestQrDialog.razor`, add matching `[Parameter]`s (`PresentationSource Source`, `string? ClaimsFetchToken`), swap `<PresentationRequestQrCard …>` for `<PresentationRequestCard …>`, and rewire the close-on-terminal handler from `OnStateChanged` / `OnVerified` to `OnOutcome`:

```csharp
    private async Task HandleOutcomeAsync(GateOutcome outcome)
    {
        if (!GateOutcomes.IsTerminal(outcome)) return;

        await Task.Delay(outcome == GateOutcome.Success
            ? HaipPollingDefaults.VerifiedCloseDelayMs
            : HaipPollingDefaults.ErrorCloseDelayMs);

        MudDialog.Close(DialogResult.Ok(outcome));
    }
```

Adjust the dialog's call sites if they inspected the old `HaipVerificationResult` result type.

- [ ] **Step 4: Migrate the sample portal**

Run `grep -rn "CredentialGateComponent" samples/` and replace each use. `CredentialGateInit` maps straight across:

```razor
@* was: <CredentialGateComponent Init="@_init" OnPresented="@HandlePresentedAsync" … > *@
<PresentationRequestCard RequestId="@_init.PresentationRequestId"
                         PresentationRequestUri="@_init.AuthorizationRequestUri"
                         ClaimsFetchToken="@_init.ClaimsFetchToken"
                         Source="PresentationSource.SorchaWallet"
                         LinkBackUrl="/services/driving-licence"
                         NameOfMissingCredentialType="Assured Identity"
                         OnClaims="@HandleClaimsAsync">
    <BlueBadgeForm Disclosed="@_disclosed" />
</PresentationRequestCard>
```

The page's handler changes shape from `DisclosedClaimsResponse` to `IReadOnlyDictionary<string, object?>?`. Where a page used `disclosed.SubjectDisplayName`, compose it from the `givenName` / `familyName` claims. Where `Init` was null (the no-gate case), render the child content directly rather than passing an empty `RequestId` — the card has no no-gate mode by design.

Add `Sorcha.UI.Core.Components.Presentation` to the sample's `_Imports.razor`, and drop the `Components.CredentialGate` import if nothing else uses it.

- [ ] **Step 5: Delete the retired surfaces**

```bash
git rm src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/CredentialGate/CredentialGateComponent.razor \
       src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/PresentationRequestQrCard.razor \
       tests/Sorcha.UI.Core.Tests/Components/CredentialGate/CredentialGateComponentTests.cs
```

Then `grep -rn "PresentationRequestQrCard\|CredentialGateComponent" src/ samples/ tests/` — expect no hits outside `obj/`.

- [ ] **Step 6: Build the whole solution and run the affected suites**

```bash
dotnet build Sorcha.sln -v q --nologo
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo
dotnet test tests/Sorcha.UI.ContractTests/Sorcha.UI.ContractTests.csproj --nologo
dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj --nologo
```
Expected: all PASS. The solution build is what proves the sample-portal migration compiles.

- [ ] **Step 7: Run the CI gates that touch this code**

```bash
pwsh scripts/check-no-snackbar.ps1
pwsh scripts/check-publish-paths.ps1
```
Expected: both PASS.

- [ ] **Step 8: Update the docs**

- `specs/127-credential-gated-service/spec.md` — `CredentialGateComponent` is superseded by `PresentationRequestCard`; the gate now runs behind `IPresentationGateTransport`.
- `.claude/skills/sorcha-architecture/SKILL.md` — F127 section: the card, the seam, and the two transports.
- `.specify/MASTER-TASKS.md` — mark the work done.

- [ ] **Step 9: Commit and open the PR**

```bash
git add -- src/Apps/Sorcha.UI samples/strathcarron-portal tests specs .claude .specify
git commit -m "feat: [F127] - route SorchaWallet gates through the presentation lifecycle"
git push -u origin feature/presentation-gate-transport
gh pr create --fill
```

---

## Verification

- [ ] `rehearse.ps1 -Scenario cyber -Target n1` still passes all four paths.
- [ ] **Manual, on n1:** submit the AIAS Cyber questionnaire on `/app`. In DevTools → Network, confirm the QR dialog polls `/api/presentations/{id}/status` and **not** `/api/v1/verifier/requests/{id}/result`, and that it reaches a terminal state instead of sitting for five minutes and reporting "Expired". Neither the rehearsal nor any unit test exercises this UI path — that is why the bug shipped, and a green suite is not evidence it is fixed.
- [ ] **Deployment note from the spec:** update the existing blueprints with these feature changes **before** the strathcarron portal is deployed. Retiring `CredentialGateComponent` is safe today only because that portal is not currently deployed.
