using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Butthurt.Contracts.Views;

namespace Butthurt.Contracts.Services;

public static class NotificationService
{
    private static StackPanel? _host;

    public static void Initialize(StackPanel hostPanel)
    {
        _host = hostPanel;
    }

    public static void Show(string message, int durationMs = 3000)
    {
        if (_host == null)
            throw new InvalidOperationException("NotificationService not initialized. Call Initialize first.");

        var notification = new NotificationControl(message);
        _ = notification.ShowAsync(_host, durationMs);
    }

    public static async Task ShowAsync(string message, int durationMs = 3000)
    {
        if (_host == null)
            throw new InvalidOperationException("NotificationService not initialized");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var notification = new NotificationControl(message);
            await notification.ShowAsync(_host, durationMs);
        });
    }
}