// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Regression guard for the credential-cache cipher migration. PR #797 switched
/// the at-rest credential cache from AES-GCM-256 (12-byte nonce, WebCrypto) to
/// XChaCha20-Poly1305 (24-byte nonce, noble) with no migration. A credential row
/// written by the pre-#797 code is fed to the XChaCha decrypt path, which throws
/// "Uint8Array expected of length 24, got length=12" — and because
/// <c>listCredentials()</c> threw on the first bad row, the whole sync aborted
/// (surfaced in the UI as "Sync error: ..."), permanently blocking sync for any
/// device carrying a legacy-scheme credential.
/// </summary>
/// <remarks>
/// The cache is a local mirror of server-authoritative state, so the fix is to
/// make it self-healing: rows that fail to decrypt (legacy scheme, corruption)
/// are evicted and skipped, and the server re-seeds them under the current
/// scheme. This test seeds a legacy-shaped (12-byte-nonce) row alongside a valid
/// current-scheme row and asserts <c>listCredentials()</c> does not throw, returns
/// the good row, and evicts the bad one.
/// </remarks>
[TestFixture]
[Category("Docker")]
[Category("CitizenWallet")]
public class CitizenWalletCredentialCacheMigrationTests : AuthenticatedCitizenWalletTestBase
{
    [Test]
    [Retry(2)]
    public async Task ListCredentials_ToleratesUndecryptableLegacyRow_AndEvictsIt()
    {
        await NavigateToWalletAndWaitForBlazorAsync();

        var bridgePresent = await Page.EvaluateAsync<bool>(
            "() => !!(window.SorchaIndexedDb && window.SorchaXChaCha)");
        if (!bridgePresent)
        {
            Assert.Inconclusive("SorchaIndexedDb / SorchaXChaCha bridge not present.");
            return;
        }

        var result = await Page.EvaluateAsync<JsonElement>(
            @"async () => {
                const db = window.SorchaIndexedDb;
                const b64url = (bytes) => btoa(String.fromCharCode(...bytes))
                    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

                // Legacy AES-GCM-shaped row: a 12-byte nonce the new XChaCha
                // decrypt path can't accept. Reproduces the production crash.
                const nonce12 = b64url(crypto.getRandomValues(new Uint8Array(12)));
                const ct = b64url(crypto.getRandomValues(new Uint8Array(48)));
                await db.put('credentials',
                    { id: 'legacy-aesgcm', nonce: nonce12, ciphertext: ct, cachedAt: new Date().toISOString() });

                // A valid current-scheme row sealed by the live bridge.
                await db.putCredential('good-1', JSON.stringify({ hello: 'world' }));

                let threw = false;
                let list = [];
                try { list = await db.listCredentials(); }
                catch (e) { threw = true; }

                const legacyStillThere = !!(await db.get('credentials', 'legacy-aesgcm'));
                const goodStillThere = !!(await db.get('credentials', 'good-1'));

                // Cleanup so the shared browser context doesn't leak into other tests.
                await db.deleteCredential('good-1');
                await db.deleteCredential('legacy-aesgcm');

                return {
                    threw,
                    count: list.length,
                    containsGood: list.some(s => s.includes('world')),
                    legacyStillThere,
                    goodStillThere
                };
            }");

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("threw").GetBoolean(), Is.False,
                "listCredentials must not throw when a cached row can't be decrypted — that aborts the whole sync.");
            Assert.That(result.GetProperty("containsGood").GetBoolean(), Is.True,
                "the valid current-scheme credential must still be returned.");
            Assert.That(result.GetProperty("goodStillThere").GetBoolean(), Is.True,
                "a decryptable row must be preserved.");
            Assert.That(result.GetProperty("legacyStillThere").GetBoolean(), Is.False,
                "an undecryptable legacy row must be evicted so it stops poisoning every sync.");
        });
    }
}
