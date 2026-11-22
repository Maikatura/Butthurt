using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Butthurt.UI;

public class RelayMultiConverter : IMultiValueConverter
{
    private readonly Func<IList<object?>, object?, CultureInfo, object?> _convert;

    public RelayMultiConverter(Func<IList<object?>, object?, CultureInfo, object?> convert) =>
        _convert = convert;

    public RelayMultiConverter(Func<IList<object?>, object?> convert) =>
        _convert = (values, _, _) => convert(values);

    public RelayMultiConverter(Func<IList<object?>> convert) => _convert = (_, _, _) => convert();

    #region IMultiValueConverter Members

    public object? Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => _convert(values, parameter, culture);

    #endregion
}
