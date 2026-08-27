using System;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pine.Application.Accounts.Commands;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.Settings;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Atomicity;

/// <summary>
/// The two structural guarantees the transactional boundary rests on: repositories never commit by
/// themselves, and everything inside one request shares one <see cref="PineDbContext"/>.
/// </summary>
public class RequestScopeAndRepositoryBoundaryTests
{
    /// <summary>
    /// Regression guard for the entire class of silent-data-loss bugs TD-01 introduced and Stage 2
    /// had to hunt down: if a repository ever calls <c>SaveChangesAsync</c> again, this fails.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Repositories_OutsideATransactionalRequest_PersistNothingOfTheirOwn()
    {
        await using var host = await AtomicityHost.CreateAsync();

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingRepository>();
            var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

            var account = Account.Create("Never committed", AccountType.Bank, new Money(99m, new CurrencyCode("USD")), timeProvider);
            accountId = account.Id;

            await accounts.SaveAsync(account);
            await settings.SaveAsync(Setting.Create("Atomicity.NeverCommitted", "staged"));

            // Deliberately no IUnitOfWork.SaveChangesAsync and no commit: the scope just ends.
        }

        (await host.QueryAsync(c => c.Accounts.AsNoTracking().CountAsync(a => a.Id == accountId)))
            .Should().Be(0, "a repository must stage changes only; committing is the unit of work's job");
        (await host.QueryAsync(c => c.Settings.AsNoTracking().CountAsync(s => s.Key == "Atomicity.NeverCommitted")))
            .Should().Be(0);

        host.UnitOfWorkLog.Calls.Should().BeEmpty("nothing opened, flushed or committed a boundary");
    }

    /// <summary>
    /// Two repositories resolved independently inside ONE request stage onto the same context.
    /// </summary>
    /// <remarks>
    /// This is proved behaviourally rather than by restating the DI registration: the handler writes
    /// an <see cref="Account"/> through <see cref="IAccountRepository"/> and a
    /// <see cref="Pine.Core.Transactions.Transaction"/> through <c>ITransactionRepository</c>, then
    /// looks at its OWN injected context's change tracker. If the two repositories held different
    /// contexts, at most one of the two entities could be tracked there — and the single commit
    /// could not possibly persist both.
    /// </remarks>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task OneRequest_SharesOneDbContextAcrossEveryRepositoryAndTheUnitOfWork()
    {
        await using var host = await AtomicityHost.CreateAsync();

        var result = await host.Mediator.Send(new ScopeProbeCommand("probe payee"));

        result.HadActiveTransaction.Should().BeTrue("the request is marked ITransactionalRequest");
        result.AccountEntriesOnContext.Should().Be(1);
        result.TransactionEntriesOnContext.Should().Be(1, "both repositories staged onto the same context");

        // The single commit persisted both, which is only possible from one shared context.
        (await host.QueryAsync(c => c.Accounts.AsNoTracking().CountAsync(a => a.Id == result.AccountId))).Should().Be(1);
        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync(t => t.Id == result.TransactionId))).Should().Be(1);

        host.UnitOfWorkLog.Calls.Should().Equal(
            UnitOfWorkCall.Begin, UnitOfWorkCall.SaveChanges, UnitOfWorkCall.Commit);
    }

    [Fact]
    public async Task EachRequest_GetsItsOwnDbContextInstance()
    {
        await using var host = await AtomicityHost.CreateAsync();

        await host.Mediator.Send(new ScopeProbeCommand("first"));
        await host.Mediator.Send(new ScopeProbeCommand("second"));

        var recorder = host.Services.GetRequiredService<ScopeProbeRecorder>();
        recorder.Contexts.Should().HaveCount(2);
        recorder.Contexts[0].Should().NotBeSameAs(recorder.Contexts[1],
            "ScopedMediator opens a fresh DI scope per Send, so one request never shares a context with another");

        await using var scope = host.Services.CreateAsyncScope();
        var otherScopeContext = scope.ServiceProvider.GetRequiredService<PineDbContext>();
        recorder.Contexts.Should().NotContain(otherScopeContext);
    }

    /// <summary>
    /// The marker is genuinely opt-in: an identical request without it gets no boundary, and its
    /// staged writes are therefore lost rather than silently committed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task UnmarkedRequest_GetsNoTransactionAndPersistsNothing()
    {
        await using var host = await AtomicityHost.CreateAsync();

        var result = await host.Mediator.Send(new UnmarkedProbeCommand("unmarked"));

        result.HadActiveTransaction.Should().BeFalse("TransactionBehavior is a no-op without the marker");
        result.AccountEntriesOnContext.Should().Be(1, "the writes were still staged on the shared context");

        (await host.QueryAsync(c => c.Accounts.AsNoTracking().CountAsync(a => a.Id == result.AccountId))).Should().Be(0);
        (await host.QueryAsync(c => c.Transactions.AsNoTracking().CountAsync(t => t.Id == result.TransactionId))).Should().Be(0);

        host.UnitOfWorkLog.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// Integration-level counterpart to <c>ScopedMediatorTests</c>: the scoping decorator reaches a
    /// handler without recursing when the container holds the full real infrastructure, not just a
    /// substituted unit of work.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Mediator_ResolvedFromTheRootProvider_IsTheScopingDecoratorAndReachesHandlers()
    {
        await using var host = await AtomicityHost.CreateAsync();

        var mediator = host.Services.GetRequiredService<IMediator>();
        mediator.Should().BeOfType<ScopedMediator>();
        host.Services.GetRequiredService<ISender>().Should().BeSameAs(mediator);

        var accountId = await mediator.Send(new CreateAccountCommand("Root send", "USD", AccountType.Bank, 12m));

        (await host.QueryAsync(c => c.Accounts.AsNoTracking().CountAsync(a => a.Id == accountId))).Should().Be(1);
    }
}
