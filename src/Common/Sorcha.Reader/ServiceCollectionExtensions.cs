// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.Reader.Services;

namespace Sorcha.Reader;

/// <summary>Registers the in-person reader (Feature 185).</summary>
/// <remarks>
/// <para>
/// The reader is a <b>component library, not an application</b>. Today it is hosted by the citizen wallet —
/// one binary, one store record, signing and lanes already proven, and the BLE plugin already present. A
/// separate <c>app.sorcha.reader</c> would have needed new App Store Connect and Play records before anything
/// could be tested at all.
/// </para>
/// <para>
/// The standalone <c>Sorcha.Verifier.Pwa</c> host consumes exactly the same library, so splitting the reader
/// into its own product later costs nothing and duplicates nothing. That decision stays open; it just is not
/// blocking a two-device test.
/// </para>
/// <para>
/// The caller must already have registered an <c>IProximityTransport</c> — the reader does not choose its own
/// radio.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds the reader. Transient: a read owns one session, and Disconnected is terminal.</summary>
    public static IServiceCollection AddSorchaReader(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<IReaderService, ReaderService>();

        return services;
    }
}
