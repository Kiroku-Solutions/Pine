using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pine.Application.Common;
using Pine.Core.Common;
using Xunit;

namespace Pine.Tests.Unit.Application.Common;

/// <summary>
/// A trivial request used to prove that <c>Send</c> reaches a handler through the scoping mediator.
/// </summary>
/// <param name="Value">A value echoed back by the handler.</param>
public sealed record ProbeRequest(string Value) : IRequest<string>;

/// <summary>
/// A scoped service resolved by the probe handler; its identity reveals which DI scope ran the request.
/// </summary>
public sealed class ScopeProbe
{
    /// <summary>Gets the identity of this instance.</summary>
    public Guid InstanceId { get; } = Guid.NewGuid();
}

/// <summary>
/// Singleton sink that records the <see cref="ScopeProbe"/> seen by each handler invocation.
/// </summary>
public sealed class ProbeRecorder
{
    private readonly List<Guid> _scopeProbeIds = new();

    /// <summary>Gets the scope probe identities observed, in invocation order.</summary>
    public IReadOnlyList<Guid> ScopeProbeIds => _scopeProbeIds;

    /// <summary>Records one observed scope probe identity.</summary>
    /// <param name="id">The observed identity.</param>
    public void Record(Guid id) => _scopeProbeIds.Add(id);
}

/// <summary>
/// Handler for <see cref="ProbeRequest"/>.
/// </summary>
public sealed class ProbeRequestHandler : IRequestHandler<ProbeRequest, string>
{
    private readonly ScopeProbe _scopeProbe;
    private readonly ProbeRecorder _recorder;

    /// <summary>Initializes a new instance of the <see cref="ProbeRequestHandler"/> class.</summary>
    /// <param name="scopeProbe">The scoped probe for the scope this handler ran in.</param>
    /// <param name="recorder">The singleton recorder.</param>
    public ProbeRequestHandler(ScopeProbe scopeProbe, ProbeRecorder recorder)
    {
        _scopeProbe = scopeProbe;
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<string> Handle(ProbeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _recorder.Record(_scopeProbe.InstanceId);
        return Task.FromResult($"handled:{request.Value}");
    }
}

public class ScopedMediatorTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // MediatR 14 resolves an ILoggerFactory when Mediator is constructed (licence check).
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        services.AddPineApplication();

        // Composition roots bind IUnitOfWork to the EF Core adapter. Pine.Application only needs
        // *an* implementation for TransactionBehavior<,> to be constructible.
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());

        services.AddSingleton<ProbeRecorder>();
        services.AddScoped<ScopeProbe>();
        services.AddTransient<IRequestHandler<ProbeRequest, string>, ProbeRequestHandler>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_FromRootProvider_ReachesHandlerWithoutRecursing()
    {
        // Arrange
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act — if ScopedMediator resolved ISender/IPublisher inside its own scope this would
        // recurse until the stack overflowed instead of returning.
        var result = await mediator.Send(new ProbeRequest("ping"));

        // Assert
        mediator.Should().BeOfType<ScopedMediator>();
        result.Should().Be("handled:ping");
        provider.GetRequiredService<ProbeRecorder>().ScopeProbeIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task Send_TwiceFromRootProvider_UsesAFreshScopePerRequest()
    {
        // Arrange
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new ProbeRequest("first"));
        await mediator.Send(new ProbeRequest("second"));

        // Assert — a scoped service resolved inside the handler must be a DIFFERENT instance for
        // each Send, which is only true if every Send opened its own DI scope.
        var recorded = provider.GetRequiredService<ProbeRecorder>().ScopeProbeIds;
        recorded.Should().HaveCount(2);
        recorded[0].Should().NotBe(recorded[1]);

        // And neither of them is the root-scope instance.
        var rootProbe = provider.GetRequiredService<ScopeProbe>();
        recorded.Should().NotContain(rootProbe.InstanceId);
    }

    [Fact]
    public async Task Sender_And_Publisher_FromRootProvider_AreTheScopingDecorator()
    {
        // Arrange
        await using var provider = BuildProvider();

        // Act
        var mediator = provider.GetRequiredService<IMediator>();
        var sender = provider.GetRequiredService<ISender>();
        var publisher = provider.GetRequiredService<IPublisher>();

        // Assert — MediatR registers ISender/IPublisher as factories over IMediator, so all three
        // must land on the same ScopedMediator singleton, never on the raw MediatR.Mediator.
        sender.Should().BeOfType<ScopedMediator>();
        publisher.Should().BeOfType<ScopedMediator>();
        sender.Should().BeSameAs(mediator);
        publisher.Should().BeSameAs(mediator);

        var result = await sender.Send(new ProbeRequest("via-sender"));
        result.Should().Be("handled:via-sender");
    }
}
