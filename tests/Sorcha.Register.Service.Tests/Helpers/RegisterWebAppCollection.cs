// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Xunit;

namespace Sorcha.Register.Service.Tests.Helpers;

/// <summary>
/// Groups every <see cref="RegisterServiceWebApplicationFactory"/>-hosted integration
/// test class into a single xUnit collection so their ASP.NET hosts do NOT start
/// concurrently (issue #918).
/// </summary>
/// <remarks>
/// By default xUnit treats each test class as its own collection and runs collections
/// in parallel, so the 13 <c>IClassFixture&lt;RegisterServiceWebApplicationFactory&gt;</c>
/// classes booted 13 hosts at once. Locally (Debug) that is fine, but on the constrained
/// Release CI runner the concurrent startups starved the thread pool and the system-register
/// genesis bootstrap raced the first request, intermittently failing the register query /
/// creation / hub tests and red-flagging <c>build-and-test</c> on essentially every PR.
///
/// This definition carries NO <c>ICollectionFixture</c> — each class keeps its own
/// <c>IClassFixture</c> host, preserving per-class storage isolation. The collection only
/// serialises host startup. <c>DisableParallelization = true</c> additionally keeps these
/// heavy host tests out of the parallel phase entirely, so they never contend with the
/// rest of the assembly either.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RegisterWebAppCollection
{
    /// <summary>The collection name referenced by <c>[Collection(...)]</c> on each host test class.</summary>
    public const string Name = "RegisterWebApp";
}
