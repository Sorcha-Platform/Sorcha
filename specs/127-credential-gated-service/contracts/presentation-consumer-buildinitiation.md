# Contract: `IPresentationConsumer.BuildInitiationAsync` extension

**Library**: `Sorcha.PresentationLifecycle.Abstractions`
**Feature**: F127 (lands the deferred F111 "non-HAIP initiation contract extension")
**Reconciliation note**: F111's `IPresentationLifecycleService.InitiateAsync` docstring noted "Non-HAIP consumers (e.g. file-upload-deadline) will land in a future phase by extending `IPresentationConsumer` with an initiation contract." F127's `SorchaWalletPresentationConsumer` is the second consumer; this contract lands the deferred extension.

## Why a new method

F111's `InitiateAsync` is currently hardcoded to produce HAIP's specific OID4VP authorization-request URI shape. When a second consumer (`SorchaWalletPresentationConsumer`) appears, the lifecycle service needs a way to ask the consumer "what wire shape do you want the citizen's wallet to see?" — that's what `BuildInitiationAsync` answers.

HAIP's current path can either:
- Migrate to use `BuildInitiationAsync` (cleaner; F127 doesn't require it but should call it out as a small follow-up); OR
- Stay on the hardcoded path with the dispatcher falling back when the consumer doesn't override (`BuildInitiationAsync` default throws `NotSupportedException`).

F127 ships the contract with the default-throws semantics so HAIP doesn't need to change in this PR. Migration is a separate cleanup.

## New method signature

```csharp
namespace Sorcha.PresentationLifecycle.Abstractions;

public interface IPresentationConsumer
{
    // existing members …
    string ConsumerName { get; }
    Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Build the wallet-facing artifact (OID4VP request URI + optional alternative URIs)
    /// for a new presentation attempt. Called by
    /// <see cref="IPresentationLifecycleService.InitiateAsync"/> after the
    /// <c>presentation-initiated</c> transaction is written. The lifecycle
    /// service returns the descriptor to the council page so it can render the
    /// hybrid universal QR / tap-link affordance.
    /// </summary>
    /// <remarks>
    /// Default implementation throws <see cref="NotSupportedException"/>;
    /// consumers using the HAIP-style hardcoded initiation path don't need to
    /// override. New consumers (starting with <c>"sorcha-wallet"</c> in F127)
    /// override this method.
    /// </remarks>
    Task<ConsumerInitiationDescriptor> BuildInitiationAsync(
        PresentationInitiationContext context,
        CancellationToken cancellationToken)
        => throw new NotSupportedException(
            $"Consumer '{ConsumerName}' does not implement BuildInitiationAsync. " +
            "If this is a new non-HAIP consumer, override the method; if HAIP, the lifecycle service is using its existing hardcoded initiation path.");
}
```

(Default interface method — preserves existing implementations.)

## `ConsumerInitiationDescriptor`

```csharp
namespace Sorcha.PresentationLifecycle.Abstractions;

/// <summary>
/// Return value of <see cref="IPresentationConsumer.BuildInitiationAsync"/>.
/// Carries the wire artifacts the council page renders to the citizen.
/// </summary>
public sealed record ConsumerInitiationDescriptor(
    string AuthorizationRequestUri,
    string? RequestUri,
    string? Nonce);
```

`AuthorizationRequestUri` is the primary artifact (OID4VP `openid4vp://…` URI). `RequestUri` and `Nonce` are optional, included when the consumer's protocol exposes them separately.

## Contract invariants

- Idempotency: not required at the consumer level. The lifecycle service guarantees one `InitiateAsync` call per `presentationRequestId`.
- Side effects: NONE inside `BuildInitiationAsync`. The method is pure — given a context, return a descriptor. Writing register transactions, stashing pending state, etc. are the lifecycle service's responsibility.
- The `PresentationInitiationContext.PresentationRequestId` (Guid) MUST appear in the returned `AuthorizationRequestUri` so the citizen's wallet can include it when posting the callback.

## Lifecycle dispatch (changes inside `PresentationLifecycleService`)

```csharp
// Inside InitiateAsync, after writing presentation-initiated tx + stashing PendingPresentation:
var consumer = _consumers.FirstOrDefault(c => c.ConsumerName == credentialRequirement.PresentationSource);
if (consumer is null)
    throw new InvalidOperationException($"No consumer registered with name '{credentialRequirement.PresentationSource}'.");

// New path: ask the consumer for its initiation artifact.
ConsumerInitiationDescriptor descriptor;
try
{
    descriptor = await consumer.BuildInitiationAsync(initiationContext, ct);
}
catch (NotSupportedException)
{
    // Consumer doesn't implement the new method — fall back to the existing
    // hardcoded path. HAIP today; cleaned up in a follow-up.
    descriptor = BuildLegacyHaipDescriptor(initiationContext);
}

return new PresentationInitiationResult(
    PresentationRequestId: presentationRequestId,
    AuthorizationRequestUri: descriptor.AuthorizationRequestUri,
    RequestUri: descriptor.RequestUri,
    Nonce: descriptor.Nonce,
    ExpiresAt: pendingPresentation.CreatedAt.AddSeconds(validityWindowSeconds),
    InitiatedTransactionId: initiatedTxId,
    ClaimsFetchToken: consumerOptsIntoClaimsFetch ? claimsFetchToken : null);
```

## Sorcha-wallet consumer's implementation

`SorchaWalletPresentationConsumer.BuildInitiationAsync` produces an OID4VP `openid4vp://` URI carrying:

- `client_id` = the council org's DID (e.g. `did:sorcha:org:strathcarron-council`).
- `response_type` = `vp_token`.
- `presentation_definition` = a DIF presentation definition derived from the `credentialRequirement` (credential type, issuer allowlist, required claims).
- `nonce` = 16-byte URL-safe base64 (echoed by the wallet in the signed VP).
- `response_uri` = absolute URL of F111's existing `POST /api/presentations/callbacks/sorcha-wallet/{presentationRequestId}` endpoint.

The wallet uses this URI to scan / tap; on success it posts the signed VP back through `response_uri`; F111's existing `HandleOutcomeAsync` dispatches to `SorchaWalletPresentationConsumer.VerifyAsync`.

## Tests

The F127 task list covers:

- Unit: `SorchaWalletPresentationConsumer.BuildInitiationAsync` produces a well-formed OID4VP URI with all required parameters; nonce is fresh per call; `response_uri` resolves to the F111 callback endpoint.
- Unit: `IPresentationConsumer.BuildInitiationAsync` default implementation throws `NotSupportedException` with a useful message.
- Integration: `PresentationLifecycleService.InitiateAsync` against a Sorcha-wallet consumer dispatches to `BuildInitiationAsync`; against the existing HAIP consumer dispatches to the legacy path; the test fixture confirms both flows still produce the same `PresentationInitiationResult` shape.
