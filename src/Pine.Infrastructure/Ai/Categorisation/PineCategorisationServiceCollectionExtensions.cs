using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pine.Core.Ai;
using Pine.Core.Security;
using Pine.Infrastructure.Security;

namespace Pine.Infrastructure.Ai.Categorisation;

/// <summary>
/// Registers the vault-scoped transaction categoriser and the file cipher it stores its model with
/// (TD-16).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS AN EXTENSION METHOD RATHER THAN FOUR LINES IN A COMPOSITION ROOT. The defect this
/// iteration closed was a REGISTRATION defect, not an algorithm defect: the categoriser was
/// registered once as <c>ICategorizationTrainer</c> while <c>DefaultLocalAiProvider</c> constructed
/// a second one for itself, so training rebuilt a prediction engine that no prediction went
/// through. Nothing caught it, because a composition root is not reachable from a test -- it is a
/// <c>static void ConfigureServices</c> inside a <c>WinExe</c>. Moving the registrations here makes
/// the relationship between the two interfaces an assertable fact rather than a convention, and
/// <c>PineCategorisationRegistrationTests</c> asserts it.
/// </para>
/// <para>
/// IT IS ALSO A SLICE OF TD-29, TAKEN OPPORTUNISTICALLY. TD-29 exists because every registration
/// has to be repeated in five roots and is forgotten in at least one; this AI block was forgotten
/// in four of them, which is TD-90. This method does not close TD-90 -- the other roots still do
/// not call it, deliberately, per the scope decision recorded in §12.29 -- but it means closing it
/// later is one call per root rather than four lines per root.
/// </para>
/// </remarks>
public static class PineCategorisationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IVaultFileCipher"/>, <see cref="ICategorizerModelStore"/> and the
    /// categoriser, with the trainer and the predictor resolving to ONE instance.
    /// </summary>
    /// <remarks>
    /// REQUIRES <c>AddPineApplication()</c> to have been called, because the cipher and the store
    /// depend on <see cref="IVaultDataKeyAccessor"/>, which that method forwards to the vault
    /// session. The dependency is lazy, so registration order between the two does not matter --
    /// only that both have happened before anything resolves a categoriser.
    /// </remarks>
    public static IServiceCollection AddPineCategorisation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IVaultFileCipher, AesGcmVaultFileCipher>();
        services.TryAddSingleton<ICategorizerModelStore, VaultCategorizerModelStore>();

        // ONE object, TWO interfaces, and the forward is the whole point. Registering
        // MlNetCategorizer separately under each interface compiles, reads naturally, and silently
        // restores the defect: the container would build two, and only one of them would ever be
        // trained. Anything that holds per-vault state has to be forwarded, never re-registered.
        services.TryAddSingleton<IMlNetCategorizer, MlNetCategorizer>();
        services.TryAddSingleton<ICategorizationTrainer>(
            sp => (ICategorizationTrainer)sp.GetRequiredService<IMlNetCategorizer>());

        return services;
    }
}
