// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// The approval payload must survive the Validator re-serialising it (Feature 189).
/// </summary>
/// <remarks>
/// <para>
/// The submitter hashes the JSON it produces and sends both the hash and the parsed element. The
/// Validator re-serialises that element with its own options and recomputes the hash, so the two
/// serialisations must be byte-identical or the transaction is refused
/// <c>TX_012 "Payload hash mismatch"</c>.
/// </para>
/// <para>
/// <b>Found live on n1, and it was intermittent.</b> The default JSON encoder escapes <c>+</c> as
/// <c>+</c> while the Validator's does not, so an approval whose base64 signature or public key
/// happened to contain a <c>+</c> was rejected and one whose bytes did not sealed normally. The first
/// of two approvals passed and the second failed, on identical code. No unit test could see it
/// because nothing compared the two sides.
/// </para>
/// </remarks>
public sealed class GovernanceApprovalPayloadHashStabilityTests
{
    /// <summary>The Validator's re-canonicalisation, as <c>ValidationEngine</c> performs it.</summary>
    private static readonly JsonSerializerOptions ValidatorReSerialisation = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string ReSerialise(string canonicalJson) =>
        JsonSerializer.Serialize(JsonDocument.Parse(canonicalJson).RootElement, ValidatorReSerialisation);

    private static GovernanceApprovalActionPayload Payload(string signature, string publicKey) => new()
    {
        ProposalId = "c4bcc68d180f64442b87044a8f80433f",
        ApproverDid = "did:sorcha:w:ws11qapprover",
        IsApproval = true,
        Signature = signature,
        PublicKey = publicKey,
        AuthMethod = ApprovalAuthMethod.Service,
        Authorisation = new ApprovalAuthorisation
        {
            Kind = AuthorisationKind.Direct,
            IndividualDid = "did:sorcha:w:ws11qindividual",
            Signature = signature,
            PublicKey = publicKey,
            AuthMethod = ApprovalAuthMethod.Service,
            Algorithm = SignatureAlgorithm.ED25519,
        }
    };

    [Theory]
    // The case that broke live: '+' is escaped by the default encoder and not by the Validator's.
    [InlineData("fFE+9QNpjWLk9+hPDXbfIFctbmex6ONxaOnMVUAkjWA=", "uS780HTirYETFCiao2cn5mWe2bQ7EQCkuPxNfpkpyYc=")]
    // Its siblings, so a fix that special-cases one character does not pass.
    [InlineData("a/b+c=", "d&e<f>g")]
    [InlineData("plain", "alsoplain")]
    public void TheJsonTheSubmitterHashes_SurvivesTheValidatorsReSerialisation(string signature, string publicKey)
    {
        var canonical = JsonSerializer.Serialize(
            Payload(signature, publicKey), GovernanceApprovalActionPayload.CanonicalJsonOptions);

        ReSerialise(canonical).Should().Be(canonical,
            "the Validator re-serialises the payload and recomputes the hash — a divergence here is a "
            + "TX_012 rejection whose cause names neither side");
    }

    [Fact]
    public void TheHashIsUnchangedByReSerialisation()
    {
        // The property that actually matters, asserted directly rather than inferred from the strings.
        var canonical = JsonSerializer.Serialize(
            Payload("fFE+9QNpjWLk9+hPDXbfIFctbmex6ONxaOnMVUAkjWA=", "uS780HTirYETFCiao2cn5mWe2bQ7EQCkuPxNfpkpyYc="),
            GovernanceApprovalActionPayload.CanonicalJsonOptions);

        var submitted = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var recomputed = SHA256.HashData(Encoding.UTF8.GetBytes(ReSerialise(canonical)));

        recomputed.Should().Equal(submitted);
    }
}
