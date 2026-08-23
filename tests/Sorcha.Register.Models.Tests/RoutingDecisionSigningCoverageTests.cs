// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text;
using Sorcha.Register.Models;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Feature 194 — the guard that stops a field being added to <see cref="RoutingDecision"/> and
/// forgotten in <see cref="RoutingDecision.ComputeSignableBytes"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists when per-field tests already do.</b> <c>RoutingDecisionTests</c> already
/// asserts that <c>RouteId</c> and <c>ReasonCode</c> are covered. Those tests are correct and stay —
/// but they are a <i>hand-written list</i>, and a hand-written list rots in exactly the same
/// direction as the bug: the developer who forgets to extend the field-by-field rebuild is the same
/// developer who forgets to add a test for the field they just added. Every existing per-field test
/// stays green when a new, uncovered field is introduced.
/// </para>
/// <para>
/// <b>What an omission costs.</b> The transaction signature covers only <c>{TxId}:{PayloadHash}</c>,
/// so it does not cover the routing decision. <c>VAL_ROUTING_002</c> verifies exactly the bytes this
/// method returns and nothing else. A property present on the record and absent from the rebuild
/// therefore <b>rides the wire unauthenticated while appearing signed</b> — no error, no warning, at
/// any layer. Feature 189 lost <c>ValidatorEntry</c> to precisely this shape, with a warning comment
/// sitting above the method the whole time.
/// </para>
/// <para>
/// <b>This test must fail on an unrecognised property type rather than skip it.</b> Skipping is how a
/// guard goes quietly inert: the next field would be of a type the mutator does not know, would be
/// passed over, and the suite would stay green while proving nothing about it.
/// </para>
/// </remarks>
public class RoutingDecisionSigningCoverageTests
{
    /// <summary>
    /// The one property deliberately excluded from the signable bytes: the signature cannot sign
    /// over itself. Its exclusion is asserted separately by
    /// <c>RoutingDecisionTests.ComputeSignableBytes_ExcludesAttestation_SoSignatureNeverSignsItself</c>.
    /// </summary>
    private const string DeliberatelyExcludedProperty = nameof(RoutingDecision.Attestation);

    [Fact]
    public void EveryRoutingDecisionProperty_IsCoveredByComputeSignableBytes()
    {
        var properties = typeof(RoutingDecision)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => p.Name != DeliberatelyExcludedProperty)
            .ToList();

        Assert.NotEmpty(properties);

        var uncovered = new List<string>();

        foreach (var property in properties)
        {
            var baseline = CreateBaseline();
            var mutated = CreateBaseline();

            // Fails the test rather than skipping — see the remarks on this class.
            var (baseValue, alternativeValue) = DistinctValuesFor(property);

            property.SetValue(baseline, baseValue);
            property.SetValue(mutated, alternativeValue);

            var baselineBytes = baseline.ComputeSignableBytes();
            var mutatedBytes = mutated.ComputeSignableBytes();

            if (baselineBytes.SequenceEqual(mutatedBytes))
            {
                uncovered.Add(property.Name);
            }
        }

        Assert.True(
            uncovered.Count == 0,
            $"RoutingDecision.ComputeSignableBytes() does not cover: {string.Join(", ", uncovered)}. " +
            "These properties ride the wire UNAUTHENTICATED while appearing signed — the validator's " +
            "VAL_ROUTING_002 check verifies only what ComputeSignableBytes() returns. Add each one to " +
            "the field-by-field rebuild in RoutingDecision.ComputeSignableBytes().");
    }

    [Fact]
    public void TheAttestationExclusion_IsTheOnlyOne_AndIsDeliberate()
    {
        // Pins the exemption list itself. If someone widens it to silence this guard, that is a
        // decision that should require editing an assertion which says why the exemption exists.
        var excluded = typeof(RoutingDecision)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Single(p => p.Name == DeliberatelyExcludedProperty);

        Assert.Equal(typeof(Attestation), excluded.PropertyType);
    }

    /// <summary>
    /// A decision with every property at a known, non-default value, so a mutation of one property
    /// is the only difference between two instances.
    /// </summary>
    private static RoutingDecision CreateBaseline() => new()
    {
        CompletedActionId = 1,
        NextActions = [new ActionRef { ActionId = 2 }],
        RouteId = "baseline-route",
        ReasonCode = "baseline-reason",
        Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "aWdub3JlZA==" },
    };

    /// <summary>
    /// Two values of the property's type that MUST produce different signable bytes.
    /// </summary>
    /// <remarks>
    /// An unrecognised type is a hard failure, not a skip. A new property of an unhandled type is
    /// exactly the case this guard exists to catch, and silently passing over it would make the
    /// whole test vacuous for the one field that needed it.
    /// </remarks>
    private static (object? Baseline, object? Alternative) DistinctValuesFor(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type == typeof(int))
        {
            return (11, 22);
        }

        if (type == typeof(string))
        {
            return ($"{property.Name}-alpha", $"{property.Name}-beta");
        }

        if (type == typeof(bool))
        {
            return (false, true);
        }

        if (type == typeof(List<ActionRef>))
        {
            return (
                new List<ActionRef> { new() { ActionId = 101, BranchKey = "alpha" } },
                new List<ActionRef> { new() { ActionId = 202, BranchKey = "beta" } });
        }

        Assert.Fail(
            $"RoutingDecision.{property.Name} is of type {property.PropertyType.Name}, which this " +
            "guard does not know how to mutate. Extend DistinctValuesFor — and while you are here, " +
            "confirm the property is actually copied into RoutingDecision.ComputeSignableBytes(). " +
            "Do NOT make this case skip: a skipped property is an unguarded property.");
        return (null, null); // unreachable — Assert.Fail throws
    }

    [Fact]
    public void SignableBytes_AreUtf8Json_SoAFailureMessageCanBeRead()
    {
        // Cheap, but it is what lets a developer debugging a coverage failure print the bytes and
        // see which field is missing, rather than staring at two byte arrays.
        var text = Encoding.UTF8.GetString(CreateBaseline().ComputeSignableBytes());

        Assert.StartsWith("{", text);
        Assert.Contains("completedActionId", text);
    }
}
