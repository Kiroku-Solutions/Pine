using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Pine.Application.Services;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.Settings;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Services;

/// <summary>
/// The categoriser is trained on a user's ledger only after they have said yes (TD-89).
/// </summary>
public class CategorizationTrainingConsentTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private readonly ISettingRepository _settings = Substitute.For<ISettingRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly ICategorizationTrainer _trainer = Substitute.For<ICategorizationTrainer>();
    private readonly ISender _sender = Substitute.For<ISender>();

    private CategorizationTrainingBackgroundService BuildService()
    {
        var transaction = Transaction.Create(
            Guid.NewGuid(), Clock.GetUtcNow(), "TESCO",
            new Money(-10m, new CurrencyCode("EUR")), TransactionType.Expense, Clock);
        transaction.ReplaceSplits(new[] { new SplitDefinition(new Money(-10m, new CurrencyCode("EUR")), "Groceries") });
        _transactions.CountPossiblyCategorisedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        _transactions.GetCategorisedPageAsync(Arg.Is<int>(0), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>(new[] { transaction }));

        var services = new ServiceCollection()
            .AddSingleton(_settings)
            .AddSingleton(_transactions)
            .AddSingleton(_trainer)
            .AddSingleton(_sender)
            .BuildServiceProvider();

        return new CategorizationTrainingBackgroundService(
            services,
            NullLogger<CategorizationTrainingBackgroundService>.Instance,
            Options.Create(new PineAiOptions { TrainingBatchThreshold = 1 }));
    }

    private void ConsentIs(string? value) =>
        _settings.GetAsync(CategorizationTrainingBackgroundService.TrainingConsentSettingKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Setting?>(value is null
                ? null
                : Setting.Create(CategorizationTrainingBackgroundService.TrainingConsentSettingKey, value)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    public async Task WithoutExplicitConsent_NothingIsReadAndNothingIsTrained(string? value)
    {
        ConsentIs(value);

        await BuildService().ProcessBatchAsync(CancellationToken.None);

        await _trainer.DidNotReceiveWithAnyArgs().TrainModelAsync(default!, default);
        await _transactions.DidNotReceiveWithAnyArgs().CountPossiblyCategorisedAsync(default);
        await _transactions.DidNotReceiveWithAnyArgs().GetCategorisedPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithConsent_TheModelIsTrained()
    {
        ConsentIs("true");

        await BuildService().ProcessBatchAsync(CancellationToken.None);

        await _trainer.Received(1).TrainModelAsync(
            Arg.Is<IEnumerable<TrainingExample>>(e => e.Count() == 1), Arg.Any<CancellationToken>());
    }
}
