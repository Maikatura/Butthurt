using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;

namespace Butthurt.Contracts.Views;

public partial class NotificationControl : UserControl
{
    /// <summary>Longest a notification stays up on its own, however long the message.</summary>
    private const int MaxDurationMs = 15000;

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<NotificationControl, string>(nameof(Message));

    private bool _dismissed;

    public string Message
    {
        get => GetValue(MessageProperty);
        set
        {
            SetValue(MessageProperty, value);
            MessageTextBlock.Text = value;
        }
    }

    public NotificationControl()
    {
        InitializeComponent();

        // Click to close it early.
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, _) => _dismissed = true;
    }

    public NotificationControl(string message) : this()
    {
        Message = message;
    }

    public async Task ShowAsync(StackPanel parent, int durationMs = 3000)
    {
        parent.Children.Add(this);

        // FillMode keeps the end value: without it the border snaps back to Opacity 0 (its XAML value) as soon as
        // the fade-in ends, and the message is invisible for the whole time it's meant to be read.
        await Fade(0, 1, new CubicEaseOut());
        RootBorder.Opacity = 1;

        // Long messages (errors) need longer to read: about 60 ms per character on top of the asked time.
        var readMs = Math.Min(Math.Max(durationMs, 2000 + (Message?.Length ?? 0) * 60), MaxDurationMs);
        var shownAt = DateTime.UtcNow;

        // Stays while the mouse is on it, so it can be read in peace.
        while (!_dismissed && (DateTime.UtcNow - shownAt < TimeSpan.FromMilliseconds(readMs) || IsPointerOver))
        {
            await Task.Delay(100);
        }

        await Fade(1, 0, new CubicEaseIn());
        RootBorder.Opacity = 0;
        parent.Children.Remove(this);
    }

    private Task Fade(double from, double to, Easing easing) => new Animation
    {
        Duration = TimeSpan.FromMilliseconds(200),
        FillMode = FillMode.Forward,
        Easing = easing,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, to) } }
        }
    }.RunAsync(RootBorder);
}
