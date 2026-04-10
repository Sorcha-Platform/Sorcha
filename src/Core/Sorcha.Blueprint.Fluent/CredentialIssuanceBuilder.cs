// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Fluent;

/// <summary>
/// Fluent builder for configuring credential issuance on blueprint actions.
/// </summary>
public class CredentialIssuanceBuilder
{
    private readonly CredentialIssuanceConfig _config = new();
    private readonly List<ClaimMapping> _claimMappings = new();
    private readonly List<string> _disclosable = new();

    /// <summary>
    /// Sets the credential type to issue (e.g., "LicenseCredential").
    /// </summary>
    public CredentialIssuanceBuilder OfType(string credentialType)
    {
        _config.CredentialType = credentialType;
        return this;
    }

    /// <summary>
    /// Maps an action data field to a credential claim.
    /// </summary>
    /// <param name="claimName">The claim key in the issued credential.</param>
    /// <param name="sourceField">JSON Pointer to the action data field (e.g., "/licenseType").</param>
    public CredentialIssuanceBuilder MapClaim(string claimName, string sourceField)
    {
        _claimMappings.Add(new ClaimMapping { ClaimName = claimName, SourceField = sourceField });
        return this;
    }

    /// <summary>
    /// Sets the recipient participant ID.
    /// </summary>
    public CredentialIssuanceBuilder ToRecipient(string participantId)
    {
        _config.RecipientParticipantId = participantId;
        return this;
    }

    /// <summary>
    /// Sets the credential expiry duration (ISO 8601 duration, e.g., "P365D").
    /// </summary>
    public CredentialIssuanceBuilder ExpiresAfter(string isoDuration)
    {
        _config.ExpiryDuration = isoDuration;
        return this;
    }

    /// <summary>
    /// Records the issued credential on a register for public queryability.
    /// </summary>
    public CredentialIssuanceBuilder RecordOnRegister(string registerId)
    {
        _config.RegisterId = registerId;
        return this;
    }

    /// <summary>
    /// Marks a top-level claim as supporting selective disclosure.
    /// </summary>
    public CredentialIssuanceBuilder MakeDisclosable(string claimName)
    {
        _disclosable.Add(claimName);
        return this;
    }

    /// <summary>
    /// Marks a nested field as independently disclosable using a JSON Pointer path.
    /// Supports object properties (e.g., "/address/locality") and array elements
    /// (e.g., "/qualifications/0"). Can be mixed with top-level <see cref="MakeDisclosable"/>.
    /// </summary>
    /// <param name="jsonPointer">JSON Pointer path (must start with "/").</param>
    public CredentialIssuanceBuilder MakeDisclosablePath(string jsonPointer)
    {
        if (!jsonPointer.StartsWith('/'))
            throw new ArgumentException("JSON Pointer path must start with '/'", nameof(jsonPointer));

        _disclosable.Add(jsonPointer);
        return this;
    }

    internal CredentialIssuanceConfig Build()
    {
        if (_claimMappings.Count > 0)
            _config.ClaimMappings = _claimMappings;

        if (_disclosable.Count > 0)
            _config.Disclosable = _disclosable;

        return _config;
    }
}
