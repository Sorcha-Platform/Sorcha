// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Sorcha.Blueprint.Service.Models.Requests;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Hot-path safety for VAL-001 (#327): the RequestValidationFilter is applied to the action-submission
/// endpoints, so a realistic *valid* <see cref="ActionSubmissionRequest"/> MUST pass (no false-reject of
/// the flow every workflow/walkthrough uses), while a missing required field MUST 400 before the handler.
/// </summary>
public class ActionSubmissionRequestValidationTests
{
    private static async Task<(object? Result, bool NextCalled)> RunAsync(object dto)
    {
        var filter = new RequestValidationFilter();
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(), dto);
        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };
        var result = await filter.InvokeAsync(context, next);
        return (result, nextCalled);
    }

    private static ActionSubmissionRequest ValidRequest() => new()
    {
        BlueprintId = "construction-permit-20260702142305",
        ActionId = "1",
        InstanceId = "e74f9a00-df05-430a-ac4b-5fd0afc25930",
        SenderWallet = "ws11qpcddjp77xyzz3lea9m4jpwq5kk8k8pl5kuvsncylqyx25da9eapuunlcqf",
        RegisterAddress = "be0658c487284081afea893b6d6d2065",
        PayloadData = new Dictionary<string, object> { ["projectName"] = "Riverside" },
        Files = new List<FileAttachment>
        {
            new() { FileName = "site.jpg", ContentType = "image/jpeg", ContentBase64 = "iVBORw0KGgo=" },
        },
    };

    [Fact]
    public async Task RealisticValidSubmission_PassesFilter()
    {
        var (_, nextCalled) = await RunAsync(ValidRequest());
        nextCalled.Should().BeTrue("a valid action submission must not be rejected by input validation");
    }

    [Fact]
    public async Task EmptyBlueprintId_Rejected400()
    {
        var (result, nextCalled) = await RunAsync(ValidRequest() with { BlueprintId = "" });
        nextCalled.Should().BeFalse();
        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task FileAttachmentMissingContent_Rejected400()
    {
        var bad = ValidRequest() with
        {
            Files = new List<FileAttachment>
            {
                new() { FileName = "x.jpg", ContentType = "image/jpeg", ContentBase64 = "" },
            },
        };
        var (result, nextCalled) = await RunAsync(bad);
        nextCalled.Should().BeFalse("a file attachment with empty content must be rejected");
        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
