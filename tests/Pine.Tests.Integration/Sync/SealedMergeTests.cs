using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Budgets;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Pine.Infrastructure.Sync;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Sync;

/// <summary>
/// The merge through §5's envelope, between two real encrypted vault files (post-audit plan item 3.1d).
/// </summary>
/// <remarks>
/// Each vault keeps the user's sync key beside it, sealed under its own data key; the second vault is given the
/// first's key the way pairing will one day give it (<see cref="VaultSyncKeyStore.Save"/>). What crosses between them
/// is only the byte array <see cref="VaultMerger.ExportSealedAsync"/> returns. Every refusal is checked against a
/// snapshot of the whole target database — every table, every row — taken before it.
/// </remarks>
public class SealedMergeTests
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Yesterday = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>One byte in each header region: magic, version, source, target, since, time, key id, salt, nonce, tag.</summary>
    private static readonly int[] RegionProbes = [0, 8, 12, 30, 44, 60, 70, 90, 118, 130];

    private sealed class VaultKeyAccessor(TempVault vault) : IVaultDataKeyAccessor
    {
        public bool IsUnlocked => true;

        public byte[]? Key => vault.Key;

        public string? DbPath => vault.VaultDirectory;
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

    private static PineDbContext On(TempVault vault, Guid device) =>
        new(vault.DatabasePath, vault.Key, timeProvider: null, deviceId: device);

    private static async Task<Guid> VaultIdAsync(TempVault vault, Guid device)
    {
        await using var context = On(vault, device);
        return await VaultMerger.GetOrCreateVaultIdAsync(context, CancellationToken.None);
    }

    private static async Task WriteAsync(TempVault vault, Guid device, Func<PineDbContext, Task> write)
    {
        await using var context = On(vault, device);
        await write(context);
        await context.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>What <paramref name="from"/> has for <paramref name="to"/>, sealed, cut from <paramref name="to"/>'s cursor.</summary>
    private static async Task<byte[]> SealAsync(TempVault from, Guid fromDevice, TempVault to, Guid toDevice, SyncKey key, long? since = null)
    {
        var fromId = await VaultIdAsync(from, fromDevice);
        var toId = await VaultIdAsync(to, toDevice);
        await using var source = On(from, fromDevice);
        await using var target = On(to, toDevice);
        return await VaultMerger.ExportSealedAsync(source, toId, since ?? await VaultMerger.ReadCursorAsync(target, fromId, CancellationToken.None), key, CancellationToken.None);
    }

    private static async Task<SyncApplyResult> OpenAsync(TempVault vault, Guid device, byte[] envelope, SyncKey key)
    {
        await using var context = On(vault, device);
        return await VaultMerger.ApplySealedAsync(context, envelope, key, CancellationToken.None);
    }

    private static async Task<SyncEnvelopeRefusal> RefusedAsync(TempVault vault, Guid device, byte[] envelope, SyncKey key)
    {
        var refusal = await FluentActions.Awaiting(() => OpenAsync(vault, device, envelope, key)).Should().ThrowAsync<SyncEnvelopeRefusedException>();
        return refusal.Which.Reason;
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

    private static long Count(TempVault vault, string sql)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static Account NewAccount(string name) =>
        Account.Create(name, AccountType.Bank, new Money(100m, Usd), TimeProvider.System);

    private static Task SpendAsync(TempVault vault, Guid device, Guid accountId, decimal amount, string payee) =>
        WriteAsync(vault, device, async context =>
        {
            context.Transactions.Add(Transaction.Create(accountId, Yesterday, payee, new Money(-amount, Usd), TransactionType.Expense, TimeProvider.System));
            (await context.Accounts.SingleAsync(a => a.Id == accountId, CancellationToken.None)).Withdraw(new Money(amount, Usd), TimeProvider.System);
        });

    /// <summary>Two vaults holding one sync key: the left made it, the right was given it.</summary>
    private static async Task<(TempVault Left, TempVault Right, SyncKey Key)> PairedVaultsAsync()
    {
        var left = await MigratedVaultAsync();
        var right = await MigratedVaultAsync();
        var key = KeyStore(left).GetOrCreate();
        KeyStore(right).Save(key);
        return (left, right, key);
    }

    [Fact]
    public async Task TwoVaults_MergeBothWays_ThroughEnvelopesOnly_AndConverge()
    {
        var (left, right, pairingKey) = await PairedVaultsAsync();
        using var disposeLeft = left;
        using var disposeRight = right;
        pairingKey.Dispose();

        var account = NewAccount("Current");
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(account); return Task.CompletedTask; });
        await SpendAsync(left, LeftDevice, account.Id, 20m, "Confidential Grocer");
        var budget = Budget.Create("Food", new Money(300m, Usd), BudgetPeriod.Monthly);
        await WriteAsync(right, RightDevice, c => { c.Budgets.Add(budget); return Task.CompletedTask; });

        var leftFiles = Directory.GetFiles(left.VaultDirectory).Select(Path.GetFileName).Order().ToList();

        // Each side opens its own sealed copy of the key: the only thing the two vaults share.
        using var leftKey = KeyStore(left).Load()!;
        using var rightKey = KeyStore(right).Load()!;
        leftKey.KeyId.Should().Equal(rightKey.KeyId);

        var toRight = await SealAsync(left, LeftDevice, right, RightDevice, leftKey);
        Encoding.UTF8.GetString(toRight).Should().NotContain("Confidential").And.NotContain("Current").And.NotContain("Transactions");
        Directory.GetFiles(left.VaultDirectory).Select(Path.GetFileName).Order().Should().Equal(leftFiles, "an export writes nothing to disk; the envelope is returned");

        var intoRight = await OpenAsync(right, RightDevice, toRight, rightKey);
        intoRight.Inserted.Should().Be(2, "the account and the transaction");

        var toLeft = await SealAsync(right, RightDevice, left, LeftDevice, rightKey);
        var intoLeft = await OpenAsync(left, LeftDevice, toLeft, leftKey);
        intoLeft.Inserted.Should().Be(1, "the budget; the account and the transaction come back equal");
        intoLeft.ConflictsRecorded.Should().Be(0);

        foreach (var vault in new[] { left, right })
        {
            Count(vault, "SELECT COUNT(*) FROM Accounts;").Should().Be(1);
            Count(vault, "SELECT COUNT(*) FROM Transactions;").Should().Be(1);
            Count(vault, "SELECT COUNT(*) FROM Budgets;").Should().Be(1);
            Count(vault, "SELECT Balance_Amount FROM Accounts;").Should().Be(Count(left, "SELECT Balance_Amount FROM Accounts;"));
        }

        // The left's merge wrote the budget here, so the left's next envelope carries it back; it compares equal.
        var settle = await OpenAsync(right, RightDevice, await SealAsync(left, LeftDevice, right, RightDevice, leftKey), rightKey);
        settle.Inserted.Should().Be(0);
        settle.Updated.Should().Be(0);
        settle.ConflictsRecorded.Should().Be(0);

        // After that the left has nothing new, and an envelope that brings nothing is refused as a replay.
        var again = await SealAsync(left, LeftDevice, right, RightDevice, leftKey);
        var before = Snapshot(right);
        (await RefusedAsync(right, RightDevice, again, rightKey)).Should().Be(SyncEnvelopeRefusal.Replayed);
        Snapshot(right).Should().Be(before);
    }

    [Fact]
    public async Task EveryRefusedEnvelope_LeavesTheTargetVaultAsItWas()
    {
        var (left, right, key) = await PairedVaultsAsync();
        using var disposeLeft = left;
        using var disposeRight = right;
        using var disposeKey = key;
        using var third = await MigratedVaultAsync();
        using var otherKey = SyncKey.Generate();

        var account = NewAccount("Current");
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(account); return Task.CompletedTask; });
        await SpendAsync(left, LeftDevice, account.Id, 20m, "Grocer");
        _ = await VaultIdAsync(right, RightDevice);

        var envelope = await SealAsync(left, LeftDevice, right, RightDevice, key);
        var forThird = await SealAsync(left, LeftDevice, third, RightDevice, key);
        var underOtherKey = await SealAsync(left, LeftDevice, right, RightDevice, otherKey);
        var sibling = await SealAsync(left, LeftDevice, right, RightDevice, key);

        var before = Snapshot(right);

        async Task Refused(byte[] candidate, SyncKey with, params SyncEnvelopeRefusal[] reasons)
        {
            reasons.Should().Contain(await RefusedAsync(right, RightDevice, candidate, with));
            Snapshot(right).Should().Be(before, "a refused envelope writes nothing");
        }

        await Refused(envelope, otherKey, SyncEnvelopeRefusal.WrongKey);
        await Refused(underOtherKey, key, SyncEnvelopeRefusal.WrongKey);
        await Refused(forThird, key, SyncEnvelopeRefusal.WrongTarget);

        // A flipped bit in each region: magic, version, vault ids, cursor, time, key id, salt, nonce, tag, and across the ciphertext.
        foreach (var index in RegionProbes.Concat(Enumerable.Range(142, envelope.Length - 142).Where(i => i % 97 == 0)).Append(envelope.Length - 1))
        {
            var tampered = (byte[])envelope.Clone();
            tampered[index] ^= 0x01;
            await Refused(tampered, key, SyncEnvelopeRefusal.Malformed, SyncEnvelopeRefusal.UnsupportedVersion, SyncEnvelopeRefusal.WrongKey, SyncEnvelopeRefusal.NotAuthentic);
        }

        await Refused(envelope[..^1], key, SyncEnvelopeRefusal.NotAuthentic);
        await Refused(envelope[..100], key, SyncEnvelopeRefusal.Malformed);
        await Refused([.. envelope, 0x00], key, SyncEnvelopeRefusal.NotAuthentic);
        await Refused([.. envelope[..SyncEnvelope.HeaderLength], .. sibling[SyncEnvelope.HeaderLength..]], key, SyncEnvelopeRefusal.NotAuthentic);

        // The envelope itself still applies after all of that, once.
        (await OpenAsync(right, RightDevice, envelope, key)).Inserted.Should().Be(2);
        before = Snapshot(right);
        await Refused(envelope, key, SyncEnvelopeRefusal.Replayed);
        await Refused(sibling, key, SyncEnvelopeRefusal.Replayed);
    }

    [Fact]
    public async Task TheCursorRules_RefuseAGapAndAnOlderEnvelope_AndAcceptANewerOneThatOverlaps()
    {
        var (left, right, key) = await PairedVaultsAsync();
        using var disposeLeft = left;
        using var disposeRight = right;
        using var disposeKey = key;

        var first = NewAccount("First");
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(first); return Task.CompletedTask; });
        var e1 = await SealAsync(left, LeftDevice, right, RightDevice, key);
        var e1Clock = SyncEnvelope.ReadHeader(e1).SourceClock;

        var second = NewAccount("Second");
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(second); return Task.CompletedTask; });
        var e2 = await SealAsync(left, LeftDevice, right, RightDevice, key, since: e1Clock);

        var before = Snapshot(right);
        (await RefusedAsync(right, RightDevice, e2, key)).Should().Be(SyncEnvelopeRefusal.Gap, "the right has not had e1");
        Snapshot(right).Should().Be(before);

        (await OpenAsync(right, RightDevice, e1, key)).Inserted.Should().Be(1);
        (await OpenAsync(right, RightDevice, e2, key)).Inserted.Should().Be(1);

        before = Snapshot(right);
        (await RefusedAsync(right, RightDevice, e1, key)).Should().Be(SyncEnvelopeRefusal.Replayed, "older than what the right has applied since");
        Snapshot(right).Should().Be(before);

        var third = NewAccount("Third");
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(third); return Task.CompletedTask; });
        var overlapping = await SealAsync(left, LeftDevice, right, RightDevice, key, since: SyncChangeSet.Never);
        var result = await OpenAsync(right, RightDevice, overlapping, key);
        result.Inserted.Should().Be(1, "only the third account is new");
        result.Unchanged.Should().Be(2);
        result.ConflictsRecorded.Should().Be(0);
    }

    [Fact]
    public async Task AVaultWithNoIdYet_IsNoEnvelopesTarget_AndIsNotGivenOneByTheRefusal()
    {
        using var left = await MigratedVaultAsync();
        using var fresh = await MigratedVaultAsync();
        using var key = SyncKey.Generate();
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(NewAccount("Current")); return Task.CompletedTask; });

        await using (var source = On(left, LeftDevice))
        {
            var envelope = await VaultMerger.ExportSealedAsync(source, Guid.NewGuid(), SyncChangeSet.Never, key, CancellationToken.None);
            var before = Snapshot(fresh);

            (await RefusedAsync(fresh, RightDevice, envelope, key)).Should().Be(SyncEnvelopeRefusal.WrongTarget);

            Snapshot(fresh).Should().Be(before);
            Count(fresh, "SELECT COUNT(*) FROM SyncClock WHERE VaultId = '00000000-0000-0000-0000-000000000000';").Should().Be(1, "the refusal did not create a vault id");
        }
    }

    [Fact]
    public async Task AnAuthenticEnvelope_ThatTheMergeRefuses_WritesNothingEither()
    {
        var (left, right, key) = await PairedVaultsAsync();
        using var disposeLeft = left;
        using var disposeRight = right;
        using var disposeKey = key;
        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(NewAccount("Current")); return Task.CompletedTask; });
        var rightId = await VaultIdAsync(right, RightDevice);

        SyncChangeSet changeSet;
        await using (var source = On(left, LeftDevice))
        {
            changeSet = await VaultMerger.ExportAsync(source, rightId, SyncChangeSet.Never, CancellationToken.None);
        }

        var fromAnotherBuild = SyncEnvelope.Seal(changeSet with { SchemaVersion = "29990101000000_FromTheFuture" }, key, DateTimeOffset.UtcNow);
        var before = Snapshot(right);

        await FluentActions.Awaiting(() => OpenAsync(right, RightDevice, fromAnotherBuild, key)).Should().ThrowAsync<SyncMergeRefusedException>();
        Snapshot(right).Should().Be(before);

        await using (var busy = On(right, RightDevice))
        {
            busy.Accounts.Add(NewAccount("Unsaved"));
            await FluentActions.Awaiting(() => VaultMerger.ApplySealedAsync(busy, SyncEnvelope.Seal(changeSet, key, DateTimeOffset.UtcNow), key, CancellationToken.None))
                .Should().ThrowAsync<SyncMergeRefusedException>();
        }

        Snapshot(right).Should().Be(before);

        // AND A FAILED APPLY LEAVES THE ENVELOPE APPLICABLE (independent review of item 3.1d). The cursor moves inside
        // the transaction the refusal rolls back, so the good envelope is not turned into a replay by a bad one.
        (await OpenAsync(right, RightDevice, SyncEnvelope.Seal(changeSet, key, DateTimeOffset.UtcNow), key)).Inserted.Should().Be(1);
    }

    /// <summary>
    /// WHAT THE REPLAY REFUSAL IS, AND WHAT IT IS NOT (independent review of item 3.1d). It is a fact about this
    /// vault's database — the cursor in `SyncPeers` — and not a record of envelopes ever seen. A restore brings back
    /// an older `pine.db`, and with it an older cursor, so an envelope this vault had already applied applies again.
    /// That is the right answer rather than a hole: the restored vault genuinely no longer holds those rows, and the
    /// apply is the same idempotent merge. It is written down here because "a replay is refused" on its own would
    /// suggest a guarantee that survives a restore, and nothing in this item gives one.
    /// </summary>
    [Fact]
    public async Task ARestoreThatRewindsTheCursor_MakesAnAppliedEnvelopeApplyAgain()
    {
        var (left, right, key) = await PairedVaultsAsync();
        using var disposeLeft = left;
        using var disposeRight = right;
        using var disposeKey = key;

        await WriteAsync(left, LeftDevice, c => { c.Accounts.Add(NewAccount("Current")); return Task.CompletedTask; });
        var envelope = await SealAsync(left, LeftDevice, right, RightDevice, key);

        (await OpenAsync(right, RightDevice, envelope, key)).Inserted.Should().Be(1);
        (await RefusedAsync(right, RightDevice, envelope, key)).Should().Be(SyncEnvelopeRefusal.Replayed);

        // A restore of an older pine.db, reduced to the one row that decides this: the peer cursor, and the account
        // that came with the envelope, both gone again.
        right.ExecuteSql("DELETE FROM SyncPeers; DELETE FROM Accounts;");

        (await OpenAsync(right, RightDevice, envelope, key)).Inserted.Should().Be(1, "the restored vault does not have these rows, and the envelope brings them back");
        Count(right, "SELECT COUNT(*) FROM Accounts;").Should().Be(1);
        (await RefusedAsync(right, RightDevice, envelope, key)).Should().Be(SyncEnvelopeRefusal.Replayed, "and the refusal stands again from the cursor the apply wrote");
    }
}
