using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pine.Application.Security;
using Pine.Core.Settings;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>TD-86: rekeying a real SQLCipher vault. Old key rejected, new key opens, data intact, failures change nothing.</summary>
public class SqlCipherVaultRekeyerTests : IDisposable
{
    private const string ProbeKey = "Rekey.Probe";
    private const string ProbeValue = "needle-in-the-haystack";

    private readonly TempVault _vault = new();
    private readonly SqlCipherVaultRekeyer _rekeyer = new();

    public void Dispose()
    {
        _vault.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedAsync()
    {
        var context = _vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        context.Settings.Add(Setting.Create(ProbeKey, ProbeValue));
        await context.SaveChangesAsync(CancellationToken.None);
        _vault.CloseContexts();
    }

    [Fact]
    public async Task Rekey_ShouldOpenWithNewKey_RejectOldKey_AndKeepData()
    {
        await SeedAsync();
        var newKey = RandomNumberGenerator.GetBytes(32);

        _rekeyer.Rekey(_vault.VaultDirectory, _vault.Key, newKey);

        _vault.ReadSetting(_vault.DatabasePath, newKey, ProbeKey).Should().Be(ProbeValue);
        var oldKeyOpen = () => _vault.ReadSetting(_vault.DatabasePath, _vault.Key, ProbeKey);
        oldKeyOpen.Should().Throw<SqliteException>("the old data key must no longer open the vault");
        Directory.GetFiles(_vault.VaultDirectory).Select(Path.GetFileName).Where(n => n!.EndsWith(".rekey-work", StringComparison.Ordinal) || n.EndsWith(".pre-rekey", StringComparison.Ordinal)).Should().BeEmpty("no old-key or work copy may linger");
    }

    [Fact]
    public async Task Rekey_WithWrongCurrentKey_ShouldFailClosed_AndLeaveVaultOpenableWithOriginalKey()
    {
        await SeedAsync();
        var wrongCurrent = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);

        var act = () => _rekeyer.Rekey(_vault.VaultDirectory, wrongCurrent, newKey);

        act.Should().Throw<VaultRekeyException>();
        _vault.ReadSetting(_vault.DatabasePath, _vault.Key, ProbeKey).Should().Be(ProbeValue);
        var newKeyOpen = () => _vault.ReadSetting(_vault.DatabasePath, newKey, ProbeKey);
        newKeyOpen.Should().Throw<SqliteException>();
        Directory.GetFiles(_vault.VaultDirectory).Select(Path.GetFileName).Where(n => n!.EndsWith(".rekey-work", StringComparison.Ordinal) || n.EndsWith(".pre-rekey", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public async Task Rekey_ShouldRefuseIdenticalOrMalformedKeys()
    {
        await SeedAsync();

        var same = () => _rekeyer.Rekey(_vault.VaultDirectory, _vault.Key, (byte[])_vault.Key.Clone());
        var shortKey = () => _rekeyer.Rekey(_vault.VaultDirectory, _vault.Key, new byte[16]);

        same.Should().Throw<ArgumentException>();
        shortKey.Should().Throw<ArgumentException>();
        _vault.ReadSetting(_vault.DatabasePath, _vault.Key, ProbeKey).Should().Be(ProbeValue);
    }

    [Fact]
    public void Rekey_WithNoDatabase_ShouldThrowVaultRekeyException()
    {
        var act = () => _rekeyer.Rekey(_vault.VaultDirectory, _vault.Key, RandomNumberGenerator.GetBytes(32));

        act.Should().Throw<VaultRekeyException>();
    }
}
