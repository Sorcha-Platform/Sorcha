// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using FluentAssertions;
using NBitcoin;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Xunit;

namespace Sorcha.Wallet.Core.Tests;

/// <summary>
/// Numerical regression test for the federation deadlock (#461 phase 4):
/// the genesis-ceremony's docket-signing pubkey MUST match the pubkey that
/// wallet-service's runtime sign-with-derivationPath produces for the same
/// mnemonic. If they diverge, the validator can never enrol on the system
/// register's roster and docket 0 never seals.
/// </summary>
/// <remarks>
/// Skipped silently if genesis-validator-key.json isn't on disk (CI / fresh checkout).
/// Run locally after a re-ceremony:
///   dotnet exec tests/Sorcha.Wallet.Core.Tests/bin/Debug/net10.0/Sorcha.Wallet.Core.Tests.dll \
///     --filter-method "*GenesisDerivationProof*"
/// </remarks>
public class GenesisDerivationProofTests
{
    private static string? FindGenesisKeyFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "genesis-validator-key.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static (string Mnemonic, string RosterPubKey)? Load()
    {
        var keyPath = FindGenesisKeyFile();
        if (keyPath is null) return null;

        // Resolve genesis file relative to the key file (repo root sibling)
        var repoRoot = new FileInfo(keyPath).Directory!.FullName;
        var genesisPath = Path.Combine(repoRoot,
            "src", "Common", "Sorcha.Register.Models", "Resources", "system-register-genesis.json");
        if (!File.Exists(genesisPath)) return null;

        using var keyDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(keyPath));
        using var genDoc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(genesisPath));

        var mnemonic = keyDoc.RootElement.GetProperty("mnemonic").GetString()!;
        var payloadB64 = genDoc.RootElement.GetProperty("genesisTransaction").GetProperty("payload").GetString()!;
        var payload = Convert.FromBase64String(payloadB64);
        using var ctrlDoc = System.Text.Json.JsonDocument.Parse(payload);
        var rosterPubKey = ctrlDoc.RootElement
            .GetProperty("validators").GetProperty("validators")[0].GetProperty("publicKey").GetString()!;

        return (mnemonic, rosterPubKey);
    }

    [Fact]
    public async Task WalletServiceRuntimeChain_MatchesGenesisRosterPubKey()
    {
        var loaded = Load();
        if (loaded is null) return; // genesis files not on disk — skip

        var (mnemonicWords, expectedRosterPubKey) = loaded.Value;
        var crypto = new CryptoModule();

        // Post-#471 chain — runtime + CLI both derive purpose keys directly off
        // the master ExtKey with a single HMAC inside ExtKey.CreateFromSeed.
        //
        // Runtime path (WalletManager.SignTransactionAsync, post-#471):
        //   1. Decrypt wallet.EncryptedMasterKeyBlob → 64-byte BIP39 PBKDF2 seed
        //   2. ExtKey.CreateFromSeed(seed) → master ExtKey
        //   3. masterExtKey.Derive("m/44'/0'/0'/0/102") → docket-signing key
        //
        // CLI ceremony path (SystemRegisterCommands.ExecuteCreateAsync) does
        // the same derivation off mnemonic.DeriveExtKey() (which is equivalent
        // to ExtKey.CreateFromSeed(mnemonic.DeriveSeed())). The genesis file
        // pubkey we load below was minted by the CLI; this test asserts the
        // runtime would arrive at the same pubkey from the stored mnemonic.
        var mnemonic = new Mnemonic(mnemonicWords);
        var masterExtKey = mnemonic.DeriveExtKey();
        var docketSigningSeed = masterExtKey.Derive(new KeyPath("m/44'/0'/0'/0/102")).PrivateKey.ToBytes();
        var docketKey = await crypto.GenerateKeySetAsync(WalletNetworks.ED25519, seed: docketSigningSeed);
        docketKey.IsSuccess.Should().BeTrue();

        var pubBase64 = Convert.ToBase64String(docketKey.Value!.PublicKey.Key!);
        Console.WriteLine($"Wallet-runtime chain pubkey: {pubBase64}");
        Console.WriteLine($"Genesis roster pubkey:       {expectedRosterPubKey}");

        pubBase64.Should().Be(expectedRosterPubKey,
            "the genesis ceremony must produce a roster matching the wallet-service " +
            "runtime derivation, otherwise the validator can never enrol pre-seal.");
    }
}
