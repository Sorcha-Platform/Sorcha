// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.CitizenWallet;

/// <summary>
/// Tests for the <see cref="CitizenWalletEndpoints"/> <c>ReportPresentationLog</c>
/// handler (Feature 114 US5 PR2). Uses the established reflection-based static-handler
/// invocation pattern from <c>CitizenWalletEnrolEndpointTests</c>; the dedupe + forward
/// orchestration is covered separately by <see cref="CitizenPresentationLogReporterTests"/>.
/// </summary>
public sealed class PresentationLogEndpointTests
{
    private static readonly Guid PlatformUserId = Guid.NewGuid();

    private readonly Mock<IValidator<PresentationLogReportRequest>> _validator = new();
    private readonly Mock<ICitizenPresentationLogReporter> _reporter = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();

    public PresentationLogEndpointTests()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<PresentationLogReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        // Wire the fire-and-forget scope so the dispatched reporter resolves cleanly.
        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(ICitizenPresentationLogReporter))).Returns(_reporter.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(sp.Object);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
    }

    private static PresentationLogReportRequest BuildRequest() => new()
    {
        Entries =
        [
            new PresentationLogEntry
            {
                Id = Guid.NewGuid(),
                CredentialId = Guid.NewGuid(),
                VerifierLabel = "Strathcarron Council",
                DisclosedClaims = ["givenName"],
                PresentedAt = DateTimeOffset.UtcNow,
                Outcome = PresentationLogOutcome.Acknowledged
            }
        ]
    };

    private static HttpContext BuildHttpContext(Guid? platformUserId)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (platformUserId is not null)
            claims.Add(new Claim("platform_user_id", platformUserId.Value.ToString()));
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        return ctx;
    }

    private async Task<IResult> InvokeAsync(PresentationLogReportRequest body, HttpContext context)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "ReportPresentationLog", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("ReportPresentationLog handler should exist");

        var result = method.Invoke(null, [
            body, context, _validator.Object, _scopeFactory.Object,
            NullLogger<Program>.Instance, CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }

    [Fact]
    public async Task ReportPresentationLog_ValidBatch_ReturnsAccepted()
    {
        var result = await InvokeAsync(BuildRequest(), BuildHttpContext(PlatformUserId));

        result.GetType().Name.Should().Contain("Accepted");
    }

    [Fact]
    public async Task ReportPresentationLog_ValidationFailure_ReturnsValidationProblem()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<PresentationLogReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("Entries", "must not be empty")]));

        var result = await InvokeAsync(new PresentationLogReportRequest(), BuildHttpContext(PlatformUserId));

        result.GetType().Name.Should().Contain("Problem");
    }

    [Fact]
    public async Task ReportPresentationLog_MissingPlatformUserClaim_ReturnsUnauthorized()
    {
        var result = await InvokeAsync(BuildRequest(), BuildHttpContext(platformUserId: null));

        result.GetType().Name.Should().Contain("Unauthorized");
    }
}
