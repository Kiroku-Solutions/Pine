using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Categories;

namespace Pine.Application.Transactions.Commands;

public record ImportTransactionsCommand(
    Guid TargetAccountId,
    Stream FileStream,
    IImporter Importer,
    ImportProfile Profile
) : IRequest<ImportTransactionsResult>, ITransactionalRequest;

/// <summary>
/// What the import decided to do with one parsed row, and why.
/// </summary>
/// <remarks>
/// Every parsed row receives exactly one disposition and every disposition is reported, including the
/// ones that resulted in a row NOT being saved. The old handler returned only the surviving
/// transactions, so a discarded row was indistinguishable from a row the file never contained.
/// </remarks>
public enum ImportRowDisposition
{
    /// <summary>The row carried an import key not seen before, and was saved.</summary>
    Imported,

    /// <summary>
    /// The row was saved, but its importer supplied no import key, so it could NOT be checked for
    /// duplicates. Reported rather than hidden: an unchecked row is a claim the user must be able to see.
    /// </summary>
    ImportedWithoutKeyCheck,

    /// <summary>The account already holds a transaction with this row's import key. Not saved.</summary>
    SkippedAlreadyInAccount,

    /// <summary>An earlier row in this same file produced the same import key. Not saved.</summary>
    SkippedDuplicateInFile,
}

/// <summary>
/// One parsed row, the decision taken on it, and a sentence explaining that decision to the user.
/// </summary>
/// <param name="Transaction">The parsed transaction, whether or not it was saved.</param>
/// <param name="Disposition">What the import did with it.</param>
/// <param name="Reason">A user-facing explanation of <paramref name="Disposition"/>.</param>
public sealed record ImportReviewRow(
    Transaction Transaction,
    ImportRowDisposition Disposition,
    string Reason)
{
    /// <summary>Gets a value indicating whether this row was written to the vault.</summary>
    public bool WasImported =>
        Disposition is ImportRowDisposition.Imported or ImportRowDisposition.ImportedWithoutKeyCheck;
}

/// <summary>
/// The outcome of an import: every parsed row, in file order, with what happened to it.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS REPLACED <c>IReadOnlyList&lt;Transaction&gt;</c>: the previous contract could only express
/// what survived. A row dropped as a duplicate left no trace anywhere — not in the return value, not in a
/// log, not on screen — which is exactly the silent data loss TD-03 records. A caller cannot warn about
/// something it was never told.
/// </para>
/// <para>
/// <see cref="Rows"/> preserves the order the importer produced, so the review list a user reads lines up
/// with the file they imported. <see cref="Imported"/> and <see cref="Skipped"/> are materialised once in
/// the constructor rather than computed per access, because the UI binds to them repeatedly.
/// </para>
/// </remarks>
public sealed record ImportTransactionsResult
{
    /// <summary>Initialises the result from the per-row decisions, in file order.</summary>
    /// <param name="rows">Every parsed row with its disposition. Never null.</param>
    public ImportTransactionsResult(IReadOnlyList<ImportReviewRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        Rows = rows;
        Imported = rows.Where(row => row.WasImported).Select(row => row.Transaction).ToArray();
        Skipped = rows.Where(row => !row.WasImported).ToArray();
    }

    /// <summary>Gets every parsed row with its disposition, in the order the importer produced them.</summary>
    public IReadOnlyList<ImportReviewRow> Rows { get; }

    /// <summary>Gets the transactions that were written to the vault.</summary>
    public IReadOnlyList<Transaction> Imported { get; }

    /// <summary>Gets the rows that were not written, each carrying the reason it was not.</summary>
    public IReadOnlyList<ImportReviewRow> Skipped { get; }
}

public sealed class ImportTransactionsCommandHandler : IRequestHandler<ImportTransactionsCommand, ImportTransactionsResult>
{
    private readonly ITransactionRepository _transactionRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly TimeProvider _timeProvider;
    private readonly IAIProvider _aiProvider;
    private readonly ICategoryRepository _categoryRepository;

    public ImportTransactionsCommandHandler(
        ITransactionRepository transactionRepository, 
        IAccountRepository accountRepository, 
        TimeProvider timeProvider,
        IAIProvider aiProvider,
        ICategoryRepository categoryRepository)
    {
        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider;
        _aiProvider = aiProvider;
        _categoryRepository = categoryRepository;
    }

