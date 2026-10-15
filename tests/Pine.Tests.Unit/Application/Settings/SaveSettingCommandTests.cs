using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Common;
using Pine.Application.Settings.Commands;
using Pine.Core.Configuration;
using Pine.Core.Settings;
using Xunit;

namespace Pine.Tests.Unit.Application.Settings;

/// <summary>
/// SaveSettingCommand exists because repositories no longer flush (TD-01). Every UI write path that
/// used to call ISettingRepository.SaveAsync directly now goes through this command, so it must be
/// marked transactional or the setting is staged and thrown away.
/// </summary>
public class SaveSettingCommandTests
{
    [Fact]
    public async Task Handle_ShouldStageTheSetting_OnTheRepository()
    {
        // Arrange
        var repository = Substitute.For<ISettingRepository>();
        var handler = new SaveSettingCommandHandler(repository, NSubstitute.Substitute.For<Pine.Core.Budgets.IBudgetRepository>(), NSubstitute.Substitute.For<Pine.Core.ExchangeRates.IExchangeRateRepository>(), NSubstitute.Substitute.For<Pine.Core.Accounts.IAccountRepository>(), System.TimeProvider.System);
        var command = new SaveSettingCommand("Appearance.Theme", "1");

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        await repository.Received(1).SaveAsync(
            Arg.Is<Setting>(s => s.Key == "Appearance.Theme" && s.Value == "1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPassTheCancellationTokenThrough()
    {
        // Arrange
        var repository = Substitute.For<ISettingRepository>();
        var handler = new SaveSettingCommandHandler(repository, NSubstitute.Substitute.For<Pine.Core.Budgets.IBudgetRepository>(), NSubstitute.Substitute.For<Pine.Core.ExchangeRates.IExchangeRateRepository>(), NSubstitute.Substitute.For<Pine.Core.Accounts.IAccountRepository>(), System.TimeProvider.System);
        using var cts = new CancellationTokenSource();

        // Act
        await handler.Handle(new SaveSettingCommand("System.Telemetry", "False"), cts.Token);

        // Assert
        await repository.Received(1).SaveAsync(Arg.Any<Setting>(), cts.Token);
    }

    [Fact]
    public void Command_MustBeTransactional_OrTheWriteIsSilentlyDiscarded()
    {
        // Repositories are pure change-trackers now: nothing but TransactionBehavior commits, and
        // TransactionBehavior only acts on requests carrying this marker.
        new SaveSettingCommand("k", "v").Should().BeAssignableTo<ITransactionalRequest>();
    }
}
