using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Core.Settings;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Security;
using Pine.Infrastructure.Sync;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Security;

/// <summary>
/// TD-86 on a REAL SQLCipher vault: the write-ahead rekey journal, with a simulated process death after each step.
/// "Death" is modelled by <see cref="CrashSwitch"/>: once tripped, every decorated component throws, so the service
/// cannot run any clean-up it would not get in a real crash. Recovery then runs on freshly built, undecorated parts.
/// </summary>
public sealed class VaultKeyRotationCrashTests : IDisposable
{
    private const string OldPassword = "correct horse battery staple";
    private const string NewPassword = "a different master password";
    private const string ProbeKey = "Rotation.Probe";
    private const string ProbeValue = "needle-in-the-haystack";

    private static readonly KdfParameters CheapKdf =
        new("argon2id", memoryKib: 8, iterations: 1, parallelism: 1, keyLengthBytes: 32, saltLengthBytes: 16);

    private readonly TempVault _temp = new();
    private readonly AesGcmVaultKeyService _keys = new(new Argon2idKeyDerivationService());
    private readonly FileVaultHeaderStore _headers = new();
    private readonly SqlCipherVaultRekeyer _rekeyer = new();
    private readonly FileVaultRekeyJournal _journal = new();
    private readonly VaultSealedFileResealer _resealer = new();

    private byte[] _oldKey = [];
    private byte[] _syncKeyId = [];
    private string Dir => _temp.VaultDirectory;

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    public enum CrashPoint
    {
        BeforeRekey,
        AfterJournalWrite,
        AfterRekey,
        AfterHeaderWrite,
        AfterReseal,
    }

    public static TheoryData<CrashPoint, bool> Crashes()
    {
        var data = new TheoryData<CrashPoint, bool>();
        foreach (var point in Enum.GetValues<CrashPoint>())
        {
            data.Add(point, false);
            data.Add(point, true);
        }

        return data;
    }

    private async Task SeedAsync()
    {
        var material = _keys.CreateVault(OldPassword, CheapKdf);
        _oldKey = material.DataKey;
        _headers.Write(Dir, material.Header);

        using (var context = new PineDbContext(Path.Combine(Dir, "pine.db"), _oldKey))
        {
            await context.Database.MigrateAsync(CancellationToken.None);
            context.Settings.Add(Setting.Create(ProbeKey, ProbeValue));
            await context.SaveChangesAsync(CancellationToken.None);
        }

        ClearPool(_oldKey);

        var cipher = new AesGcmVaultFileCipher(new Fixed(_oldKey, Dir));
        using (var syncKey = new VaultSyncKeyStore(cipher, Dir).GetOrCreate())
        {
            _syncKeyId = syncKey.KeyId.ToArray();
        }

        Directory.CreateDirectory(Path.Combine(Dir, "ai"));
        File.WriteAllBytes(Path.Combine(Dir, "ai", "categorizer.model"), cipher.Encrypt("model-bytes"u8, VaultFilePurposes.CategorizerModel));
    }

