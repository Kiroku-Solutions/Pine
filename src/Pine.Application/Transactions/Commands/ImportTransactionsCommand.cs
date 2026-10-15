using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Options;
using Pine.Application.Common;
using Pine.Core.Common;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Core.Accounts;
using Pine.Core.Rules;
using Pine.Core.Ai;
using Pine.Core.Configuration;

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
/// <param name="Reason">
/// The explanation of <paramref name="Disposition"/> a person reads, as a key from
/// <see cref="ImportReviewText"/> — worded by the presentation layer in the active language (TD-139).
/// </param>
public sealed record ImportReviewRow(
    Transaction Transaction,
    ImportRowDisposition Disposition,
    UserFacingMessage Reason)
{
    /// <summary>Gets a value indicating whether this row was written to the vault.</summary>
    public bool WasImported =>
        Disposition is ImportRowDisposition.Imported or ImportRowDisposition.ImportedWithoutKeyCheck;

    /// <summary>
    /// Gets a value indicating whether this row was written with an amount of zero and is waiting for the
    /// user to hide it or keep it (TD-62). Skipped rows are never flagged: they were not written.
    /// </summary>
    public bool IsZeroAmount => WasImported && Transaction.IsZeroAmountImport;
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
    private readonly IRuleRepository _ruleRepository;
    private readonly PineAiOptions _aiOptions;
    private readonly Pine.Application.Settings.IUserPreferences? _preferences;

    /// <summary>Creates the handler.</summary>
    /// <param name="aiOptions">
    /// Carries the confidence threshold below which an AI-predicted category is NOT applied to an
    /// imported row. It was the literal <c>0.3</c> here and, separately, in
    /// <c>CategorizeTransactionQueryHandler</c>; the two are now one value (TD-27).
    /// </param>
    public ImportTransactionsCommandHandler(
        ITransactionRepository transactionRepository,
        IAccountRepository accountRepository,
        TimeProvider timeProvider,
        IAIProvider aiProvider,
        IRuleRepository ruleRepository,
        IOptions<PineAiOptions> aiOptions,
        Pine.Application.Settings.IUserPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(ruleRepository);

        // TD-119: the confidence gate is an in-vault Setting; aiOptions is the read-fallback.
        _preferences = preferences;

        _transactionRepository = transactionRepository;
        _accountRepository = accountRepository;
        _timeProvider = timeProvider;
        _aiProvider = aiProvider;
        _ruleRepository = ruleRepository;
        _aiOptions = aiOptions.Value;
    }

    public async Task<ImportTransactionsResult> Handle(ImportTransactionsCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify account exists
        var account = await _accountRepository.GetByIdAsync(request.TargetAccountId, cancellationToken);
        if (account == null)
        {
            // Same failure shape as ImportTransactionsCommandValidator and the currency guard below.
            // The validator catches this first on the pipeline path; this is the backstop for a
            // caller that bypasses the pipeline, or an account deleted between the two reads.
            throw new RequestValidationException(new[]
            {
                new ValidationError(
                    nameof(ImportTransactionsCommand.TargetAccountId),
                    $"Account {request.TargetAccountId} was not found.",
                    ValidationText.AccountNotFound()),
            });
        }

        // 2. Parse transactions from file
        var transactions = await request.Importer.ImportAsync(account, request.FileStream, request.Profile, cancellationToken);
        
        // 2.5 Currency guard (TD-64). PLACEMENT IS THE POINT: this sits after the importer has produced the
        //    transactions -- their currency does not exist until ImportAsync has run, which is why
        //    ImportTransactionsCommandValidator cannot perform this check -- and STRICTLY BEFORE the
        //    deduplication split and the first SaveAsync below. Ahead of the first write the failure is a rejection; one line
        //    later it would be a rollback, which is correct but wasteful and reports far worse.
        //
        //    This is NOT dead code even though CsvImporter builds every Money from
        //    targetAccount.Balance.Currency and so can never trip it. IImporter is a port, and the
        //    command carries the IImporter INSTANCE as a payload field, so any plugin or future
        //    importer -- an OFX/QIF reader, a bank API adapter, a test double -- can hand back
        //    whatever currency it likes. This guard is the boundary check on that port.
        //
        //    IT INSPECTS EVERY PARSED ROW, NOT ONLY THE ONES ABOUT TO BE SAVED (TD-64). It used to run after
        //    dedup over the surviving rows, so a duplicate in the wrong currency was skipped unreported:
        //    a file rejected on first import looked clean on re-import.
        //
        //    Every mismatch is collected before throwing: an import that reports one bad row per
        //    attempt makes the user re-run the whole file to discover the next one.
        var currencyErrors = new List<ValidationError>();
        var accountCurrency = account.Balance.Currency.Value;
        foreach (var tx in transactions)
        {
            var transactionCurrency = tx.TotalAmount.Currency.Value;
            if (!string.Equals(transactionCurrency, accountCurrency, StringComparison.Ordinal))
            {
                var transactionDate = tx.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                currencyErrors.Add(new ValidationError(
                    nameof(ImportTransactionsCommand.TargetAccountId),
                    $"Imported transaction '{tx.Payee}' dated {transactionDate} is in {transactionCurrency}, but account '{account.Name}' is in {accountCurrency}.",
                    ValidationText.ImportedCurrencyMismatch(tx.Payee, tx.Date, transactionCurrency, account.Name, accountCurrency)));
            }
        }

        if (currencyErrors.Count > 0)
        {
            throw new RequestValidationException(currencyErrors);
        }

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
        //    TD-102: a row also carries the pre-h2 key it would have had, so a file imported before
        //    the scale-independent scheme is still recognised. That key is only ever PROBED, never stored.
        var candidateKeys = transactions
            .SelectMany(tx => new[] { tx.ImportKey, tx.LegacyImportKey })
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
                    ImportReviewText.ImportedWithoutKeyCheck()));
                FlagIfZeroAmount(tx);
                importedTransactions.Add(tx);
                continue;
            }

            if (existingKeyLookup.Contains(key)
                || (!string.IsNullOrEmpty(tx.LegacyImportKey) && existingKeyLookup.Contains(tx.LegacyImportKey)))
            {
                reviewRows.Add(new ImportReviewRow(
                    tx,
                    ImportRowDisposition.SkippedAlreadyInAccount,
                    ImportReviewText.SkippedAlreadyInAccount(account.Name)));
                continue;
            }

            if (!keysSeenInThisBatch.Add(key))
            {
                reviewRows.Add(new ImportReviewRow(
                    tx,
                    ImportRowDisposition.SkippedDuplicateInFile,
                    ImportReviewText.SkippedDuplicateInFile()));
                continue;
            }

            // A zero-amount row is imported like any other (its key was just checked above, so a re-import
            // of the same file still skips it) but tagged, and its reason says why it is singled out.
            var isZeroAmount = FlagIfZeroAmount(tx);
            reviewRows.Add(new ImportReviewRow(
                tx,
                ImportRowDisposition.Imported,
                isZeroAmount ? ImportReviewText.ImportedZeroAmount() : ImportReviewText.Imported()));
            importedTransactions.Add(tx);
        }


        // 4.5 The user's rules, read once for the whole run rather than once per row. They are
        //     evaluated in memory because RuleEvaluator is pure: it takes rules and values and
        //     returns a match, which is what makes `31-Rule-Engine` §6.2's idempotence checkable.
        var rules = await _ruleRepository.GetEnabledAsync(cancellationToken);

        // 5. Save each unique transaction and update account balance
        var confidenceThreshold = _preferences is null
            ? _aiOptions.CategorisationConfidenceThreshold
            : (await _preferences.GetAsync(cancellationToken)).CategorisationConfidenceThreshold;

        foreach (var tx in importedTransactions)
        {
            // Categorise, if the file itself supplied no category.
            if (tx.Splits.Count == 0)
            {
                // A RULE BEATS THE CLASSIFIER, ALWAYS, AND THE ORDER IS THE DECISION. A rule is an
                // instruction the user wrote down; the categoriser is a statistical guess with a
                // confidence threshold. When a rule matches, the model is not consulted at all --
                // not compared against, not used as a tie-break. Comparing a rule's priority with a
                // model's confidence would mix two scales that share no units and would make "why
                // did this end up in Coffee?" unanswerable.
                var match = RuleEvaluator.FirstMatch(
                    rules,
                    new RuleInput(tx.Payee ?? string.Empty, tx.Notes ?? string.Empty, tx.TotalAmount.Amount));

                if (match is not null)
                {
                    // The rule's title is the split's note, so the ledger records WHY it was filed
                    // here. "AI" appears in that slot for the classifier's guesses for the same
                    // reason.
                    tx.ReplaceSplits(new[] { new SplitDefinition(tx.TotalAmount, match.Category, match.Title) });
                    await _transactionRepository.SaveAsync(tx, cancellationToken);
                    ApplyToBalance(account, tx);
                    continue;
                }

                string transactionText = Pine.Application.Services.CategorisationText.Build(tx.Payee, tx.Notes);
                var aiResult = await _aiProvider.CategorizeTransactionAsync(transactionText, tx.TotalAmount.Amount, cancellationToken);
                
                // TD-32: this used to resolve `aiResult.CategoryId` through `ICategoryRepository`
                // and apply the split only `if (category != null)`. Nothing in this application has
                // ever written a row to `Categories`, so that lookup always returned null and the
                // import path silently applied no AI category at all, however confident the model.
                if (!string.IsNullOrWhiteSpace(aiResult.Category)
                    && aiResult.ConfidenceScore >= confidenceThreshold)
                {
                    // Set (not append) the category: a repeated categorization must not add a second split.
                    tx.ReplaceSplits(new[] { new SplitDefinition(tx.TotalAmount, aiResult.Category.Trim(), "AI") });
                }
            }

            await _transactionRepository.SaveAsync(tx, cancellationToken);
            ApplyToBalance(account, tx);
        }

        await _accountRepository.SaveAsync(account, cancellationToken);

        // 6. Return every row, imported or not, with the reason. Nothing is discarded silently.
        return new ImportTransactionsResult(reviewRows);
    }

    /// <summary>
    /// Tags a zero-amount row as such (TD-62, owner decision: import it, do not drop or reject it).
    /// </summary>
    private static bool FlagIfZeroAmount(Transaction tx)
    {
        if (tx.TotalAmount.Amount != 0m)
        {
            return false;
        }

        tx.FlagAsZeroAmountImport();
        return true;
    }

    /// <summary>
    /// Applies one imported transaction to the account balance.
    /// </summary>
    /// <remarks>
    /// Extracted when the rule branch gained its own early <c>continue</c>: two copies of a
    /// deposit-or-withdraw would be two places to get a sign wrong.
    /// </remarks>
    private void ApplyToBalance(Account account, Transaction tx)
    {
        // A zero-amount import moves nothing. Account.Deposit/Withdraw stay strictly positive (TD-62), so
        // the handler skips the call rather than the account being taught to accept a zero.
        if (tx.TotalAmount.Amount == 0m)
        {
            return;
        }

        var absoluteAmount = new Pine.Core.Common.ValueObjects.Money(
            Math.Abs(tx.TotalAmount.Amount), tx.TotalAmount.Currency);

        if (tx.TotalAmount.Amount < 0)
        {
            account.Withdraw(absoluteAmount, _timeProvider);
        }
        else
        {
            account.Deposit(absoluteAmount, _timeProvider);
        }
    }
}

