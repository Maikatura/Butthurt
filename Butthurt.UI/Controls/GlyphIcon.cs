using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Butthurt.UI;

/// <summary>
/// Stand-in for FluentIcons' <c>SymbolIcon</c> (same Symbol / IconVariant / FontSize API)
/// covering only the glyphs the control themes use. They come from
/// Assets/Fonts/SeagullIconsSubset.otf: those 10 glyphs cut out of FluentIcons' "Seagull Fluent
/// Icons" font (MIT), drawn the way SymbolIcon draws them, so they render pixel for pixel the
/// same. Dropping the FluentIcons packages saves ~5 MB in the published exe.
/// </summary>
public class GlyphIcon : Control
{
    public static readonly StyledProperty<string?> SymbolProperty =
        AvaloniaProperty.Register<GlyphIcon, string?>(nameof(Symbol));

    public static readonly StyledProperty<string?> IconVariantProperty =
        AvaloniaProperty.Register<GlyphIcon, string?>(nameof(IconVariant), "Regular");

    // Like SymbolIcon: not inherited, 20 by default; themes bind it to the text size.
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<GlyphIcon, double>(nameof(FontSize), 20d);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<GlyphIcon>();

    private static readonly Typeface IconFont =
        new("avares://Butthurt.UI/Assets/Fonts#Seagull Fluent Icons");

    // FluentIcons maps a symbol to U+F0000 + 4 * symbol + variant; these are the ones kept.
    private static readonly Dictionary<string, int> CodePoints = new()
    {
        ["CheckboxChecked/Regular"] = 0xF013C,
        ["CheckboxChecked/Filled"] = 0xF013D,
        ["CheckboxUnchecked/Regular"] = 0xF0140,
        ["Checkmark/Regular"] = 0xF0144,
        ["ChevronRight/Regular"] = 0xF0154,
        ["Dismiss/Regular"] = 0xF01C0,
        ["RadioButton/Regular"] = 0xF03C4,
        ["RadioButton/Filled"] = 0xF03C5,
        ["LineHorizontal1/Regular"] = 0xF0B90,
        ["ChevronUpDown/Regular"] = 0xF1394,
    };

    private TextLayout? _textLayout;

    static GlyphIcon()
    {
        AffectsMeasure<GlyphIcon>(FontSizeProperty);
        AffectsRender<GlyphIcon>(SymbolProperty, IconVariantProperty, ForegroundProperty, FontSizeProperty);
    }

    public string? Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public string? IconVariant
    {
        get => GetValue(IconVariantProperty);
        set => SetValue(IconVariantProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SymbolProperty || change.Property == IconVariantProperty
            || change.Property == FontSizeProperty || change.Property == ForegroundProperty
            || change.Property == FlowDirectionProperty)
        {
            _textLayout?.Dispose();
            _textLayout = null;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(Math.Min(FontSize, availableSize.Width), Math.Min(FontSize, availableSize.Height));

    public override void Render(DrawingContext context)
    {
        if (GetText() is not { } text)
            return;

        // Same layout as SymbolIcon: centred TextLayout at FontSize, mirrored for RTL.
        _textLayout ??= new TextLayout(text, IconFont, FontSize, Foreground, TextAlignment.Center,
            flowDirection: FlowDirection);

        var bounds = Bounds;
        DrawingContext.PushedState? flip = null;
        try
        {
            if (FlowDirection == FlowDirection.RightToLeft)
                flip = context.PushTransform(new Matrix(-1, 0, 0, 1, bounds.Width, 0));
            _textLayout.Draw(context, new Point((bounds.Width - FontSize) / 2, (bounds.Height - FontSize) / 2));
        }
        finally
        {
            flip?.Dispose();
        }
    }

    private string? GetText()
    {
        if (string.IsNullOrEmpty(Symbol))
            return null;

        // A filled glyph we don't carry falls back to the regular one.
        if (CodePoints.TryGetValue($"{Symbol}/{IconVariant ?? "Regular"}", out var cp)
            || CodePoints.TryGetValue($"{Symbol}/Regular", out cp))
            return char.ConvertFromUtf32(cp);
        return null;
    }
}
