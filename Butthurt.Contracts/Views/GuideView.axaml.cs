using System;
using Avalonia.Controls;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Contracts.Views;

public partial class GuideView : UserControl, IPageView
{
    public string Title => "Guide";
    public PackIconLucideKind Icon => PackIconLucideKind.BookOpen;
    public SideBarType SidebarType => SideBarType.Footer;
    public event EventHandler? BackRequested;

    public GuideView()
    {
        InitializeComponent();

        // Plugins and toy features can be turned on and off, so build everything each time the page is shown.
        AttachedToVisualTree += (_, _) =>
        {
            var toys = PluginManager.ToysEnabled;

            var steps = toys
                ? new[]
                {
                    ("Install Intiface Central", "Get it from intiface.com/central, open it and press Start Server."),
                    ("Pair your toy", "Turn your toy on and connect it in Intiface Central (or let Butthurt scan for it on the Toys page)."),
                    ("Set up what should buzz", "Pick a game or Twitch in the sidebar and choose which events you want to feel."),
                    ("Press Start", "On the Home page press Start. Butthurt connects to Intiface and starts every plugin.")
                }
                : new[]
                {
                    ("Log in to Twitch", "Open the Twitch page and log in, so your rules can react to bits, subs and redeems."),
                    ("Set up your rules", "Pick VRChat in the sidebar and choose what each Twitch event should do."),
                    ("Press Start", "On the Home page press Start to get every plugin going.")
                };

            SetupSteps.Children.Clear();
            var step = 1;
            foreach (var (title, body) in steps) SetupSteps.Children.Add(Ui.NumberedStep(step++, title, body));

            Troubleshooting.Children.Clear();
            if (toys)
            {
                Troubleshooting.Children.Add(Ui.InfoRow("Intiface won't connect",
                    "Check that the server is started in Intiface Central and that the address on the Toys page matches (default ws://localhost:12345)."));
            }

            Troubleshooting.Children.Add(Ui.InfoRow("Something happened but nothing changed",
                "Look at Recent activity on the Home page. It says why an action was skipped."));

            PluginSections.Children.Clear();
            foreach (var plugin in PluginManager.Plugins)
            {
                if (plugin.CreateGuideSection() is { } section) PluginSections.Children.Add(section);
            }
        };
    }
}
