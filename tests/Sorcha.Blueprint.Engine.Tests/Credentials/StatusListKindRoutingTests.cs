// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using Sorcha.Blueprint.Engine.Credentials;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// #1759 — Sorcha SD-JWTs carry BOTH status shapes during the W3C retirement (#1769): IETF
/// <c>status.status_list</c> and W3C <c>credentialStatus</c>. They are different formats at different
/// URLs, so each reference must reach the checker that can read it. Handed to the W3C checker, an IETF
/// list (a JWT) fails to parse, reads as Unresolved, and a fail-closed gate refuses a good credential.
/// </summary>
public sealed class StatusListKindRoutingTests
{
    private const string Issuer = "did:sorcha:org:ws11qorg";

    [Fact]
    public void ExtractStatusReferences_DualShapeCredential_MarksEachReferenceWithItsKindAndPinsIetfToTheIssuer()
    {
        using var doc = JsonDocument.Parse($$"""
            {
              "iss": "{{Issuer}}",
              "status": { "status_list": { "uri": "https://x/ietf-status-lists/L1", "idx": 4 } },
              "credentialStatus": [
                { "type": "BitstringStatusListEntry", "statusPurpose": "revocation",
                  "statusListCredential": "https://x/status-lists/L1", "statusListIndex": "4" },
                { "type": "BitstringStatusListEntry", "statusPurpose": "suspension",
                  "statusListCredential": "https://x/status-lists/L1s", "statusListIndex": "4" }
              ]
            }
            """);

        var refs = SdJwtVcFormatHandler.ExtractStatusReferences(doc.RootElement);

        refs.Should().HaveCount(3);
        refs[0].Kind.Should().Be(StatusListKind.IetfTokenStatusList);
        refs[0].ExpectedIssuer.Should().Be(Issuer);
        refs.Skip(1).Should().OnlyContain(r => r.Kind == StatusListKind.W3cBitstring);
    }

    [Fact]
    public async Task RoutingChecker_SendsEachReferenceToTheCheckerForItsKind()
    {
        var w3c = new RecordingChecker(CredentialStatusValue.Suspended);
        var ietf = new RecordingChecker(CredentialStatusValue.Invalid);
        var router = new RoutingStatusListChecker(w3c, ietf);

        var ietfAnswer = await router.CheckAsync(new StatusReference { Uri = "i", Kind = StatusListKind.IetfTokenStatusList });
        var w3cAnswer = await router.CheckAsync(new StatusReference { Uri = "w", Kind = StatusListKind.W3cBitstring });

        ietfAnswer.Should().Be(CredentialStatusValue.Invalid);
        w3cAnswer.Should().Be(CredentialStatusValue.Suspended);
        ietf.Seen.Should().Equal("i");
        w3c.Seen.Should().Equal("w");
    }

    private sealed class RecordingChecker(CredentialStatusValue answer) : IStatusListChecker
    {
        public List<string> Seen { get; } = [];

        public Task<CredentialStatusValue> CheckAsync(StatusReference statusRef, CancellationToken cancellationToken = default)
        {
            Seen.Add(statusRef.Uri);
            return Task.FromResult(answer);
        }
    }
}
