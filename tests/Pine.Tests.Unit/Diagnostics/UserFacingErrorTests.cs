using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Pine.Application.Common;
using Pine.Core.Common;
using Pine.Core.Security;
using Pine.Ui.Avalonia.Diagnostics;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// The rule that decides what a caught exception is allowed to say on screen (TD-24).
/// </summary>
/// <remarks>
/// <para>
/// These tests are written against the RULE rather than against a list of exception types, because
/// the defect being fixed is not "these eleven messages leak" — it is that the set of exceptions
/// reaching a view model is open, so any list is out of date the moment someone adds a
/// <c>throw</c>. The proposition under test is therefore the closed one: an exception is shown if
/// and only if it opted in. Every test that proves a leak is stopped uses a type this codebase does
/// not contain, so passing cannot depend on the current inventory of exceptions.
/// </para>
/// <para>
/// WHAT THESE TESTS DO NOT PROVE. They do not prove that any particular screen calls this. That is
/// <c>ErrorMessageTaxonomyTests</c>, and the two are kept separate on purpose: a seam that maps
/// correctly while nothing calls it is exactly the shape of a fix that is not one.
/// </para>
/// </remarks>
public class UserFacingErrorTests
{
    private const string Fallback = "The transaction could not be saved. Nothing was changed.";

    /// <summary>
    /// A type this codebase does not contain, carrying exactly the shape of text TD-24 is about: a
    /// filesystem path and a store internal.
    /// </summary>
    private sealed class LeakyException : Exception
    {
        public LeakyException()
            : base(@"SQLite Error 11: database disk image is malformed in C:\Users\camilo\Vaults\Personal\pine.db")
        {
        }
    }

    private sealed class OptedInException : Exception, IUserFacingFailure
    {
        public OptedInException(string userFacing)
            : base("internal diagnostic text that must not be shown")
        {
            UserFacingMessage = userFacing;
        }

        public string UserFacingMessage { get; }
    }

    [Fact]
    public void AnExceptionThatDidNotOptIn_ShowsTheCallSitesSentence_AndNoneOfItsOwn()
    {
        var result = UserFacingError.Describe(new LeakyException(), Fallback);

        result.Should().Be(Fallback);
        result.Should().NotContain(@"C:\Users");
        result.Should().NotContain("disk image");
        result.Should().NotContain("SQLite");
    }

    /// <summary>
    /// The exact string the old code produced, pinned so it cannot come back unnoticed.
    /// </summary>
    [Fact]
    public void TheOldFormatting_IsNotReproduced()
    {
        var exception = new LeakyException();

        UserFacingError.Describe(exception, Fallback)
            .Should().NotBe($"Error: {exception.Message}");
    }

    [Fact]
    public void AnExceptionThatOptedIn_ShowsItsOwnSentence_NotTheFallback()
    {
        var result = UserFacingError.Describe(
            new OptedInException("Your vault is full. Free up disk space."),
            Fallback);

        result.Should().Be("Your vault is full. Free up disk space.");
    }

    /// <summary>
    /// Opting in is trusted; being non-empty is not. An implementer returning nothing would leave the
    /// error area blank, which a user reads as success.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnOptedInExceptionWithNothingToSay_FallsBack(string empty)
    {
        UserFacingError.Describe(new OptedInException(empty), Fallback).Should().Be(Fallback);
    }

    /// <summary>
    /// The exception's own <see cref="Exception.Message"/> is not consulted even when it opted in:
    /// the two properties are separate so that editing a diagnostic string cannot edit the UI.
    /// </summary>
    [Fact]
    public void AnOptedInExceptionsDiagnosticMessage_IsNeverShown()
    {
        UserFacingError.Describe(new OptedInException("A sentence for a person."), Fallback)
            .Should().NotContain("internal diagnostic text");
    }

    [Fact]
    public void ASingleInnerAggregateException_IsUnwrapped_BecauseItIsATransportArtefact()
    {
        var aggregate = new AggregateException(new ConcurrencyConflictException());

        UserFacingError.Describe(aggregate, Fallback)
            .Should().Contain("Reload and try again.");
    }

