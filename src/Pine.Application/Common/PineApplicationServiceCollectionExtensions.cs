using System;
using System.Linq;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pine.Application.Common.Behaviors;

namespace Pine.Application.Common;

/// <summary>
/// Registers the Pine application layer (MediatR, the validation and transaction pipeline
/// behaviours, every request validator, and the per-request DI scoping decorator) into a
/// composition root.
/// </summary>
public static class PineApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Adds MediatR with every Pine.Application handler, the validation and transactional pipeline
    /// behaviours, every <see cref="IRequestValidator{TRequest}"/> in this assembly, and a scoping
    /// mediator so that each <c>Send</c>/<c>Publish</c> runs in its own DI scope.
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

        // 1. MediatR itself, plus the two open pipeline behaviours.
        //
        //    ORDER IS EXECUTION ORDER, AND IT IS A DECISION, NOT A DETAIL. MediatR runs open
        //    behaviours in the order they are added, so ValidationBehavior wraps TransactionBehavior
        //    and every validator therefore runs BEFORE the write transaction is opened. A rejected
        //    command consequently never begins a transaction at all -- its unit-of-work log is empty
        //    rather than Begin/Rollback. Swapping these two lines would run each validator's SELECT
        //    inside an open write transaction and turn every refusal into a pointless round trip on
        //    the encrypted vault. The ordering is pinned by
        //    ValidationBehavior_IsRegistered_BeforeTransactionBehavior; see the remarks on
        //    ValidationBehavior for why reading account currency outside the transaction is sound.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(PineApplicationServiceCollectionExtensions).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        // 2. Every IRequestValidator<> in this assembly, Scoped -- validators take repositories, and
        //    repositories are scoped to the request. Registration is by reflection so that adding a
        //    validator requires no edit here and cannot be forgotten in one of the five composition
        //    roots. Reflection scanning fails silently when it misses, so
        //    EveryRequestValidator_IsRegistered_ByAddPineApplication asserts the result rather than
        //    trusting the loop.
        RegisterRequestValidators(services);

        // 3. The CONCRETE MediatR implementation, Scoped. ScopedMediator resolves this type by its
        //    concrete name from inside the scope it opens. Because it is a class and not one of the
        //    MediatR interfaces, it can never resolve back to the decorator below.
        services.AddScoped<Mediator>();

        // 4. Replace (not merely add) all three MediatR interfaces with the singleton scoping
        //    decorator, so callers of IMediator, ISender or IPublisher all get a fresh scope per
        //    operation. ISender/IPublisher are resolved via IMediator so all three share one instance.
        services.Replace(ServiceDescriptor.Singleton<IMediator>(
            sp => new ScopedMediator(sp.GetRequiredService<IServiceScopeFactory>())));
        services.Replace(ServiceDescriptor.Singleton<ISender>(
            sp => sp.GetRequiredService<IMediator>()));
        services.Replace(ServiceDescriptor.Singleton<IPublisher>(
            sp => sp.GetRequiredService<IMediator>()));

        // 5. The lock-state bridge (TD-15). Pine.Infrastructure references only Pine.Core, so an
        //    infrastructure component that needs to know whether the vault is open -- currently just
        //    PineDatabaseHealthProbe -- depends on Pine.Core.Security.IVaultLockState rather than on
        //    IVaultSession. IVaultSession EXTENDS that interface, so this is a binding to the same
        //    object, not an adapter, and there is still exactly one session.
        //
        //    IT IS BOUND HERE RATHER THAN IN THE FIVE ROOTS on purpose: TD-29 exists because every
        //    such line has to be repeated five times and is forgotten in at least one of them --
        //    DiagnosticsViewModel itself was registered in only one root until TD-15. The factory is
        //    lazy, so a host that calls AddPineApplication without registering IVaultSession (the CLI
        //    and the test hosts) is unaffected unless it actually resolves IVaultLockState.
        services.TryAddSingleton<Pine.Core.Security.IVaultLockState>(
            sp => sp.GetRequiredService<Pine.Application.Security.IVaultSession>());

        return services;
    }

    /// <summary>
    /// Finds every concrete <see cref="IRequestValidator{TRequest}"/> implementation in the
    /// Pine.Application assembly and registers it Scoped against each closed interface it
    /// implements.
    /// </summary>
    /// <param name="services">The service collection to add the validators to.</param>
    /// <remarks>
    /// <c>TryAddEnumerable</c> rather than <c>AddScoped</c>: a composition root that calls
    /// <c>AddPineApplication</c> twice must not end up running every validator twice and reporting
    /// each error twice. A request may legitimately have more than one validator, which is why the
    /// enumerable form is used instead of <c>TryAddScoped</c>.
    /// </remarks>
    private static void RegisterRequestValidators(IServiceCollection services)
    {
        var assembly = typeof(PineApplicationServiceCollectionExtensions).Assembly;

        foreach (var implementation in assembly.GetTypes().Where(IsConcreteValidator))
        {
            foreach (var contract in implementation.GetInterfaces().Where(IsClosedValidatorInterface))
            {
                services.TryAddEnumerable(ServiceDescriptor.Scoped(contract, implementation));
            }
        }
    }

    private static bool IsConcreteValidator(Type type)
        => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
           && type.GetInterfaces().Any(IsClosedValidatorInterface);

    private static bool IsClosedValidatorInterface(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequestValidator<>);
}
