using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Butthurt.Contracts;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Toys.Views;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Toys;

/// <summary>
/// Toys through Intiface Central: connects on Start, disconnects on Stop, shows the Toys page and the toy status.
/// Other plugins make toys buzz through <see cref="ButthurtService.Vibrate"/>; with this plugin off nothing is
/// connected, so they simply stay quiet.
/// </summary>
public class ToysPlugin : IPlugin
{
    public const string PluginId = "Toys";

    public string Id => PluginId;
    public string Name => "Toys";
    public PackIconLucideKind Icon => PackIconLucideKind.Vibrate;
    public SideBarType Category => SideBarType.Hardware;

    public event EventHandler? StatusChanged;

    public void Initialize()
    {
        ButthurtService.StateChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
        ButthurtService.DeviceAdded += (_, args) =>
        {
            _ = NotificationService.ShowAsync($"Toy connected: {args.Device.Name}");
            StatusChanged?.Invoke(this, EventArgs.Empty);
        };
        ButthurtService.DeviceRemoved += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);

        // Quietly at startup: no "couldn't reach" pop-up when Intiface simply isn't open yet.
        if (SettingsService.Current.AutoConnect) _ = ButthurtService.Start();
    }

    public async Task Start()
    {
        if (!await ButthurtService.Start())
        {
            await NotificationService.ShowAsync("Could not reach Intiface Central. Is the server started?");
        }
    }

    public Task Stop() => ButthurtService.Stop();

    public IEnumerable<IPageView> CreatePages() => new IPageView[] { new ToysView() };

    public PluginStatus GetStatus()
    {
        if (ButthurtService.IsConnecting)
        {
            return new PluginStatus(PluginStatusLevel.Waiting, "Connecting", SettingsService.Current.IntifaceUrl) { SidebarText = "Intiface connecting…" };
        }

        if (!ButthurtService.IsConnected)
        {
            return new PluginStatus(PluginStatusLevel.Off, "Offline", ButthurtService.LastError ?? "Press Start to connect to Intiface")
                { SidebarText = "Intiface offline" };
        }

        var devices = ButthurtService.Devices;
        var enabled = devices.Count(d => ButthurtService.IsDeviceEnabled(d.Name));
        var headline = devices.Count == 1 ? "1 connected" : $"{devices.Count} connected";

        if (devices.Count == 0)
        {
            return new PluginStatus(PluginStatusLevel.Waiting, headline,
                ButthurtService.IsScanning ? "Scanning…" : "Turn on your toy and scan") { SidebarText = "Intiface connected, no toys" };
        }

        return new PluginStatus(PluginStatusLevel.Active, headline, $"{enabled} enabled for haptics")
            { SidebarText = devices.Count == 1 ? "1 toy ready" : $"{devices.Count} toys ready" };
    }

    /// <summary>The old Quick test, now on the Toys card on the home page.</summary>
    public PluginAction? HomeAction => new("Test buzz", PackIconLucideKind.Zap, async () =>
    {
        if (!ButthurtService.IsConnected)
        {
            await NotificationService.ShowAsync("Connect to Intiface Central first.");
            return;
        }

        if (ButthurtService.Devices.Count == 0)
        {
            await NotificationService.ShowAsync("No toys connected yet, try scanning on the Toys page.");
            return;
        }

        ActivityLog.Add("Test buzz at 50%", LogLevel.Event);
        await ButthurtService.Vibrate(0.5, 1000);
    });

    public Control CreateGuideSection() => Ui.Card("Toys",
        "Butthurt talks to your toys through Intiface Central, so anything Intiface supports works.",
        Ui.NumberedStep(1, "Install Intiface Central", "Get it from intiface.com/central, open it and press Start Server."),
        Ui.NumberedStep(2, "Pair your toy", "Turn your toy on and connect it in Intiface Central, or press Scan for toys on the Toys page."),
        Ui.NumberedStep(3, "Connect", "Press Start on the Home page, or Connect on the Toys page. Turn on \"Connect to Intiface when Butthurt starts\" to skip this next time."),
        Ui.InfoRow("Intiface won't connect",
            "Check that the server is started in Intiface Central and that the address on the Toys page matches (default ws://localhost:12345)."),
        Ui.InfoRow("How strong", "The master intensity on the Toys page scales every buzz from every plugin. Turn a toy off there to keep it quiet without unpairing it."));
}
