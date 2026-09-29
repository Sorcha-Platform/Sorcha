// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// TODO(095) / #1759 — the internal endpoint the Blueprint Service calls to have a status list signed
/// with the issuing organisation's VC-issuance key. The key signs that org's credentials too, so the
/// endpoint is narrowed to the Blueprint principal (the #1397 pattern) and builds the token itself.
/// </summary>
public sealed class StatusListSigningEndpointTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string Subject = "https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists/list-1";

    private readonly Mock<IStatusListTokenSigner> _signer = new();

    [Fact]
    public async Task Sign_FromBlueprintPrincipal_ReturnsTheSignedToken()
    {
        _signer.Setup(s => s.SignAsync(It.IsAny<StatusListTokenSignRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatusListToken("h.p.s", "did:sorcha:org:ws1", "did:sorcha:org:ws1#vc-issuance-0"));

        var result = await Invoke(Request(), ServiceToken("service-blueprint"));

        var ok = result.Should().BeOfType<Ok<SignStatusListTokenResponse>>().Subject;
        ok.Value!.Jwt.Should().Be("h.p.s");
        ok.Value.IssuerDid.Should().Be("did:sorcha:org:ws1");
        ok.Value.Kid.Should().Be("did:sorcha:org:ws1#vc-issuance-0");
        _signer.Verify(s => s.SignAsync(
            It.Is<StatusListTokenSignRequest>(r =>
                r.OrganizationId == OrgId && r.Subject == Subject && r.Bits == 2
                && r.Entries.SequenceEqual(new byte[] { 0xb9, 0xa3 }) && r.TtlSeconds == 300),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("service-wallet")]
    [InlineData("register-service")]
    [InlineData(null)]
    public async Task Sign_FromAnyOtherCaller_IsForbiddenAndNothingIsSigned(string? clientId)
    {
        var result = await Invoke(Request(), ServiceToken(clientId));

        result.Should().BeOfType<ForbidHttpResult>();
        _signer.Verify(s => s.SignAsync(It.IsAny<StatusListTokenSignRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Sign_OrgHasNoIssuanceKey_Returns409()
    {
        _signer.Setup(s => s.SignAsync(It.IsAny<StatusListTokenSignRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusListToken?)null);

        var result = await Invoke(Request(), ServiceToken("service-blueprint"));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Sign_SignerRejectsTheInput_Returns400()
    {
        _signer.Setup(s => s.SignAsync(It.IsAny<StatusListTokenSignRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentOutOfRangeException("request", 3, "bits must be 1, 2, 4 or 8."));

        var result = await Invoke(Request(), ServiceToken("service-blueprint"));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Sign_EntriesNotBase64_Returns400WithoutCallingTheSigner()
    {
        var result = await Invoke(Request() with { EntriesBase64 = "not base64!" }, ServiceToken("service-blueprint"));

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        _signer.Verify(s => s.SignAsync(It.IsAny<StatusListTokenSignRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Route_RequiresTheServicePolicy()
    {
        var endpoints = EndpointAuthorizationMetadata.Collect(app => app.MapStatusListSigningInternalEndpoints());
        var endpoint = EndpointAuthorizationMetadata.FindByPath(endpoints, "api/internal/status-lists/ietf/sign");

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .Should().Contain(AuthorizationPolicies.RequireService);
    }

    private Task<IResult> Invoke(SignStatusListTokenRequest request, ClaimsPrincipal user) =>
        StatusListSigningInternalEndpoints.Sign(
            request,
            new DefaultHttpContext { User = user },
            _signer.Object,
            NullLoggerFactory.Instance,
            CancellationToken.None);

    private static SignStatusListTokenRequest Request() => new()
    {
        OrganizationId = OrgId,
        Subject = Subject,
        Bits = 2,
        EntriesBase64 = Convert.ToBase64String([0xb9, 0xa3]),
        TtlSeconds = 300,
    };

    private static ClaimsPrincipal ServiceToken(string? clientId)
    {
        var claims = new List<Claim> { new("token_type", "service") };
        if (clientId is not null) claims.Add(new Claim("client_id", clientId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }
}
