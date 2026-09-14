using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Pine.Application.Accounts.Queries;
using Pine.Application.Security;
using Pine.Application.Security.Queries;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Tests.Unit.Composition;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.Utils;
using Pine.Ui.Avalonia.ViewModels;
using Xunit;

namespace Pine.Tests.Unit.Localization;

/// <summary>
/// Figures and dates are written in the language the user chose (TD-21).
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS WRONG. The string table followed the chosen language and nothing else did. Every figure and
/// date Avalonia or <c>Money</c> formatted used the operating system's culture, and two view patterns
/// were hard-coded besides — so a Spanish screen on an English machine read "1,500.00" and
/// "Sep 14, 2026", and an English screen on a Spanish machine put Spanish month names into English
/// sentences. The scanner's duplicate warning was invariant on purpose, which made it the one figure on
/// its screen in neither notation.
/// </para>
/// </remarks>
public class NumbersAndDatesFollowTheLanguageTests
{
    private static readonly CultureInfo Ecuador = CultureInfo.GetCultureInfo("es-EC");
    private static readonly CultureInfo UnitedStates = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData("es", "es-EC", "es-EC")] // the region speaks the language: its conventions are kept
    [InlineData("en", "en-GB", "en-GB")]
    [InlineData("es", "en-US", "es-ES")] // it does not: the language's own default region
    [InlineData("en", "es-EC", "en-US")]
    [InlineData("es", "", "es-ES")]      // an invariant machine is no region at all
    [InlineData("fr", "fr-FR", "en-US")] // an unshipped language resolves to English first
    public void TheDisplayCulture_IsTheLanguage_InTheMachinesRegionWhenTheRegionSpeaksIt(string language, string regional, string expected)
    {
        Localizer.DisplayCulture(language, CultureInfo.GetCultureInfo(regional)).Name.Should().Be(expected);
    }

    [Fact]
    public void SwitchingLanguage_SwitchesTheCultureFiguresAreWrittenIn()
    {
        var localizer = new Localizer(UnitedStates);
        localizer.CurrentCulture.Name.Should().Be("en-US");

        var raised = new List<string?>();
        localizer.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        localizer.SetLanguage("es");

        localizer.CurrentCulture.Name.Should().Be("es-ES");
        localizer.Format("ScanReceipt.Error.Unreconciled", 1500.5m, 2m, 3m).Should().Contain("1500,5");
        raised.Should().Contain(nameof(Localizer.CurrentCulture), "a binding to the culture must hear that it changed");
    }

    [Fact]
    public void AMoneyInsideASentence_IsWrittenInTheSentencesCulture_NotTheProcesses()
    {
        var money = new Money(1500.5m, new CurrencyCode("USD"));

        string.Format(Ecuador, "{0}", money).Should().Be("1.500,50 USD");
        string.Format(UnitedStates, "{0}", money).Should().Be("1,500.50 USD");
        money.ToAmountString(Ecuador).Should().Be("1.500,50");
    }

    [Theory]
    [InlineData("en-US", "May 3, 2026")]
    [InlineData("en-GB", "3 May 2026")]
    [InlineData("es-ES", "3 de mayo de 2026")]
    [InlineData("es-EC", "3 de mayo de 2026")]
    public void AProseDate_SpellsTheMonth_InTheCulturesOwnOrder(string culture, string expected)
    {
        var info = CultureInfo.GetCultureInfo(culture);

        new DateTime(2026, 5, 3).ToString(Converters.ProseDatePattern(info), info).Should().Be(expected);
    }

    /// <summary>The converter the views bind reads the process culture, which the heads keep equal to the language.</summary>
    [Fact]
    public void TheProseDateConverter_FormatsInTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = Ecuador;

            Converters.ProseDate.Convert(new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero), typeof(string), null, Ecuador)
                .Should().Be("3 de mayo de 2026");
            Converters.ProseDate.Convert(null, typeof(string), null, Ecuador).Should().Be(string.Empty);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task TheDuplicateWarning_IsWrittenInSpanish_AndFollowsASwitchBack()
    {
        var localizer = new Localizer(Ecuador);
        localizer.SetLanguage("es");
        var (viewModel, mediator) = Scanner(localizer);
        mediator.Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReceiptDuplicateMatch?>(
                new ReceiptDuplicateMatch(Guid.NewGuid(), new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), -1524.5m, "ACME")));

        viewModel.Merchant = "ACME";
        viewModel.Amount = 1524.5m;
        viewModel.Date = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);

        await viewModel.SaveTransactionCommand.ExecuteAsync(null);

        viewModel.DuplicateWarning.Should().Be(
            "Parece un duplicado de 1.524,50 USD a ACME del jueves, 5 de marzo de 2026. Guarda de nuevo para agregarlo de todos modos.");

        localizer.SetLanguage("en");

        viewModel.DuplicateWarning.Should().Be(
            "This looks like a duplicate of 1,524.50 USD to ACME on Thursday, March 5, 2026. Save again to add it anyway.",
            "Ecuador does not speak English, so the sentence moves to English's own conventions with it");

        viewModel.Dispose();
    }

    [Fact]
    public void TheDateAmbiguity_IsWordedAgain_WhenTheLanguageChanges()
    {
        var localizer = new Localizer(Ecuador);
        localizer.SetLanguage("es");
        var (viewModel, _) = Scanner(localizer);
        SetAmbiguity(viewModel, new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));

        viewModel.DateAmbiguityMessage.Should().Be("También podría ser el domingo, 3 de mayo de 2026. Revisa el recibo.");

        var notified = false;
        viewModel.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(ScanReceiptViewModel.DateAmbiguityMessage);
        localizer.SetLanguage("en");

        notified.Should().BeTrue("the property is computed on read, so the screen only re-reads it when told");
        viewModel.DateAmbiguityMessage.Should().Be("This could also be Sunday, May 3, 2026. Check the receipt.");

        viewModel.Dispose();
    }

    [Fact]
    public void ADisposedScanner_StopsListeningToTheLanguage()
    {
        var localizer = new Localizer(UnitedStates);
        var (viewModel, _) = Scanner(localizer);
        viewModel.Dispose();

        var notified = false;
        viewModel.PropertyChanged += (_, _) => notified = true;
        localizer.SetLanguage("es");

        notified.Should().BeFalse("the localizer lives for the whole process and would keep a disposed screen alive");
    }

    /// <summary>
    /// The page the language is switched on re-renders what it had formatted: the backup list and the
    /// balances beside the picker.
    /// </summary>
    [Fact]
    public async Task TheSettingsPage_RewordsItsBackupList_WhenTheLanguageIsSwitchedOnIt()
    {
        var localizer = new Localizer(Ecuador);
        var sender = Substitute.For<ISender>();
        var taken = new DateTimeOffset(2026, 9, 9, 10, 15, 0, TimeSpan.Zero);
        sender.Send(Arg.Any<ListVaultBackupsQuery>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<VaultBackupInfo>)new[]
            {
                new VaultBackupInfo(@"C:ackups.db", taken, VaultBackupReason.Manual, 2_048_000, null),
            });
        using var settings = new SettingsViewModel(sender, localizer, new LanguagePreferenceStore(path: null), null, null);
        await settings.LoadBackupsCommand.ExecuteAsync(null);
        settings.SelectedBackup = settings.Backups[0];

        settings.Backups[0].Size.Should().Be("2,000 KB");
        var englishReason = settings.Backups[0].Reason;

        localizer.SetLanguage("es");

        settings.Backups.Should().ContainSingle();
        settings.Backups[0].Size.Should().Be("2.000 KB", "the figure is re-written in the new language's notation");
        settings.Backups[0].Taken.Should().Be(taken.ToLocalTime().ToString("f", Ecuador));
        settings.Backups[0].Reason.Should().NotBe(englishReason, "the reason is a key, and it is worded again");
        settings.SelectedBackup.Should().BeSameAs(settings.Backups[0], "an open restore panel keeps its backup");
    }

    private static (ScanReceiptViewModel ViewModel, IMediator Mediator) Scanner(ILocalizer localizer)
    {
        var account = Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), TimeProvider.System);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetAccountsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>(new[] { account }));

        var viewModel = new ScanReceiptViewModel(mediator, Substitute.For<IShellNavigator>(), Substitute.For<IViewModelFactory>(), localizer)
        {
            SelectedAccount = account,
        };

        return (viewModel, mediator);
    }

    private static void SetAmbiguity(ScanReceiptViewModel viewModel, DateTimeOffset alternative)
    {
        // Both halves have private setters; the scan that sets them in production needs an OCR pipeline.
        typeof(ScanReceiptViewModel).GetProperty(nameof(ScanReceiptViewModel.AlternativeDate))!.SetValue(viewModel, alternative);
        typeof(ScanReceiptViewModel).GetProperty(nameof(ScanReceiptViewModel.IsDateAmbiguous))!.SetValue(viewModel, true);
    }
}

