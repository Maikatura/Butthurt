using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Butthurt.UI;

/// <summary>
/// shadcn/ui Alert: a bordered banner for page-level messages.
/// Variants are classes: (none) neutral card, <c>Success</c>, <c>Warning</c>, <c>Danger</c>, <c>Info</c>.
/// Set <see cref="DismissCommand"/> to get a close button; <see cref="ShowDot"/> adds a status dot.
/// </summary>
public class Alert : ContentControl
{
    public static readonly StyledProperty<bool> ShowDotProperty =
        AvaloniaProperty.Register<Alert, bool>(nameof(ShowDot));

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<Alert, ICommand?>(nameof(DismissCommand));

    public bool ShowDot
    {
        get => GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }

    /// <summary>Runs when the close button is clicked; the button only shows when this is set.</summary>
    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }
}
