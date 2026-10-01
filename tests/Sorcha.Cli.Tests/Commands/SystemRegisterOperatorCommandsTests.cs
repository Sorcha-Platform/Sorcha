// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using Refit;
using Sorcha.Cli.Commands;
using Sorcha.Cli.Services;
using Spectre.Console;
using Xunit;

namespace Sorcha.Cli.Tests.Commands;

/// <summary>
/// Tests for the Feature 197 operator commands: <c>system-register drift</c> and
/// <c>system-register publish</c>. The Refit client is real and bound to a fake handler, so the request
/// bytes asserted here are the ones that would go on the wire.
/// </summary>
public class SystemRegisterOperatorCommandsTests
{
    private const string Token = "test-token";

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private static (IRegisterServiceClient Client, FakeHandler Handler) CreateClient(HttpStatusCode status, string body)
    {
        var handler = new FakeHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://register.test") };
        return (RestService.For<IRegisterServiceClient>(http), handler);
    }

    private static (StringWriter Writer, IAnsiConsole Console) CreateConsole()
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 250;
        return (writer, console);
    }

    private static SystemRegisterPublishCommand NewPublishCommand() =>
        new(
            new HttpClientFactory(Mock.Of<IConfigurationService>()),
            Mock.Of<IAuthenticationService>(),
            Mock.Of<IConfigurationService>());

    [Fact]
    public void SystemRegisterGenesisCommand_RegistersDriftAndPublishSubcommands()
    {
        var parent = new SystemRegisterGenesisCommand(
            new HttpClientFactory(Mock.Of<IConfigurationService>()),
            Mock.Of<IAuthenticationService>(),
            Mock.Of<IConfigurationService>());

        parent.Subcommands.Select(c => c.Name).Should().Contain(["drift", "publish"]);
    }

    [Fact]
    public void Publish_Parse_BindsArgumentAndOptions()
    {
        var command = NewPublishCommand();

        var result = command.Parse("some-blueprint --dry-run --expected-current abc123");

        result.Errors.Should().BeEmpty();
        result.GetValue(command.Arguments.OfType<Argument<string>>().Single()).Should().Be("some-blueprint");
        result.GetValue(command.Options.OfType<Option<bool>>().Single(o => o.Name == "--dry-run")).Should().BeTrue();
        result.GetValue(command.Options.OfType<Option<string?>>().Single(o => o.Name == "--expected-current"))
            .Should().Be("abc123");
    }

    [Fact]
    public void Publish_Parse_WithoutBlueprintId_IsAnError()
    {
        NewPublishCommand().Parse("--dry-run").Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Publish_DryRun_SendsDryRunTrueInCamelCase()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK,
            """{"blueprintId":"bp-1","outcome":"dry-run","state":"image-ahead","currentPublicationTxId":"aaaa","candidatePublicationTxId":"bbbb","transactionId":null}""");
        var (_, console) = CreateConsole();

        var exit = await SystemRegisterPublishCommand.ExecuteAsync(
            client, Token, "bp-1", dryRun: true, expectedCurrent: null, structuredOutput: false, console, CancellationToken.None);

        exit.Should().Be(ExitCodes.Success);
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/system-register/blueprints/bp-1/publish");
        handler.Request.Headers.Authorization!.ToString().Should().Be($"Bearer {Token}");
        handler.RequestBody.Should().Contain("\"dryRun\":true");
        handler.RequestBody.Should().NotContain("dry_run");
    }

    [Fact]
    public async Task Publish_ExpectedCurrent_SendsExpectedCurrentValue()
    {
        var (client, handler) = CreateClient(HttpStatusCode.Accepted,
            """{"blueprintId":"bp-1","outcome":"submitted","state":"image-ahead","candidatePublicationTxId":"bbbb","transactionId":"tx-9"}""");
        var (writer, console) = CreateConsole();

        var exit = await SystemRegisterPublishCommand.ExecuteAsync(
            client, Token, "bp-1", dryRun: false, expectedCurrent: "aaaa", structuredOutput: false, console, CancellationToken.None);

        exit.Should().Be(ExitCodes.Success);
        handler.RequestBody.Should().Contain("\"expectedCurrent\":\"aaaa\"");
        handler.RequestBody.Should().Contain("\"dryRun\":false");
        var output = writer.ToString();
        output.Should().Contain("submitted").And.Contain("tx-9").And.Contain("image-ahead");
    }

    [Fact]
    public async Task Publish_409Rollback_SurfacesDetailAndReasonWithNonZeroExit()
    {
        var (client, _) = CreateClient(HttpStatusCode.Conflict,
            """{"title":"publish refused","status":409,"detail":"image is behind the register","reason":"rollback"}""");
        var (writer, console) = CreateConsole();

        var exit = await SystemRegisterPublishCommand.ExecuteAsync(
            client, Token, "bp-1", false, null, false, console, CancellationToken.None);

        exit.Should().NotBe(ExitCodes.Success);
        var output = writer.ToString();
        output.Should().Contain("image is behind the register");
        output.Should().Contain("rollback");
    }

    [Fact]
    public async Task Publish_409Concurrency_SurfacesReason()
    {
        var (client, _) = CreateClient(HttpStatusCode.Conflict,
            """{"title":"publish refused","status":409,"detail":"expectedCurrent does not match","reason":"concurrency"}""");
        var (writer, console) = CreateConsole();

        var exit = await SystemRegisterPublishCommand.ExecuteAsync(
            client, Token, "bp-1", false, "stale", false, console, CancellationToken.None);

        exit.Should().NotBe(ExitCodes.Success);
        writer.ToString().Should().Contain("concurrency").And.Contain("expectedCurrent does not match");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ExitCodes.AuthorizationError)]
    [InlineData(HttpStatusCode.NotFound, ExitCodes.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, ExitCodes.AuthenticationError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ExitCodes.ServiceError)]
    public async Task Publish_ProblemResponse_PrintsDetailAndMapsExitCode(HttpStatusCode status, int expectedExit)
    {
        var (client, _) = CreateClient(status, """{"title":"t","detail":"the specific detail"}""");
        var (writer, console) = CreateConsole();

        var exit = await SystemRegisterPublishCommand.ExecuteAsync(
            client, Token, "bp-1", false, null, false, console, CancellationToken.None);

        exit.Should().Be(expectedExit);
        writer.ToString().Should().Contain("the specific detail");
    }

    [Fact]
    public async Task Publish_NonJsonErrorBody_StillFailsWithBodyShown()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadGateway, "upstream exploded");
        var (writer, console) = CreateConsole();

        var exit = await SystemRegisterPublishCommand.ExecuteAsync(
            client, Token, "bp-1", false, null, false, console, CancellationToken.None);

        exit.Should().Be(ExitCodes.ServiceError);
        writer.ToString().Should().Contain("upstream exploded");
    }

    [Fact]
    public async Task Drift_RendersKebabStateStringsAndVersions()
    {
        var (client, handler) = CreateClient(HttpStatusCode.OK, """
            {"checkedAt":"2026-10-01T10:00:00+00:00","entries":[
              {"blueprintId":"bp-sync","state":"in-sync","currentPublicationTxId":"aaaaaaaaaaaaaaaaaaaa","currentVersion":3,"imagePublicationTxId":"aaaaaaaaaaaaaaaaaaaa","imageMatchesVersion":3,"checkedAt":"2026-10-01T10:00:00+00:00"},
              {"blueprintId":"bp-behind","state":"image-behind","currentPublicationTxId":"cccccccccccccccccccc","currentVersion":4,"imagePublicationTxId":"dddddddddddddddddddd","imageMatchesVersion":2,"checkedAt":"2026-10-01T10:00:00+00:00"},
              {"blueprintId":"bp-missing","state":"missing","currentPublicationTxId":null,"currentVersion":null,"imagePublicationTxId":"eeeeeeeeeeeeeeeeeeee","imageMatchesVersion":null,"checkedAt":"2026-10-01T10:00:00+00:00"}
            ]}
            """);
        var (writer, console) = CreateConsole();

        var exit = await SystemRegisterDriftCommand.ExecuteAsync(client, Token, false, console, CancellationToken.None);

        exit.Should().Be(ExitCodes.Success);
        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/system-register/drift");
        var output = writer.ToString();
        output.Should().Contain("in-sync").And.Contain("image-behind").And.Contain("missing");
        output.Should().Contain("bp-sync").And.Contain("bp-behind").And.Contain("bp-missing");
        output.Should().Contain("aaaaaaaaaaaa...");
        output.Should().NotContain("aaaaaaaaaaaaaaaaaaaa", "ids are shortened for display");
    }

    [Fact]
    public async Task Drift_403_PrintsDetailAndFailsWithAuthorizationExit()
    {
        var (client, _) = CreateClient(HttpStatusCode.Forbidden, """{"title":"Forbidden","detail":"SystemAdmin required"}""");
        var (writer, console) = CreateConsole();

        var exit = await SystemRegisterDriftCommand.ExecuteAsync(client, Token, false, console, CancellationToken.None);

        exit.Should().Be(ExitCodes.AuthorizationError);
        writer.ToString().Should().Contain("SystemAdmin required");
    }
}
