using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.Watchlist.Configuration;

/// <summary>
/// What a setting has to be, in one place, read from two directions.
/// </summary>
/// <remarks>
/// <para>
/// A value arriving from the configuration page is untrusted input: it is JSON a
/// browser posted, and the server hands it to this plugin without looking at it. A
/// value arriving off disk is untrusted for a different reason, because the plugin's
/// configuration file can be edited by hand and nothing on that path passes a page at
/// all.
/// </para>
/// <para>
/// The two are answered differently on purpose. A save is REFUSED, so the value the
/// server has does not move and the administrator who typed it is the one who fixes
/// it. A load is REPAIRED to the default, because there is nobody to tell at that
/// moment and the alternative is a plugin that throws on every pass over a file
/// somebody edited months ago. Both directions read the same rules from here, so the
/// two cannot drift into disagreeing about what a legal value is.
/// </para>
/// <para>
/// Nothing here trims, rounds or clamps a value on the save path. Repairing a save
/// quietly is what this issue is named against: an administrator who types a name with
/// a trailing space and is shown a saved page has been told their value was taken.
/// </para>
/// </remarks>
public static class ConfigurationValidation
{
    /// <summary>
    /// Every reason the given configuration may not be saved, one sentence per setting
    /// that is wrong, each naming the setting.
    /// </summary>
    /// <param name="configuration">The configuration a save would store.</param>
    /// <returns>The refusals, empty where there are none.</returns>
    /// <remarks>
    /// Every setting is judged rather than the first failure being returned, because
    /// an administrator who fixes one field and meets the next one is being handed the
    /// same page twice for one visit.
    /// </remarks>
    public static IReadOnlyList<string> Refusals(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var refusals = new List<string>();

        AddCapRefusal(refusals, nameof(configuration.MaxEntriesPerUser), configuration.MaxEntriesPerUser);
        AddCapRefusal(refusals, nameof(configuration.MaxEntriesInSharedList), configuration.MaxEntriesInSharedList);
        AddNameRefusal(refusals, nameof(configuration.ProjectedListName), configuration.ProjectedListName);
        AddNameRefusal(refusals, nameof(configuration.SharedListName), configuration.SharedListName);

        if (configuration.ReconciliationIntervalHours < SettingLimits.ShortestIntervalHours
            || configuration.ReconciliationIntervalHours > SettingLimits.LongestIntervalHours)
        {
            refusals.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0} is {1}. It has to be between {2} and {3} hours.",
                nameof(configuration.ReconciliationIntervalHours),
                configuration.ReconciliationIntervalHours,
                SettingLimits.ShortestIntervalHours,
                SettingLimits.LongestIntervalHours));
        }

        return refusals;
    }

    /// <summary>
    /// The given configuration with every value this would refuse replaced by its
    /// default, and one sentence per replacement.
    /// </summary>
    /// <param name="configuration">The configuration as it was read.</param>
    /// <param name="repairs">What was replaced, empty where nothing was.</param>
    /// <returns>A configuration that <see cref="Refusals"/> would accept.</returns>
    /// <remarks>
    /// The instance handed in is not modified. The server keeps the object it
    /// deserialised, and repairing it in place would leave a caller that had already
    /// read it holding a value that has since changed underneath.
    /// </remarks>
    public static PluginConfiguration Repaired(
        PluginConfiguration configuration,
        out IReadOnlyList<string> repairs)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var repaired = new PluginConfiguration
        {
            MaxEntriesPerUser = configuration.MaxEntriesPerUser,
            ProjectionEnabled = configuration.ProjectionEnabled,
            ProjectedListName = configuration.ProjectedListName,
            RemoveWhenWatched = configuration.RemoveWhenWatched,
            ReconciliationIntervalHours = configuration.ReconciliationIntervalHours,
            SharedListEnabled = configuration.SharedListEnabled,
            SharedListName = configuration.SharedListName,
            MaxEntriesInSharedList = configuration.MaxEntriesInSharedList,
        };

        var written = new List<string>();

        if (IsBadCap(repaired.MaxEntriesPerUser))
        {
            written.Add(Replacement(
                nameof(repaired.MaxEntriesPerUser),
                repaired.MaxEntriesPerUser,
                PluginConfiguration.DefaultMaxEntriesPerUser));
            repaired.MaxEntriesPerUser = PluginConfiguration.DefaultMaxEntriesPerUser;
        }

        if (IsBadCap(repaired.MaxEntriesInSharedList))
        {
            written.Add(Replacement(
                nameof(repaired.MaxEntriesInSharedList),
                repaired.MaxEntriesInSharedList,
                PluginConfiguration.DefaultMaxEntriesInSharedList));
            repaired.MaxEntriesInSharedList = PluginConfiguration.DefaultMaxEntriesInSharedList;
        }

        if (IsBadInterval(repaired.ReconciliationIntervalHours))
        {
            written.Add(Replacement(
                nameof(repaired.ReconciliationIntervalHours),
                repaired.ReconciliationIntervalHours,
                PluginConfiguration.DefaultReconciliationIntervalHours));
            repaired.ReconciliationIntervalHours = PluginConfiguration.DefaultReconciliationIntervalHours;
        }

        if (IsBadName(repaired.ProjectedListName))
        {
            written.Add(NameReplacement(
                nameof(repaired.ProjectedListName),
                PluginConfiguration.DefaultProjectedListName));
            repaired.ProjectedListName = PluginConfiguration.DefaultProjectedListName;
        }

        if (IsBadName(repaired.SharedListName))
        {
            written.Add(NameReplacement(
                nameof(repaired.SharedListName),
                PluginConfiguration.DefaultSharedListName));
            repaired.SharedListName = PluginConfiguration.DefaultSharedListName;
        }

        repairs = written;
        return repaired;
    }

    /// <summary>
    /// Whether an entry cap is one this plugin will act on.
    /// </summary>
    /// <param name="cap">The value to judge.</param>
    /// <returns>True where it is outside the bounds.</returns>
    private static bool IsBadCap(int cap) =>
        cap < SettingLimits.SmallestEntryCap || cap > SettingLimits.LargestEntryCap;

    /// <summary>
    /// Whether a reconciliation interval is one a trigger can be built from.
    /// </summary>
    /// <param name="hours">The value to judge.</param>
    /// <returns>True where it is outside the bounds.</returns>
    private static bool IsBadInterval(int hours) =>
        hours < SettingLimits.ShortestIntervalHours || hours > SettingLimits.LongestIntervalHours;

    /// <summary>
    /// Whether a list name is one a client can render and a person can read.
    /// </summary>
    /// <param name="name">The value to judge.</param>
    /// <returns>True where it is empty, untrimmed or too long.</returns>
    /// <remarks>
    /// A name that is not already trimmed is wrong rather than repairable on the save
    /// path, because trimming it is exactly the silent repair this file refuses. Null
    /// counts as empty: the server deserialises whatever the page posted, and a field
    /// that was not sent arrives as one.
    /// </remarks>
    private static bool IsBadName(string? name) =>
        string.IsNullOrWhiteSpace(name)
        || !string.Equals(name, name.Trim(), StringComparison.Ordinal)
        || name.Length > SettingLimits.LongestListNameLength;

    private static void AddCapRefusal(List<string> refusals, string setting, int cap)
    {
        if (!IsBadCap(cap))
        {
            return;
        }

        refusals.Add(string.Format(
            CultureInfo.InvariantCulture,
            "{0} is {1}. It has to be between {2} and {3}.",
            setting,
            cap,
            SettingLimits.SmallestEntryCap,
            SettingLimits.LargestEntryCap));
    }

    private static void AddNameRefusal(List<string> refusals, string setting, string? name)
    {
        if (!IsBadName(name))
        {
            return;
        }

        refusals.Add(string.Format(
            CultureInfo.InvariantCulture,
            "{0} has to be a name with no leading or trailing space, at least one character and at most {1}.",
            setting,
            SettingLimits.LongestListNameLength));
    }

    private static string Replacement(string setting, int held, int fallback) => string.Format(
        CultureInfo.InvariantCulture,
        "{0} was {1}, which is outside what this plugin accepts, so {2} is used instead.",
        setting,
        held,
        fallback);

    private static string NameReplacement(string setting, string fallback) => string.Format(
        CultureInfo.InvariantCulture,
        "{0} was not a name this plugin accepts, so \"{1}\" is used instead.",
        setting,
        fallback);
}
