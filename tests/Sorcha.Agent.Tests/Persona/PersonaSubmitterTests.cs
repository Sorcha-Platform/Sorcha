// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

public class PersonaSubmitterTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = "{}";
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public Exception? ThrowThis { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            if (ThrowThis is not null) throw ThrowThis;
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(ResponseBody)
            };
        }
    }

    private static PersonaDefinition MakeDefinition() => new()
    {
        Name = "test",
        Target = new PersonaTarget { BlueprintId = "bp-1", InstanceId = "inst-1", ActionIndex = 0 },
        Trigger = new OnceTrigger(),
        PayloadTemplate = JsonNode.Parse("{}")!
    };

    private static PersonaSubmitter MakeSubmitter(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            _ => Task.FromResult("test-token"),
            walletAddress: "wallet-1",
            registerId: "register-1",
            NullLogger<PersonaSubmitter>.Instance);

    [Fact]
    public async Task SubmitAsync_HttpOk_ReturnsSubmitted()
    {
        var handler = new StubHandler { Status = HttpStatusCode.OK };
        var submitter = MakeSubmitter(handler);

        var result = await submitter.SubmitAsync(MakeDefinition(),
            JsonNode.Parse("""{ "v": 1 }""")!.AsObject(), CancellationToken.None);

        result.Outcome.Should().Be(PersonaSubmissionOutcome.Submitted);
    }

    [Fact]
    public async Task SubmitAsync_Http503_ReturnsTransientFailure()
    {
        var handler = new StubHandler { Status = HttpStatusCode.ServiceUnavailable };
        var submitter = MakeSubmitter(handler);

        var result = await submitter.SubmitAsync(MakeDefinition(),
            new JsonObject(), CancellationToken.None);

        result.Outcome.Should().Be(PersonaSubmissionOutcome.TransientFailure);
    }

    [Fact]
    public async Task SubmitAsync_Http400_ReturnsHardFailure()
    {
        var handler = new StubHandler { Status = HttpStatusCode.BadRequest };
        var submitter = MakeSubmitter(handler);

        var result = await submitter.SubmitAsync(MakeDefinition(),
            new JsonObject(), CancellationToken.None);

        result.Outcome.Should().Be(PersonaSubmissionOutcome.HardFailure);
    }

    [Fact]
    public async Task SubmitAsync_Http429_ReturnsTransientFailure()
    {
        var handler = new StubHandler { Status = HttpStatusCode.TooManyRequests };
        var submitter = MakeSubmitter(handler);

        var result = await submitter.SubmitAsync(MakeDefinition(),
            new JsonObject(), CancellationToken.None);

        result.Outcome.Should().Be(PersonaSubmissionOutcome.TransientFailure);
    }

    [Fact]
    public async Task SubmitAsync_NetworkError_ReturnsTransientFailure()
    {
        var handler = new StubHandler { ThrowThis = new HttpRequestException("boom") };
        var submitter = MakeSubmitter(handler);

        var result = await submitter.SubmitAsync(MakeDefinition(),
            new JsonObject(), CancellationToken.None);

        result.Outcome.Should().Be(PersonaSubmissionOutcome.TransientFailure);
        result.Error.Should().Contain("boom");
    }

    [Fact]
    public async Task SubmitAsync_TargetsCorrectEndpointAndSendsPayload()
    {
        var handler = new StubHandler { Status = HttpStatusCode.OK };
        var submitter = MakeSubmitter(handler);
        var payload = JsonNode.Parse("""{ "amount": 42 }""")!.AsObject();

        await submitter.SubmitAsync(MakeDefinition(), payload, CancellationToken.None);

        handler.LastRequest!.RequestUri!.PathAndQuery.Should()
            .Be("/api/instances/inst-1/actions/0/execute");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be("test-token");
        // Blueprint Service requires both headers carry the same JWT (see PersonaSubmitter).
        handler.LastRequest.Headers.GetValues("X-Delegation-Token").Should().ContainSingle().Which.Should().Be("test-token");
        handler.LastRequestBody.Should().Contain("\"amount\":42");
        handler.LastRequestBody.Should().Contain("\"senderWallet\":\"wallet-1\"");
        handler.LastRequestBody.Should().Contain("\"registerAddress\":\"register-1\"");
    }

    [Fact]
    public async Task SubmitAsync_NullActionIndex_ThrowsInvalidOperation()
    {
        var submitter = MakeSubmitter(new StubHandler());
        var def = MakeDefinition() with
        {
            Target = new PersonaTarget { BlueprintId = "bp", InstanceId = "i", ActionName = "by-name-only" }
        };

        var act = () => submitter.SubmitAsync(def, new JsonObject(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ActionIndex*");
    }
}
