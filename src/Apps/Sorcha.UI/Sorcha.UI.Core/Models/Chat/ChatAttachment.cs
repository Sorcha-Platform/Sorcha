// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Chat;

/// <summary>
/// Client-side mirror of the server <c>ChatAttachment</c> shape. SignalR serialises this
/// type as the wire payload; field names must stay in lock-step with the server record.
/// </summary>
public record ChatAttachment
{
    /// <summary>The kind of attachment.</summary>
    public required ChatAttachmentKind Kind { get; init; }

    /// <summary>IANA media type (e.g. <c>image/jpeg</c>, <c>application/pdf</c>).</summary>
    public required string MediaType { get; init; }

    /// <summary>Raw base64-encoded payload (no <c>data:</c> URI prefix).</summary>
    public required string Base64Data { get; init; }

    /// <summary>Optional original file name.</summary>
    public string? FileName { get; init; }
}

/// <summary>Discriminator for <see cref="ChatAttachment"/>.</summary>
public enum ChatAttachmentKind
{
    /// <summary>Image content (jpeg / png / webp / gif).</summary>
    Image,

    /// <summary>PDF document.</summary>
    Pdf
}
