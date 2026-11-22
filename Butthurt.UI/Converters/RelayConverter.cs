using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Butthurt.UI;

public class RelayConverter : IValueConverter
{
    private readonly Func<object?, Type, object?, CultureInfo, object?> _convert;

    public RelayConverter(Func<object?, Type, object?, CultureInfo, object?> convert) =>
        _convert = convert;

    public RelayConverter(Func<object?, object?, object?> convert) =>
        _convert = (value, _, parameter, _) => convert(value, parameter);

    public RelayConverter(Func<object?, object?> convert) =>
        _convert = (value, _, _, _) => convert(value);

    #region IValueConverter Members

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => _convert(value, targetType, parameter, culture);

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotImplementedException();

    #endregion
}
