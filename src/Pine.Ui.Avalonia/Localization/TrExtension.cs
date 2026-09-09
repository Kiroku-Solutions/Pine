using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Pine.Ui.Avalonia.Localization;

/// <summary>
/// The XAML markup extension every translated string in a view goes through:
/// <c>Text="{loc:Tr Settings.Title}"</c>.
/// </summary>
/// <remarks>
/// <para>
/// IT RETURNS A BINDING, NOT A STRING, AND THAT IS THE WHOLE MECHANISM. A markup extension that
/// returned <c>localizer[key]</c> would evaluate once, when the view is loaded, and the language
/// picker would then need the view recreated to show anything new -- which is "restart to change
/// language" wearing a different hat. Returning a one-way <see cref="Binding"/> to the localizer's
/// INDEXER means every such string is a live binding, and
/// <c>PropertyChanged("Item[]")</c> re-evaluates all of them in one notification (D2).
/// </para>
/// <para>
/// THE SOURCE IS THE STATIC INSTANCE BECAUSE IT HAS TO BE (§5.1). The XAML loader constructs this
/// type; there is no container in reach. The static and the DI singleton are the same object, so
/// nothing diverges.
/// </para>
/// </remarks>
public sealed class TrExtension : MarkupExtension
{
    /// <summary>Creates the extension with no key, for XAML's parameterless construction path.</summary>
    public TrExtension()
    {
    }

    /// <summary>Creates the extension for a key, which is the positional-argument form.</summary>
    /// <param name="key">The string ID to bind.</param>
    public TrExtension(string key) => Key = key;

    /// <summary>The string ID to bind.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Produces the one-way binding to <c>Localizer.Instance[Key]</c>.
    /// </summary>
    /// <param name="serviceProvider">Supplied by the XAML loader; unused.</param>
    /// <returns>A <see cref="Binding"/>, which the property system then evaluates.</returns>
    /// <remarks>
    /// THE PATH IS AN INDEXER EXPRESSION AND THE KEY CONTAINS DOTS. §13 recorded, as an unverified
    /// risk, that Avalonia's binding-path parser might not accept <c>Settings.Title</c> inside
    /// <c>[...]</c>, with "switch the separator to <c>/</c>" as the fallback. It accepts them --
    /// <c>TrExtensionTests.AKeyContainingDots_ResolvesThroughARealBinding</c> is that verification,
    /// written before this file and run against a real bound control. The dotted keys stay.
    /// </remarks>
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Path = $"[{Key}]",
        Mode = BindingMode.OneWay,
        Source = Localizer.Instance,
    };
}
