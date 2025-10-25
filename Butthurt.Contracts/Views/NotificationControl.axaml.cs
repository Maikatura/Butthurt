using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.ViewModels;

namespace Butthurt.Contracts.Views;

public partial class NotificationControl : Border
{
    // Bindable Message property
        public static readonly StyledProperty<string> MessageProperty =
            AvaloniaProperty.Register<NotificationControl, string>(nameof(Message));

        public string Message
        {
            get => GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public NotificationControl()
        {
            InitializeComponent();
        }

        public NotificationControl(string message) : this()
        {
            Message = message;
        }

        public async Task ShowAsync(StackPanel parent, int durationMs = 3000)
        {
            parent.Children.Add(this);

                // Fade in
                 var fadeIn = new Animation
                 {
                     Duration = TimeSpan.FromMilliseconds(200),
                     Children =
                     {
                         new KeyFrame
                         {
                             Cue = new Cue(0),
                             Setters = { new Setter(Border.OpacityProperty, 0d) }
                         },
                         new KeyFrame
                         {
                             Cue = new Cue(1),
                             Setters = { new Setter(Border.OpacityProperty, 1d) }
                         }
                     },
                     Easing = new CubicEaseOut()
                 };

                await fadeIn.RunAsync(RootBorder);

                await Task.Delay(durationMs);

                 // Fade out
                 var fadeOut = new Animation
                 {
                     Duration = TimeSpan.FromMilliseconds(200),
                     Children =
                     {
                         new KeyFrame
                         {
                             Cue = new Cue(0),
                             Setters = { new Setter(Border.OpacityProperty, 1d) }
                         },
                         new KeyFrame
                         {
                             Cue = new Cue(1),
                             Setters = { new Setter(Border.OpacityProperty, 0d) }
                         }
                     },
                     Easing = new CubicEaseIn()
                 };

                await fadeOut.RunAsync(RootBorder);

                parent.Children.Remove(this);
        }

    
}