/// <summary>
/// Rejects an <see cref="ImportTransactionsCommand"/> whose target account does not exist, before the
/// pipeline opens a database transaction and before the file is parsed at all.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS VALIDATOR DELIBERATELY DOES NOT DO. Its siblings
/// (<see cref="AddTransactionCommandValidator"/>,
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
        var errors = new List<ValidationError>();

        var account = await _accountRepository.GetByIdAsync(request.TargetAccountId, cancellationToken);
        if (account == null)
        {
            errors.Add(new ValidationError(
                nameof(ImportTransactionsCommand.TargetAccountId),
                $"Account {request.TargetAccountId} was not found.",
                ValidationText.AccountNotFound()));
        }

        // TD-50: refuse a file no parser should be handed, before any parser is.
        var fileError = ImportFileGuard.Inspect(request.FileStream);
        if (fileError is not null)
        {
            errors.Add(fileError);
        }

        return errors;
    }
}

/// <summary>
/// The pre-parse check on an import file: a size cap and a content (magic-byte) check (TD-50).
/// </summary>
/// <remarks>
/// <para>
/// WHY: the picked file went straight to the CSV and Excel parsers with no size limit and no check that
/// it was the kind of file its name claimed. Those parsers have real CVE histories, and a hostile or
/// merely wrong file (a 4 GB export, a renamed executable) should be refused by us, cleanly and in the
/// user's language, before it reaches them.
/// </para>
/// <para>
/// CONTENT, NOT EXTENSION. A spreadsheet must open with the zip signature (<c>.xlsx</c>) or the OLE2
/// signature (<c>.xls</c>). Anything else is accepted only as delimited text: no NUL byte in the first
/// <see cref="SniffBytes"/> bytes (a UTF-16 byte-order mark is the one legitimate exception), and not one
/// of the well-known binary signatures that happen to contain none (PDF, PNG, JPEG, GIF, Windows
/// executables). Empty files are left to the importer, whose behavior on them is unchanged.
/// </para>
/// <para>
/// A STREAM THAT CANNOT SEEK IS NOT INSPECTED, because peeking would consume bytes the parser needs.
/// The wizard always passes a <c>FileStream</c>, which can seek; the unchecked case is a caller that
/// supplies something else, and it is no worse off than before this guard existed. A seekable stream is
/// restored to its original position.
/// </para>
/// </remarks>
public static class ImportFileGuard
{
    /// <summary>The largest import file accepted, in megabytes.</summary>
    public const int MaximumMegabytes = 50;

