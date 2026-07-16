// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Tests for the <see cref="CitizenWalletEndpoints"/> <c>SignKbJwt</c> handler
/// (#1195 Phase 2, Task 6a — server-custody KB-JWT signing for holder-<c>cnf</c>
/// presentations). Uses the established reflection-based static-handler pattern
/// (<see cref="CitizenWalletEnrolEndpointTests"/>). Security invariants under test:
/// the holder key is ALWAYS resolved from the authenticated caller (never the body),
/// and the endpoint refuses to act as a general-purpose signing oracle (only a
/// <c>typ: kb+jwt</c> header may be signed — the same holder key signs device
/// delegation credentials, so an unrestricted oracle would let a caller mint them).
/// </summary>
public sealed class CitizenWalletSignKbEndpointTests
{
    private static readonly Guid PlatformUserId = Guid.NewGuid();
    private const string CitizenWallet = "ws1qcitizen1";

    private readonly Mock<IValidator<KbJwtSignRequest>> _validator = new();
    private readonly Mock<IHolderKeyService> _holderKeys = new();
    private readonly Mock<Sorcha.Wallet.Core.Repositories.Interfaces.IWalletRepository> _walletRepository = new();

    public CitizenWalletSignKbEndpointTests()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<KbJwtSignRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    /// <summary>Build a KB-JWT signing input (b64url(header).b64url(payload)) with the given header fields.</summary>
    private static string BuildSigningInput(string typ = "kb+jwt", string alg = "ES256")
    {
        var header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string> { ["alg"] = alg, ["typ"] = typ, ["kid"] = "thumb" }));
        var payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object> { ["nonce"] = "n-1", ["aud"] = "did:sorcha:org:x", ["sd_hash"] = "h" }));
        return $"{header}.{payload}";
    }

    private static HttpContext BuildHttpContext(
        Guid? platformUserId,
        string? walletAddress)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (platformUserId is not null)
            claims.Add(new Claim("platform_user_id", platformUserId.Value.ToString()));
        if (walletAddress is not null)
            claims.Add(new Claim("wallet_address", walletAddress));
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        return ctx;
    }

    private async Task<IResult> InvokeAsync(KbJwtSignRequest body, HttpContext context)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "SignKbJwt",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("SignKbJwt handler should exist");

        var result = method!.Invoke(null, [
            body,
            context,
            _validator.Object,
            _holderKeys.Object,
            _walletRepository.Object,
            NullLogger<Program>.Instance,
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }

    [Fact]
    public async Task SignKbJwt_HappyPath_SignsWithCallersHolderKey_ReturnsJoseAlgorithmAndSignature()
    {
        var signingInput = BuildSigningInput(alg: "EdDSA");
        var signatureBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        // The holder key service returns the WALLET-style algorithm name; the
        // endpoint must normalise it to the JOSE identifier for the response.
        _holderKeys.Setup(h => h.SignAsync(
                CitizenWallet,
                It.Is<byte[]>(b => b.SequenceEqual(Encoding.ASCII.GetBytes(signingInput))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((signatureBytes, "ED25519"));

        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet);
        var result = await InvokeAsync(new KbJwtSignRequest { SigningInput = signingInput }, ctx);

        result.GetType().Name.Should().Contain("Ok");
        var response = result.GetType().GetProperty("Value")!.GetValue(result)
            .Should().BeOfType<KbJwtSignResponse>().Subject;
        response.Signature.Should().Be(Base64Url.EncodeToString(signatureBytes));
        response.Algorithm.Should().Be("EdDSA");

        // The wallet whose key signs is the AUTHENTICATED caller's — resolved from
        // the JWT claims, never from anything in the body.
        _holderKeys.Verify(h => h.SignAsync(
            CitizenWallet, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
        _holderKeys.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignKbJwt_MissingIdentity_ReturnsUnauthorized_AndNeverSigns()
    {
        var ctx = BuildHttpContext(platformUserId: null, walletAddress: null);
        var result = await InvokeAsync(new KbJwtSignRequest { SigningInput = BuildSigningInput() }, ctx);

        result.GetType().Name.Should().Contain("Unauthorized");
        _holderKeys.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignKbJwt_ValidationFailure_ReturnsValidationProblem()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<KbJwtSignRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("SigningInput", "is required")]));

        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet);
        var result = await InvokeAsync(new KbJwtSignRequest { SigningInput = "" }, ctx);

        result.GetType().Name.Should().ContainAny("Problem", "ValidationProblem");
        _holderKeys.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignKbJwt_HeaderNotKbJwt_ReturnsBadRequest_AndNeverSigns()
    {
        // Oracle guard: the holder key also signs device delegation credentials.
        // A non-kb+jwt header (e.g. a delegation VC or an access token) must be refused.
        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet);
        var result = await InvokeAsync(
            new KbJwtSignRequest { SigningInput = BuildSigningInput(typ: "JWT") }, ctx);

        result.GetType().Name.Should().Contain("Problem");
        var problem = result.GetType().GetProperty("ProblemDetails")!.GetValue(result)
            .Should().BeAssignableTo<Microsoft.AspNetCore.Mvc.ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        _holderKeys.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SignKbJwt_HeaderAlgMismatchesHolderKey_ReturnsNamedBadRequest()
    {
        // Never-silent invariant: a KB-JWT whose header alg cannot match the holder
        // key's real algorithm would fail verification downstream with no clue why.
        // The endpoint names the mismatch instead of returning an unusable signature.
        var signingInput = BuildSigningInput(alg: "ES256");
        _holderKeys.Setup(h => h.SignAsync(CitizenWallet, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new byte[] { 9 }, "ED25519"));

        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet);
        var result = await InvokeAsync(new KbJwtSignRequest { SigningInput = signingInput }, ctx);

        result.GetType().Name.Should().Contain("Problem");
        var detail = result.GetType().GetProperty("ProblemDetails")!.GetValue(result)
            .Should().BeAssignableTo<Microsoft.AspNetCore.Mvc.ProblemDetails>().Subject;
        detail.Status.Should().Be(StatusCodes.Status400BadRequest);
        detail.Detail.Should().Contain("ES256").And.Contain("EdDSA",
            "the error must name both the header alg and the holder key's actual alg");
    }

    [Fact]
    public async Task SignKbJwt_WalletUnknown_ReturnsNotFound()
    {
        _holderKeys.Setup(h => h.SignAsync(CitizenWallet, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("no wallet"));

        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet);
        var result = await InvokeAsync(new KbJwtSignRequest { SigningInput = BuildSigningInput(alg: "EdDSA") }, ctx);

        result.GetType().Name.Should().Contain("NotFound");
    }

    [Fact]
    public async Task SignKbJwt_MalformedSigningInput_ReturnsBadRequest_AndNeverSigns()
    {
        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet);
        var result = await InvokeAsync(
            new KbJwtSignRequest { SigningInput = "not-a-jws-signing-input" }, ctx);

        result.GetType().Name.Should().Contain("Problem");
        _holderKeys.VerifyNoOtherCalls();
    }
}
