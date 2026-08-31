using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Infrastructure.Importing;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;

namespace Pine.Ui.Avalonia.ViewModels.Importing;

public partial class ImportWizardViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly Action _onClose;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportWizardViewModel>? _logger;

    private int _stepIndex;
    public int StepIndex
    {
        get => _stepIndex;
        set
        {
            if (SetProperty(ref _stepIndex, value))
            {
                OnPropertyChanged(nameof(IsStep0));
                OnPropertyChanged(nameof(IsStep1));
                OnPropertyChanged(nameof(IsStep2));
                OnPropertyChanged(nameof(IsStep3));
            }
        }
    }

    public bool IsStep0 => StepIndex == 0;
    public bool IsStep1 => StepIndex == 1;
    public bool IsStep2 => StepIndex == 2;
    public bool IsStep3 => StepIndex == 3;

    // Step 0: Select File & Account
    [ObservableProperty]
    public partial string? SelectedFilePath { get; set; }

    [ObservableProperty]
    public partial string? SelectedFileName { get; set; }

    public ObservableCollection<Account> Accounts { get; } = new();

    [ObservableProperty]
    public partial Account? SelectedAccount { get; set; }

    // Step 1: Mapping
    [ObservableProperty]
    public partial string DateColumn { get; set; } = "Date";
    [ObservableProperty]
    public partial string PayeeColumn { get; set; } = "Payee";
    [ObservableProperty]
    public partial string AmountColumn { get; set; } = "Amount";
    [ObservableProperty]
    public partial string CategoryColumn { get; set; } = "Category";

    [ObservableProperty]
    public partial bool HasHeaders { get; set; } = true;
    [ObservableProperty]
    public partial bool InvertAmounts { get; set; } = false;

    public ObservableCollection<string> AvailableColumns { get; } = new();

    // Step 2: Preview
    public ObservableCollection<Transaction> PreviewTransactions { get; } = new();

    [ObservableProperty]
    public partial bool IsExecuting { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    // Step 3: Review.
    //
    // WHY THIS STEP EXISTS: the import handler decides, per row, whether to save it, and a skipped row is
    // a row the user's file contained but their vault will not. Before TD-03 that decision was made and
    // then thrown away -- the wizard said "Import successful!" and closed itself 1.5 seconds later,
    // whichever way the decisions had gone. A rule the user cannot see is a rule they cannot trust, so
    // every parsed row now surfaces here with the handler's own explanation attached.

    /// <summary>Gets every parsed row of the last import, in file order, with what happened to it.</summary>
    public ObservableCollection<ImportReviewRowViewModel> ReviewRows { get; } = new();

    /// <summary>Gets or sets a one-line, count-accurate statement of what the import did.</summary>
    [ObservableProperty]
    public partial string ReviewHeadline { get; set; } = "";

    [ObservableProperty]
    public partial int TotalRowCount { get; set; }

    [ObservableProperty]
    public partial int ImportedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of rows that were saved but could NOT be duplicate-checked, because their
    /// importer supplied no import key. Broken out of <see cref="ImportedCount"/> deliberately: folding
    /// them into one "imported" total would present an unverified claim as a verified one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUncheckedRows))]
    public partial int UncheckedCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkippedRows))]
    public partial int SkippedCount { get; set; }

    /// <summary>Gets or sets how many rows the target account already held from an earlier import.</summary>
    [ObservableProperty]
    public partial int SkippedAlreadyInAccountCount { get; set; }

    /// <summary>Gets or sets how many rows repeated an earlier row of the same file.</summary>
    [ObservableProperty]
    public partial int SkippedDuplicateInFileCount { get; set; }

    public bool HasSkippedRows => SkippedCount > 0;

    public bool HasUncheckedRows => UncheckedCount > 0;

    public ImportWizardViewModel(
        IMediator mediator,
        TimeProvider timeProvider,
        Action onClose,
        ILogger<ImportWizardViewModel>? logger = null)
    {
        _mediator = mediator;
        _timeProvider = timeProvider;
        _onClose = onClose;
        _logger = logger;
        _ = LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        var accounts = await _mediator.Send(new GetAccountsQuery());
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }
        SelectedAccount = Accounts.FirstOrDefault();
    }

    public void OnFileSelected(string filePath, string fileName)
    {
        SelectedFilePath = filePath;
        SelectedFileName = fileName;
        
        // Try to read headers if it's a CSV
        if (Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            ReadCsvHeaders(filePath);
        }
    }

    private void ReadCsvHeaders(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var headerLine = reader.ReadLine();
            if (headerLine != null)
            {
                var headers = headerLine.Split(',');
                AvailableColumns.Clear();
                foreach (var h in headers)
                {
                    AvailableColumns.Add(h.Trim());
                }

                // Try auto-guessing
                DateColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Date", StringComparison.OrdinalIgnoreCase)) ?? AvailableColumns.FirstOrDefault() ?? "";
                PayeeColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Payee", StringComparison.OrdinalIgnoreCase) || c.Contains("Description", StringComparison.OrdinalIgnoreCase)) ?? "";
                AmountColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Amount", StringComparison.OrdinalIgnoreCase)) ?? "";
                CategoryColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Category", StringComparison.OrdinalIgnoreCase)) ?? "";
            }
        }
        catch (Exception ex)
        {
            // WAS: "Failed to read headers: " + ex.Message (TD-24). This path opens a file the user
            // chose, so its exceptions are precisely the ones that name a filesystem path -- and the
            // user already knows which file they picked, so the path adds nothing they did not have
            // and everything an over-the-shoulder reader did not.
            StatusMessage = UserFacingError.Report(
                _logger,
                ex,
                "import-read-headers",
                "That file's columns could not be read. Check that it is a CSV file and try again.");
        }
    }

    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (StepIndex == 0)
        {
            if (string.IsNullOrEmpty(SelectedFilePath) || SelectedAccount == null)
            {
                StatusMessage = "Please select a file and an account first.";
                return;
            }
            StatusMessage = "";
            StepIndex = 1;
        }
        else if (StepIndex == 1)
        {
            await GeneratePreviewAsync();
            StepIndex = 2;
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (StepIndex > 0)
        {
            StepIndex--;
        }
    }

    private async Task GeneratePreviewAsync()
    {
        if (SelectedAccount == null || string.IsNullOrEmpty(SelectedFilePath)) return;

        IsExecuting = true;
        StatusMessage = "Generating preview...";
        PreviewTransactions.Clear();

        try
        {
            // Build the profile
            var profile = ImportProfile.Create("Temp Profile", _timeProvider);
            
            var mappings = new Dictionary<string, string>
            {
                { "Date", DateColumn },
                { "Payee", PayeeColumn },
                { "Amount", AmountColumn },
                { "Category", CategoryColumn }
            };
            
            profile.UpdateSettings("yyyy-MM-dd", ',', HasHeaders, InvertAmounts, mappings, _timeProvider);

            // Run importer on the file (without saving to DB)
            var importer = new CsvImporter(_timeProvider);
            using var stream = File.OpenRead(SelectedFilePath);
            
            var transactions = await importer.ImportAsync(SelectedAccount, stream, profile);

            foreach (var tx in transactions.Take(50)) // Preview up to 50
            {
                // Let AI categorize if the CSV didn't provide a category
                if (tx.Splits.Count == 0)
                {
                    string transactionText = $"{tx.Payee} {tx.Notes}".Trim();
                    var categoryName = await _mediator.Send(new Pine.Application.Transactions.Queries.CategorizeTransactionQuery(transactionText, tx.TotalAmount.Amount));
                    
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        // Set (not append) the category: a repeated preview must not add a second split.
                        tx.ReplaceSplits(new[] { new SplitDefinition(tx.TotalAmount, categoryName, "AI") });
                    }
                }

                PreviewTransactions.Add(tx);
            }
            
            StatusMessage = $"Previewing {PreviewTransactions.Count} transactions (out of {transactions.Count}).";
        }
        catch (Exception ex)
        {
            // WAS: $"Preview error: {ex.Message}" (TD-24). Nothing is written on the preview path,
            // so the sentence says so: a user who reads "preview error" has no way to know whether
            // the wizard has already started touching their ledger.
            StatusMessage = UserFacingError.Report(
                _logger,
                ex,
                "import-preview",
                "The preview could not be built from that file and column mapping. Nothing was imported.");
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteImportAsync()
    {
        if (SelectedAccount == null || string.IsNullOrEmpty(SelectedFilePath)) return;

        IsExecuting = true;
        StatusMessage = "Importing transactions...";

        try
        {
            var profile = ImportProfile.Create("Final Profile", _timeProvider);
            var mappings = new Dictionary<string, string>
            {
                { "Date", DateColumn },
                { "Payee", PayeeColumn },
                { "Amount", AmountColumn },
                { "Category", CategoryColumn }
            };
            profile.UpdateSettings("yyyy-MM-dd", ',', HasHeaders, InvertAmounts, mappings, _timeProvider);

            var importer = new CsvImporter(_timeProvider);
            using var stream = File.OpenRead(SelectedFilePath);

            var command = new ImportTransactionsCommand(SelectedAccount.Id, stream, importer, profile);
            var result = await _mediator.Send(command);

            ApplyImportResult(result, SelectedAccount.Name);

            // Auto-close ONLY when the review step would have nothing to add: every parsed row was saved,
            // and every one of them was actually checked against the account first. Anything else -- a
            // skipped row, or a row saved without a duplicate check -- is a claim about the user's money
            // that they have to be able to read, so the wizard stops on the review step and waits for them.
            if (!HasSkippedRows && !HasUncheckedRows)
            {
                StatusMessage = ReviewHeadline;
                await Task.Delay(1500); // Give user time to see success message
                _onClose?.Invoke();
                return;
            }

            // The review pane states the outcome in full; a second copy in the header would only compete.
            StatusMessage = "";
            StepIndex = 3;
        }
        catch (Exception ex)
        {
            // WAS: $"Import failed: {ex.Message}" (TD-24). "No transactions were imported" is a
            // claim, and it is true because ImportTransactionsCommand runs inside TransactionBehavior's
            // single database transaction: a throw anywhere in the handler rolls the whole batch back.
            // If that ever stops being true, this sentence becomes a lie and must change with it.
            StatusMessage = UserFacingError.Report(
                _logger,
                ex,
                "import-execute",
                "The import failed and was stopped. No transactions were imported.");
        }
        finally
        {
            IsExecuting = false;
        }
    }

    /// <summary>
    /// Projects the handler's per-row decisions onto the review step.
    /// </summary>
    /// <param name="result">The outcome returned by <see cref="ImportTransactionsCommand"/>.</param>
    /// <param name="accountName">Name of the account imported into, used in the headline.</param>
    /// <remarks>
    /// This counts and orders; it does not judge. Every explanatory sentence shown to the user is the
    /// <see cref="ImportReviewRow.Reason"/> the handler already wrote, carried through untouched. Composing
    /// a second set of sentences here would put the deduplication rules in two places, and the copy in the
    /// UI would be the one that silently drifts out of step with the rule it describes.
    /// </remarks>
    private void ApplyImportResult(ImportTransactionsResult result, string accountName)
    {
        ReviewRows.Clear();

        var uncheckedRows = 0;
        var alreadyInAccount = 0;
        var duplicateInFile = 0;

        foreach (var row in result.Rows)
        {
            ReviewRows.Add(new ImportReviewRowViewModel(row));

            switch (row.Disposition)
            {
                case ImportRowDisposition.ImportedWithoutKeyCheck:
                    uncheckedRows++;
                    break;
                case ImportRowDisposition.SkippedAlreadyInAccount:
                    alreadyInAccount++;
                    break;
                case ImportRowDisposition.SkippedDuplicateInFile:
                    duplicateInFile++;
                    break;
                default:
                    break;
            }
        }

        TotalRowCount = result.Rows.Count;
        ImportedCount = result.Imported.Count;
        SkippedCount = result.Skipped.Count;
        UncheckedCount = uncheckedRows;
        SkippedAlreadyInAccountCount = alreadyInAccount;
        SkippedDuplicateInFileCount = duplicateInFile;

        var rowWord = TotalRowCount == 1 ? "row" : "rows";
        ReviewHeadline = $"Imported {ImportedCount} of {TotalRowCount} {rowWord} into '{accountName}'.";
    }

    [RelayCommand]
    private void Cancel()
    {
        _onClose?.Invoke();
    }

    /// <summary>
    /// Closes the wizard from the review step. Distinct from <see cref="CancelCommand"/> only in what it
    /// means: by this point the import has already been written, so there is nothing left to cancel.
    /// </summary>
    [RelayCommand]
    private void Finish()
    {
        _onClose?.Invoke();
    }
}

