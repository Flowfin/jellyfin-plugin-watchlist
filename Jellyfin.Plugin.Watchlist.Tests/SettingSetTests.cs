using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Watchlist.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The set of server-wide settings, and the value each one holds on a server where
/// nobody opened the page.
/// </summary>
/// <remarks>
/// <para>
/// #32 fixes the set rather than leaving it to grow: the eight below and nothing else,
/// with anything further needing its own issue and a reason. A list written in a
/// document is a description of what somebody meant; the list here is read against the
/// class, so a ninth setting arriving without that issue reds the run at the moment it
/// is added rather than at review.
/// </para>
/// <para>
/// The defaults are asserted by value as well, and that is a different rule from the
/// one <see cref="SettingDefaultsTests"/> holds. That class reads the class against
/// docs/settings.md, so the two agreeing is what it proves; moving a default in both
/// places passes it. What is written here is the value itself, so moving one is an edit
/// to this file, in the change that moved it, argued in that change. A default is the
/// value most installations actually run on and the one least likely to be noticed when
/// it moves.
/// </para>
/// </remarks>
public class SettingSetTests
{
    /// <summary>
    /// The set, and the value each member holds when nothing has been saved.
    /// </summary>
    /// <remarks>
    /// Written as the settings document writes them, which is the form
    /// <see cref="SettingDefaultsTests"/> compares against and is what a person reads
    /// on the page. Ordinal order, so this reads beside the class rather than beside a
    /// culture.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> TheSet =
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["MaxEntriesInSharedList"] = "10000",
            ["MaxEntriesPerUser"] = "10000",
            ["ProjectedListName"] = "Watchlist (plugin)",
            ["ProjectionEnabled"] = "true",
            ["ReconciliationIntervalHours"] = "6",
            ["RemoveWhenWatched"] = "false",
            ["SharedListEnabled"] = "false",
            ["SharedListName"] = "Shared Watchlist (plugin)",
        };

    /// <summary>
    /// The set is exactly this. A setting added to the class without being added here
    /// is refused, which is what makes "anything else needs its own issue" a rule
    /// rather than an intention, and one removed from the class is refused from the
    /// other side.
    /// </summary>
    [Fact]
    public void TheConfigurationDeclaresExactlyTheFixedSet()
    {
        Assert.Equal(TheSet.Keys, Settings().Select(s => s.Name));
    }

    /// <summary>
    /// A check that reads nothing passes everything.
    /// </summary>
    [Fact]
    public void TheFixedSetIsNotEmpty()
    {
        Assert.NotEmpty(TheSet);
        Assert.NotEmpty(Settings());
    }

    /// <summary>
    /// Every setting holds its stated default on a configuration nobody has saved.
    /// </summary>
    [Fact]
    public void AFreshConfigurationHoldsEveryStatedDefault()
    {
        Assert.Equal(TheSet, Held(new PluginConfiguration()));
    }

    /// <summary>
    /// The two defaults a fresh install depends on being off, named rather than left
    /// to be read out of a table. Both are off for a reason that is not tidiness: a
    /// list every user on the server can see is a thing an administrator asks for, and
    /// a list that loses entries by itself is a surprise nobody can undo.
    /// </summary>
    [Fact]
    public void TheTwoSettingsThatAreOffByDefaultAreOff()
    {
        var fresh = new PluginConfiguration();

        Assert.False(fresh.SharedListEnabled);
        Assert.False(fresh.RemoveWhenWatched);
    }

    /// <summary>
    /// The other direction, which is what the third condition on #32 asks for: a fresh
    /// install works with no visit to the page, so the projection is on, both lists
    /// have a name, and every bound is set.
    /// </summary>
    [Fact]
    public void AFreshInstallWorksWithNoVisitToThePage()
    {
        var fresh = new PluginConfiguration();

        Assert.True(fresh.ProjectionEnabled);
        Assert.NotEmpty(fresh.ProjectedListName);
        Assert.NotEmpty(fresh.SharedListName);
        Assert.True(fresh.MaxEntriesPerUser > 0);
        Assert.True(fresh.MaxEntriesInSharedList > 0);
        Assert.True(fresh.ReconciliationIntervalHours > 0);
    }

    /// <summary>
    /// Neither projected name is the bare word a server's own list would take. The
    /// rule is argued at the default in the configuration class and in
    /// docs/coexistence.md; what is refused here is the one value that breaks it,
    /// because two lists under one name in one client is a state a user cannot resolve
    /// by looking.
    /// </summary>
    [Fact]
    public void NeitherProjectedNameClaimsTheGenericWord()
    {
        var fresh = new PluginConfiguration();

        Assert.NotEqual("Watchlist", fresh.ProjectedListName, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("Watchlist", fresh.SharedListName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The near miss on the set: one setting gone. It is the shape a rename produces,
    /// where the class carries a name this file does not and this file carries a name
    /// the class does not, and the comparison sees both halves at once.
    /// </summary>
    [Fact]
    public void ASetMissingOneSettingIsRefused()
    {
        var missing = TheSet.Keys
            .Where(name => !string.Equals(name, "SharedListName", StringComparison.Ordinal))
            .ToList();

        Assert.NotEqual(missing, Settings().Select(s => s.Name).ToList());
    }

    /// <summary>
    /// The near miss on a default, and it is the one that has no symptom: a value moved
    /// in the class while every sentence about it stays as it was. The mutation is on
    /// the expectation rather than on the class, because a test cannot rewrite the
    /// constant it was compiled against, and it produces the same disagreement from the
    /// same side.
    /// </summary>
    [Fact]
    public void ADefaultThatMovedByOneIsRefused()
    {
        var moved = new SortedDictionary<string, string>(
            TheSet.ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.Ordinal),
            StringComparer.Ordinal)
        {
            ["ReconciliationIntervalHours"] = "7",
        };

        Assert.NotEqual(TheSet, moved);
        Assert.NotEqual<IReadOnlyDictionary<string, string>>(moved, Held(new PluginConfiguration()));
    }

    /// <summary>
    /// The one-change neighbour of both mutations above is the tree as it is committed,
    /// and it trips neither. Without this the two tests above would prove the mutations
    /// are unusual rather than that the comparisons read them.
    /// </summary>
    [Fact]
    public void TheCommittedConfigurationTripsNeitherComparison()
    {
        Assert.Equal(TheSet.Keys, Settings().Select(s => s.Name));
        Assert.Equal(TheSet, Held(new PluginConfiguration()));
    }

    /// <summary>
    /// The settings a server can set, declared on this plugin's own configuration class
    /// rather than inherited from the server's base, which is the same set the page and
    /// the settings document are read against.
    /// </summary>
    /// <returns>The settings, ordered by name.</returns>
    private static IReadOnlyList<PropertyInfo> Settings() => typeof(PluginConfiguration)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// What a configuration holds, keyed by setting name and written as text so a
    /// failure names both values rather than reporting two boxed objects.
    /// </summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns>The value of every setting.</returns>
    private static IReadOnlyDictionary<string, string> Held(PluginConfiguration configuration)
    {
        var held = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var setting in Settings())
        {
            var value = setting.GetValue(configuration);

            held[setting.Name] = value is bool flag
                ? (flag ? "true" : "false")
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return held;
    }
}
