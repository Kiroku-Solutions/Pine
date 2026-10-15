using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MediatR;
using Pine.Application.Accounts.Queries;
using Pine.Application.Importing.Commands;
using Pine.Application.Importing.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Microsoft.Extensions.Logging;
using Pine.Ui.Avalonia.Diagnostics;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Messages;

namespace Pine.Ui.Avalonia.ViewModels.Importing;

public partial class ImportWizardViewModel : ObservableObject, IDisposable
{
    private readonly IMediator _mediator;
    private readonly Action _onClose;
    private readonly TimeProvider _timeProvider;
    private readonly IImporterFactory _importerFactory;
    private readonly ILocalizer _localizer;
    private readonly LocalizedMessageSlot _status;
    private readonly LocalizedMessageSlot _headline;
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

    /// <summary>
    /// Gets or sets the date format the mapping step uses to parse the Date column. Was hardcoded to
    /// <c>"yyyy-MM-dd"</c> in both <see cref="GeneratePreviewAsync"/> and
    /// <see cref="ExecuteImportAsync"/> before TD-151 -- a setting <see cref="ImportProfile"/> has
    /// always carried but the wizard never let the user see or change, let alone save.
    /// </summary>
    [ObservableProperty]
    public partial string DateFormat { get; set; } = "yyyy-MM-dd";

    /// <summary>
    /// Gets or sets the column delimiter, as typed text rather than a <see cref="char"/> -- a single
    /// text box is the natural control for it, and <see cref="BuildDelimiter"/> is the one place that
    /// narrows it back to the one character <see cref="ImportProfile.Delimiter"/> stores.
    /// </summary>
    [ObservableProperty]
    public partial string DelimiterText { get; set; } = ",";

    public ObservableCollection<string> AvailableColumns { get; } = new();

    // Saved profiles (TD-151). A profile used to be minted fresh on every import and thrown away --
    // "Temp Profile" for the preview, "Final Profile" for the real write -- so a user mapped the same
    // bank's columns by hand every single time. These three members are the wizard's half of fixing
    // that: a list to choose from, the one currently applied (if any), and the name under which the
    // current mapping would be saved.

    /// <summary>Gets every profile saved in this vault, loaded once when the wizard opens.</summary>
    public ObservableCollection<ImportProfile> AvailableProfiles { get; } = new();

    [ObservableProperty]
    public partial ImportProfile? SelectedProfile { get; set; }

    /// <summary>
    /// Gets or sets the name under which the current mapping will be saved. Pre-filled with the
    /// applied profile's own name when one is applied, so "Save" defaults to updating it rather than
    /// to creating an unnamed duplicate.
    /// </summary>
    [ObservableProperty]
    public partial string ProfileNameToSave { get; set; } = "";

    /// <summary>
    /// Gets or sets the id of the profile the current mapping was loaded from, or <see
    /// langword="null"/> when the mapping has never been applied from -- or already saved as -- a
    /// saved profile. <see cref="SaveProfileAsync"/> reads this to decide whether saving UPDATES that
    /// profile or CREATES a new one.
    /// </summary>
    [ObservableProperty]
    public partial Guid? LoadedProfileId { get; set; }

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

    /// <summary>
    /// Gets or sets how many zero-amount rows were imported and still wait for the user to hide or keep them
    /// (TD-62). Falls as rows are hidden.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasZeroAmountRows))]
    public partial int ZeroAmountCount { get; set; }

    /// <summary>Gets or sets how many rows the target account already held from an earlier import.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkippedAlreadyInAccountSentence))]
    public partial int SkippedAlreadyInAccountCount { get; set; }

    /// <summary>Gets or sets how many rows repeated an earlier row of the same file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkippedDuplicateInFileSentence))]
    public partial int SkippedDuplicateInFileCount { get; set; }

