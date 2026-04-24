// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 111 — string keys and well-known values used in
/// <c>TransactionMetaData.TrackingData</c> for presentation lifecycle
/// transactions. Centralised so the builder, retry-gate, and future audit
/// consumers cannot drift apart.
/// </summary>
internal static class PresentationMetadataKeys
{
    /// <summary>Key: lifecycle outcome kind ("success" or "decline").</summary>
    public const string OutcomeKind = "outcomeKind";

    /// <summary>Key: consumer name (e.g. "haip").</summary>
    public const string ConsumerName = "consumerName";

    /// <summary>Key: presentation request id (Guid).</summary>
    public const string PresentationRequestId = "presentationRequestId";

    /// <summary>Value: success kind. Matches <c>PresentationOutcomeKind.Success.ToString().ToLowerInvariant()</c>.</summary>
    public const string OutcomeKindSuccess = "success";

    /// <summary>Value: decline kind.</summary>
    public const string OutcomeKindDecline = "decline";
}
