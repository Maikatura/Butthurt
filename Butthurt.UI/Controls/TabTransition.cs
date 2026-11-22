using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Media;

namespace Butthurt.UI;

public class TabTransition : FrameTransitionBase
{
    protected override void Configure(Builder from, Builder to, Lazy<Visual> parentAccessor)
    {
        const double translate = 32d;

        from.Animation(TimeSpan.FromMilliseconds(59), new CubicEaseIn())
            .AddFrame(0d, [(Visual.OpacityProperty, 1d)])
            .AddFrame(1d, [(Visual.OpacityProperty, 0d)]);

        to.Animation(new CubicEaseOut())
            .AddFrame(0d, [(Visual.OpacityProperty, 0d), (TranslateTransform.YProperty, translate)])
            .AddFrame(1d, [(Visual.OpacityProperty, 1d), (TranslateTransform.YProperty, 0d)]);
    }
}
