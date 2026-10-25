using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butthurt.Contracts.Models;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Contracts.Views;

public partial class GuideView : UserControl, IPageView
{
    public string Title => "Guide";
    public PackIconLucideKind Icon => PackIconLucideKind.BookOpen;
    public SideBarType SidebarType => SideBarType.Footer;
    public event EventHandler? BackRequested;

    private const string LuaSample =
        """
        -- Addon/Retail/Butthurt/Butthurt.lua
        Butthurt.Colors = {
            ...
            LOOT = { 0, 1, 1, 1 },   -- cyan
        };

        -- Addon/Retail/Butthurt/Events/Butthurt_Loot.lua (add it to Butthurt.toc too)
        Butthurt:AddEvent("Loot", "LOOT_OPENED", function()
            Butthurt:Triggered("LOOT")
        end)
        """;

    private const string CSharpSample =
        """
        // Butthurt.Contracts/Models/WowEvents.cs
        public enum WowEventType { ..., Loot }

        new WowEventDefinition(WowEventType.Loot, "Looting", "Opening a loot window.",
            0, 255, 255, PackIconLucideKind.Coins,
            new EventSettings { Intensity = 0.3, DurationMs = 300 }),
        """;

    public GuideView()
    {
        InitializeComponent();

        var step = 1;
        foreach (var (title, body) in new[]
                 {
                     ("Install Intiface Central", "Get it from intiface.com/central, open it and press Start Server."),
                     ("Pair your toy", "Turn your toy on and connect it in Intiface Central (or let Butthurt scan for it on the Toys page)."),
                     ("Install the addon", "Press Auto install addon on the Home page (or pick your folder on the World of Warcraft page). Then /reload in game."),
                     ("Press Start", "On the Home page press Start. Butthurt connects to Intiface and starts watching for the game."),
                     ("Play", "Switch to WoW and get hit. Type /butthurt test in game to try it without dying.")
                 })
        {
            SetupSteps.Children.Add(NumberedStep(step++, title, body));
        }

        foreach (var definition in WowEvents.All)
        {
            ColorLegend.Children.Add(new Border
            {
                Classes = { "pill" },
                Padding = new Thickness(10, 6),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Border
                        {
                            Width = 12, Height = 12, CornerRadius = new CornerRadius(3),
                            Background = new SolidColorBrush(Color.FromRgb(definition.R, definition.G, definition.B)),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        Ui.Text($"{definition.Name}  {definition.Hex}", "muted", 12)
                    }
                }
            });
        }

        DevSteps.Children.Add(NumberedStep(1, "Pick a color and flash it from the addon",
            "Use a color nobody else uses, with every channel either fully on or fully off so it survives screen color tweaks."));
        DevSteps.Children.Add(CodeBlock(LuaSample));
        DevSteps.Children.Add(NumberedStep(2, "Teach the app the same color",
            "It shows up on the World of Warcraft page automatically, with its own toggle, sliders and test button."));
        DevSteps.Children.Add(CodeBlock(CSharpSample));

        foreach (var (title, body) in new[]
                 {
                     ("Nothing happens in game", "Make sure WoW is the active window and the watcher says Watching. Run /butthurt test in game: the Recent activity on the Home page should show it."),
                     ("Intiface won't connect", "Check that the server is started in Intiface Central and that the address on the Toys page matches (default ws://localhost:12345)."),
                     ("The addon is out of date", "Tick Load out of date AddOns on the character select AddOns screen, or update Butthurt."),
                     ("Exclusive fullscreen", "Use Windowed (Fullscreen) in WoW's graphics settings so the corner pixel can be read.")
                 })
        {
            Troubleshooting.Children.Add(new Border
            {
                Classes = { "row" },
                Child = new StackPanel { Spacing = 2, Children = { Ui.Text(title, "h3"), Ui.Text(body, "muted", 12) } }
            });
        }
    }

    private static Control NumberedStep(int number, string title, string body) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*"),
        Children =
        {
            new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                Background = Ui.Brush("AccentGradient"),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 14, 0),
                Child = new TextBlock
                {
                    Text = number.ToString(), FontWeight = FontWeight.Bold, FontSize = 13, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            },
            Ui.WithColumn(new StackPanel
            {
                Spacing = 2,
                Children = { Ui.Text(title, "h3"), Ui.Text(body, "muted") }
            }, 1)
        }
    };

    private static Control CodeBlock(string code) => new Border
    {
        Classes = { "codeblock" },
        Margin = new Thickness(42, 0, 0, 0),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = Ui.Text(code, "code")
        }
    };
}