    /// <summary>The number of leading bytes inspected for the text check.</summary>
    public const int SniffBytes = 4096;

    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole2Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private static readonly byte[][] RejectedSignatures =
    [
        [0x25, 0x50, 0x44, 0x46],
        [0x89, 0x50, 0x4E, 0x47],
        [0xFF, 0xD8, 0xFF],
        [0x47, 0x49, 0x46, 0x38],
        [0x4D, 0x5A],
    ];

    /// <summary>Checks the stream and returns the refusal, or null when the file may be parsed.</summary>
    /// <param name="stream">The picked file's stream.</param>
    /// <returns>A single error against <see cref="ImportTransactionsCommand.FileStream"/>, or null.</returns>
    public static ValidationError? Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            return null;
        }

        var property = nameof(ImportTransactionsCommand.FileStream);
        var maximumBytes = (long)MaximumMegabytes * 1024 * 1024;

        if (stream.Length > maximumBytes)
        {
            return new ValidationError(
                property,
                $"The import file is {stream.Length} bytes, over the {MaximumMegabytes} MB limit.",
                ValidationText.ImportFileTooLarge(MaximumMegabytes));
        }

        var original = stream.Position;
        var head = new byte[SniffBytes];
        var count = 0;
        try
        {
            stream.Position = 0;
            int read;
            while (count < head.Length && (read = stream.Read(head, count, head.Length - count)) > 0)
            {
                count += read;
            }
        }
        finally
        {
            stream.Position = original;
        }

        if (count == 0 || StartsWith(head, count, ZipSignature) || StartsWith(head, count, Ole2Signature))
        {
            return null;
        }

        var utf16 = count >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF));
        var looksBinary = RejectedSignatures.Any(signature => StartsWith(head, count, signature))
            || (!utf16 && Array.IndexOf(head, (byte)0, 0, count) >= 0);

        return looksBinary
            ? new ValidationError(
                property,
                "The import file is neither a spreadsheet nor delimited text.",
                ValidationText.ImportFileTypeNotSupported())
            : null;
    }

    private static bool StartsWith(byte[] head, int count, byte[] signature)
        => count >= signature.Length && head.AsSpan(0, signature.Length).SequenceEqual(signature);
}
