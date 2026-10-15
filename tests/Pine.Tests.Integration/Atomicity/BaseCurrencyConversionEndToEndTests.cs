using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Budgets.Commands;
using Pine.Application.Common;
using Pine.Application.ExchangeRates.Commands;
using Pine.Application.ExchangeRates.Queries;
using Pine.Application.Settings.Commands;
using Pine.Core.Budgets;
using Pine.Core.Common;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// Changing the vault's base currency converts the budgets once, atomically with the setting, or is refused
/// (owner decision, 2026-10-14) -- through the real pipeline and an encrypted vault.
/// </summary>
public class BaseCurrencyConversionEndToEndTests
{
    private static readonly DateTime LastWeek = DateTime.UtcNow.Date.AddDays(-7);

    private static Task<string?> StoredSetting(AtomicityHost host) =>
        host.QueryAsync(async c => (await c.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "Vault.BaseCurrency"))?.Value);

    [Fact]
    public async Task WithTheRateEntered_TheChangeConvertsEveryBudgetOnce_AndRecordsIt()
    {
        await using var host = await AtomicityHost.CreateAsync();
        await host.SeedAccountAsync("Current", 1000m, "USD");
        await host.Mediator.Send(new CreateBudgetCommand("Groceries", 100.10m, BudgetPeriod.Monthly));
        await host.Mediator.Send(new CreateBudgetCommand("Rent", 800m, BudgetPeriod.Monthly));
        await host.Mediator.Send(new SetExchangeRateCommand("USD", "EUR", 0.9125m, LastWeek));
        host.UnitOfWorkLog.Clear();

        await host.Mediator.Send(new SaveSettingCommand("Vault.BaseCurrency", "eur"));

        var budgets = await host.QueryAsync(c => c.Budgets.AsNoTracking().ToListAsync());
        var groceries = budgets.Single(b => b.Category == "Groceries");
        groceries.Limit.Currency.Value.Should().Be("EUR");
        groceries.Limit.Amount.Should().Be(91.34m, "100.10 x 0.9125 = 91.34125, rounded to the cent");
        groceries.PreviousLimitAmount.Should().Be(100.10m);
        groceries.PreviousLimitCurrency.Should().Be("USD");
        groceries.LimitConversionRate.Should().Be(0.9125m);
        groceries.LimitConvertedAtUtc.Should().NotBeNull();
        budgets.Single(b => b.Category == "Rent").Limit.Amount.Should().Be(730m);
        (await StoredSetting(host)).Should().Be("EUR");
        host.UnitOfWorkLog.Calls.Should().Equal(UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);

        // Saving the same currency again is not a second conversion.
        await host.Mediator.Send(new SaveSettingCommand("Vault.BaseCurrency", "EUR"));
        (await host.QueryAsync(c => c.Budgets.AsNoTracking().SingleAsync(b => b.Category == "Rent"))).Limit.Amount.Should().Be(730m);
    }

    [Fact]
    public async Task WithoutTheRate_TheChangeIsRefused_AndNothingIsWritten()
    {
        await using var host = await AtomicityHost.CreateAsync();
        await host.SeedAccountAsync("Current", 1000m, "USD");
        await host.Mediator.Send(new CreateBudgetCommand("Groceries", 100m, BudgetPeriod.Monthly));
        host.UnitOfWorkLog.Clear();

        var act = () => host.Mediator.Send(new SaveSettingCommand("Vault.BaseCurrency", "EUR"));

        var thrown = (await act.Should().ThrowAsync<RequestValidationException>()).Which;
        thrown.Errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be("Validation.BaseCurrency.RateMissing");
        var budget = await host.QueryAsync(c => c.Budgets.AsNoTracking().SingleAsync());
        budget.Limit.Currency.Value.Should().Be("USD");
        budget.Limit.Amount.Should().Be(100m);
        budget.PreviousLimitCurrency.Should().BeNull();
        (await StoredSetting(host)).Should().BeNull();
        host.UnitOfWorkLog.Calls.Should().BeEmpty("the refusal happens before a transaction opens");
    }

    [Fact]
    public async Task WhenAConversionFailsMidWay_NeitherTheBudgetsNorTheSettingChange()
    {
        await using var host = await AtomicityHost.CreateAsync(services =>
        {
            services.AddScoped<IBudgetRepository>(sp => new FailOnSecondSaveBudgetRepository(
                new SqliteBudgetRepository(sp.GetRequiredService<PineDbContext>()),
                sp.GetRequiredService<IUnitOfWork>()));
        });
        await host.SeedAccountAsync("Current", 1000m, "USD");
        await host.Mediator.Send(new CreateBudgetCommand("Groceries", 100m, BudgetPeriod.Monthly));
        await host.Mediator.Send(new CreateBudgetCommand("Rent", 800m, BudgetPeriod.Monthly));
        await host.Mediator.Send(new SetExchangeRateCommand("USD", "EUR", 0.9m, LastWeek));
        FailOnSecondSaveBudgetRepository.Armed = true;
        try
        {
            var act = () => host.Mediator.Send(new SaveSettingCommand("Vault.BaseCurrency", "EUR"));

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            FailOnSecondSaveBudgetRepository.Armed = false;
        }

        var budgets = await host.QueryAsync(c => c.Budgets.AsNoTracking().ToListAsync());
        budgets.Should().OnlyContain(b => b.Limit.Currency.Value == "USD" && b.PreviousLimitCurrency == null,
            "the first budget was flushed inside the transaction and must have been rolled back with the rest");
        (await StoredSetting(host)).Should().BeNull();
    }

    [Fact]
    public async Task EnteredRates_AreListed_NewestFirst_WithTheOneInForceMarked()
    {
        await using var host = await AtomicityHost.CreateAsync();
        await host.Mediator.Send(new SetExchangeRateCommand("USD", "EUR", 0.8m, DateTime.UtcNow.Date.AddDays(-30)));
        await host.Mediator.Send(new SetExchangeRateCommand("USD", "EUR", 0.9m, DateTime.UtcNow.Date.AddDays(-2)));
        await host.Mediator.Send(new SetExchangeRateCommand("USD", "EUR", 1.5m, DateTime.UtcNow.Date.AddDays(10)));

        var rates = await host.Mediator.Send(new GetExchangeRatesQuery());

        rates.Select(r => r.Rate).Should().Equal(1.5m, 0.9m, 0.8m);
        rates.Select(r => r.InForce).Should().Equal(false, true, false);
        rates.Should().OnlyContain(r => r.Source == "manual");
    }

    [Fact]
    public async Task ARateForAPairAndDay_IsReplaced_NotDuplicated()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var first = await host.Mediator.Send(new SetExchangeRateCommand("USD", "EUR", 0.9m, LastWeek));
        var second = await host.Mediator.Send(new SetExchangeRateCommand("USD", "EUR", 0.95m, LastWeek.AddHours(3)));

        second.Should().Be(first);
        var rate = (await host.QueryAsync(c => c.ExchangeRates.AsNoTracking().ToListAsync())).Should().ContainSingle().Subject;
        rate.Rate.Should().Be(0.95m);
        host.CountRowsRaw("ExchangeRates").Should().Be(1);
    }

    /// <summary>Saves the first budget normally; on the second, flushes inside the open transaction and then fails.</summary>
    private sealed class FailOnSecondSaveBudgetRepository : IBudgetRepository
    {
        public static volatile bool Armed;

        private readonly IBudgetRepository _inner;
        private readonly IUnitOfWork _unitOfWork;
        private int _saves;

        public FailOnSecondSaveBudgetRepository(IBudgetRepository inner, IUnitOfWork unitOfWork)
        {
            _inner = inner;
            _unitOfWork = unitOfWork;
        }

        public Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => _inner.GetByIdAsync(id, cancellationToken);

        public Task<IEnumerable<Budget>> GetAllAsync(CancellationToken cancellationToken = default) => _inner.GetAllAsync(cancellationToken);

        public async Task SaveAsync(Budget budget, CancellationToken cancellationToken = default)
        {
            await _inner.SaveAsync(budget, cancellationToken);
            if (Armed && ++_saves == 2)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("injected failure after the budgets were flushed");
            }
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => _inner.DeleteAsync(id, cancellationToken);
    }
}
