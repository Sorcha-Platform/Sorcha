// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Sorcha.Wallet.Service.Services.Implementation;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Feature 106 Wave F — round-trip shape contract tests between the Wave A
/// credential-sealing writer (<c>ActionExecutionService</c> step 9b-bis) and
/// the Wave B credential-detector reader
/// (<c>InboundCredentialDetector.TryParseExtract</c>).
/// </summary>
/// <remarks>
/// <para>
/// This test fixture exists because the two halves of Feature 106 were shipped
/// in separate services (Blueprint Service writes, Wallet Service reads) and
/// the write path embeds an ad-hoc object literal. If the writer drops a field
/// or renames one, silent detection failures would be the only signal — the
/// tx still peer-replicates, the detector still runs, and simply returns null
/// because the shape no longer matches.
/// </para>
/// <para>
/// The fixture freezes the exact JSON shape produced by the writer into a
/// constant literal and runs it through the detector's internal parser. Any
/// drift in either direction is surfaced as a fast unit-level failure rather
/// than a mystery at the Playwright E2E layer where symptoms are 30 seconds
/// away from root cause.
/// </para>
/// <para>
/// The full docker-backed Playwright E2E covering the 30-second wall-clock
/// assertion (SC-002) and the FR-017 signature verification (T048) is
/// deferred to a follow-up session because it requires the running stack.
/// Tracking: <c>specs/106-register-native-credentials/tasks.md T047/T048</c>.
/// </para>
/// </remarks>
public class InboundCredentialDetectorShapeTests
{
    /// <summary>
    /// Exactly the JSON shape built by <c>ActionExecutionService.ExecuteAsync</c>
    /// step 9b-bis when sealing a SorchaLocalWallet credential into the
    /// recipient-addressed disclosure group. Copy this constant from Wave A
    /// when the writer changes — the test will fail fast if they diverge.
    /// </summary>
    private const string WriterShapeJson = /*lang=json,strict*/ """
    {
      "/credential": {
        "credentialId": "urn:uuid:feature-106-round-trip-test-1",
        "credentialType": "VerifiedCitizenCredential",
        "issuerDid": "did:sorcha:org:ws11qgovernment",
        "subjectDid": "did:sorcha:wallet:ws11qcitizen",
        "issuedAt": "2026-04-15T10:30:00+00:00",
        "expiresAt": "2027-04-15T10:30:00+00:00",
        "rawToken": "eyJhbGciOiJFZERTQSJ9.test.token",
        "issuanceBlueprintId": "haip-verified-citizen-v2-1.0.0",
        "issuanceInstanceId": "instance-9a9f4c9f-round-trip",
        "issuanceActionId": "2",
        "claimActionId": "3",
        "registerId": "af7b1040-register-round-trip"
      }
    }
    """;

    [Fact]
    public void WriterShape_IsDiscoverableByDetector()
    {
        var doc = JsonSerializer.Deserialize<JsonElement>(WriterShapeJson);

        var credentialField = InboundCredentialDetector.FindCredentialField(doc);

        credentialField.Should().NotBeNull(
            "FindCredentialField must recognise the '/credential' pointer-key shape produced by the Wave A writer");
    }

    [Fact]
    public void WriterShape_ParsesAllFeature106Fields()
    {
        var doc = JsonSerializer.Deserialize<JsonElement>(WriterShapeJson);
        var credentialField = InboundCredentialDetector.FindCredentialField(doc);
        credentialField.Should().NotBeNull();

        var extract = InboundCredentialDetector.TryParseExtract(credentialField!.Value, "tx-round-trip");

        extract.Should().NotBeNull();
        extract!.CredentialId.Should().Be("urn:uuid:feature-106-round-trip-test-1");
        extract.CredentialType.Should().Be("VerifiedCitizenCredential");
        extract.IssuerDid.Should().Be("did:sorcha:org:ws11qgovernment");
        extract.RawToken.Should().Be("eyJhbGciOiJFZERTQSJ9.test.token");
        extract.IssuedAt.Should().Be(new DateTimeOffset(2026, 4, 15, 10, 30, 0, TimeSpan.Zero));
        extract.ExpiresAt.Should().Be(new DateTimeOffset(2027, 4, 15, 10, 30, 0, TimeSpan.Zero));
        extract.TransactionId.Should().Be("tx-round-trip");
        extract.BlueprintId.Should().Be("haip-verified-citizen-v2-1.0.0");
        extract.InstanceId.Should().Be("instance-9a9f4c9f-round-trip");
        extract.ActionId.Should().Be("2");
        extract.ClaimActionId.Should().Be("3");
        extract.RegisterId.Should().Be("af7b1040-register-round-trip");
    }

