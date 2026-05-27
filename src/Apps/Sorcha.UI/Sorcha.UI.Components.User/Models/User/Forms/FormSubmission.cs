// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// Output of a completed form submission, including data, signature, and attachments.
/// </summary>
public class FormSubmission
{
    /// <summary>
    /// User-entered form values keyed by scope
    /// </summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>
    /// Engine-calculated values
    /// </summary>
    public Dictionary<string, object?> CalculatedValues { get; set; } = new();

    /// <summary>
    /// Wallet signature over the data hash (base64)
    /// </summary>
    public string? Signature { get; set; }

    /// <summary>
    /// Address of the wallet that signed
    /// </summary>
    public string SigningWalletAddress { get; set; } = string.Empty;

    /// <summary>
    /// Presented credentials satisfying action requirements
    /// </summary>
    public List<CredentialPresentation> CredentialPresentations { get; set; } = [];

    /// <summary>
    /// Uploaded file attachments
    /// </summary>
    public List<FileAttachmentInfo> FileAttachments { get; set; } = [];

    /// <summary>
    /// ZKP proof attachments
    /// </summary>
    public List<ProofAttachment> ProofAttachments { get; set; } = [];
}
