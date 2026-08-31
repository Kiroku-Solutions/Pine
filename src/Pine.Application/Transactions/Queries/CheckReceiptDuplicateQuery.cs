using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Core.Importing;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Queries;

/// <summary>
/// What a matched receipt duplicate looks like to the caller: enough to name the collision, nothing
/// that would let it be mutated.
/// </summary>
/// <remarks>
/// A PROJECTION, NOT THE AGGREGATE (TD-20). The scan screen only needs to warn the user, never to
/// touch the transaction it collided with — handing back the live <see cref="Transaction"/> would
/// let a caller that only meant to look edit or void it by accident.
/// </remarks>
/// <param name="TransactionId">The id of the transaction already carrying this receipt's key.</param>
/// <param name="Date">The matched transaction's date.</param>
/// <param name="Amount">The matched transaction's signed ledger amount.</param>
/// <param name="Payee">The matched transaction's payee.</param>
public sealed record ReceiptDuplicateMatch(Guid TransactionId, DateTimeOffset Date, decimal Amount, string Payee);

/// <summary>
/// Asks whether a scanned receipt has already been saved, by deriving the same key
/// <see cref="ImportKeyFactory.FromReceipt"/> would give the transaction being saved and looking for
/// it on the target account (TD-20).
/// </summary>
/// <remarks>
/// <para>
/// <paramref name="SignedAmount"/> MUST BE THE SIGNED LEDGER AMOUNT — negative for an expense — not
/// the positive figure printed on the receipt. The write path negates the printed total before it
/// ever reaches a command, and the key stored on the saved transaction is derived from that negated
/// value; deriving this query's key from the positive figure would produce a key that can never
/// match anything the vault stores, and the whole feature would silently do nothing.
/// </para>
/// <para>
/// This is a read; it neither writes nor acknowledges anything. The caller decides what a match
/// means — warn, block, or (as here) let the user proceed a second time with the same inputs.
/// </para>
/// </remarks>
/// <param name="AccountId">The account the receipt is about to be saved to.</param>
/// <param name="Date">The receipt's date.</param>
/// <param name="SignedAmount">The receipt's signed ledger amount.</param>
/// <param name="CurrencyCode">The receipt's ISO 4217 currency code.</param>
/// <param name="Merchant">The receipt's merchant.</param>
public sealed record CheckReceiptDuplicateQuery(
    Guid AccountId,
    DateTimeOffset Date,
    decimal SignedAmount,
    string CurrencyCode,
    string Merchant) : IRequest<ReceiptDuplicateMatch?>;

public sealed class CheckReceiptDuplicateQueryHandler : IRequestHandler<CheckReceiptDuplicateQuery, ReceiptDuplicateMatch?>
{
    private readonly ITransactionRepository _transactionRepository;

    public CheckReceiptDuplicateQueryHandler(ITransactionRepository transactionRepository)
    {
        _transactionRepository = transactionRepository;
    }

    public async Task<ReceiptDuplicateMatch?> Handle(CheckReceiptDuplicateQuery request, CancellationToken cancellationToken)
    {
        var key = ImportKeyFactory.FromReceipt(
            request.AccountId,
            request.Date,
            request.SignedAmount,
            request.CurrencyCode,
            request.Merchant);

        var existing = await _transactionRepository.GetByImportKeyAsync(request.AccountId, key, cancellationToken);

        return existing is null
            ? null
            : new ReceiptDuplicateMatch(existing.Id, existing.Date, existing.TotalAmount.Amount, existing.Payee);
    }
}
