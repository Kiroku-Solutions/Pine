using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pine.Application.Settings.Commands;
using Pine.Application.Settings.Queries;
using Pine.Ui.Avalonia.Services;
using Pine.Ui.Avalonia.ViewModels;
using Pine.Ui.Avalonia.Utils;
using Xunit;

namespace Pine.Tests.Unit.ViewModels;

/// <summary>
/// Construction no longer starts work, and a failed load no longer disappears (TD-35).
/// </summary>
/// <remarks>
/// <para>
/// Nine view-model constructors used to end in <c>_ = LoadSomethingAsync();</c>. That produced two
/// distinct defects, and these tests separate them because the fixes are different. The first is
/// the unobserved exception — covered by <c>ObservedTaskTests</c> and by the settings test below.
/// The second is the init race: a constructor that has returned while its object is still filling
/// itself in, so "constructed" and "ready" are different states that nothing distinguished.
/// </para>
/// </remarks>
public class AsyncInitialisationTests
{
    /// <summary>
    /// The settings page silently stopped saving anything for the rest of the session if any of
    /// three unguarded reads threw. This is that defect, pinned.
    /// </summary>
    /// <remarks>
    /// <c>_isInitializing</c> starts <see langword="true"/> so that applying a stored value does not
    /// write it straight back. It used to be cleared on the last line of the happy path, after three
    /// awaited queries with no <c>catch</c>. Every one of the three change handlers opens with
    /// <c>if (_isInitializing) return;</c>, so a single failed read latched the page into a state
    /// where it still APPLIED every change — <c>ThemeManager.Apply*</c> runs first — and saved none
    /// of them. The user saw the new theme and lost it at the next launch, with nothing said.
    /// </remarks>
    [Fact]
    public async Task WhenReadingTheSavedSettingsFails_TheSettingsPageStillSavesLaterChanges()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetSettingQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("the settings table is unreadable"));

        var viewModel = new SettingsViewModel(sender);

        // The failure happens here, inside initialisation, exactly as it would at runtime.
        await InitialiseAndSwallowAsync(viewModel);

        // The LANGUAGE index rather than the theme index: the theme handler calls
        // ThemeManager.ApplyVariant, which touches Avalonia objects and demands the UI thread, and
        // that would make this a test about dispatcher affinity instead of about the latch. The
        // language handler is guarded by the same _isInitializing flag and does nothing else.
        viewModel.SelectedLanguageIndex = 1;

        await WaitForAsync(() => sender.ReceivedCalls().Any(
            call => call.GetArguments()[0] is SaveSettingCommand));

        await sender.Received().Send(
            Arg.Is<SaveSettingCommand>(command => command.Key == SettingsViewModel.LanguageSettingKey),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A view model is inert until it is initialised: constructing it starts no work.
    /// </summary>
    /// <remarks>
    /// This is the falsifiable half of "constructors stopped doing I/O". If a constructor started
    /// the load again, the mediator would have been asked for something before
    /// <c>InitializeAsync</c> was ever called, and this fails.
    /// </remarks>
    [Fact]
    public void ConstructingAPage_StartsNoWork()
    {
        var mediator = Substitute.For<IMediator>();
        var navigation = Substitute.For<INavigationService>();

        _ = new BudgetsViewModel(mediator, navigation);

        mediator.ReceivedCalls().Should().BeEmpty(
            "construction must leave the object inert -- 'constructed' and 'ready' are different "
            + "states, and the constructor is not allowed to blur them (TD-35).");
    }

    /// <summary>
    /// The declared initialisation is what fills the object in.
    /// </summary>
    [Fact]
    public async Task InitialisingAPage_IsWhatLoadsIt()
    {
        var mediator = Substitute.For<IMediator>();
        var navigation = Substitute.For<INavigationService>();
        var viewModel = new BudgetsViewModel(mediator, navigation);

        await viewModel.InitializeAsync(CancellationToken.None);

        mediator.ReceivedCalls().Should().NotBeEmpty(
            "InitializeAsync is now the only thing that starts the page's load.");
    }

    /// <summary>
    /// A failed initialisation says so, instead of leaving an empty screen.
    /// </summary>
    [Fact]
    public async Task WhenInitialisationFails_ThePageSaysSo()
    {
        var mediator = Substitute.For<IMediator>();
        var navigation = Substitute.For<INavigationService>();
        mediator.Send(Arg.Any<IRequest<object>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("unreadable"));

        var viewModel = new BudgetsViewModel(mediator, navigation);
        var policy = viewModel.InitializationFailurePolicy;

        policy.Report.Should().NotBeNull(
            "a page whose load can fail must have somewhere to put the sentence.");
        policy.Fallback.Should().NotBeNullOrWhiteSpace();

        // Drive the reporting path the way PageActivation does.
        Pine.Ui.Avalonia.Diagnostics.ObservedTask.Run(
            () => throw new InvalidOperationException("unreadable"),
            policy.Logger,
            policy.Operation,
            policy.Fallback,
            policy.Report);

        await WaitForAsync(() => !string.IsNullOrEmpty(viewModel.ErrorMessage));

        viewModel.ErrorMessage.Should().Be(policy.Fallback);
    }

    private static async Task InitialiseAndSwallowAsync(SettingsViewModel viewModel)
    {
        try
        {
            await viewModel.InitializeAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // The caller under test is PageActivation/ObservedTask, which catches. Here the throw is
            // deliberately absorbed so the assertion below is about the LATCH, not the exception.
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }
    }
}
