using System;
using System.Collections.Generic;
using System.Linq;
using Butthurt.Twitch.Models;

namespace Butthurt.Twitch.Services;

public enum RewardChangeKind
{
    Create,
    Update,

    /// <summary>A reward made by hand on the Twitch page that a rule now asks for: the rules take it over.</summary>
    Adopt,
    Delete
}

public record RewardChange(RewardChangeKind Kind, string Title, int Cost, bool Enabled, ManagedReward? Existing);

/// <summary>
/// Works out what has to change on Twitch so the channel's rewards match what the rules ask for.
/// Pure logic, the Twitch calls happen in <see cref="TwitchService"/>.
/// </summary>
public static class RewardSync
{
    /// <summary>
    /// Merges what every rule asks for. Twitch reward names are unique (ignoring case), so rules naming the
    /// same reward share it: the first rule's cost wins and it is visible while any of them is on.
    /// </summary>
    public static List<DesiredReward> Merge(IEnumerable<DesiredReward> wanted) =>
        wanted.Where(w => !string.IsNullOrWhiteSpace(w.Title))
            .Select(w => w with { Title = w.Title.Trim(), Cost = Math.Max(1, w.Cost) })
            .GroupBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DesiredReward(g.First().Title, g.First().Cost, g.Any(w => w.Enabled)))
            .ToList();

    public static List<RewardChange> Plan(IEnumerable<DesiredReward> wanted, IReadOnlyList<ManagedReward> existing)
    {
        var changes = new List<RewardChange>();
        var desired = Merge(wanted);

        foreach (var want in desired)
        {
            var current = existing.FirstOrDefault(r => string.Equals(r.Title, want.Title, StringComparison.OrdinalIgnoreCase));

            if (current == null)
            {
                changes.Add(new RewardChange(RewardChangeKind.Create, want.Title, want.Cost, want.Enabled, null));
            }
            else if (!current.OwnedByRules)
            {
                changes.Add(new RewardChange(RewardChangeKind.Adopt, want.Title, want.Cost, want.Enabled, current));
            }
            else if (current.Cost != want.Cost || current.IsEnabled != want.Enabled)
            {
                changes.Add(new RewardChange(RewardChangeKind.Update, want.Title, want.Cost, want.Enabled, current));
            }
        }

        // Rule rewards nobody asks for anymore: the rule was deleted, renamed or stopped managing it.
        // Rewards made by hand are never touched.
        foreach (var reward in existing.Where(r => r.OwnedByRules))
        {
            if (!desired.Any(d => string.Equals(d.Title, reward.Title, StringComparison.OrdinalIgnoreCase)))
            {
                changes.Add(new RewardChange(RewardChangeKind.Delete, reward.Title, reward.Cost, reward.IsEnabled, reward));
            }
        }

        return changes;
    }
}
