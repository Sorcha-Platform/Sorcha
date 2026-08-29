// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Feature 196: every <see cref="ExemptionKind"/> must have an authority rule.
/// </summary>
/// <remarks>
/// <para>
/// Reflective rather than a hand-maintained list, for the same reason
/// <c>ExecutableDefinitionCoverageTests</c> is: a list can silently fall behind the enum, and both
/// defaults are wrong in different directions. A kind with no rule would either be granted
/// unconditionally — reinstating #1591 — or refused unconditionally, breaking whatever legitimate
/// traffic carries it. Adding a kind must therefore be a decision, not an omission.
/// </para>
/// <para>
/// Deleting the resolver's <c>switch</c> arm for any kind must make this fail.
/// </para>
/// </remarks>
public class ExemptionKindCoverageTests
{
    [Fact]
    public async Task EveryExemptionKind_IsClassifiedByTheResolver()
    {
        var roster = new Mock<IGovernanceRosterService>();
        roster.Setup(r => r.GetCurrentRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((AdminRoster?)null);

        var resolver = ExemptionAuthorityTestKit.Resolver(
            ExemptionAuthorityTestKit.NoAnchor(), roster.Object);

        var unclassified = new List<ExemptionKind>();

        foreach (var kind in Enum.GetValues<ExemptionKind>())
        {
            var tx = TransactionClaiming(kind);
            var claim = ExemptionAuthorityResolver.ReadClaim(tx);

            // 1. The kind must be reachable from the wire at all — otherwise the enum names
            //    something no transaction can claim, and the rule guarding it is dead code.
            if (claim.Kind != kind)
            {
                unclassified.Add(kind);
                continue;
            }

            // 2. The resolver must reach a real verdict for it. The catch-all arm returns
            //    NotEntitled with this exact wording, which is what an unclassified kind looks like.
            var decision = await resolver.ResolveAsync(tx);
            if (decision.Detail?.StartsWith("No authority rule for kind", StringComparison.Ordinal) == true)
            {
                unclassified.Add(kind);
            }
        }

        unclassified.Should().BeEmpty(
            "every exemption kind needs an authority rule — a kind with none is either granted "
            + "unconditionally (which is #1591) or refused unconditionally (which breaks legitimate "
            + "administrative traffic). Add the rule in ExemptionAuthorityResolver.");
    }

    [Fact]
    public void NoExemptionIsGrantedWithoutAClaim()
    {
        // The other half of the invariant: authority without a claim grants nothing either. A
        // genuine genesis signer submitting an ordinary transaction gets an ordinary transaction.
        var tx = Build("some-workflow", []);

        ExemptionAuthorityResolver.ReadClaim(tx).IsClaimed.Should().BeFalse();
    }

    private static Transaction TransactionClaiming(ExemptionKind kind) => kind switch
    {
        // Genesis and RegisterGenesis carry the SAME label and are told apart by register, so the
        // Genesis case must be built on the system register or it classifies as the other one.
        ExemptionKind.Genesis => Build("some-workflow", new() { ["Type"] = "Genesis" },
            SystemRegisterConstants.SystemRegisterId),
        ExemptionKind.RegisterGenesis => Build("some-workflow", new() { ["Type"] = "Genesis" }),
        ExemptionKind.Control => Build("some-workflow", new() { ["Type"] = "Control" }),
        ExemptionKind.BlueprintPublish => Build("some-workflow", new() { ["Type"] = "BlueprintPublish" }),

        // A new kind with no wire representation here fails the test by construction, which is the
        // intent: adding one must force a decision about how it is claimed.
        _ => Build("some-workflow", [])
    };

    private static Transaction Build(
        string blueprintId, Dictionary<string, string> metadata, string registerId = "test-register") =>
        new()
        {
            TransactionId = Guid.NewGuid().ToString("N"),
            RegisterId = registerId,
            BlueprintId = blueprintId,
            ActionId = "1",
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = new byte[32],
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ],
            Metadata = metadata
        };
}
