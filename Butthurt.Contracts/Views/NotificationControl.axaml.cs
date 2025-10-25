using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.ViewModels;

namespace Butthurt.Contracts.Views;

public partial class NotificationControl : Border
{
    private TextBlock _messageText;

    public NotificationControl(string message)
    {
        CornerRadius = new CornerRadius(5);
        Background = Brushes.DimGray;
        Padding = new Thickness(10);
        Opacity = 0; // start invisible
        _messageText = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White
        };
        Child = _messageText;
    }

    public async Task ShowAsync(StackPanel parent, int durationMs = 3000)
    {
        parent.Children.Add(this);

        // Fade in
        for (double i = 0; i <= 1; i += 0.1)
        {
            Opacity = i;
            await Task.Delay(20);
        }

        // Wait duration
        await Task.Delay(durationMs);

        // Fade out
        for (double i = 1; i >= 0; i -= 0.1)
        {
            Opacity = i;
            await Task.Delay(20);
        }

        parent.Children.Remove(this);
    }
}