    // Scoped to this vault's own pool (same connection string PineDbContext builds). ClearAllPools() is
    // process-wide and forbidden by TestHarnessPoolingTests (TD-140).
    private void ClearPool(byte[] key)
    {
        using var forThisDatabase = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(Dir, "pine.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = Convert.ToHexString(key),
        }.ToString());
        SqliteConnection.ClearPool(forThisDatabase);
    }

    private VaultKeyRotationService Service(CrashSwitch? crash = null) =>
        crash is null
            ? new VaultKeyRotationService(_headers, _keys, _rekeyer, _journal, _resealer)
            : new VaultKeyRotationService(
                new CrashingHeaders(_headers, crash), _keys, new CrashingRekeyer(_rekeyer, crash),
                new CrashingJournal(_journal, crash), new CrashingResealer(_resealer, crash));

    private void AssertVaultFullyRotated(byte[] sessionKey)
    {
        sessionKey.Should().NotEqual(_oldKey, "the data key must really have changed");
        _temp.ReadSetting(Path.Combine(Dir, "pine.db"), sessionKey, ProbeKey).Should().Be(ProbeValue);
        var withOld = () => _temp.ReadSetting(Path.Combine(Dir, "pine.db"), _oldKey, ProbeKey);
        withOld.Should().Throw<SqliteException>("the old data key must no longer open the database");

        var header = _headers.Read(Dir);
        _keys.UnwrapDataKey(NewPassword, header).Should().Equal(sessionKey);
        var oldPasswordOnHeader = () => _keys.UnwrapDataKey(OldPassword, header);
        oldPasswordOnHeader.Should().Throw<VaultKeyUnwrapException>();

        var cipher = new AesGcmVaultFileCipher(new Fixed(sessionKey, Dir));
        using (var syncKey = new VaultSyncKeyStore(cipher, Dir).Load())
        {
            syncKey.Should().NotBeNull();
            syncKey!.KeyId.ToArray().Should().Equal(_syncKeyId, "sync.key must survive the rotation unchanged in content");
        }

        cipher.Decrypt(File.ReadAllBytes(Path.Combine(Dir, "ai", "categorizer.model")), VaultFilePurposes.CategorizerModel)
            .Should().Equal("model-bytes"u8.ToArray());

        _journal.Exists(Dir).Should().BeFalse("the journal, and with it the old key material, must be gone");
        Directory.GetFiles(Dir, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".pre-rekey", StringComparison.Ordinal) || f.EndsWith(".rekey-work", StringComparison.Ordinal) || f.EndsWith(".tmp", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ChangePassword_HappyPath_RotatesDatabaseHeaderAndSealedFiles()
    {
        await SeedAsync();

        var newKey = Service().ChangePassword(Dir, OldPassword, NewPassword);

        AssertVaultFullyRotated(newKey);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ChangesNothing()
    {
        await SeedAsync();
        var before = File.ReadAllBytes(Path.Combine(Dir, FileVaultHeaderStore.HeaderFileName));

        var act = () => Service().ChangePassword(Dir, "not the password", NewPassword);

        act.Should().Throw<VaultKeyUnwrapException>();
        File.ReadAllBytes(Path.Combine(Dir, FileVaultHeaderStore.HeaderFileName)).Should().Equal(before);
        _journal.Exists(Dir).Should().BeFalse();
        _temp.ReadSetting(Path.Combine(Dir, "pine.db"), _oldKey, ProbeKey).Should().Be(ProbeValue);
    }

    [Theory]
    [MemberData(nameof(Crashes))]
    public async Task CrashAtAnyStep_ThenUnlockWithEitherPassword_NeverLeavesTheVaultUnopenable(CrashPoint point, bool useNewPassword)
    {
        await SeedAsync();
        var crash = new CrashSwitch(point);

        var act = () => Service(crash).ChangePassword(Dir, OldPassword, NewPassword);
        act.Should().Throw<SimulatedCrashException>();
        crash.Tripped.Should().BeTrue();

        var restarted = Service();
        restarted.HasPendingRekey(Dir).Should().BeTrue("the journal is written before anything destructive and removed last");

        var committed = point is CrashPoint.AfterHeaderWrite or CrashPoint.AfterReseal;
        if (!useNewPassword && committed)
        {
            // The header already carries the new password: the old one is rightly refused, and nothing is damaged.
            var refused = () => restarted.RecoverAndUnwrap(Dir, OldPassword);
            refused.Should().Throw<VaultKeyUnwrapException>();
            _journal.Exists(Dir).Should().BeTrue();
        }

        var key = restarted.RecoverAndUnwrap(Dir, useNewPassword || committed ? NewPassword : OldPassword);

        AssertVaultFullyRotated(key);
    }

    [Fact]
    public async Task Recovery_WithAPasswordThatMatchesNeither_ChangesNothing_AndKeepsTheJournal()
    {
        await SeedAsync();
        var crash = new CrashSwitch(CrashPoint.AfterRekey);
        var act = () => Service(crash).ChangePassword(Dir, OldPassword, NewPassword);
        act.Should().Throw<SimulatedCrashException>();

        var wrong = () => Service().RecoverAndUnwrap(Dir, "something else entirely");

        wrong.Should().Throw<VaultKeyUnwrapException>();
        _journal.Exists(Dir).Should().BeTrue();
        var key = Service().RecoverAndUnwrap(Dir, NewPassword);
        AssertVaultFullyRotated(key);
    }

    [Fact]
    public async Task Recovery_IsIdempotent_WhenItIsItselfInterrupted()
    {
        await SeedAsync();
        var first = new CrashSwitch(CrashPoint.AfterRekey);
        var act = () => Service(first).ChangePassword(Dir, OldPassword, NewPassword);
        act.Should().Throw<SimulatedCrashException>();

        // Recovery dies after rewriting the header ...
        var second = new CrashSwitch(CrashPoint.AfterHeaderWrite);
        var recover = () => Service(second).RecoverAndUnwrap(Dir, OldPassword);
        recover.Should().Throw<SimulatedCrashException>();

        // ... and a third start (now only the NEW password works on the header) still finishes.
        var key = Service().RecoverAndUnwrap(Dir, NewPassword);
        AssertVaultFullyRotated(key);
    }

    [Fact]
    public async Task UnreadableJournal_WithDatabaseUnderTheHeaderKey_IsDiscardedAndTheVaultOpens()
    {
        await SeedAsync();
        File.WriteAllText(Path.Combine(Dir, FileVaultRekeyJournal.FileName), "{ not json");

        var key = Service().RecoverAndUnwrap(Dir, OldPassword);

        key.Should().Equal(_oldKey);
        _journal.Exists(Dir).Should().BeFalse();
        _temp.ReadSetting(Path.Combine(Dir, "pine.db"), _oldKey, ProbeKey).Should().Be(ProbeValue);
    }

    [Fact]
    public async Task JournalOnDisk_ContainsNoPlaintextKey()
    {
        await SeedAsync();
        var crash = new CrashSwitch(CrashPoint.AfterJournalWrite);
        var act = () => Service(crash).ChangePassword(Dir, OldPassword, NewPassword);
        act.Should().Throw<SimulatedCrashException>();

        var text = File.ReadAllText(Path.Combine(Dir, FileVaultRekeyJournal.FileName));

        text.Should().NotContain(Convert.ToBase64String(_oldKey));
        text.Should().NotContain(Convert.ToHexString(_oldKey));
    }

    // ---- crash simulation ----

    private sealed class SimulatedCrashException : Exception
    {
    }

    private sealed class CrashSwitch(CrashPoint point)
    {
        public bool Tripped { get; private set; }

        public void Before(CrashPoint at)
        {
            if (Tripped) throw new SimulatedCrashException();
            if (at == point) { Tripped = true; throw new SimulatedCrashException(); }
        }

        public void After(CrashPoint at)
        {
            if (at == point) { Tripped = true; throw new SimulatedCrashException(); }
        }

        public void Guard()
        {
            if (Tripped) throw new SimulatedCrashException();
        }
    }

    private sealed class CrashingJournal(IVaultRekeyJournal inner, CrashSwitch crash) : IVaultRekeyJournal
    {
        public bool Exists(string d) { crash.Guard(); return inner.Exists(d); }
        public VaultRekeyJournalEntry? TryRead(string d) { crash.Guard(); return inner.TryRead(d); }
        public void Delete(string d) { crash.Guard(); inner.Delete(d); }
        public void Write(string d, VaultRekeyJournalEntry e) { crash.Guard(); inner.Write(d, e); crash.After(CrashPoint.AfterJournalWrite); }
    }

    private sealed class CrashingRekeyer(IVaultDatabaseRekeyer inner, CrashSwitch crash) : IVaultDatabaseRekeyer
    {
        public bool CanOpen(string d, byte[] k) { crash.Guard(); return inner.CanOpen(d, k); }
        public void Rekey(string d, byte[] c, byte[] n) { crash.Before(CrashPoint.BeforeRekey); inner.Rekey(d, c, n); crash.After(CrashPoint.AfterRekey); }
    }

    private sealed class CrashingHeaders(IVaultHeaderStore inner, CrashSwitch crash) : IVaultHeaderStore
    {
        public bool Exists(string d) { crash.Guard(); return inner.Exists(d); }
        public VaultHeader Read(string d) { crash.Guard(); return inner.Read(d); }
        public void Write(string d, VaultHeader h) { crash.Guard(); inner.Write(d, h); crash.After(CrashPoint.AfterHeaderWrite); }
    }

    private sealed class CrashingResealer(IVaultSealedFileResealer inner, CrashSwitch crash) : IVaultSealedFileResealer
    {
        public void Reseal(string d, byte[] o, byte[] n) { crash.Guard(); inner.Reseal(d, o, n); crash.After(CrashPoint.AfterReseal); }
    }

    private sealed class Fixed(byte[] key, string dir) : IVaultDataKeyAccessor
    {
        public bool IsUnlocked => true;
        public byte[]? Key => key;
        public string? DbPath => dir;
    }
}
