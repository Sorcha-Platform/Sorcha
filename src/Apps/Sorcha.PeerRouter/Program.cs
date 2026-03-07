// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.PeerRouter.Endpoints;
using Sorcha.PeerRouter.GrpcServices;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

var builder = WebApplication.CreateBuilder(args);

// Parse CLI configuration
var config = RouterConfiguration.FromArgs(args);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<RoutingTable>();
builder.Services.AddSingleton<EventBuffer>();
builder.Services.AddHostedService<PeerTimeoutService>();

builder.Services.AddGrpc();

var app = builder.Build();

app.UseStaticFiles();

// gRPC services
app.MapGrpcService<RouterDiscoveryService>();
app.MapGrpcService<RouterHeartbeatService>();

// HTTP endpoints
app.MapEventStreamEndpoints();
app.MapPeerEndpoints();
app.MapHealthEndpoints();

app.Run();
