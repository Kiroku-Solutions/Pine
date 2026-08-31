using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

/// <summary>
/// <see cref="CheckReceiptDuplicateQueryHandler"/>: the decision half of TD-20's duplicate check, in
/// memory over a substitute repository.
/// </summary>
/// <remarks>
/// These tests pin two things: the handler derives the SAME key
/// <see cref="ImportKeyFactory.FromReceipt"/> would (rather than re-implementing the derivation, or
/// drifting from it), and it hands back a <see cref="ReceiptDuplicateMatch"/> projection rather than
/// the live <see cref="Transaction"/> aggregate, so a caller that only meant to warn cannot mutate what
/// it found.
/// </remarks>
public class CheckReceiptDuplicateQueryTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTimeOffset Date = new(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

    private readonly ITransactionRepository _repository = Substitute.For<ITransactionRepository>();
    private readonly CheckReceiptDuplicateQueryHandler _handler;

    public CheckReceiptDuplicateQueryTests()
    {
        _handler = new CheckReceiptDuplicateQueryHandler(_repository);
    }

    private static Transaction StoredTransaction(DateTimeOffset date, decimal amount, string payee)
    {
        var transaction = Transaction.Create(
            AccountId, date, payee, new Money(amount, new CurrencyCode("USD")),
            amount >= 0 ? TransactionType.Income : TransactionType.Expense, TimeProvider.System);
        transaction.AssignImportKey(ImportKeyFactory.FromReceipt(AccountId, date, amount, "USD", payee));
        return transaction;
    }

    [Fact]
    public async Task A_receipt_matching_a_stored_key_reports_the_transaction()
    {
        var stored = StoredTransaction(Date, -24.50m, "ACME");
        var key = ImportKeyFactory.FromReceipt(AccountId, Date, -24.50m, "USD", "ACME");
        _repository.GetByImportKeyAsync(AccountId, key, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Transaction?>(stored));

        var match = await _handler.Handle(
            new CheckReceiptDuplicateQuery(AccountId, Date, -24.50m, "USD", "ACME"), default);

        match.Should().NotBeNull();
        match!.TransactionId.Should().Be(stored.Id);
        match.Payee.Should().Be("ACME");
        match.Amount.Should().Be(-24.50m);
        match.Date.Should().Be(Date);
    }

    [Fact]
    public async Task A_receipt_with_no_match_reports_nothing()
    {
        _repository.GetByImportKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Transaction?>(null));

        var match = await _handler.Handle(
            new CheckReceiptDuplicateQuery(AccountId, Date, -24.50m, "USD", "ACME"), default);

        match.Should().BeNull();
    }

    [Fact]
    public async Task The_query_asks_for_the_key_the_factory_derives()
    {
        _repository.GetByImportKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Transaction?>(null));

        await _handler.Handle(new CheckReceiptDuplicateQuery(AccountId, Date, -24.50m, "USD", "ACME"), default);

        var expectedKey = ImportKeyFactory.FromReceipt(AccountId, Date, -24.50m, "USD", "ACME");
        await _repository.Received(1).GetByImportKeyAsync(AccountId, expectedKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_positive_printed_amount_derives_a_different_key_than_the_signed_ledger_amount()
    {
        // The contract this feature lives or dies by: FromReceipt must be called with the SIGNED
        // ledger amount. If the handler (or a caller) ever passed the positive printed figure, this
        // would derive the wrong key and the whole feature would silently never match anything.
        _repository.GetByImportKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Transaction?>(null));

        await _handler.Handle(new CheckReceiptDuplicateQuery(AccountId, Date, -24.50m, "USD", "ACME"), default);

        var wrongSignKey = ImportKeyFactory.FromReceipt(AccountId, Date, 24.50m, "USD", "ACME");
        await _repository.DidNotReceive().GetByImportKeyAsync(AccountId, wrongSignKey, Arg.Any<CancellationToken>());
    }
}