/// <summary>
/// One row of the import review list: the parsed transaction, and flags describing its outcome.
/// </summary>
/// <remarks>
/// Deliberately exposes booleans and nothing else derived. The outcome's explanation is
/// <see cref="Reason"/>, written by the import handler and passed through verbatim; the flags exist only so
/// the view can tint a row and pick a short status chip, which needs the shape of the decision rather than
/// a restatement of it.
/// </remarks>
public sealed class ImportReviewRowViewModel
{
    private readonly ImportReviewRow _row;

    /// <summary>Initialises the row from one decision returned by the import handler.</summary>
    /// <param name="row">The parsed row and its disposition. Never null.</param>
    public ImportReviewRowViewModel(ImportReviewRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _row = row;
    }

    /// <summary>Gets the parsed transaction, whether or not it was saved.</summary>
    public Transaction Transaction => _row.Transaction;

    /// <summary>Gets the handler's user-facing explanation of this row's outcome.</summary>
    public string Reason => _row.Reason;

    /// <summary>Gets a value indicating whether this row was written to the vault.</summary>
    public bool WasImported => _row.WasImported;

    /// <summary>Gets a value indicating whether this row was not written to the vault.</summary>
    public bool WasSkipped => !_row.WasImported;

    /// <summary>Gets a value indicating whether this row was saved without any duplicate check.</summary>
    public bool WasImportedWithoutKeyCheck =>
        _row.Disposition == ImportRowDisposition.ImportedWithoutKeyCheck;

    /// <summary>Gets a value indicating whether this row was saved after passing a duplicate check.</summary>
    public bool WasImportedAndChecked => _row.Disposition == ImportRowDisposition.Imported;
}
