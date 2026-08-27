using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Pine.Application.Common;

/// <summary>
/// An <see cref="IMediator"/> that opens a fresh dependency-injection scope for every
/// <c>Send</c>, <c>CreateStream</c> and <c>Publish</c>, then delegates to the real MediatR
/// implementation resolved from inside that scope.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS: Pine's UI resolves a mediator from the ROOT provider. Without this decorator,
/// making <c>PineDbContext</c>, the repositories and <c>IUnitOfWork</c> Scoped would achieve
/// nothing — every resolution would land in the root scope and every request would share one
/// long-lived context for the entire lifetime of the process. This type is what makes "one DI
/// scope, one DbContext, one transaction per request" true.
/// </para>
/// <para>
/// WHY IT DELEGATES TO THE CONCRETE <see cref="Mediator"/> AND NOT TO <see cref="ISender"/>:
/// MediatR's <c>AddMediatR</c> registers <c>IMediator</c> against the concrete <c>Mediator</c>, and
/// registers <c>ISender</c> and <c>IPublisher</c> as FACTORIES that resolve <c>IMediator</c>
/// (verified against MediatR 14.2.0). Since this decorator replaces the <c>IMediator</c>
/// registration, resolving <c>ISender</c> or <c>IPublisher</c> from inside the new scope would
/// resolve straight back to this decorator, which would open another scope, resolve again, and
/// recurse until the stack overflowed. Resolving the CONCRETE <see cref="Mediator"/> class — which
/// is registered separately as Scoped by
/// <see cref="PineApplicationServiceCollectionExtensions.AddPineApplication"/> — cannot route back
/// here, no matter how MediatR wires its interfaces. That is the whole recursion defence.
/// </para>
/// <para>
/// NESTED SENDS: a handler that injects <see cref="IMediator"/>/<see cref="ISender"/>/
/// <see cref="IPublisher"/> and sends a nested request would get a NESTED SCOPE, and therefore a
/// separate <c>PineDbContext</c> and a separate transaction from its caller. The inner work would
/// commit independently of the outer, defeating atomicity. There is currently no handler in
/// Pine.Application that injects a mediator; keep it that way. Compose handlers by calling shared
/// application services or repositories directly, not by sending nested requests.
/// </para>
/// <para>
/// This type is registered as a SINGLETON and holds only an <see cref="IServiceScopeFactory"/>,
/// so it captures no scoped state.
/// </para>
/// </remarks>
public sealed class ScopedMediator : IMediator
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopedMediator"/> class.
    /// </summary>
    /// <param name="scopeFactory">Factory used to open one DI scope per mediator operation.</param>
    public ScopedMediator(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<Mediator>()
            .Send(request, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<Mediator>()
            .Send(request, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<Mediator>()
            .Send(request, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Implemented as an async iterator so the DI scope stays alive for the WHOLE enumeration and
    /// is disposed only when the consumer finishes (or breaks out of) the stream. Creating and
    /// disposing the scope eagerly would tear the <c>DbContext</c> down before the first item was
    /// ever pulled.
    /// </remarks>
    public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var stream = scope.ServiceProvider
            .GetRequiredService<Mediator>()
            .CreateStream(request, cancellationToken);

        await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Async iterator for the same reason as the generic overload: the scope must outlive the
    /// enumeration, not the call that created the enumerable.
    /// </remarks>
    public async IAsyncEnumerable<object?> CreateStream(
        object request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var stream = scope.ServiceProvider
            .GetRequiredService<Mediator>()
            .CreateStream(request, cancellationToken);

        await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public async Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<Mediator>()
            .Publish(notification, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<Mediator>()
            .Publish(notification, cancellationToken)
            .ConfigureAwait(false);
    }
}
