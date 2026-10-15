using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Accounts;
using Pine.Core.Attachments;
using Pine.Core.Rules;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;

namespace Pine.Application.Transactions.Commands;

/// <summary>
/// One line of the itemisation attached to an <see cref="AddTransactionCommand"/>.
/// </summary>
/// <remarks>
/// <para>
/// AMOUNTS ARE IN THE TRANSACTION SIGN, NOT THE RECEIPT SIGN (TD-19). The whole point of the
/// invariant is that the lines sum to <see cref="AddTransactionCommand.Amount"/>, and a sum can
/// only equal a negative expense total if its terms are negative too. The caller that knows the
/// document -- the receipt scanner -- performs that conversion, exactly as it already does for the
/// total itself.
/// </para>
/// <para>
/// Before this ticket the handler stored receipt line items as printed, i.e. POSITIVE, against a
/// negative expense total. That set could never have reconciled under any sign convention, which is
/// part of why nothing had ever compared the two.
/// </para>
/// </remarks>
public record AddTransactionLineItemCommand(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    LineItemKind Kind = LineItemKind.Product
);

/// <summary>
/// The document a transaction is being recorded from, to be kept with it as evidence.
/// </summary>
/// <param name="FileName">A display name for the file.</param>
/// <param name="ContentType">The IANA media type of <paramref name="Content"/>.</param>
/// <param name="Content">The bytes. Hashed by the domain; never trusted from here.</param>
/// <remarks>
/// IT RIDES ON THE TRANSACTION COMMAND RATHER THAN FOLLOWING IT, AND THAT IS THE DESIGN. A second
/// request after this one would be a second database transaction, so a crash between them leaves a
/// transaction whose receipt was lost or a receipt for a transaction that was rolled back. Carrying
/// the bytes here means <c>TransactionBehavior</c> commits the ledger row and its evidence
/// together or neither -- which is the property that made storing the blob in <c>pine.db</c> the
/// right call in the first place.
/// </remarks>
public sealed record AddTransactionAttachmentCommand(
    string FileName,
    string ContentType,
    byte[] Content);

public record AddTransactionCommand(
    Guid AccountId,
    DateTimeOffset Date,
    string Payee,
    decimal Amount,
    string Currency,
    string Notes,
    string Category,
    System.Collections.Generic.IReadOnlyList<AddTransactionLineItemCommand>? LineItems = null,
    string? ImportKey = null,
    AddTransactionAttachmentCommand? Attachment = null
) : IRequest<Guid>, ITransactionalRequest;