    /// <summary>
    /// Several failures at once: picking one of them to show would be a fabrication, so none is.
    /// </summary>
    [Fact]
    public void AMultiInnerAggregateException_IsNotUnwrapped()
    {
        var aggregate = new AggregateException(
            new ConcurrencyConflictException(),
            new ConcurrencyConflictException());

        UserFacingError.Describe(aggregate, Fallback).Should().Be(Fallback);
    }

    /// <summary>
    /// A wrapper that did not opt in keeps its own framing. Reaching past it to a nested sentence
    /// would silently overrule the layer that chose to wrap.
    /// </summary>
    [Fact]
    public void AnInnerUserFacingFailure_IsNotReachedThroughANonUserFacingWrapper()
    {
        var wrapped = new InvalidOperationException("wrapper", new ConcurrencyConflictException());

        UserFacingError.Describe(wrapped, Fallback).Should().Be(Fallback);
    }

    [Fact]
    public void ANullException_IsARejectedCallSite_NotABlankScreen()
    {
        var act = () => UserFacingError.Describe(null!, Fallback);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// A blank fallback renders an empty error area, which reads as success. That is a defect in the
    /// call site and is refused loudly rather than shown quietly.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ABlankFallback_IsRefused(string? blank)
    {
        var act = () => UserFacingError.Describe(new LeakyException(), blank!);

        act.Should().Throw<ArgumentException>();
    }

    // ---- The three types that opted in, and what each of them now says -----------------------

    [Fact]
    public void AConcurrencyConflict_ReachesTheUserWithItsReloadInstruction()
    {
        // TD-98: nothing caught this type, so a refused write either arrived as an unhandled
        // exception or was flattened into the call site's sentence. The instruction that RESOLVES
        // the conflict is the whole value of the message, and it now survives the trip to a screen.
        var result = UserFacingError.Describe(
            ConcurrencyConflictException.From(new InvalidOperationException("store detail")),
            Fallback);

        result.Should().Contain("Reload and try again.");
        result.Should().NotContain("store detail");
    }

    [Fact]
    public void AValidationFailure_ShowsTheValidatorsOwnSentences()
    {
        var exception = new RequestValidationException(new[]
        {
            new ValidationError("Amount", "The amount must be greater than zero."),
        });

        UserFacingError.Describe(exception, Fallback)
            .Should().Be("The amount must be greater than zero.");
    }

    /// <summary>
    /// The case the two-property design exists for: every construction site of
    /// <see cref="VaultLockedException"/> writes a message for a log, and none of them is the right
    /// thing to show a person whose session simply timed out.
    /// </summary>
    [Fact]
    public void ALockedVault_ShowsAFixedSentence_NotTheDiagnosticOne()
    {
        var exception = new VaultLockedException("The vault is locked, so no file key can be derived.");

        var result = UserFacingError.Describe(exception, Fallback);

        result.Should().NotContain("file key");
        result.Should().Contain("Unlock the vault");
        result.Should().NotBe(Fallback, "a timed-out session is not a defect and must not read like one");
    }

    // ---- Report: the detail is moved to the log, not deleted --------------------------------

    [Fact]
    public void Report_WritesTheWholeExceptionToTheLog()
    {
        var logger = new RecordingLogger();
        var exception = new LeakyException();

        var shown = UserFacingError.Report(logger, exception, "add-transaction", Fallback);

        shown.Should().Be(Fallback);
        logger.Records.Should().ContainSingle();
        logger.Records[0].Level.Should().Be(LogLevel.Error);
        logger.Records[0].Exception.Should().BeSameAs(exception,
            "refusing to show the detail is only honest if the detail still goes somewhere");
        logger.Records[0].Text.Should().Contain("add-transaction");
    }

    [Fact]
    public void Report_WithoutALogger_StillProducesTheSentence()
    {
        UserFacingError.Report(null, new LeakyException(), "add-transaction", Fallback)
            .Should().Be(Fallback);
    }

    [Fact]
    public void Report_AndDescribe_Agree()
    {
        var exception = ConcurrencyConflictException.From(new InvalidOperationException("x"));

        UserFacingError.Report(new RecordingLogger(), exception, "op", Fallback)
            .Should().Be(UserFacingError.Describe(exception, Fallback));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Text, Exception? Exception)> Records { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Records.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
