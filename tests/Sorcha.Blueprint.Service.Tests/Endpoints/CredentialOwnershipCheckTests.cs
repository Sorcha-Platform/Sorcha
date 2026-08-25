// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Polly.CircuitBreaker;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.ServiceClients.Wallet;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Issue #1506 — <c>GetAndVerifyIssuer</c> turned a dependency failure into an ownership verdict.
/// </summary>
/// <remarks>
/// <para>Live on n1: a participant lookup 500 inside the Wallet Service surfaced at
/// <c>POST /api/v1/credentials/{id}/revoke</c> as a 500 whose body read
/// <c>"Failed to verify credential ownership"</c>. The check never ran; the message asserted its
/// result anyway, sending an operator to debug their credential rather than the outage.</para>
///
/// <para>These pin all four outcomes together on purpose. Fixing the wording alone would leave the
/// two failure kinds indistinguishable (a broken circuit is retryable, a mapping fault is not — the
/// #1476 lesson), and asserting only the failures would let a regression that 503s everything,
/// including a genuine non-owner, pass.</para>
/// </remarks>
public sealed class CredentialOwnershipCheckTests
{
    private const string IssuerWallet = "ws1qissuer000000000000000000000000000000";
    private const string OtherWallet = "ws1qother0000000000000000000000000000000";
    private const string CredentialId = "urn:uuid:7b9722cf-0000-0000-0000-000000000001";

    private static Mock<IWalletServiceClient> WalletThat(Func<Task<CredentialIssuanceResult?>> behaviour)
    {
        var wallet = new Mock<IWalletServiceClient>();
        wallet.Setup(w => w.GetCredentialAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(behaviour);
        return wallet;
    }

    private static CredentialIssuanceResult Credential(string issuedBy) => new()
    {
        CredentialId = CredentialId,
        Type = "TestCredential",
        IssuerDid = issuedBy,
        SubjectDid = "ws1qsubject00000000000000000000000000000",
        Claims = new Dictionary<string, object>(),
        IssuedAt = DateTimeOffset.UnixEpoch,
        RawToken = "eyJ.test.token",
    };

    private static Task<(CredentialIssuanceResult? Value, IResult? Error)> RunAsync(Mock<IWalletServiceClient> wallet)
        => CredentialEndpoints.GetAndVerifyIssuer(
            CredentialId, IssuerWallet, wallet.Object, NullLogger.Instance, CancellationToken.None);

    private static int StatusOf(IResult? result)
        => result switch
        {
            ProblemHttpResult p => p.StatusCode,
            IStatusCodeHttpResult s => s.StatusCode ?? 0,
            _ => 0
        };

    public static TheoryData<Exception> Retryable() => new()
    {
        // What the Wallet client throws for any non-404 status (see its #1475 comment).
        new HttpRequestException("Wallet returned 500 reading credential '…'."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
        new TimeoutException("timed out"),
        // The literal n1 failure once the shared breaker had opened.
        new BrokenCircuitException("The circuit is now open and is not allowing calls."),
    };

    [Theory]
    [MemberData(nameof(Retryable))]
    public async Task AnUnreachableWallet_Is503_AndNeverClaimsAnOwnershipVerdict(Exception thrown)
    {
        var (value, error) = await RunAsync(WalletThat(() => Task.FromException<CredentialIssuanceResult?>(thrown)));

        value.Should().BeNull();
        StatusOf(error).Should().Be(StatusCodes.Status503ServiceUnavailable,
            "the check did not run, and a retryable outage is not a refusal");
        error.Should().BeOfType<ProblemHttpResult>()
             .Which.ProblemDetails.Title.Should().NotContain("Failed to verify credential ownership",
                 "that wording states a verdict this code never reached");
    }

    [Fact]
    public async Task AFaultReadingTheCredential_Is500_NotARetryable503()
    {
        // WalletServiceClient throws rather than returning null when the response cannot be mapped,
        // so a bug cannot masquerade as absence (#1475). Retrying it will never succeed.
        var (value, error) = await RunAsync(WalletThat(
            () => Task.FromException<CredentialIssuanceResult?>(new InvalidOperationException("mapping failed"))));

        value.Should().BeNull();
        StatusOf(error).Should().Be(StatusCodes.Status500InternalServerError,
            "a permanent fault dressed up as a retryable 503 invites the retry storm #1476 closed");
    }

    [Fact]
    public async Task AnAbsentCredential_Is404()
    {
        var (value, error) = await RunAsync(WalletThat(() => Task.FromResult<CredentialIssuanceResult?>(null)));

        value.Should().BeNull();
        StatusOf(error).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ACredentialIssuedByAnother_Is403()
    {
        var (value, error) = await RunAsync(WalletThat(() => Task.FromResult<CredentialIssuanceResult?>(
            Credential(issuedBy: OtherWallet))));

        value.Should().BeNull();
        // Results.Forbid() yields a ForbidHttpResult, which carries no status of its own — the auth
        // handler sets 403. Asserting a number here would be asserting the test's own guess.
        error.Should().BeOfType<ForbidHttpResult>("this is the ONLY outcome that is an ownership verdict");
    }

    [Fact]
    public async Task TheIssuersOwnCredential_IsReturnedWithNoError()
    {
        var (value, error) = await RunAsync(WalletThat(() => Task.FromResult<CredentialIssuanceResult?>(
            Credential(issuedBy: IssuerWallet))));

        error.Should().BeNull();
        value!.CredentialId.Should().Be(CredentialId);
    }
}