public class AddTransactionCommandHandler : IRequestHandler<AddTransactionCommand, Guid>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IAttachmentRepository _attachmentRepository;
    private readonly IRuleRepository _ruleRepository;
    private readonly TimeProvider _timeProvider;

    public AddTransactionCommandHandler(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        IAttachmentRepository attachmentRepository,
        IRuleRepository ruleRepository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ruleRepository);
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _attachmentRepository = attachmentRepository;
        _ruleRepository = ruleRepository;
        _timeProvider = timeProvider;
    }

    public async Task<Guid> Handle(AddTransactionCommand request, CancellationToken cancellationToken)
    {
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null)
            throw new RequestValidationException(new[]
            {
                new ValidationError(
                    nameof(AddTransactionCommand.AccountId),
                    $"Account {request.AccountId} was not found.",
                    ValidationText.AccountNotFound()),
            });

        // Note: For income the amount is positive, for expense it should be negative.
        // We'll trust the UI/command passes the signed decimal.
        var moneyAmount = new Money(request.Amount, new CurrencyCode(request.Currency));
        
        var type = moneyAmount.Amount >= 0 ? Pine.Core.Transactions.TransactionType.Income : Pine.Core.Transactions.TransactionType.Expense;
        var transaction = Transaction.Create(
            request.AccountId, 
            request.Date, 
            request.Payee, 
            moneyAmount, 
            type,
            _timeProvider, 
            request.Notes);

        // The category, and the one place a rule may speak here.
        //
        // A RULE ONLY FILLS A BLANK; IT NEVER OVERRULES THE USER. On this path the category arrives
        // from a person who typed it into a box, and an instruction they wrote last week must not
        // silently rewrite what they typed thirty seconds ago. So the rules run only when the field
        // is empty -- which is also the case the receipt scanner leaves when OCR found no category.
        // The same precedence holds as on the import path: a matching rule wins outright and the
        // classifier is not consulted.
        var category = request.Category;
        var appliedRule = (RuleMatch?)null;

        if (string.IsNullOrWhiteSpace(category))
        {
            appliedRule = RuleEvaluator.FirstMatch(
                await _ruleRepository.GetEnabledAsync(cancellationToken),
                new RuleInput(request.Payee ?? string.Empty, request.Notes ?? string.Empty, request.Amount));

            category = appliedRule?.Category ?? category;
        }

        // Add a single split for the whole amount to the specified category
        transaction.AddSplit(moneyAmount, category, appliedRule?.Title ?? request.Notes ?? string.Empty);

        // Same mechanism as the file importer (ImportTransactionsCommand): the receipt scanner is
        // just another source of an import key, and a transaction is meant to carry at most one.
        if (!string.IsNullOrWhiteSpace(request.ImportKey))
        {
            transaction.AssignImportKey(request.ImportKey);
        }

        if (request.LineItems != null && request.LineItems.Count > 0)
        {
            foreach (var li in request.LineItems)
            {
                // Line item amounts arrive in the transaction sign (see AddTransactionLineItemCommand),
                // because Transaction.EnsureValid requires them to sum to the total. The validator has
                // already checked that sum, so this loop cannot be the first place a mismatch is seen --
                // but AddLineItem stays deliberately incremental and the aggregate boundary remains the
                // backstop for any caller that bypasses the pipeline.
                transaction.AddLineItem(
                    li.Description,
                    li.Quantity,
                    new Money(li.UnitPrice, moneyAmount.Currency),
                    new Money(li.TotalPrice, moneyAmount.Currency),
                    li.Kind);
            }
        }

        await _transactionRepository.SaveAsync(transaction, cancellationToken);

        // The receipt, if one came with the request. AFTER the transaction is staged, because the
        // attachment carries a foreign key to it; both are staged on the same DbContext and flushed
        // by the same unit of work, so the ordering here is about the insert order SQLite sees, not
        // about two separate writes. Attachment.Create computes the hash and enforces the size
        // limit -- neither is the caller's to decide (10-Domain-Model §7).
        if (request.Attachment is { } attachment)
        {
            await _attachmentRepository.AddAsync(
                Pine.Core.Attachments.Attachment.Create(
                    transaction.Id,
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.Content,
                    _timeProvider),
                cancellationToken);
        }
        
        // Also update account balance. In a real CQRS system, this might be handled 
        // by listening to the TransactionRegisteredEvent, but for simplicity we do it here.
        if (moneyAmount.Amount >= 0)
        {
            account.Deposit(moneyAmount, _timeProvider);
        }
        else
        {
            account.Withdraw(-moneyAmount, _timeProvider);
        }
        
        await _accountRepository.SaveAsync(account, cancellationToken);
        
        return transaction.Id;
    }
}