/// <summary>
/// The process culture follows the localizer once a head attaches it (TD-21).
/// </summary>
/// <remarks>
/// In the process-wide collection: it changes <see cref="CultureInfo.DefaultThreadCurrentCulture"/>, which
/// every test running beside it would read.
/// </remarks>
[Collection("ProcessWideState")]
public class DisplayCultureFollowerTests
{
    [Fact]
    public void TheProcessCulture_FollowsTheLanguage_UntilTheHandleIsDisposed()
    {
        var previousDefault = CultureInfo.DefaultThreadCurrentCulture;
        var previousCurrent = CultureInfo.CurrentCulture;
        var localizer = new Localizer(CultureInfo.GetCultureInfo("es-EC"));

        try
        {
            using (DisplayCultureFollower.Follow(localizer))
            {
                CultureInfo.CurrentCulture.Name.Should().Be("en-US", "applied at once, not only on the first change");

                localizer.SetLanguage("es");

                CultureInfo.CurrentCulture.Name.Should().Be("es-EC");
                CultureInfo.DefaultThreadCurrentCulture!.Name.Should().Be("es-EC", "the thread pool formats too");
                new Money(1500m, new CurrencyCode("USD")).ToString().Should().Be("1.500,00 USD",
                    "Money.ToString, Avalonia's StringFormat and every control format in the process culture");
            }

            localizer.SetLanguage("en");
            CultureInfo.CurrentCulture.Name.Should().Be("es-EC", "a disposed handle no longer follows");
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = previousDefault;
            CultureInfo.CurrentCulture = previousCurrent;
        }
    }

    /// <summary>The heads attach it, and tests — which build the container — do not.</summary>
    [Fact]
    public void OnlyTheUiInitialisation_AttachesIt_NotTheContainerBuild()
    {
        var root = RepositoryRoot();
        var composition = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "src", "Pine.Ui.Avalonia", "Composition", "PineCompositionRoot.cs"));

        var initializeUi = composition.IndexOf("public static void InitializeUi(", StringComparison.Ordinal);
        var follow = composition.IndexOf("DisplayCultureFollower.Follow(", StringComparison.Ordinal);

        initializeUi.Should().BePositive();
        follow.Should().BeGreaterThan(initializeUi, "it is attached inside InitializeUi");
        composition.IndexOf("DisplayCultureFollower.Follow(", follow + 1, StringComparison.Ordinal).Should().Be(-1,
            "attached once; a second call in Build() would switch the culture of every test that builds a container");
    }

    private static string RepositoryRoot()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return directory!.FullName;
    }
}
