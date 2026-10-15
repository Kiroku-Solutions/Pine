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
/// The training set is read a page at a time, never as one whole-ledger query (TD-133).
/// </summary>
public class CategorizationTrainingPagingTests
{
    private static readonly string[] AllPayees = { "PAYEE0", "PAYEE1", "PAYEE2", "PAYEE3", "PAYEE4" };

    private readonly ISettingRepository _settings = Substitute.For<ISettingRepository>();
    private readonly ITransactionRepository _transactions = Substitute.For<ITransactionRepository>();
    private readonly ICategorizationTrainer _trainer = Substitute.For<ICategorizationTrainer>();
    private readonly ISender _sender = Substitute.For<ISender>();

    private static Transaction Categorised(string payee)
    {
        var amount = new Money(-5m, new CurrencyCode("EUR"));
        var t = Transaction.Create(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), payee, amount, TransactionType.Expense, TimeProvider.System);
        t.ReplaceSplits(new[] { new SplitDefinition(amount, "Groceries") });
        return t;
    }

    private CategorizationTrainingBackgroundService Build(IReadOnlyList<Transaction> ledger, int pageSize, int threshold, int possibleCount)
    {
        _settings.GetAsync(CategorizationTrainingBackgroundService.TrainingConsentSettingKey, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Setting?>(Setting.Create(
                CategorizationTrainingBackgroundService.TrainingConsentSettingKey,
                CategorizationTrainingBackgroundService.TrainingConsentGranted)));
        _transactions.CountPossiblyCategorisedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(possibleCount));
        _transactions.GetCategorisedPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Transaction>>(
                ledger.Skip((int)call[0]).Take((int)call[1]).ToList()));

        var services = new ServiceCollection()
            .AddSingleton(_settings).AddSingleton(_transactions).AddSingleton(_trainer).AddSingleton(_sender)
            .BuildServiceProvider();

        return new CategorizationTrainingBackgroundService(
            services,
            NullLogger<CategorizationTrainingBackgroundService>.Instance,
            Options.Create(new PineAiOptions { TrainingBatchThreshold = threshold }))
        {
            TrainingPageSize = pageSize,
        };
    }

    [Fact]
    public async Task TheLedgerIsReadInPages_NeverWholesale_AndEveryExampleReachesTheTrainer()
    {
        var ledger = Enumerable.Range(0, 5).Select(i => Categorised($"PAYEE{i}")).ToList();

        await Build(ledger, pageSize: 2, threshold: 1, possibleCount: 5).ProcessBatchAsync(CancellationToken.None);

        await _transactions.DidNotReceiveWithAnyArgs().GetAllAsync(default);
        await _transactions.Received(1).GetCategorisedPageAsync(0, 2, Arg.Any<CancellationToken>());
        await _transactions.Received(1).GetCategorisedPageAsync(2, 2, Arg.Any<CancellationToken>());
        await _transactions.Received(1).GetCategorisedPageAsync(4, 2, Arg.Any<CancellationToken>());
        await _trainer.Received(1).TrainModelAsync(
            Arg.Is<IEnumerable<TrainingExample>>(e => e.Select(x => x.Text).OrderBy(x => x)
                .SequenceEqual(AllPayees)),
            Arg.Any<CancellationToken>());
        await _sender.Received(1).Send(
            Arg.Is<Pine.Application.Settings.Commands.SaveSettingCommand>(c => c.Value == "5"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenTheStoreCountIsBelowTheThreshold_NoPageIsRead()
    {
        var ledger = Enumerable.Range(0, 3).Select(i => Categorised($"P{i}")).ToList();

        await Build(ledger, pageSize: 2, threshold: 10, possibleCount: 3).ProcessBatchAsync(CancellationToken.None);

        await _transactions.DidNotReceiveWithAnyArgs().GetCategorisedPageAsync(default, default, default);
        await _trainer.DidNotReceiveWithAnyArgs().TrainModelAsync(default!, default);
    }
}
