using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Butthurt.Contracts.Views;
using Butthurt.Warcraft.Models;

namespace Butthurt.Warcraft;

/// <summary>The World of Warcraft part of the Guide page.</summary>
internal static class GuideSection
{
    private const string LuaSample =
        """
        -- Addon/Retail/Butthurt/Butthurt.lua
        Butthurt.Colors = {
            ...
            LOOT = { 0, 1, 1, 1 },   -- cyan
        };
        Butthurt.EventInfo = { ..., { key = "LOOT", label = "Looting" } };

        -- Addon/Retail/Butthurt/Events/Butthurt_Loot.lua (add it to Butthurt.toc too)
        Butthurt:AddEvent("Loot", "LOOT_OPENED", function()
            Butthurt:Triggered("LOOT")
        end)
        """;

    private const string CSharpSample =
        """
        // Butthurt.Warcraft/Models/WowEvents.cs
        public enum WowEventType { ..., Loot }

        new WowEventDefinition(WowEventType.Loot, "Looting", "Opening a loot window.",
            0, 255, 255, PackIconLucideKind.Coins,
            new EventSettings { Intensity = 0.3, DurationMs = 300 }),
        """;

    public static Control Create()
    {
        var legend = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var definition in WowEvents.All)
        {
            legend.Children.Add(new Border
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

        return new StackPanel
        {
            Spacing = 20,
            Children =
            {
                Ui.Card("World of Warcraft",
                    "WoW addons can't talk to other programs, so the addon flashes a small square in the top-left corner of the game in a color that stands for the event. Butthurt reads that pixel while the game is the active window.",
                    legend,
                    Ui.NumberedStep(1, "Install the addon", "Press Auto install addon on the Home page, then /reload in game."),
                    Ui.NumberedStep(2, "Try it", "Type /butthurt in game to open the addon's window and press Test on any event."),
                    Ui.InfoRow("Nothing happens in game", "Make sure WoW is the active window and the watcher says Watching. The addon window's Corner pixel should change color when you press Test."),
                    Ui.InfoRow("The addon is out of date", "Tick Load out of date AddOns on the character select AddOns screen, or update Butthurt."),
                    Ui.InfoRow("Exclusive fullscreen", "Use Windowed (Fullscreen) in WoW's graphics settings so the corner pixel can be read.")),

                Ui.Card("Adding a WoW event", "For tinkerers. Change the addon and the Butthurt.Warcraft project, then rebuild.",
                    Ui.NumberedStep(1, "Pick a color and flash it from the addon",
                        "Use a color nobody else uses, with every channel either fully on or fully off so it survives screen color tweaks."),
                    Ui.CodeBlock(LuaSample),
                    Ui.NumberedStep(2, "Teach the app the same color",
                        "It shows up on the World of Warcraft page automatically, with its own toggle, sliders and test button."),
                    Ui.CodeBlock(CSharpSample))
            }
        };
    }
}
