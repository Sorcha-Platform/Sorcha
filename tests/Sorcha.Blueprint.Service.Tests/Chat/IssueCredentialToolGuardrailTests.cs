// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Templates;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Service.Tests.Chat;

/// <summary>
/// Issue #1550 — across four cold designer sessions the generator never set <c>vct</c>,
/// <c>displayName</c>, <c>issuanceCondition</c> or <c>disclosable</c>, despite the operator
/// stating the requirement in plain English every time.
/// </summary>
/// <remarks>
/// <para>
/// Two distinct problems, addressed differently. <c>vct</c> and <c>issuanceCondition</c> were
/// <b>expressible but unused</b> — the tool description already said "REQUIRED in practice" and
/// "USE THIS ON ANY APPROVE/REJECT ACTION", and the success payload already reported
/// "(none — ALWAYS issues, including on rejection)". Passive prose did not work, so <c>vct</c> is
/// now genuinely required and the decision shape is surfaced by <c>validate_blueprint</c>.
/// </para>
/// <para>
/// <c>disclosable</c> and <c>holderKeySourceField</c> were <b>not expressible at all</b> — the
/// tool had no such parameters, so the model could not have set them. They are now accepted.
/// </para>
/// </remarks>
public class IssueCredentialToolGuardrailTests
{
    private static BlueprintToolExecutor Executor() => new(
        NullLogger<BlueprintToolExecutor>.Instance,
        new Mock<ISchemaIndexService>().Object,
        new Mock<IBlueprintTemplateService>().Object);

    private static BlueprintBuilder BuilderWith(params BlueprintAction[] actions)
    {
        var builder = BlueprintBuilder.Create();
        var draft = builder.BuildDraft();
        draft.Title = "Credential Blueprint";
        draft.Description = "Exercises the issue_credential guardrails.";
        draft.Participants = [new Participant { Id = "applicant", Name = "Applicant" },
                              new Participant { Id = "assessor",  Name = "Assessor"  }];
        draft.Actions = [.. actions];
        return builder;
    }

    private static BlueprintAction Decide(IEnumerable<Route>? routes = null) => new()
    {
        Id = 2,
        Title = "Assess",
        Sender = "assessor",
        Disclosures = [new Disclosure("assessor", ["/*"])],
        Routes = routes
    };

    private static async Task<(bool Ok, string Payload, string? Error)> IssueAsync(
        BlueprintBuilder builder, string argsJson)
    {
        using var args = JsonDocument.Parse(argsJson);
        var r = await Executor().ExecuteAsync("issue_credential", args, builder);
        return (r.Success, r.Result?.RootElement.ToString() ?? string.Empty, r.Error);
    }

    private static async Task<string> ValidateAsync(BlueprintBuilder builder)
    {
        using var args = JsonDocument.Parse("{}");
        var r = await Executor().ExecuteAsync("validate_blueprint", args, builder);
        return r.Result?.RootElement.ToString() ?? string.Empty;
    }

    private const string Mappings = """[{"claimName":"companyName","sourceField":"/companyName"}]""";

    // ---- vct: expressible, was optional, now required ----------------------------------------

    [Fact]
    public async Task IssueCredential_WithoutVct_IsRefused()
    {
        var (ok, _, error) = await IssueAsync(BuilderWith(Decide()),
            $$"""{"actionId":2,"credentialType":"Cert","claimMappings":{{Mappings}},"recipientParticipantId":"applicant"}""");

        ok.Should().BeFalse("SD-JWT VC makes vct the credential's only type claim — without it no " +
                            "conforming verifier can match the credential to a requested type");
        error.Should().Contain("vct is required");
    }

    [Fact]
    public async Task IssueCredential_WithRelativeVct_IsRefused()
    {
        var (ok, _, error) = await IssueAsync(BuilderWith(Decide()),
            $$"""{"actionId":2,"credentialType":"Cert","vct":"not-a-uri","claimMappings":{{Mappings}},"recipientParticipantId":"applicant"}""");

        ok.Should().BeFalse();
        error.Should().Contain("absolute URI");
    }

    [Fact]
    public async Task IssueCredential_WithAbsoluteVct_IsAccepted()
    {
        var builder = BuilderWith(Decide());
        var (ok, payload, error) = await IssueAsync(builder,
            $$"""{"actionId":2,"credentialType":"Cert","vct":"https://sorcha.dev/vc/cert/v1","claimMappings":{{Mappings}},"recipientParticipantId":"applicant"}""");

        ok.Should().BeTrue(error);
        payload.Should().Contain("https://sorcha.dev/vc/cert/v1");
        builder.BuildDraft().Actions.Single().CredentialIssuanceConfig!.Vct
            .Should().Be("https://sorcha.dev/vc/cert/v1");
    }