/// <summary>
/// Rejects an <see cref="AddTransactionCommand"/> whose currency can never be applied to the target
/// account, before the pipeline opens a database transaction and before a single row is written.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A VALIDATOR AND NOT A HANDLER CHECK (TD-12). The handler builds the
/// <see cref="Money"/>, creates the transaction, saves it, and only THEN calls
/// <c>Account.Deposit</c>/<c>Account.Withdraw</c>. On a currency mismatch the failure therefore
/// surfaces from deep inside <c>Money.Add</c> as
/// <c>InvalidOperationException("Cannot add money of different currencies: USD and EUR")</c>. That
/// is not a data-corruption bug -- <c>TransactionBehavior</c> rolls the whole command back, so
/// nothing is persisted -- but it is a failure at the wrong layer: a domain invariant message,
/// phrased for the domain, escapes all the way to the view model, which renders it verbatim. The
/// user is told about "money of different currencies" when what they need to be told is which field
/// they got wrong and what the account actually expects.
/// </para>
/// <para>
/// Running the check here rather than at the top of the handler also means a rejected command never
/// opens a transaction at all: <c>ValidationBehavior</c> sits OUTSIDE <c>TransactionBehavior</c>, so
/// the failure is a rejection, not a rollback. That distinction matters for the vault, which is a
/// single-writer SQLCipher file -- work that is never begun cannot contend with work that is.
/// </para>
/// <para>
/// WHY THE CURRENCY CHECK IS NOT A CONSTRUCTOR CALL. Validating by simply constructing a
/// <see cref="CurrencyCode"/> and catching the failure would reintroduce exactly the leak being
/// removed: its constructor throws <see cref="ArgumentException"/> with a domain-phrased message.
/// <see cref="CurrencyCodeText.TryNormalise"/> is the non-throwing projection of that constructor.
/// Since TD-18 it DELEGATES to <c>CurrencyCode.TryCreate</c> rather than restating the rule, so the
/// validator now rejects a well-shaped non-currency such as <c>"ZZZ"</c> as well as a malformed one
/// -- which closes TD-61, the debt that this helper's copy of the shape rule would drift from the
/// domain's the moment the domain gained ISO 4217 membership validation.
/// </para>
/// <para>
/// WHY THE NORMALISATION IS LOAD-BEARING. <see cref="CurrencyCode"/> trims and upper-cases its
/// input, so a command carrying <c>"usd"</c> against a USD account is accepted by the handler today
/// and must keep being accepted. Comparing the raw string would turn a working case into a reported
/// mismatch -- a regression dressed up as a validation improvement.
/// </para>
/// </remarks>
public sealed class AddTransactionCommandValidator : IRequestValidator<AddTransactionCommand>
{
    private readonly IAccountRepository _accountRepository;