    /// <summary>How many rows the account already held, as a sentence.</summary>
    /// <remarks>
    /// <para>
    /// THIS IS A PROPERTY BECAUSE <c>StringFormat</c> CANNOT TAKE A TRANSLATION. The XAML used to
    /// read <c>StringFormat='{}{0} already in this account'</c>, and the obvious localisation --
    /// <c>StringFormat={loc:Tr Key}</c> -- does not work: <c>StringFormat</c> expects a string and
    /// a markup extension hands back a <c>Binding</c>. Avalonia's compiled bindings caught it at
    /// build time rather than at run time, which is the one place this project would rather find
    /// out.
    /// </para>
    /// <para>
    /// So the sentence is composed here, where a localizer is in reach, and the view binds a plain
    /// string. <c>NotifyPropertyChangedFor</c> on the count keeps the two in step.
    /// </para>
    /// </remarks>
    public string SkippedAlreadyInAccountSentence =>
        _localizer.Format("ImportWizard.Review.AlreadyInAccountFormat", SkippedAlreadyInAccountCount);

    /// <summary>How many rows repeated inside the file, as a sentence.</summary>
    public string SkippedDuplicateInFileSentence =>
        _localizer.Format("ImportWizard.Review.DuplicateInFileFormat", SkippedDuplicateInFileCount);

    public bool HasSkippedRows => SkippedCount > 0;

    public bool HasUncheckedRows => UncheckedCount > 0;

    /// <summary>Gets a value indicating whether any zero-amount row is still waiting for a decision.</summary>
    public bool HasZeroAmountRows => ZeroAmountCount > 0;

    /// <summary>
    /// Creates the wizard.
    /// </summary>
    /// <param name="mediator">The application-layer entry point.</param>
    /// <param name="timeProvider">The head's clock, used to stamp the temporary import profile.</param>
    /// <param name="importerFactory">
    /// Chooses the reader for the file the user picked. Injected rather than constructed (TD-28):
    /// this screen previously named a concrete CSV importer, which both tied the UI assembly to the
    /// infrastructure layer and meant every format the file picker offers -- spreadsheets, PDFs,
    /// photographs -- was parsed as comma-separated text.
    /// </param>
    /// <param name="onClose">Invoked when the wizard is finished with, however it ends.</param>
    /// <param name="logger">
    /// Optional; supplied by the container through <c>PineViewModelFactory</c>. Optional only so a
    /// test can construct the wizard without one.
    /// </param>
    public ImportWizardViewModel(
        IMediator mediator,
        TimeProvider timeProvider,
        IImporterFactory importerFactory,
        ILocalizer localizer,
        Action onClose,
        ILogger<ImportWizardViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _mediator = mediator;
        _timeProvider = timeProvider;
        _importerFactory = importerFactory;
        _localizer = localizer;
        _status = new LocalizedMessageSlot(localizer, text => StatusMessage = text ?? string.Empty);
        _headline = new LocalizedMessageSlot(localizer, text => ReviewHeadline = text ?? string.Empty);
        _onClose = onClose;
        // NOT MOVED ONTO IAsyncInitializable, AND THE REASON IS STRUCTURAL (TD-35). That interface
        // is driven by PageActivation, which only ever sees a page assigned to a navigator's
        // CurrentPage. This view model is an overlay: DashboardViewModel builds it through
        // IViewModelFactory and shows it in place, so navigation never sees it and would never
        // start its initialisation. The load therefore stays here -- what changes is that its
        // failure is now read instead of discarded.
        _logger = logger;
        ObservedTask.Run(
            LoadAccountsAsync,
            _logger,
            "import-accounts-load",
            new UserFacingMessage("ImportWizard.Failure.AccountsLoad"),
            _status.Set);
        ObservedTask.Run(
            LoadProfilesAsync,
            _logger,
            "import-profiles-load",
            new UserFacingMessage("ImportWizard.Failure.ProfilesLoad"),
            _status.Set);
    }

    /// <summary>
    /// The view model's logger, for the code-behind that reports a failure through it (TD-127). A view is built by the XAML loader with no container reach, so it borrows the logger of the view model it is the face of.
    /// </summary>
    internal Microsoft.Extensions.Logging.ILogger? Logger => _logger;

    /// <summary>
    /// Shows <paramref name="messages"/> in the status line, in the active language, and keeps them in
    /// step with a language change. For the view's code-behind, which has no localizer (TD-139).
    /// </summary>
    /// <param name="messages">What <c>ObservedTask</c> produced.</param>
    public void ReportFailure(IReadOnlyList<UserFacingMessage> messages) => _status.Set(messages);

