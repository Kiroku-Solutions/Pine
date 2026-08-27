using System;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pine.Application.Common.Behaviors;

namespace Pine.Application.Common;

/// <summary>
/// Registers the Pine application layer (MediatR, the transaction pipeline behaviour and the
/// per-request DI scoping decorator) into a composition root.
/// </summary>
public static class PineApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Adds MediatR with every Pine.Application handler, the transactional pipeline behaviour, and
    /// a scoping mediator so that each <c>Send</c>/<c>Publish</c> runs in its own DI scope.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// Composition roots must additionally register <c>Pine.Core.Common.IUnitOfWork</c> against
    /// <c>Pine.Infrastructure.Data.EfCoreUnitOfWork</c> as SCOPED. Pine.Application cannot see
    /// Pine.Infrastructure, so that binding cannot be made here.
    /// </remarks>
    public static IServiceCollection AddPineApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 1. MediatR itself, plus the opt-in transaction boundary as an open pipeline behaviour.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(PineApplicationServiceCollectionExtensions).Assembly);
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        // 2. The CONCRETE MediatR implementation, Scoped. ScopedMediator resolves this type by its
        //    concrete name from inside the scope it opens. Because it is a class and not one of the
        //    MediatR interfaces, it can never resolve back to the decorator below.
        services.AddScoped<Mediator>();

        // 3. Replace (not merely add) all three MediatR interfaces with the singleton scoping
        //    decorator, so callers of IMediator, ISender or IPublisher all get a fresh scope per
        //    operation. ISender/IPublisher are resolved via IMediator so all three share one instance.
        services.Replace(ServiceDescriptor.Singleton<IMediator>(
            sp => new ScopedMediator(sp.GetRequiredService<IServiceScopeFactory>())));
        services.Replace(ServiceDescriptor.Singleton<ISender>(
            sp => sp.GetRequiredService<IMediator>()));
        services.Replace(ServiceDescriptor.Singleton<IPublisher>(
            sp => sp.GetRequiredService<IMediator>()));

        return services;
    }
}
