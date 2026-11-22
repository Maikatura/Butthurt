using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace Butthurt.UI;

[PseudoClasses(":busy")]
public class BusyContainer : ContentControl
{
    public static readonly StyledProperty<object?> PendingContentProperty =
        AvaloniaProperty.Register<BusyContainer, object?>(nameof(PendingContent));

    public static readonly StyledProperty<bool> IsBusyProperty = AvaloniaProperty.Register<
        BusyContainer,
        bool
    >(nameof(IsBusy));

    public object? PendingContent
    {
        get => GetValue(PendingContentProperty);
        set => SetValue(PendingContentProperty, value);
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsBusyProperty)
        {
            PseudoClasses.Set(":busy", change.GetNewValue<bool>());
        }
    }
}
