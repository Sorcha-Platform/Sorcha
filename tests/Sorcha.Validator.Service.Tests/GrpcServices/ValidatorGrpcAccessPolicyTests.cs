// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

using FluentAssertions;

using Sorcha.Validator.Service.GrpcServices;

using Xunit;

namespace Sorcha.Validator.Service.Tests.GrpcServices;

/// <summary>
/// The validator's gRPC peer surface carried no authorization at all — while every REST group in the
/// same <c>Program.cs</c> did — and the gRPC port is published (<c>5801:8081</c>).
///
/// <para>It cannot simply be put behind <c>RequireAuthorization</c>: consensus is federated across
/// installations and Sorcha service tokens are installation-scoped (Feature 136 deliberately rejects
/// another installation's tokens), so a blanket gate would permanently sever federation. These tests
/// pin the tiered policy that replaces it, and — via reflection over the generated service base —
/// force every RPC to be a deliberate decision rather than an accident.</para>
/// </summary>
public class ValidatorGrpcAccessPolicyTests
{
    [Theory]
    [InlineData("RequestVote")]
    [InlineData("ValidateDocket")]
    [InlineData("ExchangeSignature")]
    [InlineData("ReceiveConfirmedDocket")]
    [InlineData("GetHealthStatus")]
    public void FederationMethods_AreReachableWithoutAuthentication(string method)
    {
        // These must stay open or cross-installation consensus breaks. Each is either read-only or
        // carries a signature that is verified against the validator roster downstream.
        ValidatorGrpcAccessPolicy.IsFederationReachable(method).Should().BeTrue(
            $"{method} is required for federated consensus between installations");
    }

    [Fact]
    public void ReceiveTransaction_RequiresAuthentication()
    {
        // Mempool ingest has no roster gate on admission and no cross-installation caller, so an
        // open endpoint is an unbounded invitation to spend this node's validation budget.
        ValidatorGrpcAccessPolicy.IsFederationReachable("ReceiveTransaction").Should().BeFalse();
        ValidatorGrpcAccessPolicy.AuthenticatedOnlyReason("ReceiveTransaction").Should().NotBeNullOrEmpty(
            "the reason a method is closed belongs next to its name");
    }

    [Fact]
    public void FullGrpcPath_IsAccepted_NotJustBareMethodName()
    {
        // ServerCallContext.Method is the full path; a policy that only matched bare names would
        // silently classify every real call as unclassified.
        ValidatorGrpcAccessPolicy.IsFederationReachable(
            "/sorcha.validator.v1.ValidatorService/RequestVote").Should().BeTrue();

        ValidatorGrpcAccessPolicy.IsFederationReachable(
            "/sorcha.validator.v1.ValidatorService/ReceiveTransaction").Should().BeFalse();
    }

    [Theory]
    [InlineData("SomeNewRpcNobodyClassified")]
    [InlineData("")]
    [InlineData("/svc/")]
    public void UnclassifiedMethods_FailClosed(string method)
    {
        // A new RPC must be private by default. The opposite default would mean "someone added an
        // RPC and forgot the policy" silently publishes it.
        ValidatorGrpcAccessPolicy.IsFederationReachable(method).Should().BeFalse(
            "unclassified methods must require authentication, not be exposed by omission");
    }

    [Fact]
    public void EveryRpcOnTheServiceBase_HasAnExplicitDecision()
    {
        // The drift guard. Reflects over the generated ValidatorServiceBase: every RPC must appear
        // in the policy's classified set. Adding an RPC to the proto without deciding its access
        // level fails HERE, rather than shipping either an open endpoint or a broken peer.
        var serviceBase = typeof(Sorcha.Validator.Grpc.V1.ValidatorService.ValidatorServiceBase);

        var rpcNames = serviceBase
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsVirtual && !m.IsFinal)
            .Select(m => m.Name)
            .Distinct()
            .ToList();

        rpcNames.Should().NotBeEmpty("reflection must actually find the generated RPC methods");

        var classified = ValidatorGrpcAccessPolicy.ClassifiedMethods;

        rpcNames.Should().OnlyContain(
            name => classified.Contains(name),
            "every validator RPC must be explicitly classified as federation-reachable or "
            + "authenticated-only — see ValidatorGrpcAccessPolicy. Unclassified RPCs still fail "
            + "closed at runtime, but the decision should be recorded deliberately.");
    }

    [Fact]
    public void ClassifiedMethods_ContainsNoStaleEntries()
    {
        // The mirror of the guard above: a policy entry for an RPC that no longer exists is dead
        // weight that makes the policy look more considered than it is.
        var serviceBase = typeof(Sorcha.Validator.Grpc.V1.ValidatorService.ValidatorServiceBase);

        var rpcNames = serviceBase
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsVirtual && !m.IsFinal)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        ValidatorGrpcAccessPolicy.ClassifiedMethods.Should().OnlyContain(
            name => rpcNames.Contains(name),
            "the policy should not classify methods the service no longer exposes");
    }
}
