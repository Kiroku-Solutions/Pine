using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Core.Sync;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Pine.Infrastructure.Sync;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Sync;

/// <summary>
/// Pairing between two real encrypted vault files, and the envelope that can cross between them afterwards
/// (post-audit plan item 3.1e; TD-154).
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE TEST ITEM 3.1d COULD NOT WRITE. Its <c>SealedMergeTests</c> put the first vault's key into the second
/// with <c>VaultSyncKeyStore.Save</c>, standing in for pairing. Here the second vault is given the key the way a
/// user gives it: an offer file written by one vault and a code carried by hand to the other. The end-to-end
/// assertion is the one that matters — after pairing, and only after it, an envelope sealed by one opens on the
/// other.
/// </para>
/// <para>
/// STILL NOTHING ON A NETWORK. Both vaults are files in a temporary directory; what crosses is a byte array and a
/// string a person would have read aloud.
/// </para>
/// </remarks>
public class SyncPairingTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class VaultKeyAccessor(TempVault vault) : IVaultDataKeyAccessor
    {
        public bool IsUnlocked => true;

        public byte[]? Key => vault.Key;

        public string? DbPath => vault.VaultDirectory;
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static PineDbContext On(TempVault vault, Guid device) =>
        new(vault.DatabasePath, vault.Key, timeProvider: null, deviceId: device);

    private static VaultSyncPairing Pairing(PineDbContext context, TempVault vault, TimeProvider clock)
    {
        var accessor = new VaultKeyAccessor(vault);
        return new VaultSyncPairing(context, accessor, new AesGcmVaultFileCipher(accessor), clock);
    }

    private static VaultSyncKeyStore KeyStore(TempVault vault) =>
        new(new AesGcmVaultFileCipher(new VaultKeyAccessor(vault)), vault.VaultDirectory);

    private static async Task<TempVault> MigratedVaultAsync()
    {
        var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return vault;
    }

    private static async Task<Guid> VaultIdAsync(TempVault vault, Guid device)
    {
        await using var context = On(vault, device);
        return await VaultMerger.GetOrCreateVaultIdAsync(context, CancellationToken.None);
    }

    /// <summary>Every row of every table, in a stable order: what "the vault is untouched" is compared against.</summary>
    private static string Snapshot(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        var tables = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            using var reader = list.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var text = new StringBuilder();
        foreach (var table in tables)
        {
            using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{table}\" ORDER BY rowid;";
            using var reader = select.ExecuteReader();
            text.Append('[').Append(table).Append(']').AppendLine();
            while (reader.Read())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    text.Append(value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, CultureInfo.InvariantCulture)).Append('|');
                }

                text.AppendLine();
            }
        }

        return text.ToString();
    }

    private static async Task<SyncPairingRefusal> RefusedAsync(Func<Task> act) =>
        (await FluentActions.Awaiting(act).Should().ThrowAsync<SyncPairingRefusedException>()).Which.Refusal;

    [Fact]
    public async Task TwoVaultsPair_AndThenAnEnvelopeSealedByOneOpensOnTheOther()
    {
        var ct = CancellationToken.None;
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var clock = new FakeClock(Noon);
        var offerPath = Path.Combine(right.VaultDirectory, "carried-by-hand.offer");

        var leftId = await VaultIdAsync(left, LeftDevice);
        var rightId = await VaultIdAsync(right, RightDevice);

        // Before pairing there is nothing the right vault could open the left's envelope with.
        KeyStore(right).Load().Should().BeNull();

        SyncPairingOfferIssued issued;
        await using (var context = On(left, LeftDevice))
        {
            issued = await Pairing(context, left, clock).CreateOfferAsync(offerPath, SyncPairingLimits.DefaultLifetime, ct);
        }

        SyncPairingCompleted completed;
        await using (var context = On(right, RightDevice))
        {
            completed = await Pairing(context, right, clock).AcceptOfferAsync(offerPath, issued.Code, ct);
        }

        completed.Fingerprint.Should().Be(issued.Fingerprint, "a silent mismatch is the failure this fingerprint exists to show");
        File.Exists(offerPath).Should().BeFalse();

        // End to end: a real change, sealed on the left, opened and applied on the right, under the paired key.
        await using (var context = On(left, LeftDevice))
        {
            context.Accounts.Add(Account.Create("Current", AccountType.Bank, new Money(120m, Usd), TimeProvider.System));
            await context.SaveChangesAsync(ct);
        }

        byte[] envelope;
        using (var leftKey = KeyStore(left).Load())
        await using (var source = On(left, LeftDevice))
        await using (var target = On(right, RightDevice))
        {
            envelope = await VaultMerger.ExportSealedAsync(
                source, rightId, await VaultMerger.ReadCursorAsync(target, leftId, ct), leftKey!, ct);
        }

        using (var rightKey = KeyStore(right).Load())
        await using (var context = On(right, RightDevice))
        {
            var applied = await VaultMerger.ApplySealedAsync(context, envelope, rightKey!, ct);
            applied.Inserted.Should().Be(1);
            applied.SourceVaultId.Should().Be(leftId);
        }

        await using (var context = On(right, RightDevice))
        {
            (await context.Accounts.SingleAsync(ct)).Name.Should().Be("Current");
        }
    }

    [Fact]
    public async Task EveryRefusedPairing_LeavesBothVaultsExactlyAsTheyWere()
    {
        var ct = CancellationToken.None;
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var clock = new FakeClock(Noon);
        var offerPath = Path.Combine(right.VaultDirectory, "carried-by-hand.offer");

        await VaultIdAsync(left, LeftDevice);
        await using (var context = On(left, LeftDevice))
        {
            context.Accounts.Add(Account.Create("Current", AccountType.Bank, new Money(120m, Usd), TimeProvider.System));
            await context.SaveChangesAsync(ct);
        }

        SyncPairingOfferIssued issued;
        await using (var context = On(left, LeftDevice))
        {
            issued = await Pairing(context, left, clock).CreateOfferAsync(offerPath, TimeSpan.FromMinutes(5), ct);
        }

        var leftBefore = Snapshot(left);
        var rightBefore = Snapshot(right);

        var garbage = Path.Combine(right.VaultDirectory, "garbage.offer");
        File.WriteAllBytes(garbage, RandomNumberGenerator.GetBytes(SyncPairingOffer.Length));

        var tampered = Path.Combine(right.VaultDirectory, "tampered.offer");
        var bytes = File.ReadAllBytes(offerPath);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(tampered, bytes);

        var refusals = new List<SyncPairingRefusal>();
        await using (var context = On(right, RightDevice))
        {
            var pairing = Pairing(context, right, clock);
            refusals.Add(await RefusedAsync(() => pairing.AcceptOfferAsync(Path.Combine(right.VaultDirectory, "missing.offer"), issued.Code, ct)));
            refusals.Add(await RefusedAsync(() => pairing.AcceptOfferAsync(garbage, issued.Code, ct)));
            refusals.Add(await RefusedAsync(() => pairing.AcceptOfferAsync(tampered, issued.Code, ct)));
            refusals.Add(await RefusedAsync(() => pairing.AcceptOfferAsync(offerPath, SyncPairingCode.Generate(), ct)));
            refusals.Add(await RefusedAsync(() => pairing.AcceptOfferAsync(offerPath, "not a code at all", ct)));
        }

        // Its own offer, on the vault that made it.
        await using (var context = On(left, LeftDevice))
        {
            refusals.Add(await RefusedAsync(() => Pairing(context, left, clock).AcceptOfferAsync(offerPath, issued.Code, ct)));
        }

        // And after the offer has expired.
        clock.Now = Noon.AddMinutes(6);
        await using (var context = On(right, RightDevice))
        {
            refusals.Add(await RefusedAsync(() => Pairing(context, right, clock).AcceptOfferAsync(offerPath, issued.Code, ct)));
        }

        refusals.Should().Equal(
            SyncPairingRefusal.OfferNotFound,
            SyncPairingRefusal.Malformed,
            SyncPairingRefusal.NotAuthentic,
            SyncPairingRefusal.NotAuthentic,
            SyncPairingRefusal.CodeFormat,
            SyncPairingRefusal.SameVault,
            SyncPairingRefusal.Expired);

        Snapshot(left).Should().Be(leftBefore, "a refused pairing writes nothing to the vault that offered");
        Snapshot(right).Should().Be(rightBefore, "a refused pairing writes nothing to the vault that tried");
        KeyStore(right).Load().Should().BeNull("no refusal may leave a sync key behind");
        File.Exists(offerPath).Should().BeTrue("a refused pairing does not consume the offer either");
    }

    [Fact]
    public async Task AVaultThatAlreadyHoldsADifferentKey_IsRefused_AndKeepsIt()
    {
        var ct = CancellationToken.None;
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var clock = new FakeClock(Noon);
        var offerPath = Path.Combine(right.VaultDirectory, "carried-by-hand.offer");

        await VaultIdAsync(left, LeftDevice);
        await VaultIdAsync(right, RightDevice);

        SyncPairingOfferIssued issued;
        await using (var context = On(left, LeftDevice))
        {
            issued = await Pairing(context, left, clock).CreateOfferAsync(offerPath, SyncPairingLimits.DefaultLifetime, ct);
        }

        using var theirs = SyncKey.Generate();
        KeyStore(right).Save(theirs);
        var sealedKeyBefore = File.ReadAllBytes(Path.Combine(right.VaultDirectory, VaultSyncKeyStore.FileName));
        var rightBefore = Snapshot(right);

        await using (var context = On(right, RightDevice))
        {
            (await RefusedAsync(() => Pairing(context, right, clock).AcceptOfferAsync(offerPath, issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.DifferentKey);
        }

        File.ReadAllBytes(Path.Combine(right.VaultDirectory, VaultSyncKeyStore.FileName)).Should().Equal(sealedKeyBefore);
        using var still = KeyStore(right).Load();
        still!.HasId(theirs.KeyId).Should().BeTrue();
        Snapshot(right).Should().Be(rightBefore);
    }

    [Fact]
    public async Task AnOfferUsedOnce_IsRefusedAsAReplayIfItComesBack()
    {
        var ct = CancellationToken.None;
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        var clock = new FakeClock(Noon);
        var offerPath = Path.Combine(right.VaultDirectory, "carried-by-hand.offer");
        var copy = Path.Combine(right.VaultDirectory, "kept-a-copy.offer");

        await VaultIdAsync(left, LeftDevice);
        await VaultIdAsync(right, RightDevice);

        SyncPairingOfferIssued issued;
        await using (var context = On(left, LeftDevice))
        {
            issued = await Pairing(context, left, clock).CreateOfferAsync(offerPath, SyncPairingLimits.DefaultLifetime, ct);
        }

        File.Copy(offerPath, copy);

        await using (var context = On(right, RightDevice))
        {
            await Pairing(context, right, clock).AcceptOfferAsync(offerPath, issued.Code, ct);
        }

        var after = Snapshot(right);
        var sealedKey = File.ReadAllBytes(Path.Combine(right.VaultDirectory, VaultSyncKeyStore.FileName));

        await using (var context = On(right, RightDevice))
        {
            (await RefusedAsync(() => Pairing(context, right, clock).AcceptOfferAsync(copy, issued.Code, ct)))
                .Should().Be(SyncPairingRefusal.Replayed);
        }

        Snapshot(right).Should().Be(after);
        File.ReadAllBytes(Path.Combine(right.VaultDirectory, VaultSyncKeyStore.FileName)).Should().Equal(sealedKey,
            "not even a re-seal of the same key: a replay writes nothing");
    }
}