    /// <summary>
    /// Initialises the validator with the account repository used to resolve the target account.
    /// </summary>
    /// <param name="accountRepository">Repository used to look up the account being posted to.</param>
    public AddTransactionCommandValidator(IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    /// <summary>
    /// Checks that the target account exists and that the requested currency is both well formed and
    /// the account's own currency.
    /// </summary>
    /// <param name="request">The command about to be handled.</param>
    /// <param name="cancellationToken">Token observed while loading the account.</param>
    /// <returns>
    /// An empty list when the command may proceed, otherwise the errors that stop it. The rules are
    /// ordered and short-circuit deliberately: with no account there is nothing to compare a currency
    /// against, and with a malformed currency there is nothing meaningful to compare. Reporting a
    /// "mismatch" derived from a value that is not a currency at all would be noise, not help.
    /// </returns>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        AddTransactionCommand request,
        CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(request.AccountId, cancellationToken);
        if (account == null)
        {
            return new[]
            {
                new ValidationError(
                    nameof(AddTransactionCommand.AccountId),
                    $"Account {request.AccountId} was not found.",
                    ValidationText.AccountNotFound()),
            };
        }

        // TD-62: a zero amount is rejected here, not discovered when the handler reaches
        // Account.Deposit/Withdraw ("... must be strictly positive."). Negative is a valid expense --
        // the handler withdraws its absolute value -- so ONLY zero is refused. The test runs over Money
        // so that an amount which rounds to zero in its currency (0.004 USD) is refused too, since
        // that is what the domain would see. With an unusable currency there is no scale to round
        // to, so the raw value is tested and the currency error is reported alongside it.
        if (!CurrencyCodeText.TryNormalise(request.Currency, out var normalised))
        {
            var invalidCurrency = new List<ValidationError>();
            if (request.Amount == 0m)
            {
                invalidCurrency.Add(AmountZeroError());
            }

            invalidCurrency.Add(new ValidationError(
                nameof(AddTransactionCommand.Currency),
                $"'{request.Currency}' is not a valid ISO 4217 currency code.",
                ValidationText.CurrencyCodeInvalid(request.Currency)));
            return invalidCurrency;
        }

        var errors = new List<ValidationError>();
        if (new Money(request.Amount, new CurrencyCode(normalised)).Amount == 0m)
        {
            errors.Add(AmountZeroError());
        }

        var accountCurrency = account.Balance.Currency.Value;
        if (!string.Equals(normalised, accountCurrency, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError(
                nameof(AddTransactionCommand.Currency),
                $"Transaction currency {normalised} does not match account currency {accountCurrency}.",
                ValidationText.CurrencyMismatch(normalised, accountCurrency)));
        }

        // TD-95: a line quantity is strictly positive and has at most LineQuantity.Scale decimals. A
        // person's over-precise entry is REJECTED here with a message, never silently rounded; the
        // receipt scanner rounds what OCR recognised before it builds the command (LineQuantity).
        if (request.LineItems is { Count: > 0 })
        {
            for (var i = 0; i < request.LineItems.Count; i++)
            {
                var quantity = request.LineItems[i].Quantity;
                if (quantity <= 0m)
                {
                    errors.Add(new ValidationError(
                        $"{nameof(AddTransactionCommand.LineItems)}[{i}].{nameof(AddTransactionLineItemCommand.Quantity)}",
                        $"Line {i + 1}: the quantity must be greater than zero.",
                        ValidationText.LineItemQuantityNotPositive(i + 1)));
                }
                else if (LineQuantity.HasExcessScale(quantity))
                {
                    errors.Add(new ValidationError(
                        $"{nameof(AddTransactionCommand.LineItems)}[{i}].{nameof(AddTransactionLineItemCommand.Quantity)}",
                        $"Line {i + 1}: the quantity cannot have more than {LineQuantity.Scale} decimal places.",
                        ValidationText.LineItemQuantityScale(i + 1, LineQuantity.Scale)));
                }
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        // TD-19, at the layer that can phrase it for a human. The aggregate enforces the same rule at
        // the persistence boundary and would throw an InvalidOperationException carrying a
        // domain-phrased message straight out to the view model (the TD-24 leak). Checking it here
        // means the command is REJECTED before TransactionBehavior opens a database transaction, and
        // the caller is told the size of the difference rather than that a sum is not equal to a total.
        //
        // The comparison runs over Money rather than the raw decimals so that it agrees with what the
        // aggregate will actually compute: every figure is normalised to the minor units of its
        // currency on construction (TD-18), so comparing unrounded inputs could reject a set the domain
        // would accept, or -- worse -- accept one it would refuse.
        if (request.LineItems is { Count: > 0 })
        {
            var currency = new CurrencyCode(normalised);

            // Shared with UpdateTransactionCommandValidator's split-sum check via AmountSumCheck
            // (TD-68) -- the arithmetic lives once, and each validator only supplies its own field
            // name and user-facing message.
            if (!AmountSumCheck.Matches(request.LineItems.Select(li => li.TotalPrice), request.Amount, currency, out var lineItemSum, out var total))
            {
                var difference = total - lineItemSum;
                return new[]
                {
                    new ValidationError(
                        nameof(AddTransactionCommand.LineItems),
                        $"The line items add up to {lineItemSum.Amount} but the transaction total is " +
                        $"{total.Amount}. The {difference.Amount} difference must be recorded " +
                        "as an adjustment line (tax, tip, discount, deposit, rounding or unattributed).",
                        ValidationText.LineItemsDoNotAddUp(lineItemSum.Amount, total.Amount, difference.Amount)),
                };
            }
        }

        return Array.Empty<ValidationError>();
    }

    private static ValidationError AmountZeroError() => new(
        nameof(AddTransactionCommand.Amount),
        "Transaction amount cannot be zero.",
        ValidationText.TransactionAmountZero());
}
