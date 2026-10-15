using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Common.ValueObjects;
using Pine.Core.ExchangeRates;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Sync;
using Pine.Tests.Integration.Data;
using Xunit;

namespace Pine.Tests.Integration.Sync;

/// <summary>Exchange rates travel between vaults like the other tables (owner decision, 2026-10-14).</summary>
public class ExchangeRateSyncTests
{
    private static readonly Guid LeftDevice = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RightDevice = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTime Day = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<TempVault> MigratedVaultAsync()
    {
        var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);
        return vault;
    }

    private static PineDbContext On(TempVault vault, Guid device) =>
        new(vault.DatabasePath, vault.Key, timeProvider: null, deviceId: device);

    private static async Task AddRateAsync(TempVault vault, Guid device, decimal rate)
    {
        await using var context = On(vault, device);
        context.ExchangeRates.Add(ExchangeRate.Create(new CurrencyCode("USD"), new CurrencyCode("EUR"), rate, Day));
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task MergeAsync(TempVault left, TempVault right)
    {
        await using var l = On(left, LeftDevice);
        await using var r = On(right, RightDevice);
        await VaultMerger.MergeAsync(l, r, CancellationToken.None);
    }

    private static long Count(TempVault vault)
    {
        using var connection = vault.OpenRaw(vault.DatabasePath, vault.Key);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT COUNT(*) FROM ""ExchangeRates"";";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static async Task<decimal[]> RatesAsync(TempVault vault)
    {
        await using var context = vault.CreateContext();
        var rows = await context.ExchangeRates.AsNoTracking().ToListAsync();
        return rows.ConvertAll(r => r.Rate).ToArray();
    }

    [Fact]
    public async Task ARateEnteredOnOneDevice_ReachesTheOther_ExactlyOnce()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await AddRateAsync(left, LeftDevice, 0.92m);

        await MergeAsync(left, right);
        await MergeAsync(left, right);

        Count(right).Should().Be(1);
        (await RatesAsync(right)).Should().Equal(0.92m);
    }

    [Fact]
    public async Task TheSamePairAndDayEnteredOnBothDevices_IsOneRow_NotTwo()
    {
        using var left = await MigratedVaultAsync();
        using var right = await MigratedVaultAsync();
        await AddRateAsync(left, LeftDevice, 0.92m);
        await AddRateAsync(right, RightDevice, 0.92m);

        await MergeAsync(left, right);

        Count(left).Should().Be(1);
        Count(right).Should().Be(1);
    }
}
