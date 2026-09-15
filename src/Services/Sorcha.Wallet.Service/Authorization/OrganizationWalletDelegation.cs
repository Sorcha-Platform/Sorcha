// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Sorcha.ServiceClients.Auth;
using Sorcha.Wallet.Contracts.Constants;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Service.Authorization;

/// <summary>The outcome of a delegated-signing check.</summary>
/// <param name="Allowed">Whether the signature may be produced.</param>
/// <param name="Reason">Why, for the audit log and the refusal.</param>
/// <param name="Grant">The delegation that authorised it, when allowed.</param>
public sealed record DelegatedSigningDecision(bool Allowed, string Reason, WalletAccess? Grant = null);

/// <summary>
/// Signing with an organisation-owned wallet by a person acting for the organisation (#1643).
/// </summary>
/// <remarks>
/// <para>
/// An organisation's signing wallet is owned by the organisation (#1525), and <c>SignTransaction</c>
/// let a user token sign only with a wallet it owns, so no person could ever sign with one and
/// <c>sorcha_register_create</c> could never succeed. Rather than a special case, this completes the
/// existing delegation model (<see cref="WalletAccess"/>): an organisation's Administrator may sign
/// with its wallet under a grant.
/// </para>
/// <para>
/// <b>Every condition is checked at signing time, and all must hold.</b> An active <c>ReadWrite</c>
/// grant for the caller; the caller's CURRENT token still names the owning organisation and the
/// Administrator role, so a departed or demoted admin's grant stops working at once without anyone
/// remembering to revoke it; and the grant is scoped to named derivation contexts that include the one
/// being signed at. An unscoped grant would reach the organisation's governance, issuance and every
/// other key: the #1397 signing oracle, with a user token.
/// </para>
/// <para>
/// Authority comes from who proved they hold the grant and the role, never from a label the caller
/// supplies (CLAUDE.md pattern 23). Delegated signing on personal wallets is not introduced here.
/// </para>
/// </remarks>
public static class OrganizationWalletDelegation
{
    private const string AdministratorRole = "Administrator";

    /// <summary>
    /// Derivation contexts granted to the Administrator who creates an organisation's wallet: register
    /// creation only. Widening it is a deliberate decision, pinned by a test.
    /// </summary>
    public static readonly IReadOnlyList<string> CreatorGrantContexts = [SorchaDerivationPaths.RegisterAttestation];

    /// <summary>How long the creator's grant lasts before it must be renewed through the access endpoints.</summary>
    public static readonly TimeSpan CreatorGrantLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// Whether the caller is currently an Administrator of the organisation that owns the wallet. Read
    /// from the token alone: <c>org_id</c> is bound to it, so a caller cannot name an organisation they
    /// are not in. A personal wallet's owner is a user id, which never equals an organisation id.
    /// </summary>
    public static bool IsAdministratorOfOwningOrganisation(ClaimsPrincipal user, string? walletOwner)
    {
        ArgumentNullException.ThrowIfNull(user);

        var callerOrg = user.FindFirstValue(TokenClaimConstants.OrgId) ?? user.FindFirstValue("organization_id");

        return Guid.TryParse(callerOrg, out var callerOrgId)
            && Guid.TryParse(walletOwner, out var ownerOrgId)
            && callerOrgId == ownerOrgId
            && user.IsInRole(AdministratorRole);
    }

    /// <summary>Decides whether a caller who does not own the wallet may sign with it under a delegation.</summary>
    /// <param name="user">The caller's principal.</param>
    /// <param name="callerId">The caller's identity as grants record it (platform user id).</param>
    /// <param name="walletOwner">The wallet's owner.</param>
    /// <param name="grants">The wallet's access grants.</param>
    /// <param name="derivationPath">The derivation context or BIP44 path being signed at.</param>
    public static DelegatedSigningDecision EvaluateSigning(
        ClaimsPrincipal user,
        string callerId,
        string walletOwner,
        IEnumerable<WalletAccess> grants,
        string? derivationPath)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (!IsAdministratorOfOwningOrganisation(user, walletOwner))
        {
            return Refuse("the caller is not currently an Administrator of the organisation that owns this wallet");
        }

        var grant = grants.FirstOrDefault(g =>
            string.Equals(g.Subject, callerId, StringComparison.Ordinal)
            && g.IsActive
            && g.AccessRight is AccessRight.ReadWrite or AccessRight.Owner);

        if (grant is null)
        {
            return Refuse("the caller holds no active signing delegation on this wallet");
        }

        if (grant.AllowedDerivationContexts is not { Count: > 0 })
        {
            return Refuse("the delegation is unscoped; signing with an organisation's wallet requires a delegation scoped to named derivation contexts");
        }

        if (string.IsNullOrWhiteSpace(derivationPath))
        {
            return Refuse("a delegated signature must name a derivation context; an organisation wallet's default key is never delegated");
        }

        if (!TryResolve(derivationPath, out var requestedPath))
        {
            return Refuse($"'{derivationPath}' is not a known derivation context");
        }

        foreach (var context in grant.AllowedDerivationContexts)
        {
            if (TryResolve(context, out var allowedPath)
                && string.Equals(allowedPath, requestedPath, StringComparison.OrdinalIgnoreCase))
            {
                return new DelegatedSigningDecision(true, $"delegated signature at '{context}' under grant {grant.Id}", grant);
            }
        }

        return Refuse($"'{derivationPath}' is outside the delegation's scope ({string.Join(", ", grant.AllowedDerivationContexts)})");
    }

    /// <summary>Validates a grant request; returns the problem, or null when it is acceptable.</summary>
    /// <param name="accessRight">The right being granted.</param>
    /// <param name="allowedDerivationContexts">The contexts the grant may sign at.</param>
    /// <param name="viaOrganisationAdministrator">True when the grantor is acting as an Administrator of the owning organisation rather than as the wallet's owner.</param>
    public static string? ValidateGrant(
        AccessRight accessRight,
        IReadOnlyCollection<string>? allowedDerivationContexts,
        bool viaOrganisationAdministrator)
    {
        if (allowedDerivationContexts is { Count: > 0 })
        {
            foreach (var context in allowedDerivationContexts)
            {
                // Named contexts only: a raw BIP44 path can name slots that are not system keys, and a
                // mistyped context silently derives a different valid key (CLAUDE.md pattern 15).
                if (!SorchaDerivationPaths.IsSystemPath(context) || !TryResolve(context, out _))
                {
                    return $"'{context}' is not a known Sorcha derivation context. Name contexts such as "
                           + $"'{SorchaDerivationPaths.RegisterAttestation}', not raw BIP44 paths.";
                }
            }
        }

        if (!viaOrganisationAdministrator)
        {
            return null;
        }

        if (accessRight == AccessRight.Owner)
        {
            return "An organisation Administrator cannot grant Owner rights over the organisation's wallet.";
        }

        if (allowedDerivationContexts is not { Count: > 0 })
        {
            return "A grant on an organisation's wallet must name the derivation contexts it may sign at "
                   + $"(for example '{SorchaDerivationPaths.RegisterAttestation}').";
        }

        return null;
    }

    private static bool TryResolve(string pathOrContext, out string bip44Path)
    {
        try
        {
            bip44Path = SorchaDerivationPaths.ResolvePath(pathOrContext);
            return true;
        }
        catch (ArgumentException)
        {
            bip44Path = string.Empty;
            return false;
        }
    }

    private static DelegatedSigningDecision Refuse(string reason) => new(false, reason);
}
