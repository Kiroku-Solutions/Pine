using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// The merge through §5's envelope, once, in the unit suite (post-audit plan item 3.1d) — for the reason
/// <see cref="VaultMergerRoundTripTests"/> gives: the gate measures this suite, and only a real vault exercises the
/// sealed export and apply. Every refusal is held in detail by the integration suite's <c>SealedMergeTests</c>.
/// </summary>
public sealed class SealedMergeRoundTripTests : IDisposable
{
    private static readonly CurrencyCode Usd = new("USD");
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SealedMergeRoundTripTests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    static SealedMergeRoundTripTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public SealedMergeRoundTripTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "left"));
        Directory.CreateDirectory(Path.Combine(_directory, "right"));
    }

    private string LeftPath => Path.Combine(_directory, "left", "pine.db");

    private string RightPath => Path.Combine(_directory, "right", "pine.db");

    public void Dispose()
    {
        foreach (var path in new[] { LeftPath, RightPath })
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Password = Convert.ToHexString(_key),
            }.ToString());
            SqliteConnection.ClearPool(connection);
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a green test over.
        }
    }

    private PineDbContext Left() => new(LeftPath, _key, timeProvider: null, deviceId: LeftDevice);

    private PineDbContext Right() => new(RightPath, _key, timeProvider: null, deviceId: RightDevice);

    [Fact]
    public async Task AChangeSet_TravelsSealed_AndAReplayIsRefusedWithoutAWrite()
    {
        var ct = CancellationToken.None;
        using var syncKey = SyncKey.Generate();
        using var wrongKey = SyncKey.Generate();

        Guid leftId;
        Guid rightId;
        await using (var l = Left())
        {
            await l.Database.MigrateAsync(ct);
            leftId = await VaultMerger.GetOrCreateVaultIdAsync(l, ct);
        }

        await using (var r = Right())
        {
            await r.Database.MigrateAsync(ct);
            rightId = await VaultMerger.GetOrCreateVaultIdAsync(r, ct);
        }

        var account = Account.Create("Current", AccountType.Bank, new Money(100m, Usd), TimeProvider.System);
        await using (var l = Left())
        {
            l.Accounts.Add(account);
            await l.SaveChangesAsync(ct);
        }

        byte[] envelope;
        await using (var l = Left())
        await using (var r = Right())
        {
            envelope = await VaultMerger.ExportSealedAsync(l, rightId, await VaultMerger.ReadCursorAsync(r, leftId, ct), syncKey, ct);
        }

        SyncEnvelope.ReadHeader(envelope).SourceVaultId.Should().Be(leftId);

        await using (var r = Right())
        {
            (await FluentActions.Awaiting(() => VaultMerger.ApplySealedAsync(r, envelope, wrongKey, ct))
                .Should().ThrowAsync<SyncEnvelopeRefusedException>()).Which.Reason.Should().Be(SyncEnvelopeRefusal.WrongKey);
        }

        await using (var r = Right())
        {
            var applied = await VaultMerger.ApplySealedAsync(r, envelope, syncKey, ct);
            applied.Inserted.Should().Be(1);
            applied.SourceVaultId.Should().Be(leftId);
        }

        await using (var r = Right())
        {
            (await r.Accounts.SingleAsync(a => a.Id == account.Id, ct)).Name.Should().Be("Current");
            (await FluentActions.Awaiting(() => VaultMerger.ApplySealedAsync(r, envelope, syncKey, ct))
                .Should().ThrowAsync<SyncEnvelopeRefusedException>()).Which.Reason.Should().Be(SyncEnvelopeRefusal.Replayed);
        }

        // And back: the right's own merge writes travel to the left under the same key; the left's rows compare equal.
        byte[] back;
        await using (var l = Left())
        await using (var r = Right())
        {
            back = await VaultMerger.ExportSealedAsync(r, leftId, await VaultMerger.ReadCursorAsync(l, rightId, ct), syncKey, ct);
        }

        await using (var l = Left())
        {
            var applied = await VaultMerger.ApplySealedAsync(l, back, syncKey, ct);
            applied.Inserted.Should().Be(0);
            applied.ConflictsRecorded.Should().Be(0);
        }
    }
}
