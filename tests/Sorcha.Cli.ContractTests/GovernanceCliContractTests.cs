// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

using FluentAssertions;

using Sorcha.Cli.Commands;
using Sorcha.Cli.Services;
using Sorcha.Register.Models;

namespace Sorcha.Cli.ContractTests;

/// <summary>
/// The governance CLI speaks the server's own types, so there is no wire contract to drift
/// (Feature 189 T082, CLAUDE.md #18).
/// </summary>
/// <remarks>
/// The DRIFT-002 audit found thirty CLI commands whose hand-maintained DTOs had quietly stopped
/// matching the server: no crash, no compile error, just wrong output or a request the server
/// ignored. The governance commands avoid that class entirely by binding
/// <c>Sorcha.Register.Models</c> types directly — which only stays true if nobody later
/// "tidies" them into CLI copies. That is what these assert.
/// </remarks>
public sealed class GovernanceCliContractTests
{
    private static MethodInfo Method(string name) =>
        typeof(IRegisterServiceClient).GetMethod(name)
        ?? throw new InvalidOperationException($"{name} is missing from IRegisterServiceClient");

    [Fact]
    public void TheSigningRequest_IsTheServersOwnType()
    {
        var returned = Method(nameof(IRegisterServiceClient.GetGovernanceSigningRequestAsync))
            .ReturnType.GetGenericArguments().Single();

        returned.Should().Be<GovernanceSigningRequest>(
            "a CLI copy of this shape is a second declaration that can silently stop matching");
    }

    [Fact]
    public void TheApprovalSubmission_IsTheServersOwnType()
    {
        var body = Method(nameof(IRegisterServiceClient.ApproveGovernanceProposalAsync))
            .GetParameters().Single(p => p.Name == "submission");

        body.ParameterType.Should().Be<GovernanceApprovalSubmission>();
    }

    /// <summary>
    /// The digest is derived by the client, never taken from the server (FR-028).
    /// </summary>
    /// <remarks>
    /// A server-supplied digest could fail to match the operation the client displayed, reinstating at
    /// the transport layer exactly the substitution statement v2 closes inside the digest. So the
    /// signing request must carry no digest field at all — there is then nothing for the two to
    /// disagree about.
    /// </remarks>
    [Fact]
    public void TheSigningRequest_CarriesNoDigestForTheClientToTrust()
    {
        typeof(GovernanceSigningRequest).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Digest", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ws11qorgwallet", "did:sorcha:w:ws11qorgwallet")]
    [InlineData("did:sorcha:w:ws11qorgwallet", "did:sorcha:w:ws11qorgwallet")]
    public void AWalletAddressOrADid_BothResolveToTheSameSubject(string input, string expected)
    {
        // The roster records subjects as wallet DIDs, but an operator has a wallet address in front of
        // them. Accepting only one of the two spellings turns a correct approval into a 403 that reads
        // like an entitlement problem.
        var toWalletDid = typeof(GovernanceCommand).Assembly
            .GetType("Sorcha.Cli.Commands.GovernanceCliHelpers")!
            .GetMethod("ToWalletDid", BindingFlags.NonPublic | BindingFlags.Static)!;

        toWalletDid.Invoke(null, [input]).Should().Be(expected);
    }
}