    private async Task LoadAccountsAsync()
    {
        var accounts = await _mediator.Send(new GetAccountsQuery());
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }
        SelectedAccount = Accounts.FirstOrDefault();
    }

    /// <summary>Loads every profile saved in this vault, so the mapping step can offer one (TD-151).</summary>
    private async Task LoadProfilesAsync()
    {
        var profiles = await _mediator.Send(new GetImportProfilesQuery());
        AvailableProfiles.Clear();
        foreach (var profile in profiles)
        {
            AvailableProfiles.Add(profile);
        }
    }

    /// <summary>
    /// Pre-fills the mapping step from a saved profile, so a user who has already mapped this bank's
    /// columns once never has to do it by hand again (TD-151).
    /// </summary>
    /// <param name="profile">The profile to apply. Does nothing when <see langword="null"/>.</param>
    /// <remarks>
    /// Column mappings are applied even when <see cref="AvailableColumns"/> does not yet contain the
    /// saved header -- the file may not have been read yet, or the statement may use slightly
    /// different capitalisation -- so the value is set directly rather than through the combo box's
    /// item list. <see cref="LoadedProfileId"/> and <see cref="ProfileNameToSave"/> are set together so
    /// that <see cref="SaveProfileAsync"/> updates this same profile unless the user changes the name.
    /// </remarks>
    [RelayCommand]
    private void ApplyProfile(ImportProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        DateColumn = profile.ColumnMappings.GetValueOrDefault("Date") ?? DateColumn;
        PayeeColumn = profile.ColumnMappings.GetValueOrDefault("Payee") ?? PayeeColumn;
        AmountColumn = profile.ColumnMappings.GetValueOrDefault("Amount") ?? AmountColumn;
        CategoryColumn = profile.ColumnMappings.GetValueOrDefault("Category") ?? CategoryColumn;
        HasHeaders = profile.HasHeaderRecord;
        InvertAmounts = profile.InvertAmounts;
        DateFormat = profile.DateFormat;
        DelimiterText = profile.Delimiter.ToString();

        LoadedProfileId = profile.Id;
        ProfileNameToSave = profile.Name;

        _status.Set("ImportWizard.Profile.Applied", profile.Name);
    }

    /// <summary>
    /// Saves the mapping step's current settings as a named profile -- creating one, or updating the
    /// profile <see cref="LoadedProfileId"/> names, so re-using and then tweaking a saved profile
    /// updates it rather than silently forking it (TD-151).
    /// </summary>
    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(ProfileNameToSave))
        {
            _status.Set("ImportWizard.Profile.NameRequired");
            return;
        }

        var mappings = new Dictionary<string, string>
        {
            { "Date", DateColumn },
            { "Payee", PayeeColumn },
            { "Amount", AmountColumn },
            { "Category", CategoryColumn }
        };
        var delimiter = BuildDelimiter();

        try
        {
            if (LoadedProfileId is { } id)
            {
                var updated = await _mediator.Send(new UpdateImportProfileCommand(
                    id, DateFormat, delimiter, HasHeaders, InvertAmounts, mappings));

                var index = IndexOfProfile(id);
                if (index >= 0)
                {
                    AvailableProfiles[index] = updated;
                }
                else
                {
                    AvailableProfiles.Add(updated);
                }

                SelectedProfile = updated;
            }
            else
            {
                var created = await _mediator.Send(new CreateImportProfileCommand(
                    ProfileNameToSave.Trim(), DateFormat, delimiter, HasHeaders, InvertAmounts, mappings));

                AvailableProfiles.Add(created);
                LoadedProfileId = created.Id;
                SelectedProfile = created;
            }

            _status.Set("ImportWizard.Profile.Saved", ProfileNameToSave.Trim());
        }
        catch (Exception ex)
        {
            _status.Set(UserFacingError.Report(
                _logger,
                ex,
                "import-save-profile",
                new UserFacingMessage("ImportWizard.Failure.SaveProfile")));
        }
    }

    private int IndexOfProfile(Guid id)
    {
        for (var i = 0; i < AvailableProfiles.Count; i++)
        {
            if (AvailableProfiles[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Narrows <see cref="DelimiterText"/> to the single character <see cref="ImportProfile.Delimiter"/>
    /// stores, falling back to a comma when the box is empty -- never throwing on what a user typed.
    /// </summary>
    private char BuildDelimiter() => string.IsNullOrEmpty(DelimiterText) ? ',' : DelimiterText[0];

    /// <summary>
    /// Builds the <see cref="ImportProfile"/> the importer is run against, for the preview and for
    /// the real write alike.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE IDENTITY RULE THAT MAKES TD-66's FIX WORTH HAVING. When the current mapping was applied
    /// from a saved profile (<see cref="LoadedProfileId"/> set, and still present in
    /// <see cref="AvailableProfiles"/>), that SAME profile instance is reused and only its settings
    /// are refreshed -- its id never changes. Every importer derives the transaction's import key
    /// from the profile's id, so reusing a saved profile reproduces the same keys an earlier import
    /// under it produced, which is exactly what makes duplicate detection work across two imports
    /// separated by a save. Building a fresh profile here instead would mint a new id every time and
    /// silently defeat that, whatever the profile's name happened to be.
    /// </para>
    /// <para>
    /// Otherwise -- no profile applied, or it was removed from the list -- a new, unsaved profile is
    /// created. Its name is cosmetic only (TD-66): it is never read back out of the import key.
    /// </para>
    /// </remarks>
    private ImportProfile BuildWorkingProfile()
    {
        var mappings = new Dictionary<string, string>
        {
            { "Date", DateColumn },
            { "Payee", PayeeColumn },
            { "Amount", AmountColumn },
            { "Category", CategoryColumn }
        };

        var profile = LoadedProfileId is { } id
            ? AvailableProfiles.FirstOrDefault(p => p.Id == id)
            : null;

        profile ??= ImportProfile.Create(
            string.IsNullOrWhiteSpace(ProfileNameToSave) ? _localizer["ImportWizard.Profile.DefaultName"] : ProfileNameToSave,
            _timeProvider);

        profile.UpdateSettings(DateFormat, BuildDelimiter(), HasHeaders, InvertAmounts, mappings, _timeProvider);
        return profile;
    }

    public void OnFileSelected(string filePath, string fileName)
    {
        SelectedFilePath = filePath;
        SelectedFileName = fileName;

        ReadHeaders(filePath, fileName);
    }

    /// <summary>
    /// Populates <see cref="AvailableColumns"/> from the selected file's header row, for whichever
    /// format the import picker offers (TD-123).
    /// </summary>
    /// <remarks>
    /// <para>
    /// WAS: a hardcoded <c>.csv</c> check guarding inline comma-splitting -- <c>ReadCsvHeaders</c> --
    /// so <see cref="AvailableColumns"/> stayed empty for a spreadsheet even after TD-151/TD-66 gave
    /// <c>ExcelImporter</c> a real, column-mapping-aware <c>ImportAsync</c>. The user was asked to
    /// map columns from a list that was never populated, for a format whose importer would have
    /// honoured the mapping if only it had been given one.
    /// </para>
    /// <para>
    /// NOW: the SAME <see cref="IImporterFactory"/> the preview and the real import already use
    /// (TD-28) picks the importer, and header reading is asked of it through
    /// <see cref="IImportHeaderReader"/> -- a capability only the two importers that have a header
    /// row carry. Any format the factory accepts gets this for free the moment its importer
    /// implements the interface; no extension dispatch lives in this view model at all.
    /// </para>
    /// </remarks>
    private void ReadHeaders(string filePath, string fileName)
    {
        try
        {
            var importer = _importerFactory.ForFile(fileName);
            if (importer is not IImportHeaderReader headerReader)
            {
                // No format currently reaches here that lacks the capability -- the picker offers
                // only .csv/.xlsx/.xls (TD-122) and both have one -- but a future importer without
                // one must leave the mapping step's columns empty rather than throw.
                return;
            }

            using var stream = File.OpenRead(filePath);
            var headers = headerReader.ReadHeaders(stream);

            AvailableColumns.Clear();
            foreach (var header in headers)
            {
                AvailableColumns.Add(header);
            }

            // Try auto-guessing
            DateColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Date", StringComparison.OrdinalIgnoreCase)) ?? AvailableColumns.FirstOrDefault() ?? "";
            PayeeColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Payee", StringComparison.OrdinalIgnoreCase) || c.Contains("Description", StringComparison.OrdinalIgnoreCase)) ?? "";
            AmountColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Amount", StringComparison.OrdinalIgnoreCase)) ?? "";
            CategoryColumn = AvailableColumns.FirstOrDefault(c => c.Contains("Category", StringComparison.OrdinalIgnoreCase)) ?? "";
        }
        catch (UnsupportedImportFormatException)
        {
            // The picker already restricts selection to formats the factory recognises (TD-122), so
            // this is not an expected path -- but it is not a reason to show a scary error either:
            // NextStepAsync's own file/account check is what tells the user they picked nothing
            // usable. Leaving AvailableColumns empty is honest here, same as it always was for an
            // unreadable format.
        }
        catch (Exception ex)
        {
            // WAS: "Failed to read headers: " + ex.Message (TD-24). This path opens a file the user
            // chose, so its exceptions are precisely the ones that name a filesystem path -- and the
            // user already knows which file they picked, so the path adds nothing they did not have
            // and everything an over-the-shoulder reader did not.
            _status.Set(UserFacingError.Report(
                _logger,
                ex,
                "import-read-headers",
                new UserFacingMessage("ImportWizard.Failure.ReadHeaders")));
        }
    }

    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (StepIndex == 0)
        {
            if (string.IsNullOrEmpty(SelectedFilePath) || SelectedAccount == null)
            {
                _status.Set("ImportWizard.Status.SelectFileAndAccount");
                return;
            }
            _status.Clear();
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
        _status.Set("ImportWizard.Status.GeneratingPreview");
        PreviewTransactions.Clear();

        try
        {
            var profile = BuildWorkingProfile();

            // Run importer on the file (without saving to DB). The importer is chosen from the
            // file's own extension: a .xlsx or .pdf used to be handed to the CSV reader here, and a
            // format with no importer now throws a sentence rather than producing plausible rubbish.
            var importer = _importerFactory.ForFile(SelectedFilePath);
            using var stream = File.OpenRead(SelectedFilePath);
            
            var transactions = await importer.ImportAsync(SelectedAccount, stream, profile);

            foreach (var tx in transactions.Take(50)) // Preview up to 50
            {
                // Let AI categorize if the CSV didn't provide a category
                if (tx.Splits.Count == 0)
                {
                    string transactionText = Pine.Application.Services.CategorisationText.Build(tx.Payee, tx.Notes);
                    var categoryName = await _mediator.Send(new Pine.Application.Transactions.Queries.CategorizeTransactionQuery(transactionText, tx.TotalAmount.Amount));
                    
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        // Set (not append) the category: a repeated preview must not add a second split.
                        tx.ReplaceSplits(new[] { new SplitDefinition(tx.TotalAmount, categoryName, "AI") });
                    }
                }

                PreviewTransactions.Add(tx);
            }
            
            _status.Set("ImportWizard.Status.PreviewingFormat", PreviewTransactions.Count, transactions.Count);
        }
        catch (Exception ex)
        {
            // WAS: $"Preview error: {ex.Message}" (TD-24). Nothing is written on the preview path,
            // so the sentence says so: a user who reads "preview error" has no way to know whether
            // the wizard has already started touching their ledger.
            _status.Set(UserFacingError.Report(
                _logger,
                ex,
                "import-preview",
                new UserFacingMessage("ImportWizard.Failure.Preview")));
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
        _status.Set("ImportWizard.Status.Importing");

        try
        {
            // TD-151/TD-66: when the current mapping came from a saved profile, BuildWorkingProfile
            // returns THAT profile -- same id, settings refreshed from whatever the user has on
            // screen -- so the import key every importer derives from the profile's id (TD-66) is the
            // one already on file for every earlier import made under it. Building a brand new
            // profile here, as "Final Profile" used to, would have silently broken that continuity on
            // every single import even after TD-66 moved the key off the profile's name.
            var profile = BuildWorkingProfile();

            // Same choice as the preview made, from the same file, so the rows the user approved
            // on the preview step are the rows this writes.
            var importer = _importerFactory.ForFile(SelectedFilePath);
            using var stream = File.OpenRead(SelectedFilePath);

            var command = new ImportTransactionsCommand(SelectedAccount.Id, stream, importer, profile);
            var result = await _mediator.Send(command);

            ApplyImportResult(result, SelectedAccount.Name);

            // THE IMPORT NOW SAYS IT CHANGED THE LEDGER (post-audit plan item 1.3). Every other write
            // path -- add, void, create account, rules, receipt save -- broadcasts DataChangedMessage;
            // this one never did. The dashboard's close callback reloaded a collection no view bound,
            // so after importing a month's statement every tile on the dashboard kept its old figures
            // until the user navigated away and back. Sent before the auto-close, and only when rows
            // were actually written, so a fully-skipped import does not trigger five pointless reads.
            if (ImportedCount > 0)
            {
                WeakReferenceMessenger.Default.Send(new DataChangedMessage());
            }

            // Auto-close ONLY when the review step would have nothing to add: every parsed row was saved,
            // and every one of them was actually checked against the account first. Anything else -- a
            // skipped row, or a row saved without a duplicate check -- is a claim about the user's money
            // that they have to be able to read, so the wizard stops on the review step and waits for them.
            // A zero-amount row is the same kind of claim: it was kept, not judged, and the decision to hide
            // it belongs to the user, so the wizard must not close over it.
            if (!HasSkippedRows && !HasUncheckedRows && !HasZeroAmountRows)
            {
                _status.Set(_headline.Messages);
                await Task.Delay(1500); // Give user time to see success message
                _onClose?.Invoke();
                return;
            }

            // The review pane states the outcome in full; a second copy in the header would only compete.
            _status.Clear();
            StepIndex = 3;
        }
        catch (Exception ex)
        {
            // WAS: $"Import failed: {ex.Message}" (TD-24). "No transactions were imported" is a
            // claim, and it is true because ImportTransactionsCommand runs inside TransactionBehavior's
            // single database transaction: a throw anywhere in the handler rolls the whole batch back.
            // If that ever stops being true, this sentence becomes a lie and must change with it.
            _status.Set(UserFacingError.Report(
                _logger,
                ex,
                "import-execute",
                new UserFacingMessage("ImportWizard.Failure.Execute")));
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
        ClearReviewRows();

        var uncheckedRows = 0;
        var alreadyInAccount = 0;
        var duplicateInFile = 0;
        var zeroAmount = 0;

        foreach (var row in result.Rows)
        {
            ReviewRows.Add(new ImportReviewRowViewModel(row, _localizer));
            if (row.IsZeroAmount)
            {
                zeroAmount++;
            }

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
        ZeroAmountCount = zeroAmount;
        SkippedAlreadyInAccountCount = alreadyInAccount;
        SkippedDuplicateInFileCount = duplicateInFile;

        // Two keys rather than one template with a "row"/"rows" argument: which word agrees with
        // the count is a rule of each language, not of this code (TD-139).
        _headline.Set(
            TotalRowCount == 1 ? "ImportWizard.Review.HeadlineOne" : "ImportWizard.Review.HeadlineMany",
            ImportedCount,
            TotalRowCount,
            accountName);
    }

    /// <summary>Empties the review list, releasing each row's subscription to the localizer.</summary>
    private void ClearReviewRows()
    {
        foreach (var row in ReviewRows)
        {
            row.Dispose();
        }

        ReviewRows.Clear();
    }

    /// <summary>
    /// Hides a zero-amount row the user does not want: voids it, which keeps the row and its import key (so
    /// re-importing the file does not bring it back) but takes it out of the transaction list.
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="VoidTransactionCommand"/> rather than adding a delete. A zero amount has no balance
    /// effect to reverse, and voiding is the one removal the ledger already audits and can undo.
    /// </remarks>
    [RelayCommand]
    private async Task HideZeroAmountRowAsync(ImportReviewRowViewModel? row)
    {
        if (row is null || !row.CanHide)
        {
            return;
        }

        try
        {
            await _mediator.Send(new VoidTransactionCommand(
                row.Transaction.Id,
                _localizer["ImportWizard.Review.HideZeroAmountReason"]));
            row.MarkHidden();
            ZeroAmountCount = Math.Max(0, ZeroAmountCount - 1);
            WeakReferenceMessenger.Default.Send(new DataChangedMessage());
        }
        catch (Exception ex)
        {
            _status.Set(UserFacingError.Report(
                _logger,
                ex,
                "import-hide-zero-amount",
                new UserFacingMessage("ImportWizard.Failure.HideZeroAmount")));
        }
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

    /// <summary>Releases the localized message slot.</summary>
    /// <remarks>
    /// CA1001 again, and the same reason as everywhere else: the slot subscribes to a
    /// process-lifetime singleton, so not releasing it keeps this wizard reachable for the life of
    /// the application.
    /// </remarks>
    public void Dispose()
    {
        _status.Dispose();
        _headline.Dispose();
        ClearReviewRows();
        GC.SuppressFinalize(this);
    }

}

/// <summary>
/// One row of the import review list: the parsed transaction, and flags describing its outcome.
/// </summary>
/// <remarks>
/// Deliberately exposes booleans and nothing else derived. The outcome's explanation is
/// <see cref="Reason"/>, named by the import handler and worded here in the active language; the flags
/// exist only so the view can tint a row and pick a short status chip, which needs the shape of the
/// decision rather than a restatement of it.
/// </remarks>
public sealed partial class ImportReviewRowViewModel : ObservableObject, IDisposable
{
    private readonly ImportReviewRow _row;
    private readonly LocalizedMessageSlot _reason;

    /// <summary>Initialises the row from one decision returned by the import handler.</summary>
    /// <param name="row">The parsed row and its disposition. Never null.</param>
    /// <param name="localizer">Words <see cref="Reason"/>, and re-words it when the language changes.</param>
    public ImportReviewRowViewModel(ImportReviewRow row, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(localizer);
        _row = row;
        _reason = new LocalizedMessageSlot(localizer, text => Reason = text ?? string.Empty);
        _reason.Set(row.Reason);
    }

    /// <summary>Gets the parsed transaction, whether or not it was saved.</summary>
    public Transaction Transaction => _row.Transaction;

    /// <summary>Gets the handler's explanation of this row's outcome, in the active language.</summary>
    [ObservableProperty]
    public partial string Reason { get; private set; } = string.Empty;

    /// <summary>Releases the reason's subscription to the process-lifetime localizer.</summary>
    public void Dispose() => _reason.Dispose();

    /// <summary>Gets a value indicating whether this row was written to the vault.</summary>
    public bool WasImported => _row.WasImported;

    /// <summary>Gets a value indicating whether this row was not written to the vault.</summary>
    public bool WasSkipped => !_row.WasImported;

    /// <summary>Gets a value indicating whether this row was saved without any duplicate check.</summary>
    public bool WasImportedWithoutKeyCheck =>
        _row.Disposition == ImportRowDisposition.ImportedWithoutKeyCheck;

    /// <summary>Gets a value indicating whether this row was saved with an amount of zero (TD-62).</summary>
    public bool IsZeroAmount => _row.IsZeroAmount;

    /// <summary>Gets a value indicating whether the user has hidden (voided) this zero-amount row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanHide))]
    public partial bool IsHidden { get; private set; }

    /// <summary>Gets a value indicating whether the hide action applies: a zero-amount row not yet hidden.</summary>
    public bool CanHide => IsZeroAmount && !IsHidden;

    /// <summary>Records that the row was voided.</summary>
    internal void MarkHidden() => IsHidden = true;

    /// <summary>Gets a value indicating whether this row was saved after passing a duplicate check.</summary>
    public bool WasImportedAndChecked => _row.Disposition == ImportRowDisposition.Imported;
}
