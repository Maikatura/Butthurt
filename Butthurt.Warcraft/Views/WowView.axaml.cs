using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Butthurt.Contracts;
using Butthurt.Contracts.Models;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Contracts.Views;
using Butthurt.Warcraft.Models;
using Butthurt.Warcraft.Services;
using IconPacks.Avalonia.Lucide;

namespace Butthurt.Warcraft.Views;

public partial class WowView : UserControl, IPageView
{
    public string Title => "World of Warcraft";
    public PackIconLucideKind Icon => PackIconLucideKind.Swords;
    public SideBarType SidebarType => SideBarType.Plugins;
    public event EventHandler? BackRequested;

    private readonly HashSet<string> _expandedRules = new();
    private bool _loading;

    private static List<WowRule> Rules => WarcraftPlugin.Settings.GetRules();

    public WowView()
    {
        InitializeComponent();

        AddRuleButton.Flyout = WowConditionEditor.AddRuleMenu(AddRule);

        WowPathBox.LostFocus += (_, _) => SetWowPath(WowPathBox.Text);
        WowWatcherService.StatusChanged += (_, _) => Dispatcher.UIThread.Post(UpdateWatcher);
        WowWatcherService.EventDetected += (_, _) => Dispatcher.UIThread.Post(UpdateWatcher);
        PluginManager.PluginsChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            ToyRulesCard.IsVisible = PluginManager.IsOn("Toys");
            BuildExtraSections();
        });

        AttachedToVisualTree += (_, _) =>
        {
            if (string.IsNullOrEmpty(WarcraftPlugin.Settings.WowPath))
            {
                // First visit, try to save the user a trip through the folder picker.
                var detected = AddonInstaller.DetectWowPath();
                if (detected != null)
                {
                    WarcraftPlugin.Settings.WowPath = detected;
                    SettingsService.Save();
                }
            }

            WowPathBox.Text = WarcraftPlugin.Settings.WowPath;
            // Toy rules only matter with the Toys plugin on ("Toys" is Butthurt.Toys' plugin id).
            ToyRulesCard.IsVisible = PluginManager.IsOn("Toys");
            BuildRuleList();
            BuildExtraSections();
            RefreshFlavors();
            UpdateWatcher();

            _loading = true;
            AutoWatchToggle.IsChecked = WarcraftPlugin.Settings.AutoStartWatcher;
            PollSlider.Value = WarcraftPlugin.Settings.PollIntervalMs;
            PollText.Text = $"{WarcraftPlugin.Settings.PollIntervalMs} ms";
            _loading = false;
        };

        AutoWatchToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            WarcraftPlugin.Settings.AutoStartWatcher = AutoWatchToggle.IsChecked == true;
            SettingsService.Save();
        };

        PollSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            var value = (int)(Math.Round(PollSlider.Value / 5) * 5);
            PollText.Text = $"{value} ms";
            if (_loading) return;
            WarcraftPlugin.Settings.PollIntervalMs = value;
            SettingsService.SaveSoon();
        };
    }

    private void WatcherButton_Click(object? sender, RoutedEventArgs e)
    {
        if (WowWatcherService.IsRunning) WowWatcherService.Stop();
        else WowWatcherService.Start();
    }

    private void UpdateWatcher()
    {
        var running = WowWatcherService.IsRunning;
        WatcherButton.Content = running ? "Stop" : "Start";
        WatcherButton.Classes.Set("primary", !running);
        WatcherButton.Classes.Set("danger", running);

        var (text, brushKey) = !running ? ("STOPPED", "DimBrush")
            : WowWatcherService.IsWowFocused ? ("WATCHING", "SuccessBrush")
            : ("WAITING FOR WOW", "WarningBrush");

        WatcherPillText.Text = text;
        WatcherPillText.Foreground = Ui.Brush(brushKey);

        var boxes = WowWatcherService.BoxCount switch
        {
            0 => "",
            1 => " · reading 1 box",
            var n => $" · reading {n} boxes"
        };

        WatcherText.Text = WowWatcherService.LastEvent is { } last && WowWatcherService.LastEventTime is { } time
            ? $"Last event: {last.Name} at {time:HH:mm:ss}{boxes}"
            : "The addon flashes tiny boxes in the top-left corner of the game, one per event. Butthurt reads them while WoW is the active window" +
              (boxes.Length > 0 ? $"{boxes}." : ".");
    }

    /// <summary>Cards other plugins put on this page, e.g. PiShock rules.</summary>
    private void BuildExtraSections()
    {
        ExtraSections.Children.Clear();
        foreach (var card in PageSections.CreateFor(WarcraftPlugin.PluginId)) ExtraSections.Children.Add(card);
    }

    #region Toy rules

    private void AddRule(WowEventType type)
    {
        var definition = WowEvents.Get(type);
        var rule = new WowRule
        {
            Name = definition.Name,
            Event = type,
            Intensity = definition.Defaults.Intensity,
            DurationMs = definition.Defaults.DurationMs
        };

        Rules.Add(rule);
        _expandedRules.Add(rule.Id);
        SettingsService.Save();
        BuildRuleList();
    }

    /// <summary>Rules grouped by category (Combat, Progress…), in the order the events are listed.</summary>
    private void BuildRuleList()
    {
        RuleList.Children.Clear();
        var order = WowEvents.Categories.SelectMany(WowEvents.InCategory).Select(e => e.Type).ToList();

        foreach (var category in WowEvents.Categories)
        {
            var rules = Rules.Where(r => WowEvents.Get(r.Event).Category == category)
                .OrderBy(r => order.IndexOf(r.Event))
                .ToList();
            if (rules.Count == 0) continue;

            var caption = Ui.Text(category.ToUpperInvariant(), "caption");
            caption.Margin = new Thickness(2, RuleList.Children.Count == 0 ? 0 : 10, 0, 0);
            RuleList.Children.Add(caption);
            foreach (var rule in rules) RuleList.Children.Add(CreateRuleRow(rule));
        }

        NoRulesText.IsVisible = Rules.Count == 0;
    }

    /// <summary>"60% for 400 ms · 10s cooldown", with the event in front when the rule has a name of its own.</summary>
    private static string Summary(WowRule rule)
    {
        var eventName = WowEvents.Get(rule.Event).Name;
        var when = Label(rule) == eventName ? "" : $"{eventName} → ";
        var cooldown = rule.CooldownSeconds > 0 ? $" · {rule.CooldownSeconds}s cooldown" : "";
        return $"{when}{rule.Intensity * 100:0}% for {Ui.FormatDuration(rule.DurationMs)}{cooldown}";
    }

    private Control CreateRuleRow(WowRule rule)
    {
        var nameText = Ui.Text(Label(rule), "h3");
        var summaryText = Ui.Text(Summary(rule), "muted", 12);
        var tile = WowConditionEditor.Tile(rule.Event);

        void Changed()
        {
            nameText.Text = Label(rule);
            summaryText.Text = Summary(rule);
            SettingsService.SaveSoon();
        }

        var toggle = new ToggleSwitch { IsChecked = rule.Enabled, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };

        var test = new Button { Classes = { "bt", "small" }, Content = "Test", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        test.Click += async (_, _) =>
        {
            if (!ButthurtService.IsConnected) await NotificationService.ShowAsync("Connect to Intiface Central to feel the test.");
            WowWatcherService.Test(rule);
        };

        var expanded = _expandedRules.Contains(rule.Id);
        var edit = Ui.IconButton(expanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.Pencil, "Edit");
        var delete = Ui.IconButton(PackIconLucideKind.Trash2, "Delete");

        var editor = CreateEditor(rule, Changed, () =>
        {
            // Another event: new tile, a matching name if it still had an event's name, and maybe another heading.
            WowConditionEditor.SetTile(tile, rule.Event);
            if (string.IsNullOrWhiteSpace(rule.Name) || WowEvents.All.Any(e => e.Name == rule.Name))
            {
                rule.Name = WowEvents.Get(rule.Event).Name;
                Changed();
            }

            Dispatcher.UIThread.Post(BuildRuleList);
        });
        editor.IsVisible = expanded;

        var body = new StackPanel { Spacing = 12 };
        var row = new Border { Classes = { "row" }, Child = body, Opacity = rule.Enabled ? 1 : 0.6 };

        toggle.IsCheckedChanged += (_, _) =>
        {
            rule.Enabled = toggle.IsChecked == true;
            row.Opacity = rule.Enabled ? 1 : 0.6;
            SettingsService.Save();
        };

        edit.Click += (_, _) =>
        {
            editor.IsVisible = !editor.IsVisible;
            if (editor.IsVisible) _expandedRules.Add(rule.Id);
            else _expandedRules.Remove(rule.Id);
            ((PackIconLucide)edit.Content!).Kind = editor.IsVisible ? PackIconLucideKind.ChevronUp : PackIconLucideKind.Pencil;
        };

        var confirmDelete = false;
        delete.Click += (_, _) =>
        {
            // Two clicks, so a misclick doesn't throw away a carefully tuned rule.
            if (!confirmDelete)
            {
                confirmDelete = true;
                delete.Content = Ui.Text("Delete?", "body", 12);
                delete.Classes.Add("danger");
                return;
            }

            Rules.Remove(rule);
            _expandedRules.Remove(rule.Id);
            SettingsService.Save();
            BuildRuleList();
        };

        body.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
            Children =
            {
                tile,
                Ui.WithColumn(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { nameText, summaryText } }, 1),
                Ui.WithColumn(toggle, 2),
                Ui.WithColumn(test, 3),
                Ui.WithColumn(edit, 4),
                Ui.WithColumn(delete, 5)
            }
        });
        body.Children.Add(editor);

        return row;
    }

    private static Control CreateEditor(WowRule rule, Action changed, Action eventChanged)
    {
        var fields = new StackPanel { Spacing = 12, Margin = new Thickness(54, 4, 0, 4) };

        var name = new TextBox { Classes = { "field" }, Text = rule.Name, Watermark = WowEvents.Get(rule.Event).Name };
        name.TextChanged += (_, _) =>
        {
            rule.Name = name.Text ?? "";
            changed();
        };
        fields.Children.Add(Ui.Field("Name", name));
        fields.Children.Add(WowConditionEditor.Create(rule, changed, eventChanged));

        var intensityLabel = Ui.Text($"{rule.Intensity * 100:0}%", "muted", 12);
        var intensity = new Slider { Minimum = 5, Maximum = 100, Value = rule.Intensity * 100 };
        intensity.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            rule.Intensity = Math.Round(intensity.Value) / 100.0;
            intensityLabel.Text = $"{intensity.Value:0}%";
            changed();
        };

        var durationLabel = Ui.Text(Ui.FormatDuration(rule.DurationMs), "muted", 12);
        var duration = new Slider { Minimum = 100, Maximum = 5000, Value = rule.DurationMs };
        duration.PropertyChanged += (_, e) =>
        {
            if (e.Property != Slider.ValueProperty) return;
            rule.DurationMs = (int)(Math.Round(duration.Value / 50) * 50);
            durationLabel.Text = Ui.FormatDuration(rule.DurationMs);
            changed();
        };

        fields.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*"),
            Children =
            {
                Ui.SliderBlock("Intensity", intensityLabel, intensity),
                Ui.WithColumn(Ui.SliderBlock("Duration", durationLabel, duration), 2)
            }
        });

        fields.Children.Add(WowConditionEditor.Cooldown(rule, changed));
        return fields;
    }

    private static string Label(WowRule rule) => string.IsNullOrWhiteSpace(rule.Name) ? WowEvents.Get(rule.Event).Name : rule.Name;

    #endregion

    #region Addon

    private void SetWowPath(string? path)
    {
        var normalized = AddonInstaller.NormalizeWowPath(path);
        if (normalized == WarcraftPlugin.Settings.WowPath)
        {
            RefreshFlavors();
            return;
        }

        WarcraftPlugin.Settings.WowPath = normalized ?? path?.Trim();
        SettingsService.Save();
        WowPathBox.Text = WarcraftPlugin.Settings.WowPath;
        RefreshFlavors();
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select your World of Warcraft folder",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) SetWowPath(path);
    }

    private async void DetectButton_Click(object? sender, RoutedEventArgs e)
    {
        var detected = AddonInstaller.DetectWowPath();
        if (detected == null)
        {
            await NotificationService.ShowAsync("Couldn't find World of Warcraft, please pick the folder with Browse.");
            return;
        }

        SetWowPath(detected);
        await NotificationService.ShowAsync($"Found World of Warcraft in {detected}");
    }

    private async void AutoInstallButton_Click(object? sender, RoutedEventArgs e)
    {
        AutoInstallButton.IsEnabled = false;
        await AddonInstaller.AutoInstallAndNotify();
        AutoInstallButton.IsEnabled = true;

        WowPathBox.Text = WarcraftPlugin.Settings.WowPath;
        RefreshFlavors();
    }

    /// <summary>The addon card starts folded up; this opens or closes the folder and game version rows.</summary>
    private void AddonExpand_Click(object? sender, RoutedEventArgs e)
    {
        AddonDetails.IsVisible = !AddonDetails.IsVisible;
        AddonExpandIcon.Kind = AddonDetails.IsVisible ? PackIconLucideKind.ChevronUp : PackIconLucideKind.ChevronDown;
    }

    private void RefreshFlavors()
    {
        FlavorList.Children.Clear();

        var wowPath = WarcraftPlugin.Settings.WowPath;
        if (string.IsNullOrWhiteSpace(wowPath) || !Directory.Exists(wowPath))
        {
            FlavorHint.Text = "Pick the folder that contains _retail_ (or _classic_), usually C:\\Program Files (x86)\\World of Warcraft.";
            FlavorHint.IsVisible = true;
            AddonSummary.Text = "World of Warcraft folder not set. Press Auto install, or open this to pick it.";
            return;
        }

        var flavors = AddonInstaller.GetFlavors(wowPath).ToList();
        FlavorHint.IsVisible = flavors.Count == 0;
        FlavorHint.Text = "No game versions found in this folder. It should contain _retail_ or _classic_.";

        foreach (var flavor in flavors)
        {
            FlavorList.Children.Add(CreateFlavorRow(wowPath, flavor));
        }

        AddonSummary.Text = SummarizeAddon(wowPath, flavors);
    }

    /// <summary>One line for the folded-up card, e.g. "Installed for Retail and Classic" or "Update available for Retail".</summary>
    private static string SummarizeAddon(string wowPath, List<WowFlavor> flavors)
    {
        if (flavors.Count == 0) return "No game versions found in the World of Warcraft folder.";

        var installed = flavors.Where(f => AddonInstaller.IsInstalled(wowPath, f)).ToList();
        var outdated = installed.Where(f => AddonInstaller.GetInstalledVersion(wowPath, f) != AddonInstaller.GetBundledVersion(f)).ToList();

        static string Names(List<WowFlavor> list) => list.Count switch
        {
            1 => list[0].DisplayName,
            _ => string.Join(", ", list.Take(list.Count - 1).Select(f => f.DisplayName)) + " and " + list[^1].DisplayName
        };

        if (installed.Count == 0) return "Not installed yet. Press Auto install.";
        if (outdated.Count > 0) return $"Update available for {Names(outdated)}. Press Auto install.";
        return $"Installed for {Names(installed)}. Reinstall it after updating Butthurt to get new events.";
    }

    private Control CreateFlavorRow(string wowPath, WowFlavor flavor)
    {
        var installed = AddonInstaller.GetInstalledVersion(wowPath, flavor);
        var isInstalled = AddonInstaller.IsInstalled(wowPath, flavor);
        var bundled = AddonInstaller.GetBundledVersion(flavor);
        var upToDate = isInstalled && installed == bundled;

        var status = !isInstalled ? "Not installed"
            : upToDate ? $"Installed · {installed}"
            : $"Installed · {installed ?? "unknown"} → {bundled ?? "unknown"} available";

        var button = new Button
        {
            Classes = { "bt", "small" },
            Content = !isInstalled ? "Install" : upToDate ? "Reinstall" : "Update",
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = bundled != null
        };
        if (!upToDate) button.Classes.Add("primary");

        button.Click += async (_, _) =>
        {
            try
            {
                AddonInstaller.Install(wowPath, flavor);
                await NotificationService.ShowAsync($"Addon installed for {flavor.DisplayName}. Type /reload in game.");
            }
            catch (UnauthorizedAccessException)
            {
                await NotificationService.ShowAsync("Windows blocked the install. Try running Butthurt as administrator.", 5000);
            }
            catch (Exception ex)
            {
                ActivityLog.Add($"Addon install failed: {ex.Message}", LogLevel.Error);
                await NotificationService.ShowAsync($"Install failed: {ex.Message}", 5000);
            }

            RefreshFlavors();
        };

        return new Border
        {
            Classes = { "row" },
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new PackIconLucide
                    {
                        Kind = upToDate ? PackIconLucideKind.CircleCheck : PackIconLucideKind.Download,
                        Width = 18, Height = 18,
                        Foreground = upToDate ? Ui.Brush("SuccessBrush") : Ui.Brush("MutedBrush"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 14, 0)
                    },
                    Ui.WithColumn(new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Spacing = 2,
                        Children =
                        {
                            Ui.Text(flavor.DisplayName, "h3"),
                            Ui.Text(status, "muted", 12)
                        }
                    }, 1),
                    Ui.WithColumn(button, 2)
                }
            }
        };
    }

    #endregion
}