    public async Task<ImportTransactionsResult> Handle(ImportTransactionsCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify account exists
        var account = await _accountRepository.GetByIdAsync(request.TargetAccountId, cancellationToken);
        if (account == null)
            throw new ArgumentException("Target account not found.");

        // 2. Parse transactions from file
        var transactions = await request.Importer.ImportAsync(account, request.FileStream, request.Profile, cancellationToken);
        
        // 3. Deduplication on the stable import key the importer assigned (TD-03).
        //
        //    WHAT THIS REPLACED, AND WHY: the previous implementation loaded every transaction on the
        //    account and matched on (Date.Date, TotalAmount.Amount). Two genuine coffees of the same price
        //    on the same day are two transactions, and that rule threw the second one away -- silently,
        //    with the return value giving the caller no way to even know a row had existed. It also grew
        //    linearly with the account's history on every import.
        //
        //    Now: the importer states which external row each transaction came from, and the repository is
        //    asked only "which of these keys do you already have?" -- an indexed lookup over the candidate
        //    keys, not a full account load.
        //
        //    THREE OUTCOMES, ALL REPORTED. A key already on the account is a re-import of the same row. A
        //    key seen earlier in THIS batch is the same file row reaching us twice. A row with NO key
        //    cannot be checked at all: it is still imported, because refusing to import it would be the
        //    very data loss this ticket removes, but it is reported as unchecked rather than quietly
        //    treated as if it had passed a check it never took. Legacy rows already in the vault have a
        //    NULL ImportKey and are simply absent from the lookup's results, so they neither match
        //    everything nor block anything.
        var candidateKeys = transactions
            .Select(tx => tx.ImportKey)
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var existingKeys = candidateKeys.Length == 0
            ? Array.Empty<string>()
            : (IReadOnlyCollection<string>)await _transactionRepository.GetExistingImportKeysAsync(
                request.TargetAccountId, candidateKeys, cancellationToken);

        var existingKeyLookup = new HashSet<string>(existingKeys, StringComparer.Ordinal);
        var keysSeenInThisBatch = new HashSet<string>(StringComparer.Ordinal);

        var reviewRows = new List<ImportReviewRow>(transactions.Count);
        var importedTransactions = new List<Transaction>();

        foreach (var tx in transactions)
        {
            var key = tx.ImportKey;

            if (string.IsNullOrEmpty(key))
            {
                reviewRows.Add(new ImportReviewRow(
                    tx,
                    ImportRowDisposition.ImportedWithoutKeyCheck,
                    "Imported, but this importer supplied no import key, so it could not be checked against transactions already in the account."));
                importedTransactions.Add(tx);
                continue;
            }

            if (existingKeyLookup.Contains(key))
            {
                reviewRows.Add(new ImportReviewRow(
                    tx,
                    ImportRowDisposition.SkippedAlreadyInAccount,
                    $"Not imported: '{account.Name}' already holds this row from an earlier import of the same file."));
                continue;
            }

            if (!keysSeenInThisBatch.Add(key))
            {
                reviewRows.Add(new ImportReviewRow(
                    tx,
                    ImportRowDisposition.SkippedDuplicateInFile,
                    "Not imported: an earlier row in this same file is identical to it; only the first occurrence was imported."));
                continue;
            }

            reviewRows.Add(new ImportReviewRow(
                tx,
                ImportRowDisposition.Imported,
                "Imported as a new transaction."));
            importedTransactions.Add(tx);
        }


        // 4. Currency guard. PLACEMENT IS THE POINT: this sits after the importer has produced the
        //    transactions -- their currency does not exist until ImportAsync has run, which is why
        //    ImportTransactionsCommandValidator cannot perform this check -- and STRICTLY BEFORE the
        //    first SaveAsync below. Ahead of the first write the failure is a rejection; one line
        //    later it would be a rollback, which is correct but wasteful and reports far worse.
        //
        //    This is NOT dead code even though CsvImporter builds every Money from
        //    targetAccount.Balance.Currency and so can never trip it. IImporter is a port, and the
        //    command carries the IImporter INSTANCE as a payload field, so any plugin or future
        //    importer -- an OFX/QIF reader, a bank API adapter, a test double -- can hand back
        //    whatever currency it likes. This guard is the boundary check on that port.
        //
        //    Every mismatch is collected before throwing: an import that reports one bad row per
        //    attempt makes the user re-run the whole file to discover the next one.
        var currencyErrors = new List<ValidationError>();
        var accountCurrency = account.Balance.Currency.Value;
        foreach (var tx in importedTransactions)
        {
            var transactionCurrency = tx.TotalAmount.Currency.Value;
            if (!string.Equals(transactionCurrency, accountCurrency, StringComparison.Ordinal))
            {
                var transactionDate = tx.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                currencyErrors.Add(new ValidationError(
                    nameof(ImportTransactionsCommand.TargetAccountId),
                    $"Imported transaction '{tx.Payee}' dated {transactionDate} is in {transactionCurrency}, but account '{account.Name}' is in {accountCurrency}."));
            }
        }

        if (currencyErrors.Count > 0)
        {
            throw new RequestValidationException(currencyErrors);
        }

        // 5. Save each unique transaction and update account balance
        foreach (var tx in importedTransactions)
        {
            // Apply AI Categorization if no category was provided by the CSV
            if (tx.Splits.Count == 0)
            {
                string transactionText = $"{tx.Payee} {tx.Notes}".Trim();
                var aiResult = await _aiProvider.CategorizeTransactionAsync(transactionText, tx.TotalAmount.Amount, cancellationToken);
                
                if (aiResult.CategoryId != Guid.Empty && aiResult.ConfidenceScore >= 0.3)
                {
                    var category = await _categoryRepository.GetByIdAsync(aiResult.CategoryId, cancellationToken);
                    if (category != null)
                    {
                        // Set (not append) the category: a repeated categorization must not add a second split.
                        tx.ReplaceSplits(new[] { new SplitDefinition(tx.TotalAmount, category.Name, "AI") });
                    }
                }
            }

            await _transactionRepository.SaveAsync(tx, cancellationToken);
            
            var absoluteAmount = new Pine.Core.Common.ValueObjects.Money(Math.Abs(tx.TotalAmount.Amount), tx.TotalAmount.Currency);
            if (tx.TotalAmount.Amount < 0)
            {
                account.Withdraw(absoluteAmount, _timeProvider);
            }
            else
            {
                account.Deposit(absoluteAmount, _timeProvider);
            }
        }

        await _accountRepository.SaveAsync(account, cancellationToken);

        // 6. Return every row, imported or not, with the reason. Nothing is discarded silently.
        return new ImportTransactionsResult(reviewRows);
    }
}

