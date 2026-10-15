using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Importing.Commands;
using Pine.Application.Importing.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Accounts;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.ViewModels.Importing;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The wizard's half of TD-151: offering a saved profile instead of a blank mapping step, and
/// saving the mapping back as one -- creating it the first time, updating it on reuse.
/// </summary>
[Collection("ProcessWideState")]
public sealed class ImportWizardProfileTests : IDisposable
{
    private static readonly CurrencyCode Usd = new("USD");

    private readonly string _file = Path.Combine(Path.GetTempPath(), "pine-import-profile-" + Guid.NewGuid().ToString("N") + ".csv");

    public ImportWizardProfileTests()
    {
        File.WriteAllText(_file, "Date,Payee,Amount\n2026-09-01,SHOP,-5\n");
    }

    public void Dispose() => File.Delete(_file);

    private static Account MakeAccount() => Account.Create("Current", AccountType.Bank, new Money(0m, Usd), TimeProvider.System);

    private static IMediator MakeMediator(Account account, IReadOnlyList<ImportProfile> savedProfiles)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));
        mediator.Send(Arg.Any<GetImportProfilesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(savedProfiles));
        return mediator;
    }

    private static ImportWizardViewModel MakeWizard(IMediator mediator, Account account, string file)
    {
        var importers = Substitute.For<IImporterFactory>();
        importers.ForFile(Arg.Any<string>()).Returns(Substitute.For<IImporter>());

        return new ImportWizardViewModel(mediator, TimeProvider.System, importers, new Localizer(), () => { })
        {
            SelectedAccount = account,
            SelectedFilePath = file,
        };
    }

    /// <summary>Waits for the fire-and-forget construction-time loads to settle.</summary>
    private static async Task SettleAsync() => await Task.Delay(50);

    [Fact]
    public async Task OnConstruction_TheWizardLoadsEverySavedProfile()
    {
        var account = MakeAccount();
        var saved = ImportProfile.Create("Chase Checking", TimeProvider.System);
        var mediator = MakeMediator(account, new[] { saved });

        var wizard = MakeWizard(mediator, account, _file);
        await SettleAsync();

        wizard.AvailableProfiles.Should().ContainSingle().Which.Name.Should().Be("Chase Checking");
    }

    [Fact]
    public void ApplyingAProfile_PreFillsTheMappingStep_WithoutAskingTheUserToTypeItAgain()
    {
        var account = MakeAccount();
        var profile = ImportProfile.Create("Chase Checking", TimeProvider.System);
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Date"] = "Posted",
            ["Payee"] = "Merchant",
            ["Amount"] = "Value",
            ["Category"] = "Bucket",
        };
        profile.UpdateSettings("dd/MM/yyyy", ';', hasHeader: false, invertAmounts: true, mappings, TimeProvider.System);

        var mediator = MakeMediator(account, Array.Empty<ImportProfile>());
        var wizard = MakeWizard(mediator, account, _file);

        wizard.ApplyProfileCommand.Execute(profile);

        wizard.DateColumn.Should().Be("Posted");
        wizard.PayeeColumn.Should().Be("Merchant");
        wizard.AmountColumn.Should().Be("Value");
        wizard.CategoryColumn.Should().Be("Bucket");
        wizard.HasHeaders.Should().BeFalse();
        wizard.InvertAmounts.Should().BeTrue();
        wizard.DateFormat.Should().Be("dd/MM/yyyy");
        wizard.DelimiterText.Should().Be(";");
        wizard.LoadedProfileId.Should().Be(profile.Id);
        wizard.ProfileNameToSave.Should().Be("Chase Checking");
    }

    [Fact]
    public void ApplyingNoProfile_DoesNothing()
    {
        var account = MakeAccount();
        var mediator = MakeMediator(account, Array.Empty<ImportProfile>());
        var wizard = MakeWizard(mediator, account, _file);
        var originalDateColumn = wizard.DateColumn;

        wizard.ApplyProfileCommand.Execute(null);

        wizard.DateColumn.Should().Be(originalDateColumn);
        wizard.LoadedProfileId.Should().BeNull();
    }

    [Fact]
    public async Task SavingWithNoLoadedProfile_CreatesANewOne()
    {
        var account = MakeAccount();
        var mediator = MakeMediator(account, Array.Empty<ImportProfile>());
        var created = ImportProfile.Create("Chase Checking", TimeProvider.System);
        mediator.Send(Arg.Any<CreateImportProfileCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(created));

        var wizard = MakeWizard(mediator, account, _file);
        wizard.ProfileNameToSave = "Chase Checking";
        wizard.DateColumn = "Date";
        wizard.AmountColumn = "Amount";

        await wizard.SaveProfileCommand.ExecuteAsync(null);

        await mediator.Received(1).Send(
            Arg.Is<CreateImportProfileCommand>(c => c.Name == "Chase Checking"),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().Send(Arg.Any<UpdateImportProfileCommand>(), Arg.Any<CancellationToken>());

        wizard.AvailableProfiles.Should().Contain(p => p.Id == created.Id);
        wizard.LoadedProfileId.Should().Be(created.Id);
    }

    [Fact]
    public async Task SavingWhileAProfileIsLoaded_UpdatesItInstead_OfCreatingADuplicate()
    {
        var account = MakeAccount();
        var original = ImportProfile.Create("Chase Checking", TimeProvider.System);
        var mediator = MakeMediator(account, new[] { original });

        var updated = ImportProfile.Create("Chase Checking", TimeProvider.System);
        mediator.Send(Arg.Any<UpdateImportProfileCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(updated));

        var wizard = MakeWizard(mediator, account, _file);
        await SettleAsync();

        wizard.ApplyProfileCommand.Execute(original);
        wizard.AmountColumn = "Amount2"; // the user tweaks the mapping after loading it

        await wizard.SaveProfileCommand.ExecuteAsync(null);

        await mediator.Received(1).Send(
            Arg.Is<UpdateImportProfileCommand>(c => c.Id == original.Id),
            Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().Send(Arg.Any<CreateImportProfileCommand>(), Arg.Any<CancellationToken>());

        // Exactly one profile named "Chase Checking" -- the update replaced it in the list rather
        // than appending a second entry under the same name.
        wizard.AvailableProfiles.Should().ContainSingle();
    }

    [Fact]
    public async Task SavingWithNoName_AsksForOne_AndSendsNothing()
    {
        var account = MakeAccount();
        var mediator = MakeMediator(account, Array.Empty<ImportProfile>());
        var wizard = MakeWizard(mediator, account, _file);
        wizard.ProfileNameToSave = "   ";

        await wizard.SaveProfileCommand.ExecuteAsync(null);

        await mediator.DidNotReceive().Send(Arg.Any<CreateImportProfileCommand>(), Arg.Any<CancellationToken>());
        await mediator.DidNotReceive().Send(Arg.Any<UpdateImportProfileCommand>(), Arg.Any<CancellationToken>());
        wizard.StatusMessage.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// TD-66, proven at the wizard: importing with a loaded profile must send the SAME profile id
    /// down <see cref="ImportTransactionsCommand"/> on every import, not a freshly minted one --
    /// otherwise the import key every importer derives from the profile's id would drift across
    /// reuses, and duplicate detection would silently stop working for anyone who ever saves a
    /// profile.
    /// </summary>
    [Fact]
    public async Task ExecutingAnImport_WithALoadedProfile_SendsThatSameProfilesId()
    {
        var account = MakeAccount();
        var saved = ImportProfile.Create("Chase Checking", TimeProvider.System);
        var mediator = MakeMediator(account, new[] { saved });

        ImportProfile? sentProfile = null;
        mediator.Send(Arg.Any<ImportTransactionsCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sentProfile = ((ImportTransactionsCommand)call[0]).Profile;
                return Task.FromResult(new ImportTransactionsResult(Array.Empty<ImportReviewRow>()));
            });

        var wizard = MakeWizard(mediator, account, _file);
        await SettleAsync();

        wizard.ApplyProfileCommand.Execute(saved);
        await wizard.ExecuteImportCommand.ExecuteAsync(null);

        sentProfile.Should().NotBeNull();
        sentProfile!.Id.Should().Be(saved.Id);
    }
}
