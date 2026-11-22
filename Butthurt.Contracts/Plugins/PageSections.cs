using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace Butthurt.Contracts.Plugins;

/// <summary>
/// Lets one plugin put a card on another plugin's page. Hardware plugins (PiShock…) only hold their setup on their own
/// page; what Twitch or Fansly should make them do is a card on the Twitch or Fansly page, offered from here.
/// Platform pages call <see cref="CreateFor"/> and show whatever comes back below their own cards.
/// </summary>
public static class PageSections
{
    private sealed record Entry(string PageId, IPlugin Owner, int Order, Func<Control> Create);

    private static readonly List<Entry> Entries = new();

    /// <param name="pageId">The id of the plugin whose page gets the card, e.g. "Twitch".</param>
    /// <param name="owner">The plugin offering it. The card only shows while that plugin is on (and available).</param>
    /// <param name="create">Makes a new card. Called each time the page rebuilds its extra cards.</param>
    public static void Register(string pageId, IPlugin owner, Func<Control> create, int order = 0)
    {
        lock (Entries) Entries.Add(new Entry(pageId, owner, order, create));
    }

    /// <summary>New cards for the page, from every plugin that is turned on, in order.</summary>
    public static List<Control> CreateFor(string pageId)
    {
        List<Entry> entries;
        lock (Entries) entries = Entries.Where(e => e.PageId == pageId).OrderBy(e => e.Order).ToList();

        var active = PluginManager.Plugins;
        return entries.Where(e => active.Contains(e.Owner)).Select(e => e.Create()).ToList();
    }
}
