using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Layout;

namespace Butthurt.UI;

[PseudoClasses(":vertical", ":horizontal")]
public class Divider : ContentControl
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<Divider, Orientation>(nameof(Orientation));

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == OrientationProperty)
        {
            switch (change.GetNewValue<Orientation>())
            {
                case Orientation.Horizontal:
                    PseudoClasses.Set(":vertical", false);
                    PseudoClasses.Set(":horizontal", true);
                    break;
                case Orientation.Vertical:
                    PseudoClasses.Set(":vertical", true);
                    PseudoClasses.Set(":horizontal", false);
                    break;
            }
        }
    }
}
