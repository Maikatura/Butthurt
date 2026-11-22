using Avalonia;
using Avalonia.Controls;

namespace Butthurt.UI;

/// <summary>
/// shadcn/ui Badge. Variants are classes:
/// (none) filled primary, <c>Secondary</c>, <c>Outline</c>,
/// <c>Success</c> / <c>Warning</c> / <c>Danger</c> / <c>Info</c> (these win over Secondary/Outline,
/// so <c>Classes="Secondary" Classes.Success="{Binding Ok}"</c> works),
/// plus the modifiers <c>Pill</c> (fully rounded) and <c>Small</c>.
/// A local Background/Foreground always beats the variant.
/// </summary>
public class Badge : ContentControl
{
    public static readonly StyledProperty<bool> ShowDotProperty =
        AvaloniaProperty.Register<Badge, bool>(nameof(ShowDot));

    /// <summary>Shows a small status dot before the content, in the variant's solid colour.</summary>
    public bool ShowDot
    {
        get => GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }
}
