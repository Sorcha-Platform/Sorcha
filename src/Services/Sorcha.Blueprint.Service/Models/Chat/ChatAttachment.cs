// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models.Chat;

/// <summary>
/// A binary attachment (image or PDF) sent alongside a user chat message.
/// The data is base64-encoded; the orchestrator forwards it to the AI provider as a
/// content block (Anthropic ImageContent or DocumentContent).
/// </summary>
public record ChatAttachment
{
    /// <summary>The kind of attachment — drives which Anthropic content block is emitted.</summary>
    public required ChatAttachmentKind Kind { get; init; }

    /// <summary>IANA media type (e.g. <c>image/jpeg</c>, <c>application/pdf</c>).</summary>
    public required string MediaType { get; init; }

    /// <summary>Raw base64-encoded payload (no <c>data:</c> URI prefix).</summary>
    public required string Base64Data { get; init; }

    /// <summary>Optional original file name, retained for display in chat history.</summary>
    public string? FileName { get; init; }
}

/// <summary>Discriminator for <see cref="ChatAttachment"/>.</summary>
public enum ChatAttachmentKind
{
    /// <summary>Renders as an Anthropic image content block.</summary>
    Image,

    /// <summary>Renders as an Anthropic document (PDF) content block.</summary>
    Pdf
}
