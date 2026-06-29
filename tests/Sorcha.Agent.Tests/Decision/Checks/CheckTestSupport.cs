// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Sorcha.Agent.Tests.Decision.Checks;

/// <summary>
/// Shared helpers for the external-check tests: builds the top-level payload dictionary the checks
/// consume, and a scriptable <see cref="HttpMessageHandler"/> for the postcode check.
/// </summary>
internal static class CheckTestSupport
{
    /// <summary>Projects a JSON object literal into the <c>{ key -> JsonNode }</c> payload checks receive.</summary>
    public static IReadOnlyDictionary<string, object?> Payload(string json)
    {
        var obj = JsonNode.Parse(json)!.AsObject();
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in obj)
            dict[key] = value;
        return dict;
    }
}

/// <summary>Minimal log provider that forwards entries to a caller-supplied delegate.</summary>
internal sealed class CapturingLoggerProvider(Action<string, LogLevel, string> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);
    public void Dispose() { }

    private sealed class CapturingLogger(string category, Action<string, LogLevel, string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => sink(category, logLevel, formatter(state, exception));
    }
}

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that returns a canned response, or throws to simulate a
/// network fault (for exercising the offline fallback).
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public int Calls { get; private set; }

    private StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    /// <summary>Returns 200 with the given JSON body for every request.</summary>
    public static StubHttpMessageHandler Json(string body) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    /// <summary>Throws <see cref="HttpRequestException"/> for every request (simulates an offline venue).</summary>
    public static StubHttpMessageHandler Faulting() =>
        new(_ => throw new HttpRequestException("network down"));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(_responder(request));
    }

    public HttpClient Client() => new(this);
}