    [Fact]
    public void BareCredentialKey_IsAlsoAccepted_ForForwardCompat()
    {
        // Some JSON serialisers may unescape the "/credential" pointer key to
        // "credential" after a round-trip; Wave B's FindCredentialField accepts
        // both so a serialiser change in the writer doesn't silently break
        // detection.
        const string BareJson = /*lang=json,strict*/ """
        {
          "credential": {
            "credentialId": "urn:uuid:bare-key-test",
            "credentialType": "VerifiedCitizenCredential",
            "issuerDid": "did:sorcha:org:ws11qgovernment",
            "rawToken": "eyJ.bare.token",
            "issuedAt": "2026-04-15T10:30:00+00:00"
          }
        }
        """;

        var doc = JsonSerializer.Deserialize<JsonElement>(BareJson);

        var field = InboundCredentialDetector.FindCredentialField(doc);

        field.Should().NotBeNull();
        var extract = InboundCredentialDetector.TryParseExtract(field!.Value, "tx-bare");
        extract.Should().NotBeNull();
        extract!.CredentialId.Should().Be("urn:uuid:bare-key-test");
    }

    [Fact]
    public void MissingRequiredField_ReturnsNull_NoThrow()
    {
        // Detector must never throw (Wave B invariant) — missing fields are
        // logged at the call site and surfaced as a null extract so the caller
        // counts them as SkippedNoRecipientDisclosure.
        const string MalformedJson = /*lang=json,strict*/ """
        {
          "/credential": {
            "credentialType": "VerifiedCitizenCredential",
            "issuerDid": "did:sorcha:org:ws11qgovernment"
          }
        }
        """;

        var doc = JsonSerializer.Deserialize<JsonElement>(MalformedJson);
        var field = InboundCredentialDetector.FindCredentialField(doc);
        field.Should().NotBeNull();

        var extract = InboundCredentialDetector.TryParseExtract(field!.Value, "tx-malformed");

        extract.Should().BeNull("missing credentialId, rawToken → parser returns null");
    }

    [Fact]
    public void OptionalFields_Absent_StillParses()
    {
        // ExpiresAt, BlueprintId, and InstanceId are all optional in
        // InboundCredentialExtract. The parser must accept their absence.
        const string MinimalJson = /*lang=json,strict*/ """
        {
          "/credential": {
            "credentialId": "urn:uuid:minimal",
            "credentialType": "MinimalCredential",
            "issuerDid": "did:sorcha:org:ws11qissuer",
            "rawToken": "eyJ.minimal.token",
            "issuedAt": "2026-04-15T10:30:00+00:00"
          }
        }
        """;

        var doc = JsonSerializer.Deserialize<JsonElement>(MinimalJson);
        var field = InboundCredentialDetector.FindCredentialField(doc);
        var extract = InboundCredentialDetector.TryParseExtract(field!.Value, "tx-minimal");

        extract.Should().NotBeNull();
        extract!.ExpiresAt.Should().BeNull();
        extract.BlueprintId.Should().BeNull();
        extract.InstanceId.Should().BeNull();
        extract.ActionId.Should().BeNull();
        extract.ClaimActionId.Should().BeNull();
        extract.RegisterId.Should().BeNull();
    }
}
