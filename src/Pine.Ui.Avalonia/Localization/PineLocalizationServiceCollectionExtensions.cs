using System;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pine.Ui.Avalonia.Localization;

/// <summary>
/// Registers the localizer and applies the stored language before the first window exists.
/// </summary>
/// <remarks>
/// <para>
/// D12 SAID "CALLED FROM ALL FIVE COMPOSITION ROOTS", AND THAT IS NO LONGER THE RIGHT SHAPE. The
/// design was written before iteration 22 collapsed the five hand-written roots into
/// <c>PineCompositionRoot</c>, and its stated rationale -- "per-head hand registration is a known
/// trap in this repo", citing the diagnostics view model registered on one head of five -- argues
/// for the shared root now that one exists. <c>AddPineLocalization()</c> is called once, from
/// <c>PineCompositionRoot.AddPine</c>, so a head cannot forget it.
/// </para>
/// <para>
/// THE LANGUAGE IS APPLIED AT REGISTRATION TIME, NOT ON FIRST RESOLVE. §6.1 requires the
/// pre-unlock screens to be translated, and those are constructed almost immediately after the
/// container is built. Waiting for something to resolve <see cref="ILocalizer"/> would mean the
/// unlock screen renders in English and then flickers -- which is the visible version of the
/// defect this whole feature exists to fix.
/// </para>
/// </remarks>
public static class PineLocalizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ILocalizer"/> and <see cref="LanguagePreferenceStore"/>, and applies
    /// the startup language.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPineLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var store = new LanguagePreferenceStore();

        // THE SAME OBJECT THE MARKUP EXTENSION READS (§5.1). Registering a second Localizer would
        // give XAML one string table and view models another, and the two would agree right up
        // until someone changed the language.
        services.TryAddSingleton<ILocalizer>(Localizer.Instance);
        services.TryAddSingleton(store);

        ApplyStartupLanguage(Localizer.Instance, store);

        return services;
    }

    /// <summary>
    /// Chooses the language for this session: the stored preference, else the OS UI culture, else
    /// English.
    /// </summary>
    /// <param name="localizer">The localizer to apply the language to.</param>
    /// <param name="store">The preference store to read.</param>
    /// <remarks>
    /// THE OS CULTURE IS THE MIDDLE STEP AND NOT THE LAST ONE. A user whose machine is Spanish and
    /// who has never opened Settings should get Spanish; a user whose machine is Japanese gets
    /// English, because Japanese is not shipped (D8) and <c>Localizer.Resolve</c> maps anything
    /// unshipped onto the fallback rather than leaving a half-translated screen.
    /// </remarks>
    public static void ApplyStartupLanguage(ILocalizer localizer, LanguagePreferenceStore store)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(store);

        var stored = store.Read();

        var code = stored is not null
            ? Localizer.Resolve(stored)
            : Localizer.Resolve(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        localizer.SetLanguage(code);
    }
}
