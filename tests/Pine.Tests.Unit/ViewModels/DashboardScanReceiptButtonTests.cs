using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pine.Core.Security;
using Pine.Tests.Unit.Localization;
using Pine.Ui.Avalonia.Composition;
using Pine.Ui.Avalonia.Localization;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Views;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// The dashboard's "Scan Receipt" button does what it says (post-audit plan item 1.4).
/// </summary>
/// <remarks>
/// <para>
/// WHAT WAS WRONG. The button had no <c>Command</c> binding at all: it rendered, it was clickable, and
/// it did nothing -- for as long as the dashboard has existed. Nothing failed anywhere, because there
/// was nothing to fail; a missing binding is invisible to the compiler, to the view models, and to
/// every test that does not look at the rendered control.
/// </para>
/// <para>
/// So the first test reads the BUTTON, not the view model: the button is the only place the defect
/// existed. The second drives it against the real Windows container and asserts where it lands -- in
/// the shell, with the sidebar lit -- because a command that replaced the whole shell would satisfy the
/// first test and be TD-121 again. The third is the head that cannot scan at all.
/// </para>
/// </remarks>
[Collection("ProcessWideState")]
public class DashboardScanReceiptButtonTests
{
    private static Button ScanReceiptButton(DashboardView view, ILocalizer localizer) =>
        view.GetLogicalDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, localizer["Dashboard.Actions.ScanReceipt"]));

    /// <summary>Runs <paramref name="body"/> on the UI thread with the shared localizer in English.</summary>
    private static Task InEnglish(Action<Localizer> body) => AvaloniaUiThread.On(() =>
    {
        var localizer = Localizer.Instance;
        var previous = localizer.CurrentLanguage;
        localizer.SetLanguage("en");

        try
        {
            body(localizer);
        }
        finally
        {
            localizer.SetLanguage(previous);
        }
    });

    /// <summary>Renders the dashboard a head really builds, and returns its Scan Receipt button.</summary>
    private static (Button Button, ShellViewModel Shell, ServiceProvider Provider) Render(
        PineHeadProfile profile,
        ILocalizer localizer)
    {
        var provider = BuildWithAnOpenVault(profile);
        var shell = provider.GetRequiredService<IViewModelFactory>().CreateShell();
        var dashboard = (DashboardViewModel)shell.CurrentPage;

        var view = new DashboardView { DataContext = dashboard };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();

        // PUMPED MORE THAN ONCE, AND THE REASON IS A REAL FLAKE THIS TEST HAD. Bindings are applied
        // from queued dispatcher jobs, and this page builds a tile list before the header's button is
        // bound. One RunJobs() was enough when the class ran alone and not when the whole suite ran:
        // the three head-profile cases failed on a button whose IsVisible binding had not been applied
        // yet, which reads exactly like the product defect they exist to catch.
        //
        // WHAT THIS DOES AND DOES NOT PROVE (narrowed after an independent review said the first
        // wording overclaimed). A binding that is MISSING never settles, so pumping cannot hide one --
        // re-verified: deleting the Command or IsVisible binding still fails these tests. A binding
        // that is applied WRONG and corrected asynchronously would be invisible here; the reviewer
        // tried to construct that case and could not make it run in the headless session, so it is
        // unproven rather than ruled out. The count is a bound, not a measurement.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        return (ScanReceiptButton(view, localizer), shell, provider);
    }

    /// <summary>The button has a command, and it is enabled where the head can scan.</summary>
    [Fact]
    public Task TheScanReceiptButton_HasACommand() => InEnglish(localizer =>
    {
        var (button, shell, provider) = Render(PineHeadProfile.Windows, localizer);

        using (provider)
        {
            button.IsVisible.Should().BeTrue();
            button.Command.Should().NotBeNull("a button that renders, is clickable and does nothing is the defect");
            button.IsEffectivelyEnabled.Should().BeTrue();
            shell.CurrentPage = null!;
        }
    });

    /// <summary>Pressing it opens the receipt page INSIDE the shell, with the sidebar lit.</summary>
    [Fact]
    public Task PressingIt_OpensTheReceiptPageInTheShell() => InEnglish(localizer =>
    {
        var (button, shell, provider) = Render(PineHeadProfile.Windows, localizer);

        using (provider)
        {
            button.Command!.Execute(button.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            shell.CurrentPage.Should().BeOfType<ScanReceiptViewModel>(
                "the button opens the same page the sidebar's Scan Receipt opens");
            shell.IsScanReceiptActive.Should().BeTrue("the sidebar has to light the page now showing");

            shell.CurrentPage = null!;
        }
    });

    /// <summary>On a head with no receipt pipeline there is no button to press.</summary>
    /// <remarks>
    /// The same rule as the sidebar's two receipt buttons (TD-85): a visible button always has a page
    /// behind it. Both halves are asserted -- hidden AND refused -- because the visibility is what a
    /// user sees and the command's guard is what a keyboard or a future second binding would reach.
    /// </remarks>
    [Theory]
    [InlineData(PineHeadProfile.MacOs)]
    [InlineData(PineHeadProfile.Android)]
    [InlineData(PineHeadProfile.Ios)]
    public Task OnAHeadWithoutReceiptVision_TheButtonIsNotThere(PineHeadProfile profile) => InEnglish(localizer =>
    {
        var (button, shell, provider) = Render(profile, localizer);

        using (provider)
        {
            var dashboard = (DashboardViewModel)shell.CurrentPage;

            button.IsVisible.Should().BeFalse($"{profile} has no receipt pipeline, so the button must not be offered");
            button.Command!.CanExecute(null).Should().BeFalse("and it must refuse to run if something reaches it anyway");
            dashboard.HasReceiptVision.Should().BeFalse();

            shell.CurrentPage = null!;
        }
    });

    /// <summary>The head's real container, with the vault reported open by a substitute.</summary>
    private static ServiceProvider BuildWithAnOpenVault(PineHeadProfile profile)
    {
        var vault = Substitute.For<IVaultDataKeyAccessor>();
        vault.IsUnlocked.Returns(true);
        vault.DbPath.Returns(Path.Combine(Path.GetTempPath(), "pine-scan-receipt-button-tests"));
        vault.Key.Returns(new byte[32]);

        var services = new ServiceCollection();
        services.AddSingleton(vault);
        services.AddPine(profile);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }
}
