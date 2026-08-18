// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Constants;

/// <summary>
/// Verifiable Credential Type URIs for citizen-wallet-issued credentials.
/// </summary>
public static class VctUris
{
    /// <summary>
    /// Device delegation credential v1. Issued by the citizen's holder key
    /// to a specific enrolled device authorising it to make presentations.
    /// </summary>
    public const string CitizenDeviceDelegationV1 =
        "https://sorcha.dev/vc/citizen-device-delegation/v1";

    /// <summary>Assured identity credential v1.</summary>
    public const string AssuredIdentityV1 =
        "https://sorcha.dev/vc/assured-identity/v1";

    /// <summary>Driving licence credential v1.</summary>
    public const string DrivingLicenceV1 =
        "https://sorcha.dev/vc/driving-licence/v1";

    /// <summary>Blue badge credential v1.</summary>
    public const string BlueBadgeV1 =
        "https://sorcha.dev/vc/blue-badge/v1";

    /// <summary>Membership credential v1.</summary>
    public const string MembershipV1 =
        "https://sorcha.dev/vc/membership/v1";

    /// <summary>Licence credential v1.</summary>
    public const string LicenceV1 =
        "https://sorcha.dev/vc/licence/v1";

    /// <summary>Council digital ID credential v1.</summary>
    public const string CouncilDigitalIdV1 =
        "https://sorcha.dev/vc/council-digital-id/v1";

    /// <summary>Verified invoice credential v1.</summary>
    public const string VerifiedInvoiceV1 =
        "https://sorcha.dev/vc/verified-invoice/v1";

    /// <summary>Trade finance credential v1.</summary>
    public const string TradeFinanceV1 =
        "https://sorcha.dev/vc/trade-finance/v1";

    /// <summary>Planning permission credential v1.</summary>
    public const string PlanningPermissionV1 =
        "https://sorcha.dev/vc/planning-permission/v1";

    /// <summary>Building warrant credential v1.</summary>
    public const string BuildingWarrantV1 =
        "https://sorcha.dev/vc/building-warrant/v1";

    /// <summary>Completion certificate credential v1.</summary>
    public const string CompletionCertificateV1 =
        "https://sorcha.dev/vc/completion-certificate/v1";

    /// <summary>Job assignment credential v1.</summary>
    public const string JobAssignmentV1 =
        "https://sorcha.dev/vc/job-assignment/v1";

    /// <summary>Service completion credential v1.</summary>
    public const string ServiceCompletionV1 =
        "https://sorcha.dev/vc/service-completion/v1";

    /// <summary>Forest product digital product passport credential v1.</summary>
    public const string ForestProductDppV1 =
        "https://sorcha.dev/vc/forest-product-dpp/v1";

    /// <summary>Cyber Essentials UAC credential v1.</summary>
    public const string CyberEssentialsUacV1 =
        "https://sorcha.dev/vc/cyber-essentials-uac/v1";

    /// <summary>Refurbishment certificate credential v1.</summary>
    public const string RefurbishmentCertificateV1 =
        "https://sorcha.dev/vc/refurbishment-certificate/v1";

    /// <summary>Building permit credential v1.</summary>
    public const string BuildingPermitV1 =
        "https://sorcha.dev/vc/building-permit/v1";

    /// <summary>
    /// AIAS Cyber Level credential v1. Issued after the cyber-hygiene questionnaire is
    /// scored into a band; carries the level plus the portrait carried forward from the
    /// Assured Identity credential presented at the gate.
    /// </summary>
    public const string CyberLevelV1 =
        "https://sorcha.dev/vc/cyber-level/v1";

    /// <summary>
    /// Credential-lifecycle conformance credential v1. Issued by
    /// <c>walkthroughs/CredentialLifecycle</c> purely so its status can be driven through every
    /// state the two status-list specifications define — active, suspended, reinstated, revoked.
    /// </summary>
    /// <remarks>
    /// It is a real platform type rather than an ad-hoc string because the conformance suite has to
    /// exercise the same issuance path every other credential uses; a type outside the catalogue
    /// would be testing a path nothing else takes. It carries no meaning outside that suite and
    /// should not be required by any production blueprint.
    /// </remarks>
    public const string CredentialLifecycleConformanceV1 =
        "https://sorcha.dev/vc/credential-lifecycle-conformance/v1";
}
