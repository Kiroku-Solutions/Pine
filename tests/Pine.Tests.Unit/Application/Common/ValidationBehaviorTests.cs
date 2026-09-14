using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Pine.Application.Common;
using Pine.Application.Common.Behaviors;
using Xunit;

namespace Pine.Tests.Unit.Application.Common;

/// <summary>
/// A request used only to drive <see cref="ValidationBehavior{TRequest, TResponse}"/> in isolation.
/// </summary>
/// <param name="Value">A value the fake handler echoes back.</param>
public sealed record ValidatedProbeRequest(string Value) : IRequest<string>;

/// <summary>
/// A validator that returns a fixed verdict and records the token it was handed.
/// </summary>
public sealed class FakeProbeValidator : IRequestValidator<ValidatedProbeRequest>
{
    private readonly IReadOnlyList<ValidationError> _errors;

    /// <summary>Initializes a new instance of the <see cref="FakeProbeValidator"/> class.</summary>
    /// <param name="errors">The verdict this validator always returns.</param>
    public FakeProbeValidator(params ValidationError[] errors) => _errors = errors;

    /// <summary>Gets the number of times <see cref="ValidateAsync"/> was called.</summary>
    public int CallCount { get; private set; }

    /// <summary>Gets the cancellation token observed on the last call.</summary>
    public CancellationToken ObservedToken { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(
        ValidatedProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        ObservedToken = cancellationToken;
        return Task.FromResult(_errors);
    }
}

/// <summary>
/// <see cref="ValidationBehavior{TRequest, TResponse}"/> in isolation: no database, no MediatR
/// container, no repositories — just the behaviour, hand-built validators and a fake continuation.
/// </summary>
/// <remarks>
/// The integration tests in <c>CurrencyValidationTests</c> prove the wired-up behaviour rejects a
/// real command without touching the vault. These prove the mechanics that make that possible:
/// pass-through when nothing is registered, short-circuit when something fails, aggregation across
/// validators, and honest cancellation propagation. Each of those is a separate way the behaviour
/// could be wrong while the integration tests still passed.
/// </remarks>
public class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WithNoRegisteredValidator_RunsTheHandlerUntouched()
    {
        var handlerRan = false;
        var behavior = new ValidationBehavior<ValidatedProbeRequest, string>([]);

        var result = await behavior.Handle(
            new ValidatedProbeRequest("payload"),
            Next(() => handlerRan = true),
            CancellationToken.None);

        handlerRan.Should().BeTrue("a request with no validator is not a validated request");
        result.Should().Be("handled:payload");
    }

    [Fact]
    public async Task Handle_WhenTheValidatorReportsNoErrors_RunsTheHandler()
    {
        var handlerRan = false;
        var validator = new FakeProbeValidator();
        var behavior = new ValidationBehavior<ValidatedProbeRequest, string>([validator]);

        var result = await behavior.Handle(
            new ValidatedProbeRequest("payload"),
            Next(() => handlerRan = true),
            CancellationToken.None);

        validator.CallCount.Should().Be(1);
        handlerRan.Should().BeTrue();
        result.Should().Be("handled:payload");
    }

    /// <summary>
    /// The whole point of the seam. If the handler still ran, the behaviour would be a logging
    /// decorator rather than a gate, and the command would reach the database anyway.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Handle_WhenTheValidatorReportsErrors_ThrowsAndNeverInvokesTheHandler()
    {
        var handlerRan = false;
        var error = new ValidationError("Currency", "Transaction currency EUR does not match account currency USD.", ValidationText.CurrencyMismatch("EUR", "USD"));
        var behavior = new ValidationBehavior<ValidatedProbeRequest, string>([new FakeProbeValidator(error)]);

        var act = async () => await behavior.Handle(
            new ValidatedProbeRequest("payload"),
            Next(() => handlerRan = true),
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Should().ContainSingle().Which.Should().Be(error);
        thrown.Which.Message.Should().Contain(error.Message);

        handlerRan.Should().BeFalse("a rejected request must never reach its handler");
    }

    /// <summary>
    /// Several validators may apply to one request — a shape check and a database-backed rule, for
    /// instance. All of them run and their findings are reported together, so a caller is not sent
    /// round the loop once per rule.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Handle_WithTwoValidators_RunsBothAndAggregatesTheirErrors()
    {
        var first = new FakeProbeValidator(new ValidationError("Currency", "First problem.", ValidationText.RequestRejected()));
        var second = new FakeProbeValidator(new ValidationError("Amount", "Second problem.", ValidationText.TransferAmountNotPositive()));
        var behavior = new ValidationBehavior<ValidatedProbeRequest, string>([first, second]);

        var act = async () => await behavior.Handle(
            new ValidatedProbeRequest("payload"),
            Next(() => { }),
            CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();

        first.CallCount.Should().Be(1, "a failing first validator must not short-circuit the second");
        second.CallCount.Should().Be(1);
        thrown.Which.Errors.Select(e => e.Message)
            .Should().BeEquivalentTo(["First problem.", "Second problem."]);
    }

    /// <summary>
    /// Validators perform I/O — the currency rule reads the account — so a validator that silently
    /// received <see cref="CancellationToken.None"/> would keep querying a vault whose request the
    /// user has already abandoned.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Handle_PassesTheCancellationTokenToTheValidator()
    {
        using var cancellation = new CancellationTokenSource();
        var validator = new FakeProbeValidator();
        var behavior = new ValidationBehavior<ValidatedProbeRequest, string>([validator]);

        await behavior.Handle(new ValidatedProbeRequest("payload"), Next(() => { }), cancellation.Token);

        validator.ObservedToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// Builds a fake continuation that records that it ran.
    /// </summary>
    /// <param name="onInvoked">Callback fired when the continuation is reached.</param>
    /// <returns>A delegate standing in for the rest of the MediatR pipeline.</returns>
    private static RequestHandlerDelegate<string> Next(Action onInvoked)
        => _ =>
        {
            onInvoked();
            return Task.FromResult("handled:payload");
        };
}
