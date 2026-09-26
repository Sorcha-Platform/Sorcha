// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Endpoints;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// Issue #1684 — <c>disclosureMetadata: minimal</c> is a reserved, not-yet-implemented value. A
/// governance change that sets it must be refused at set time, not silently accepted: a policy
/// value nothing honours is worse than not having the field at all (CLAUDE.md pattern 23, applied
/// here to a policy rather than an exemption). Covers <see cref="RegisterPolicyEndpoints.ValidateDisclosureMetadata"/>,
/// the check <c>POST /api/registers/{registerId}/policy/update</c> runs before accepting a proposal.
/// </summary>
public class RegisterPolicyEndpointsTests
{
    [Fact]
    public void ValidateDisclosureMetadata_Public_IsAccepted()
    {
        var error = RegisterPolicyEndpoints.ValidateDisclosureMetadata(DisclosureMetadataPolicy.Public);

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateDisclosureMetadata_Minimal_IsRefusedWithAReason()
    {
        var error = RegisterPolicyEndpoints.ValidateDisclosureMetadata(DisclosureMetadataPolicy.Minimal);

        error.Should().NotBeNull();
        error.Should().Contain("reserved");
        error.Should().Contain("not yet implemented");
    }

    [Fact]
    public void ValidateDisclosureMetadata_AbsentOnANewPolicy_DefaultsToPublicAndIsAccepted()
    {
        // A RegisterPolicy that never set DisclosureMetadata explicitly (every register created
        // before this feature, and every caller that only sets the fields it cares about) carries
        // the enum's default value — this proves that default passes the same gate as an explicit
        // "Public", so absent really does mean public.
        var policy = new RegisterPolicy();

        var error = RegisterPolicyEndpoints.ValidateDisclosureMetadata(policy.DisclosureMetadata);

        error.Should().BeNull();
    }

    // ── #1697 — RegisterPolicyValidator existed with a full rule set and was never invoked, so every
    // field on a proposed policy was accepted unchecked. These pin that the endpoint's gate runs it.

    [Fact]
    public void ValidateProposedPolicy_TheDefaultPolicy_IsAccepted()
    {
        // The rules must not refuse the policy every register is created with.
        RegisterPolicyEndpoints.ValidateProposedPolicy(RegisterPolicy.CreateDefault())
            .Should().BeEmpty();
    }

    [Fact]
    public void ValidateProposedPolicy_AnOutOfRangeField_IsRefusedAndNamed()
    {
        var policy = RegisterPolicy.CreateDefault();
        policy.Governance.ProposalTtlDays = 365;

        var errors = RegisterPolicyEndpoints.ValidateProposedPolicy(policy);

        errors.Should().ContainKey("Governance.ProposalTtlDays");
    }

    [Fact]
    public void ValidateProposedPolicy_ANestedCollectionEntry_IsChecked()
    {
        var policy = RegisterPolicy.CreateDefault();
        policy.Validators.ApprovedValidators = [new ApprovedValidator { Did = "", PublicKey = "" }];

        var errors = RegisterPolicyEndpoints.ValidateProposedPolicy(policy);

        errors.Keys.Should().Contain(k => k.StartsWith("Validators.ApprovedValidators[0]"));
    }
}
