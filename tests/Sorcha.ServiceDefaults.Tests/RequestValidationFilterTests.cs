// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Sorcha.ServiceDefaults.Tests;

/// <summary>
/// Tests for <see cref="RequestValidationFilter"/> (VAL-001, #327) — DataAnnotations on request DTOs
/// short-circuit to 400 ValidationProblem before the handler runs; valid requests pass through.
/// </summary>
public class RequestValidationFilterTests
{
    public record SampleRequest
    {
        [Required(AllowEmptyStrings = false)]
        [StringLength(10, MinimumLength = 2)]
        public string? Name { get; init; }

        public SampleNested? Nested { get; init; }
    }

    public record SampleNested
    {
        [RegularExpression("^#[0-9a-fA-F]{6}$")]
        public string? Colour { get; init; }
    }

    // Type with no validation attributes — must be skipped entirely (e.g. an injected service arg).
    public record NoAttributes
    {
        public string? Anything { get; init; }
    }

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

    private static int? StatusOf(object? result) => (result as IStatusCodeHttpResult)?.StatusCode;

    [Fact]
    public async Task Valid_CallsNext()
    {
        var (_, nextCalled) = await RunAsync(new SampleRequest { Name = "OK" });
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task MissingRequired_ShortCircuitsWith400()
    {
        var (result, nextCalled) = await RunAsync(new SampleRequest { Name = "" });
        nextCalled.Should().BeFalse("an invalid request must not reach the handler");
        StatusOf(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task TooShort_ShortCircuitsWith400()
    {
        var (result, nextCalled) = await RunAsync(new SampleRequest { Name = "x" }); // min length 2
        nextCalled.Should().BeFalse();
        StatusOf(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task InvalidNestedDto_ShortCircuitsWith400()
    {
        var (result, nextCalled) = await RunAsync(
            new SampleRequest { Name = "OK", Nested = new SampleNested { Colour = "not-a-colour" } });
        nextCalled.Should().BeFalse("nested request DTOs must be validated too");
        StatusOf(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ValidNestedDto_CallsNext()
    {
        var (_, nextCalled) = await RunAsync(
            new SampleRequest { Name = "OK", Nested = new SampleNested { Colour = "#1A2B3C" } });
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task TypeWithoutAttributes_IsSkipped_CallsNext()
    {
        var (_, nextCalled) = await RunAsync(new NoAttributes { Anything = "" });
        nextCalled.Should().BeTrue("types with no validation attributes (e.g. injected services) are not validated");
    }
}
