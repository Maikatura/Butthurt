using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Butthurt.UI;

/// <summary>The Butthurt design system: tokens, component themes and built-in control themes.</summary>
public class ButthurtTheme : Styles
{
    public ButthurtTheme() => AvaloniaXamlLoader.Load(this);
}
