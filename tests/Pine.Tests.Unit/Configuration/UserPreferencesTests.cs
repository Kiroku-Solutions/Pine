using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pine.Application.Settings;
using Pine.Application.Transactions.Queries;
using Pine.Core.Ai;
using Pine.Core.Configuration;
using Pine.Core.Settings;
using Xunit;

namespace Pine.Tests.Unit.Configuration;

/// <summary>
/// TD-119: auto-lock timeout, confidence gate and page size are encrypted in-vault Settings; the
/// old configuration value is only the read-fallback.
/// </summary>
public class UserPreferencesTests
{
    private static ISettingRepository SettingsWith(params (string Key, string Value)[] rows)
    {
        var repository = Substitute.For<ISettingRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Setting?>(null));

        foreach (var (key, value) in rows)
        {
            repository.GetAsync(key, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<Setting?>(Setting.Create(key, value)));
        }

        return repository;
    }

    private static UserPreferences With(ISettingRepository settings) => new(
        settings,
        Options.Create(new PineSecurityOptions { AutoLockIdleTimeout = TimeSpan.FromMinutes(20) }),
        Options.Create(new PineAiOptions { CategorisationConfidenceThreshold = 0.6 }),
        Options.Create(new PineUiOptions { TransactionsPageSize = 40 }));

    [Fact]
    public async Task Get_ShouldFallBackToTheOldConfigValues_WhenNoSettingExists()
    {
        var snapshot = await With(SettingsWith()).GetAsync(CancellationToken.None);

        snapshot.AutoLockIdleTimeout.Should().Be(TimeSpan.FromMinutes(20));
        snapshot.CategorisationConfidenceThreshold.Should().Be(0.6);
        snapshot.TransactionsPageSize.Should().Be(40);
    }

    [Fact]
    public async Task Get_ShouldPreferTheVaultSetting_OverConfiguration()
    {
        var settings = SettingsWith(
            (UserPreferenceKeys.AutoLockIdleMinutes, "5"),
            (UserPreferenceKeys.CategorisationConfidenceThreshold, "0.85"),
            (UserPreferenceKeys.TransactionsPageSize, "75"));

        var snapshot = await With(settings).GetAsync(CancellationToken.None);

        snapshot.AutoLockIdleTimeout.Should().Be(TimeSpan.FromMinutes(5));
        snapshot.CategorisationConfidenceThreshold.Should().Be(0.85);
        snapshot.TransactionsPageSize.Should().Be(75);
    }

    [Fact]
    public async Task Get_ShouldIgnoreAnInvalidSetting_AndKeepTheFallback()
    {
        var settings = SettingsWith(
            (UserPreferenceKeys.AutoLockIdleMinutes, "0"),
            (UserPreferenceKeys.CategorisationConfidenceThreshold, "7"),
            (UserPreferenceKeys.TransactionsPageSize, "lots"));

        var snapshot = await With(settings).GetAsync(CancellationToken.None);

        snapshot.AutoLockIdleTimeout.Should().Be(TimeSpan.FromMinutes(20));
        snapshot.CategorisationConfidenceThreshold.Should().Be(0.6);
        snapshot.TransactionsPageSize.Should().Be(40);
    }

    /// <summary>The consumer honours the Setting, not the config: config says 0.9 (reject), Setting says 0.4 (accept).</summary>
    [Fact]
    public async Task TheSuggestionPath_ShouldUseTheVaultSettingOverTheConfiguredGate()
    {
        var ai = Substitute.For<IAIProvider>();
        ai.CategorizeTransactionAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CategorisationResult("Groceries", 0.5)));

        var aiOptions = Options.Create(new PineAiOptions { CategorisationConfidenceThreshold = 0.9 });
        var preferences = new UserPreferences(
            SettingsWith((UserPreferenceKeys.CategorisationConfidenceThreshold, "0.4")), null, aiOptions, null);

        var handler = new CategorizeTransactionQueryHandler(ai, aiOptions, preferences);

        (await handler.Handle(new CategorizeTransactionQuery("SUPERMARKET", 10m), CancellationToken.None))
            .Should().Be("Groceries");
    }
}
