# Consumer Contract: IPresentationConsumer

**Feature**: 111-presentation-lifecycle
**Project**: `src/Common/Sorcha.PresentationLifecycle.Abstractions` (new)

External consumers of the Timebound Presentation Lifecycle (HAIP Service today, others in future) implement `IPresentationConsumer`. The contract deliberately carries no OpenID4VP or HAIP-specific types — the lifecycle primitive is consumer-agnostic.

---

## Interface

```csharp
namespace Sorcha.PresentationLifecycle.Abstractions;

public interface IPresentationConsumer
{
    /// <summary>
    /// Stable short identifier (e.g. "haip", "file-upload-deadline"). Referenced by
    /// blueprints (via credentialRequirements.PresentationSource) and carried in
    /// the PresentationInitiated transaction metadata.
    /// </summary>
    string ConsumerName { get; }

    /// <summary>
    /// Called by Blueprint Service's PresentationLifecycleService when this consumer's
    /// verifier callback is received. The consumer verifies the payload and returns a
    /// lifecycle outcome. The lifecycle service handles transaction writing; the
    /// consumer only verifies.
    /// </summary>
    Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken cancellationToken);
}
```

## Records

```csharp
public record PresentationInitiationContext(
    Guid PresentationRequestId,
    Guid InstanceId,
    int ActionId,
    string RegisterId,
    string BlueprintId,
    string SubmitterWallet,
    byte[] RequirementsDigest,
    DateTimeOffset InitiatedAt);

public record PresentationOutcome(
    PresentationOutcomeKind Kind,
    IReadOnlyDictionary<string, object>? VerifiedClaims,
    PresentationDeclineReason? Reason,
    IReadOnlyDictionary<string, object>? VerifierDiagnostics,
    string? PresentationSubmissionHash);

public enum PresentationOutcomeKind
{
    Success,
    Decline
}

public enum PresentationDeclineReason
{
    ExpiredCredential,
    WrongIssuer,
    Revoked,
    SchemaMismatch,
    SignatureInvalid,
    ActionNoLongerAvailable,
    VerifierError
}
```

## DI registration pattern

```csharp
// In Sorcha.Haip.Service/Program.cs
builder.Services.AddSingleton<IPresentationConsumer, HaipPresentationConsumer>();
```

Blueprint Service's `PresentationLifecycleService` receives `IEnumerable<IPresentationConsumer>` via DI and resolves by `ConsumerName` when a callback arrives at `/api/presentations/callbacks/{consumerName}`.

## Contract invariants consumers MUST uphold

1. **`VerifyAsync` is synchronous w.r.t. the caller's await** — no background processing after return. If the consumer needs to call an external verifier (as HAIP does internally), it does so within the method.
2. **No side effects on the register** — consumers MUST NOT write to the register directly. They return an outcome; the lifecycle service writes it.
3. **Idempotency at the consumer level is NOT required** — the lifecycle service guards via the outcome sentinel. If `VerifyAsync` is called twice for the same requestId, the consumer can re-verify (at worst a duplicate CPU cost) and the lifecycle service discards the duplicate.
4. **`VerifiedClaims` carry ONLY the claims the blueprint's `requiredClaims` asked for** — consumers MUST filter out anything the verifier returned beyond the required set, to preserve minimal disclosure.
5. **`VerifierDiagnostics` format is consumer-defined** — logged and optionally included on the register when `outcomeDetailLevel = "verbose"`. Consumers SHOULD NOT put PII in diagnostics.
6. **`verifierPayload` is passed through uninterpreted by the lifecycle service** — the consumer deserialises into its own types. This keeps the lifecycle service free of consumer-specific payload shapes.

## Example consumer: HAIP

```csharp
public sealed class HaipPresentationConsumer : IPresentationConsumer
{
    public string ConsumerName => "haip";

    public async Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken ct)
    {
        // HAIP-specific deserialisation of verifierPayload
        var haipResult = (HaipVerificationResult)verifierPayload;

        if (haipResult.IsValid)
        {
            return new PresentationOutcome(
                Kind: PresentationOutcomeKind.Success,
                VerifiedClaims: haipResult.VerifiedClaims,
                Reason: null,
                VerifierDiagnostics: null,
                PresentationSubmissionHash: haipResult.SubmissionHash);
        }

        return new PresentationOutcome(
            Kind: PresentationOutcomeKind.Decline,
            VerifiedClaims: null,
            Reason: MapHaipErrorToDeclineReason(haipResult.ErrorCode),
            VerifierDiagnostics: haipResult.Diagnostics,
            PresentationSubmissionHash: null);
    }
}
```

## Future consumer examples (not implemented in this feature)

- **File-upload deadline consumer** — receives uploaded file, verifies signature + checksum against declared hash, returns success with the file-hash claim or decline with reason.
- **Step-up MFA consumer** — receives OTP / WebAuthn assertion, returns success with the authenticator-claim or decline with auth-failed reason.
- **External-signature consumer** — receives a signed DocuSign envelope, returns success with signatory identity or decline with signature-invalid.

None of these require changes to the lifecycle primitive — they register as new `IPresentationConsumer` implementations with their own `ConsumerName`.
