// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Server.Kestrel.Core;

using Sorcha.PeerRouter.Endpoints;
using Sorcha.PeerRouter.GrpcServices;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

var builder = WebApplication.CreateBuilder(args);

// Parse CLI configuration
var config = RouterConfiguration.FromArgs(args);

// Configure Kestrel for HTTP/2 cleartext (h2c) so gRPC works behind
// reverse proxies that terminate TLS (e.g. Azure Container Apps Envoy).
// Http2-only is required for h2c without TLS — Http1AndHttp2 only enables
// HTTP/2 via TLS ALPN negotiation, which doesn't apply to plain HTTP.
// Browsers still work because the reverse proxy handles HTTP/1.1 on the frontend.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(config.HttpPort, o =>
        o.Protocols = HttpProtocols.Http2);
});

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<RoutingTable>();
builder.Services.AddSingleton<EventBuffer>();
builder.Services.AddHostedService<PeerTimeoutService>();

builder.Services.AddGrpc();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// gRPC services
app.MapGrpcService<RouterDiscoveryService>();
app.MapGrpcService<RouterHeartbeatService>();

if (config.EnableRelay)
{
    app.MapGrpcService<RouterCommunicationService>();
    app.Logger.LogInformation("Relay mode enabled — RouterCommunicationService mapped");
}

// HTTP endpoints
app.MapEventStreamEndpoints();
app.MapPeerEndpoints();
app.MapHealthEndpoints();

app.Run();
