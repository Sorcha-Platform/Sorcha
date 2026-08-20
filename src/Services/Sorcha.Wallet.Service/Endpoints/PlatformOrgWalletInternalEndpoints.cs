// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Sorcha.ServiceDefaults;
using Sorcha.Wallet.Core.Services.Implementation;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Service-to-service creation of a wallet owned by a PLATFORM organisation, returning its recovery
/// phrase so the caller can hand it to the administrator performing the action (#1530).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a deliberate, narrow exception to #1525</b>, which exists precisely to stop the
/// platform minting organisation wallets — because doing so generates a BIP39 recovery phrase with
/// nobody present to receive it, and the organisation can then never be recovered.
/// </para>
/// <para>
/// The exception is needed because a platform organisation (the Public Org) has no administrator of
/// its own to create its wallet. Someone has to, and the only human in the loop is the system
/// administrator who enables it. So the phrase is <b>returned to the caller</b> rather than
/// discarded — that return value is the entire justification for this endpoint existing, and a
/// caller that drops it has reintroduced the original defect.
/// </para>
/// <para>
/// Kept as its own named route rather than relaxing the org gate on <c>POST /api/v1/wallets</c>:
/// that gate refusing service principals is what makes #1525 hold, and a general relaxation would
/// quietly restore "the platform can mint any organisation's wallet". This route can only be
/// reached with a service token, and its name says exactly what it does.
/// </para>
/// </remarks>
public static class PlatformOrgWalletInternalEndpoints
{
    /// <summary>Maps the internal platform-org wallet route.</summary>
    public static IEndpointRouteBuilder MapPlatformOrgWalletInternalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/platform-org-wallet", CreatePlatformOrgWallet)
            .WithName("CreatePlatformOrgWalletWithRecovery")
            .WithSummary("Create a wallet owned by a platform organisation and return its recovery phrase")
            .WithDescription(
                "Creates a wallet owned by the named organisation and RETURNS its BIP39 recovery "
                + "phrase, so the caller can present it to the administrator performing the action. "
                + "Narrow exception to #1525: organisation wallets are normally created by an "
                + "administrator of that organisation, because the phrase is shown once, never "
                + "stored, and cannot be reissued. A platform organisation has no such "
                + "administrator, so the platform creates it on behalf of the system administrator "
                + "who is present — and hands them the phrase. Service token required. The caller "
                + "MUST surface the phrase; dropping it makes the organisation unrecoverable.")
            .RequireAuthorization(AuthorizationPolicies.RequireService)
            .Produces<PlatformOrgWalletResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> CreatePlatformOrgWallet(
        [FromBody] PlatformOrgWalletRequest request,
        [FromServices] WalletManager walletManager,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        if (request is null || request.OrganizationId == Guid.Empty)
        {
            return Results.Problem(
                title: "organizationId is required",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var orgId = request.OrganizationId.ToString();
        var name = string.IsNullOrWhiteSpace(request.Name) ? $"org-{orgId}-signing" : request.Name;
        var algorithm = string.IsNullOrWhiteSpace(request.Algorithm) ? "ED25519" : request.Algorithm;

        // Owner AND tenant are the organisation: the wallet belongs to it and outlives whoever
        // enabled it, exactly as an admin-created org wallet does.
        var (wallet, mnemonic) = await walletManager.CreateWalletAsync(
            name, algorithm, orgId, orgId, 24, null, null, ct);

        logger.LogInformation(
            "Created platform-org wallet {Address} for organisation {OrgId}. Its recovery phrase was "
            + "returned to the caller and is NOT stored — if the caller did not surface it, the "
            + "organisation cannot be recovered.",
            wallet.Address, orgId);

        return Results.Ok(new PlatformOrgWalletResponse(
            OrganizationId: request.OrganizationId,
            Address: wallet.Address,
            PublicKey: wallet.PublicKey,
            Algorithm: wallet.Algorithm,
            MnemonicWords: mnemonic.Phrase.Split(' ')));
    }
}

/// <summary>Request to create a platform organisation's wallet (#1530).</summary>
/// <param name="OrganizationId">The platform organisation that will OWN the wallet.</param>
/// <param name="Name">Optional wallet name; defaults to <c>org-{id}-signing</c>.</param>
/// <param name="Algorithm">Optional algorithm; defaults to ED25519.</param>
public sealed record PlatformOrgWalletRequest(
    [Required] Guid OrganizationId,
    string? Name = null,
    string? Algorithm = null);

/// <summary>
/// The created wallet and its recovery phrase. <see cref="MnemonicWords"/> is returned ONCE and is
/// never stored anywhere — the caller must present it to the administrator.
/// </summary>
public sealed record PlatformOrgWalletResponse(
    Guid OrganizationId,
    string Address,
    string PublicKey,
    string Algorithm,
    string[] MnemonicWords);
