// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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

// gRPC services mapped in Phase 3 (US1)
// app.MapGrpcService<Sorcha.PeerRouter.GrpcServices.RouterDiscoveryService>();
// app.MapGrpcService<Sorcha.PeerRouter.GrpcServices.RouterHeartbeatService>();

// HTTP endpoints mapped in Phase 4 (US2)
// app.MapGet("/", ...);


app.Run();