    // ---- disclosable and holderKeySourceField: were not expressible at all --------------------

    [Fact]
    public async Task Disclosable_IsNowSettable()
    {
        var builder = BuilderWith(Decide());
        var (ok, _, error) = await IssueAsync(builder,
            $$"""{"actionId":2,"credentialType":"Cert","vct":"https://sorcha.dev/vc/cert/v1","claimMappings":{{Mappings}},"recipientParticipantId":"applicant","disclosable":["companyName"]}""");

        ok.Should().BeTrue(error);
        builder.BuildDraft().Actions.Single().CredentialIssuanceConfig!.Disclosable
            .Should().BeEquivalentTo(["companyName"],
                "before this the tool had no such parameter, so the model could not have set it");
    }

    [Fact]
    public async Task HolderKeySourceField_IsNowSettable()
    {
        var builder = BuilderWith(Decide());
        var (ok, _, error) = await IssueAsync(builder,
            $$"""{"actionId":2,"credentialType":"Cert","vct":"https://sorcha.dev/vc/cert/v1","claimMappings":{{Mappings}},"recipientParticipantId":"applicant","holderKeySourceField":"/holderKeys/holderJwk"}""");

        ok.Should().BeTrue(error);
        builder.BuildDraft().Actions.Single().CredentialIssuanceConfig!.HolderKeySourceField
            .Should().Be("/holderKeys/holderJwk",
                "without it an open/late-bound recipient cannot be delivered to — issuance fails " +
                "closed at runtime with VAL_RUNTIME_CRED_004");
    }

    // ---- validate_blueprint feedback ---------------------------------------------------------

    private static Route Cond() => new()
    {
        Id = "approve",
        NextActionIds = [],
        Condition = System.Text.Json.Nodes.JsonNode.Parse("""{ "==": [{ "var": "decision" }, "approved"] }""")
    };
    private static Route Default() => new() { Id = "reject", NextActionIds = [], IsDefault = true };

    [Fact]
    public async Task Validate_WarnsWhenADecisionActionMintsUnconditionally()
    {
        var builder = BuilderWith(Decide([Cond(), Default()]));
        await IssueAsync(builder,
            $$"""{"actionId":2,"credentialType":"Cert","vct":"https://sorcha.dev/vc/cert/v1","claimMappings":{{Mappings}},"recipientParticipantId":"applicant","disclosable":["companyName"]}""");

        (await ValidateAsync(builder)).Should().Contain("WARN_BP_CRED_005",
            "the author must be told at authoring time, not at Go-live");
    }

    [Fact]
    public async Task Validate_DoesNotWarnWhenIssuanceConditionIsPresent()
    {
        var builder = BuilderWith(Decide([Cond(), Default()]));
        await IssueAsync(builder,
            """{"actionId":2,"credentialType":"Cert","vct":"https://sorcha.dev/vc/cert/v1","claimMappings":"""
            + Mappings
            + ""","recipientParticipantId":"applicant","disclosable":["companyName"],"issuanceCondition":{"==":[{"var":"decision"},"approved"]}}""");

        (await ValidateAsync(builder)).Should().NotContain("WARN_BP_CRED_005");
    }

    [Fact]
    public async Task Validate_WarnsWhenNoDisclosableSetIsDeclared()
    {
        var builder = BuilderWith(Decide([Default()]));
        await IssueAsync(builder,
            $$"""{"actionId":2,"credentialType":"Cert","vct":"https://sorcha.dev/vc/cert/v1","claimMappings":{{Mappings}},"recipientParticipantId":"applicant"}""");

        (await ValidateAsync(builder)).Should().Contain("NO_DISCLOSABLE_SET",
            "a null disclosable set is expanded to EVERY claim name at signing time — it does not " +
            "mean 'none', which is the opposite of what an author would assume");
    }

    [Fact]
    public async Task Validate_DoesNotWarnWhenDisclosableIsDeclared()
    {
        var builder = BuilderWith(Decide([Default()]));
        await IssueAsync(builder,
            $$"""{"actionId":2,"credentialType":"Cert","vct":"https://sorcha.dev/vc/cert/v1","claimMappings":{{Mappings}},"recipientParticipantId":"applicant","disclosable":["companyName"]}""");

        (await ValidateAsync(builder)).Should().NotContain("NO_DISCLOSABLE_SET");
    }
}
