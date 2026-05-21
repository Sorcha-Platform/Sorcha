// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Regression guard for the sign-out data-purge fix. Sign-out used to clear ONLY
/// the access token, leaving credentials, personas, history and the welcome/tour
/// flags in IndexedDB for the next citizen on the same device — a cross-user data
/// leak and the cause of the "welcome wizard won't reappear for a new user"
/// symptom.
/// </summary>
/// <remarks>
/// The real <c>Sign out</c> button on the Settings page only renders for a
/// signed-in citizen, which needs a citizen JWT seeded into IndexedDB — the
/// fixture gap tracked by Issue #700. So this test exercises the mechanism the
/// sign-out path delegates to: <c>SorchaIndexedDb.wipe()</c>, which
/// <c>IndexedDbLocalDataPurge</c> calls. It seeds a representative set of object
/// stores (keyed and keyPath shapes), wipes, and asserts every store is empty.
/// </remarks>
[TestFixture]
[Category("Docker")]
[Category("CitizenWallet")]
public class CitizenWalletSignOutWipeTests : AuthenticatedCitizenWalletTestBase
{
    [Test]
    [Retry(2)]
    public async Task IndexedDbWipe_ClearsEveryObjectStore()
    {
        var jsErrors = new List<string>();
        Page.PageError += (_, error) => jsErrors.Add(error);

        await NavigateToWalletAndWaitForBlazorAsync();

        var bridgePresent = await Page.EvaluateAsync<bool>("() => !!window.SorchaIndexedDb");
        if (!bridgePresent)
        {
            Assert.Inconclusive("SorchaIndexedDb bridge not present — PWA static assets may not be served.");
            return;
        }

        var hasWipe = await Page.EvaluateAsync<bool>(
            "() => typeof window.SorchaIndexedDb.wipe === 'function'");
        Assert.That(hasWipe, Is.True,
            "SorchaIndexedDb.wipe must be exported — the sign-out purge calls it.");

        // Seed a representative spread of stores: the shared `device` store
        // (flags + access-token), a keyPath store (`credentials`), a keyed
        // store (`personas`), and two more keyPath stores (`verifications`,
        // `presentationLog`). Then wipe and re-read.
        var result = await Page.EvaluateAsync<JsonElement>(
            @"async () => {
                const db = window.SorchaIndexedDb;
                await db.put('device', { welcomedAt: new Date().toISOString() }, 'flags');
                await db.put('device', { accessToken: 'jwt-leak' }, 'access-token');
                await db.put('credentials', { id: 'cred-leak', nonce: 'n', ciphertext: 'c' });
                await db.put('personas', { value: 1 }, 'persona-leak');
                await db.put('verifications', { id: 'verif-leak' });
                await db.put('presentationLog', { id: 'plog-leak' });

                const snapshot = async () => ({
                    flags: !!(await db.get('device', 'flags')),
                    token: !!(await db.get('device', 'access-token')),
                    creds: (await db.getAll('credentials')).length,
                    personas: !!(await db.get('personas', 'persona-leak')),
                    verifs: (await db.getAll('verifications')).length,
                    plog: (await db.getAll('presentationLog')).length,
                });

                const before = await snapshot();
                await db.wipe();
                const after = await snapshot();
                return { before, after };
            }");

        var before = result.GetProperty("before");
        var after = result.GetProperty("after");

        // Sanity: the seed actually landed (otherwise the wipe assertion is vacuous).
        Assert.Multiple(() =>
        {
            Assert.That(before.GetProperty("flags").GetBoolean(), Is.True, "seed: flags");
            Assert.That(before.GetProperty("token").GetBoolean(), Is.True, "seed: access-token");
            Assert.That(before.GetProperty("creds").GetInt32(), Is.GreaterThan(0), "seed: credentials");
            Assert.That(before.GetProperty("personas").GetBoolean(), Is.True, "seed: personas");
            Assert.That(before.GetProperty("verifs").GetInt32(), Is.GreaterThan(0), "seed: verifications");
            Assert.That(before.GetProperty("plog").GetInt32(), Is.GreaterThan(0), "seed: presentationLog");
        });

        // The fix: every store is empty after wipe.
        Assert.Multiple(() =>
        {
            Assert.That(after.GetProperty("flags").GetBoolean(), Is.False, "wipe must clear welcome/tour flags");
            Assert.That(after.GetProperty("token").GetBoolean(), Is.False, "wipe must clear the access token");
            Assert.That(after.GetProperty("creds").GetInt32(), Is.Zero, "wipe must clear credentials");
            Assert.That(after.GetProperty("personas").GetBoolean(), Is.False, "wipe must clear personas");
            Assert.That(after.GetProperty("verifs").GetInt32(), Is.Zero, "wipe must clear verification history");
            Assert.That(after.GetProperty("plog").GetInt32(), Is.Zero, "wipe must clear the presentation log");
        });

        Assert.That(jsErrors, Is.Empty,
            $"wipe must not raise uncaught JS errors. Got: {string.Join("\n", jsErrors)}");
    }
}
