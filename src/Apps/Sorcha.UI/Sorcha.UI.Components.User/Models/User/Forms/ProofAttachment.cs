// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// Represents a Zero-Knowledge Proof attachment submitted through the form.
/// </summary>
public class ProofAttachment
{
    /// <summary>
    /// Human-readable description of what is proven
    /// </summary>
    public string ClaimDescription { get; set; } = string.Empty;

    /// <summary>
    /// Proof system identifier (e.g., "groth16", "plonk")
    /// </summary>
    public string ProofType { get; set; } = string.Empty;

    /// <summary>
    /// Serialized proof data
    /// </summary>
    public byte[] ProofData { get; set; } = [];

    /// <summary>
    /// Public inputs to the proof
    /// </summary>
    public Dictionary<string, object> PublicInputs { get; set; } = new();
}
