// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Proximity;
using Sorcha.Proximity.Capacitor;
using Sorcha.Verifier.Pwa;
using Sorcha.Reader;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Transient: a proximity transport is per-exchange and Disconnected is terminal — a shared instance would
// carry a dead channel into the next read.
builder.Services.AddTransient<IProximityTransport, CapacitorProximityTransport>();
builder.Services.AddSorchaReader();

// NOTE: there is deliberately NO HttpClient registered.
//
// This app verifies on-device, offline, with no server. Registering one would invite exactly the kind of
// "just fetch the status list" convenience that quietly turns an offline reader into an online one — and
// then fails at a roadside with no signal, which is precisely where it is needed most.

await builder.Build().RunAsync();
