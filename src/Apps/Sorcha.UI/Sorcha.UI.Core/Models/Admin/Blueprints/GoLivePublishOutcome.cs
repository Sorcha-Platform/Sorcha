// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.UI.Core.Models.Blueprints;

/// <summary>
/// UI-level discriminated outcome of a Feature 142 Go-live publish attempt (T039/T040). Extends
/// the service-client <see cref="PublishBlueprintOutcome"/> (which only models success vs the 409
/// rehearsal soft-gate) with the <c>403</c> no-rights and generic-error cases the Go-live UI must
/// surface distinctly.
/// </summary>
public sealed record GoLivePublishOutcome
{
    /// <summary>The kind of outcome.</summary>
    public required GoLivePublishStatus Status { get; init; }

    /// <summary>The successful publish result (version + overridden flag); set only on <see cref="GoLivePublishStatus.Published"/>.</summary>
    public PublishBlueprintResult? Result { get; init; }

    /// <summary>The 409 soft-gate body (carries the unrehearsed exec-def hash); set only on <see cref="GoLivePublishStatus.RehearsalRequired"/>.</summary>
    public RehearsalRequiredError? RehearsalRequired { get; init; }

    /// <summary>A human-readable error message for <see cref="GoLivePublishStatus.Forbidden"/> / <see cref="GoLivePublishStatus.Error"/>.</summary>
    public string? Message { get; init; }

    /// <summary>The publish committed a new immutable version.</summary>
    public static GoLivePublishOutcome Published(PublishBlueprintResult result) =>
        new() { Status = GoLivePublishStatus.Published, Result = result };

    /// <summary>The publish was blocked by the rehearsal soft gate (HTTP 409).</summary>
    public static GoLivePublishOutcome NeedsRehearsal(RehearsalRequiredError error) =>
        new() { Status = GoLivePublishStatus.RehearsalRequired, RehearsalRequired = error };

    /// <summary>The caller is not authorised to publish to the register (HTTP 403).</summary>
    public static GoLivePublishOutcome Refused(string message) =>
        new() { Status = GoLivePublishStatus.Forbidden, Message = message };

    /// <summary>The publish failed for another reason (network / server error).</summary>
    public static GoLivePublishOutcome Errored(string message) =>
        new() { Status = GoLivePublishStatus.Error, Message = message };
}

/// <summary>The status discriminator for <see cref="GoLivePublishOutcome"/>.</summary>
public enum GoLivePublishStatus
{
    /// <summary>Published successfully (200) — a new immutable version was committed.</summary>
    Published,

    /// <summary>Blocked by the rehearsal soft gate (409 REHEARSAL_REQUIRED) — may be overridden by an authorised user.</summary>
    RehearsalRequired,

    /// <summary>Refused — the caller lacks publish-governance rights on the register (403).</summary>
    Forbidden,

    /// <summary>Failed for another reason.</summary>
    Error
}
