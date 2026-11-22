using System;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace Butthurt.UI;

/// <summary>The two converters the control themes bind to.</summary>
internal static class InternalConverters
{
    public static IMultiValueConverter StringFormat { get; } =
        new RelayMultiConverter(
            (v, _, info) =>
            {
                if (v is [string format, ..])
                {
                    return string.Format(info, format, [.. v.Skip(1)]);
                }

                return v;
            }
        );

    public static IValueConverter KeyGestureToString { get; } =
        new RelayConverter(
            (v, _) =>
            {
                return v switch
                {
                    null => null,
                    KeyGesture gesture => gesture.ToString("p", null),
                    _ => throw new NotSupportedException(),
                };
            }
        );
}
