// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Models.Credentials;

using Xunit;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// A credential carrying one status entry per purpose must have EVERY purpose evaluated.
/// </summary>
/// <remarks>
/// <para>
/// Splitting revocation and suspension into separate lists (#1491) gave a credential two
/// <c>credentialStatus</c> entries. The reader kept returning ONE reference and preferred the
/// revocation entry, so a credential that was suspended but not revoked had only its (clear)
/// revocation bit checked and passed verification. Before the split, suspension set the revocation
/// bit — so it was refused, for the wrong reason but refused. The split therefore stopped
/// suspension being enforced at all.
/// </para>
/// <para>
/// The multi-purpose reader existed but had NO CALLERS, which is the giveaway: a helper written for
/// "callers that must evaluate every purpose" when there were none.
/// </para>
/// </remarks>
public class SuspendedCredentialIsRefusedTests
{
    private const string RevocationList = "https://n1.test/api/v1/credentials/status-lists/ws11q-reg-revocation-1";
    private const string SuspensionList = "https://n1.test/api/v1/credentials/status-lists/ws11q-reg-suspension-1";

    [Fact]
    public void EveryDeclaredPurposeIsSurfaced_NotJustTheFirstOrPreferredOne()
    {
        // A verifier cannot refuse a suspension it never looked at, so the reader has to hand back
        // both entries rather than choosing one.
        var payload = PayloadWithBothPurposes();

        var all = SdJwtVcFormatHandler.ExtractStatusReferences(payload);

        all.Should().HaveCount(2, "the credential declares one entry per purpose");
        all.Select(r => r.Purpose).Should().BeEquivalentTo(["revocation", "suspension"]);
        all.Select(r => r.Uri).Should().BeEquivalentTo([RevocationList, SuspensionList]);
    }

    [Fact]
    public void TheSuspensionEntryCarriesItsOwnListAndIndex()
    {
        // The two purposes share an index but NOT a list, so the suspension reference must point at
        // the suspension list — checking the right bit in the wrong list proves nothing.
        var payload = PayloadWithBothPurposes();

        var suspension = SdJwtVcFormatHandler.ExtractStatusReferences(payload)
            .Single(r => r.Purpose == "suspension");

        suspension.Uri.Should().Be(SuspensionList);
        suspension.Index.Should().Be(7);
    }

    [Fact]
    public void ASingleObjectEntryStillReadsAsOneReference()
    {
        // Back-compat: a credential issued before the split carries one object, not an array.
        var json = $$"""
        {
          "iss": "did:sorcha:org:ws11qissuer",
          "credentialStatus": {
            "id": "{{RevocationList}}#7",
            "type": "BitstringStatusListEntry",
            "statusPurpose": "revocation",
            "statusListIndex": "7",
            "statusListCredential": "{{RevocationList}}"
          }
        }
        """;

        var all = SdJwtVcFormatHandler.ExtractStatusReferences(
            JsonSerializer.Deserialize<JsonElement>(json));

        all.Should().ContainSingle();
        all[0].Purpose.Should().Be("revocation");
    }

    [Fact]
    public async Task AVerifierRefusesACredentialThatIsSuspendedButNotRevoked()
    {
        // The regression this file exists for, driven end to end through a real signed SD-JWT.
        // Revocation clear, suspension set: evaluating only the preferred (revocation) reference
        // accepts the credential, which is how splitting the purposes stopped suspension being
        // enforced at all.
        var minted = EngineSdJwtTestFactory.MintEs256(
            "LicenseCredential",
            "did:sorcha:issuer:gov",
            credentialStatusClaim: new object[]
            {
                new Dictionary<string, object>
                {
                    ["id"] = $"{RevocationList}#7",
                    ["type"] = "BitstringStatusListEntry",
                    ["statusPurpose"] = "revocation",
                    ["statusListIndex"] = "7",
                    ["statusListCredential"] = RevocationList
                },
                new Dictionary<string, object>
                {
                    ["id"] = $"{SuspensionList}#7",
                    ["type"] = "BitstringStatusListEntry",
                    ["statusPurpose"] = "suspension",
                    ["statusListIndex"] = "7",
                    ["statusListCredential"] = SuspensionList
                }
            });

        var verifier = EngineSdJwtTestFactory.BuildVerifier(
            directory: null,
            statusChecker: new PerPurposeStatusChecker(revocationSet: false, suspensionSet: true),
            minted);

        var requirements = new[] { new CredentialRequirement { Type = "LicenseCredential" } };
        var presentation = new CredentialPresentation
        {
            CredentialId = "cred-1",
            RawPresentation = minted.Raw
        };

        var result = await verifier.VerifyAsync(requirements, [presentation]);

        result.IsValid.Should().BeFalse(
            "a suspended credential must be refused even though its revocation bit is clear");
    }

    /// <summary>Answers per reference, so revocation and suspension can differ.</summary>
    private sealed class PerPurposeStatusChecker(bool revocationSet, bool suspensionSet) : IStatusListChecker
    {
        public Task<CredentialStatusValue> CheckAsync(StatusReference statusRef, CancellationToken ct = default)
        {
            var isSuspension = statusRef.Purpose == "suspension";
            var set = isSuspension ? suspensionSet : revocationSet;
            if (!set) return Task.FromResult(CredentialStatusValue.Valid);
            return Task.FromResult(isSuspension
                ? CredentialStatusValue.Suspended
                : CredentialStatusValue.Invalid);
        }
    }

    private static JsonElement PayloadWithBothPurposes()
    {
        var json = $$"""
        {
          "iss": "did:sorcha:org:ws11qissuer",
          "credentialStatus": [
            {
              "id": "{{RevocationList}}#7",
              "type": "BitstringStatusListEntry",
              "statusPurpose": "revocation",
              "statusListIndex": "7",
              "statusListCredential": "{{RevocationList}}"
            },
            {
              "id": "{{SuspensionList}}#7",
              "type": "BitstringStatusListEntry",
              "statusPurpose": "suspension",
              "statusListIndex": "7",
              "statusListCredential": "{{SuspensionList}}"
            }
          ]
        }
        """;

        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