/// <summary>
/// Rejects an <see cref="ImportTransactionsCommand"/> whose target account does not exist, before the
/// pipeline opens a database transaction and before the file is parsed at all.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS VALIDATOR DELIBERATELY DOES NOT DO. Its siblings
/// (<see cref="AddTransactionCommandValidator"/>, <see cref="RecordTransactionCommandValidator"/>,
/// <see cref="TransferFundsCommandValidator"/>) also check that the money's currency matches the
/// account's. This one cannot, and pretending otherwise would be worse than not trying: an import
/// command carries a <see cref="Stream"/> and an <see cref="IImporter"/>, not transactions. The
/// transactions -- and therefore their currencies -- do not exist until
/// <c>Importer.ImportAsync</c> has run, which happens INSIDE the handler. A pre-handler validator has
/// nothing to inspect.
/// </para>
/// <para>
/// Reading the stream here to find out would be worse still: it would consume a forward-only stream
/// the handler is about to need, and it would parse the file twice for the sake of symmetry with the
/// other validators.
/// </para>
/// <para>
/// SO WHERE DOES THE CURRENCY CHECK LIVE? In <see cref="ImportTransactionsCommandHandler"/>, in the
/// narrow window that does exist: after <c>ImportAsync</c> and after deduplication, but strictly
/// before the first <c>SaveAsync</c>. That is the earliest moment at which the currencies are known,
/// and it is still ahead of every write, so the command is rejected rather than rolled back. It
/// throws the same <see cref="RequestValidationException"/> this validator would have thrown, so
/// callers and the UI see one failure shape regardless of which of the two checks fired.
/// </para>
/// <para>
/// What is left for this validator is the one pre-condition that IS knowable up front, and it is
/// worth checking here rather than in the handler: a missing account should not cost the user a file
/// parse, an AI categorisation round trip, or an opened transaction.
/// </para>
/// </remarks>
public sealed class ImportTransactionsCommandValidator : IRequestValidator<ImportTransactionsCommand>
{
    private readonly IAccountRepository _accountRepository;

    /// <summary>
    /// Initialises the validator with the account repository used to resolve the import target.
    /// </summary>
    /// <param name="accountRepository">Repository used to look up the account being imported into.</param>
    public ImportTransactionsCommandValidator(IAccountRepository accountRepository)
    {
        _accountRepository = accountRepository;
    }

    /// <summary>
    /// Checks that the account the file is being imported into exists.
    /// </summary>
    /// <param name="request">The command about to be handled.</param>
    /// <param name="cancellationToken">Token observed while loading the account.</param>
    /// <returns>
    /// An empty list when the account resolves, otherwise a single error against
    /// <see cref="ImportTransactionsCommand.TargetAccountId"/>. Currency errors are not reported here;
    /// see the remarks on this type for why they cannot be, and where they are raised instead.
    /// </returns>
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(
        ImportTransactionsCommand request,
        CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(request.TargetAccountId, cancellationToken);
        if (account == null)
        {
            return new[]
            {
                new ValidationError(
                    nameof(ImportTransactionsCommand.TargetAccountId),
                    $"Account {request.TargetAccountId} was not found."),
            };
        }

        return Array.Empty<ValidationError>();
    }
}
