using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Watchlist.Configuration;
using MediaBrowser.Common.Plugins;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The rules a setting is judged against, at both boundaries of each one and from
/// both directions: a save that is refused and a load that is repaired.
/// </summary>
/// <remarks>
/// <para>
/// Every case here is one character away from a legal value. A validator tested with
/// obviously wrong input proves it rejects nonsense, which nobody types; what decides
/// whether it is worth having is the value one off the bound, which is the value
/// somebody actually saves.
/// </para>
/// <para>
/// The two directions are asserted separately because they are meant to disagree
/// about what happens and to agree about what is legal. A save is refused so the
/// stored value does not move; a load falls back to the default so a hand-edited file
/// does not throw on every pass. Nothing here trims, rounds or clamps on the save
/// path, which is the silent repair #34 is named against.
/// </para>
/// </remarks>
public class ConfigurationValidationTests
{
    /// <summary>
    /// A configuration nobody has touched is saveable. Without this every refusal
    /// below could be produced by a rule that refuses everything.
    /// </summary>
    [Fact]
    public void TheDefaultConfigurationIsAccepted()
    {
        Assert.Empty(ConfigurationValidation.Refusals(new PluginConfiguration()));
    }

    /// <summary>
    /// Every entry cap at and inside its bounds is accepted, including zero and one.
    /// Neither is nonsense: nothing removes entries when the cap falls under an
    /// existing list, so zero means the list takes nothing further, which is a thing
    /// an administrator can want.
    /// </summary>
    /// <param name="cap">The value to save.</param>
    [Theory]
    [InlineData(SettingLimits.SmallestEntryCap)]
    [InlineData(SettingLimits.SmallestEntryCap + 1)]
    [InlineData(10000)]
    [InlineData(SettingLimits.LargestEntryCap - 1)]
    [InlineData(SettingLimits.LargestEntryCap)]
    public void AnEntryCapInsideItsBoundsIsAccepted(int cap)
    {
        var settings = new PluginConfiguration { MaxEntriesPerUser = cap, MaxEntriesInSharedList = cap };

        Assert.Empty(ConfigurationValidation.Refusals(settings));
    }

    /// <summary>
    /// One under the floor and one over the ceiling, for each of the two caps, with
    /// the refusal naming the setting so a reader of the log knows which field to fix.
    /// </summary>
    /// <param name="cap">The value to save.</param>
    [Theory]
    [InlineData(SettingLimits.SmallestEntryCap - 1)]
    [InlineData(SettingLimits.LargestEntryCap + 1)]
    public void AnEntryCapOutsideItsBoundsIsRefusedByName(int cap)
    {
        var perUser = Assert.Single(
            ConfigurationValidation.Refusals(new PluginConfiguration { MaxEntriesPerUser = cap }));
        var shared = Assert.Single(
            ConfigurationValidation.Refusals(new PluginConfiguration { MaxEntriesInSharedList = cap }));

        Assert.Contains(nameof(PluginConfiguration.MaxEntriesPerUser), perUser, StringComparison.Ordinal);
        Assert.Contains(nameof(PluginConfiguration.MaxEntriesInSharedList), shared, StringComparison.Ordinal);
        Assert.Contains(cap.ToString(CultureInfo.InvariantCulture), perUser, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both ends of the interval range, inside.
    /// </summary>
    /// <param name="hours">The value to save.</param>
    [Theory]
    [InlineData(SettingLimits.ShortestIntervalHours)]
    [InlineData(SettingLimits.ShortestIntervalHours + 1)]
    [InlineData(SettingLimits.LongestIntervalHours - 1)]
    [InlineData(SettingLimits.LongestIntervalHours)]
    public void AnIntervalInsideItsBoundsIsAccepted(int hours)
    {
        var settings = new PluginConfiguration { ReconciliationIntervalHours = hours };

        Assert.Empty(ConfigurationValidation.Refusals(settings));
    }

    /// <summary>
    /// One outside each end. Zero is the value that looks harmless and is not: a
    /// trigger built from it is a schedule that never fires or one that never stops.
    /// </summary>
    /// <param name="hours">The value to save.</param>
    [Theory]
    [InlineData(SettingLimits.ShortestIntervalHours - 1)]
    [InlineData(-1)]
    [InlineData(SettingLimits.LongestIntervalHours + 1)]
    public void AnIntervalOutsideItsBoundsIsRefusedByName(int hours)
    {
        var refusal = Assert.Single(
            ConfigurationValidation.Refusals(new PluginConfiguration { ReconciliationIntervalHours = hours }));

        Assert.Contains(
            nameof(PluginConfiguration.ReconciliationIntervalHours),
            refusal,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A name of one character, a name of exactly the greatest length, and a name with
    /// a space inside it, which is not the same as a space at the end.
    /// </summary>
    /// <param name="name">The value to save.</param>
    [Theory]
    [InlineData("W")]
    [InlineData("Watchlist (plugin)")]
    [InlineData("My list of things to watch one day")]
    public void ANameInsideItsBoundsIsAccepted(string name)
    {
        var settings = new PluginConfiguration { ProjectedListName = name, SharedListName = name };

        Assert.Empty(ConfigurationValidation.Refusals(settings));
    }

    /// <summary>
    /// The longest legal name, built from the bound rather than typed, so this case
    /// follows the bound when it moves.
    /// </summary>
    [Fact]
    public void ANameOfExactlyTheGreatestLengthIsAccepted()
    {
        var name = new string('n', SettingLimits.LongestListNameLength);

        Assert.Empty(ConfigurationValidation.Refusals(new PluginConfiguration { ProjectedListName = name }));
    }

    /// <summary>
    /// One character past the bound, which is the near miss on a length rule.
    /// </summary>
    [Fact]
    public void ANameOneCharacterTooLongIsRefused()
    {
        var name = new string('n', SettingLimits.LongestListNameLength + 1);

        var refusal = Assert.Single(
            ConfigurationValidation.Refusals(new PluginConfiguration { ProjectedListName = name }));

        Assert.Contains(nameof(PluginConfiguration.ProjectedListName), refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The empty and the whitespace-only forms, and the one that matters most: a name
    /// with a single trailing space. It renders identically in the field an
    /// administrator is looking at, and trimming it silently is the repair this rule
    /// exists instead of.
    /// </summary>
    /// <param name="name">The value to save.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Watchlist (plugin) ")]
    [InlineData(" Watchlist (plugin)")]
    [InlineData("\tWatchlist (plugin)")]
    public void ANameThatIsEmptyOrNotTrimmedIsRefusedByName(string name)
    {
        var projected = Assert.Single(
            ConfigurationValidation.Refusals(new PluginConfiguration { ProjectedListName = name }));
        var shared = Assert.Single(
            ConfigurationValidation.Refusals(new PluginConfiguration { SharedListName = name }));

        Assert.Contains(nameof(PluginConfiguration.ProjectedListName), projected, StringComparison.Ordinal);
        Assert.Contains(nameof(PluginConfiguration.SharedListName), shared, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name the page never sent at all. The server deserialises what the browser
    /// posted, so a field that was left out arrives as null rather than as an empty
    /// string, and a rule that only reads length would throw here instead of refusing.
    /// </summary>
    [Fact]
    public void ANameThatWasNotSentAtAllIsRefusedRatherThanThrown()
    {
        var settings = new PluginConfiguration { ProjectedListName = null! };

        var refusal = Assert.Single(ConfigurationValidation.Refusals(settings));

        Assert.Contains(nameof(PluginConfiguration.ProjectedListName), refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every wrong setting is reported, not the first one. An administrator who fixes
    /// one field and meets the next is being handed the same page twice for one visit.
    /// </summary>
    [Fact]
    public void EverySettingThatIsWrongIsNamedInOnePass()
    {
        var settings = new PluginConfiguration
        {
            MaxEntriesPerUser = -1,
            MaxEntriesInSharedList = SettingLimits.LargestEntryCap + 1,
            ReconciliationIntervalHours = 0,
            ProjectedListName = string.Empty,
            SharedListName = "  ",
        };

        var refusals = ConfigurationValidation.Refusals(settings);

        Assert.Equal(5, refusals.Count);
    }

    /// <summary>
    /// A configuration that needs nothing is returned unchanged and reports no repair.
    /// </summary>
    [Fact]
    public void AGoodConfigurationIsRepairedIntoItself()
    {
        var stored = new PluginConfiguration
        {
            MaxEntriesPerUser = 12,
            ProjectionEnabled = false,
            ProjectedListName = "Kept",
            RemoveWhenWatched = true,
            ReconciliationIntervalHours = 12,
            SharedListEnabled = true,
            SharedListName = "Kept as well",
            MaxEntriesInSharedList = 34,
        };

        var repaired = ConfigurationValidation.Repaired(stored, out var repairs);

        Assert.Empty(repairs);
        Assert.Equal(12, repaired.MaxEntriesPerUser);
        Assert.False(repaired.ProjectionEnabled);
        Assert.Equal("Kept", repaired.ProjectedListName);
        Assert.True(repaired.RemoveWhenWatched);
        Assert.Equal(12, repaired.ReconciliationIntervalHours);
        Assert.True(repaired.SharedListEnabled);
        Assert.Equal("Kept as well", repaired.SharedListName);
        Assert.Equal(34, repaired.MaxEntriesInSharedList);
    }

    /// <summary>
    /// Every value a save would refuse falls back to its own default, and every value
    /// beside it is left alone. A repair that resets the whole file because one number
    /// is wrong takes settings from an administrator who never touched them.
    /// </summary>
    [Fact]
    public void EveryBadValueFallsBackToItsOwnDefaultAndNothingElseMoves()
    {
        var stored = new PluginConfiguration
        {
            MaxEntriesPerUser = -5,
            ProjectionEnabled = false,
            ProjectedListName = "  ",
            RemoveWhenWatched = true,
            ReconciliationIntervalHours = 100000,
            SharedListEnabled = true,
            SharedListName = new string('n', SettingLimits.LongestListNameLength + 1),
            MaxEntriesInSharedList = SettingLimits.LargestEntryCap + 1,
        };

        var repaired = ConfigurationValidation.Repaired(stored, out var repairs);

        Assert.Equal(5, repairs.Count);
        Assert.Empty(ConfigurationValidation.Refusals(repaired));

        Assert.Equal(PluginConfiguration.DefaultMaxEntriesPerUser, repaired.MaxEntriesPerUser);
        Assert.Equal(PluginConfiguration.DefaultProjectedListName, repaired.ProjectedListName);
        Assert.Equal(PluginConfiguration.DefaultReconciliationIntervalHours, repaired.ReconciliationIntervalHours);
        Assert.Equal(PluginConfiguration.DefaultSharedListName, repaired.SharedListName);
        Assert.Equal(PluginConfiguration.DefaultMaxEntriesInSharedList, repaired.MaxEntriesInSharedList);

        Assert.False(repaired.ProjectionEnabled);
        Assert.True(repaired.RemoveWhenWatched);
        Assert.True(repaired.SharedListEnabled);
    }

    /// <summary>
    /// Every repair names its setting, so what a person reads says which value moved
    /// rather than that something did.
    /// </summary>
    [Fact]
    public void EveryRepairNamesItsSetting()
    {
        var stored = new PluginConfiguration
        {
            MaxEntriesPerUser = -5,
            MaxEntriesInSharedList = -5,
            ReconciliationIntervalHours = 0,
            ProjectedListName = string.Empty,
            SharedListName = null!,
        };

        var expected = new[]
        {
            nameof(PluginConfiguration.MaxEntriesPerUser),
            nameof(PluginConfiguration.MaxEntriesInSharedList),
            nameof(PluginConfiguration.ReconciliationIntervalHours),
            nameof(PluginConfiguration.ProjectedListName),
            nameof(PluginConfiguration.SharedListName),
        };

        ConfigurationValidation.Repaired(stored, out var repairs);

        Assert.All(expected, setting => Assert.Contains(
            repairs,
            repair => repair.Contains(setting, StringComparison.Ordinal)));
    }

    /// <summary>
    /// The object handed in is not the object handed back, and it is not modified. The
    /// server keeps the instance it deserialised, so repairing it in place would move a
    /// value under a caller that had already read it.
    /// </summary>
    [Fact]
    public void TheStoredConfigurationIsNotModifiedInPlace()
    {
        var stored = new PluginConfiguration { MaxEntriesPerUser = -5 };

        var repaired = ConfigurationValidation.Repaired(stored, out _);

        Assert.NotSame(stored, repaired);
        Assert.Equal(-5, stored.MaxEntriesPerUser);
    }

    /// <summary>
    /// Neither direction takes a null. A configuration that is not there is a caller's
    /// mistake rather than a value to judge, and answering it with "no refusals" would
    /// read as a save this plugin accepted.
    /// </summary>
    [Fact]
    public void NeitherDirectionAcceptsAMissingConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigurationValidation.Refusals(null!));
        Assert.Throws<ArgumentNullException>(() => ConfigurationValidation.Repaired(null!, out _));
    }

    /// <summary>
    /// The two directions agree about what is legal, over every case this file
    /// exercises. They are allowed to differ in what they do and not in where the line
    /// is, and a rule added to one and forgotten in the other is exactly how they come
    /// apart.
    /// </summary>
    [Fact]
    public void WhatALoadRepairsIsWhatASaveRefuses()
    {
        foreach (var stored in EveryCase())
        {
            var refused = ConfigurationValidation.Refusals(stored).Count > 0;
            ConfigurationValidation.Repaired(stored, out var repairs);

            Assert.Equal(refused, repairs.Count > 0);
        }
    }

    /// <summary>
    /// The seam the save path depends on, read off the server's own type rather than
    /// off a comment about it. If <c>UpdateConfiguration</c> ever stops being virtual,
    /// this plugin's refusal stops being reached and nothing else here would say so.
    /// </summary>
    [Fact]
    public void TheServersSavePathIsStillOverridable()
    {
        var method = typeof(BasePlugin<PluginConfiguration>).GetMethod(
            "UpdateConfiguration",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.True(method.IsVirtual);
        Assert.False(method.IsFinal);
    }

    /// <summary>
    /// And that this plugin takes it. The override itself cannot be executed here:
    /// reaching it means constructing the server's base, which wants the application
    /// paths and the XML serialiser and writes a configuration file where the server
    /// keeps them, which is the reason Plugin.cs is the file the coverage floor
    /// excludes. So this reads that the method is declared on this type and the rest
    /// of the save path is unexercised by any test in this suite. That is a stated
    /// absence rather than a gap somebody has to find.
    /// </summary>
    [Fact]
    public void ThisPluginTakesThatSeam()
    {
        var declared = typeof(Plugin).GetMethod(
            "UpdateConfiguration",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(declared);
        Assert.Equal(typeof(Plugin), declared.DeclaringType);
    }

    /// <summary>
    /// The seam the load path depends on, for the same reason. The property that loads
    /// the configuration is not virtual and the method behind it is private, so the
    /// only way in is the protected setter, and a plugin repairing a stored value has
    /// nowhere else to put the result.
    /// </summary>
    [Fact]
    public void TheServersLoadedConfigurationCanStillBeReplaced()
    {
        var property = typeof(BasePlugin<PluginConfiguration>).GetProperty(
            "Configuration",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.NotNull(property.GetMethod);
        Assert.False(property.GetMethod!.IsVirtual);

        var setter = property.SetMethod;
        Assert.NotNull(setter);
        Assert.True(setter!.IsFamily);
    }

    private static IEnumerable<PluginConfiguration> EveryCase()
    {
        yield return new PluginConfiguration();
        yield return new PluginConfiguration { MaxEntriesPerUser = SettingLimits.SmallestEntryCap };
        yield return new PluginConfiguration { MaxEntriesPerUser = SettingLimits.SmallestEntryCap - 1 };
        yield return new PluginConfiguration { MaxEntriesPerUser = SettingLimits.LargestEntryCap };
        yield return new PluginConfiguration { MaxEntriesPerUser = SettingLimits.LargestEntryCap + 1 };
        yield return new PluginConfiguration { MaxEntriesInSharedList = SettingLimits.LargestEntryCap + 1 };
        yield return new PluginConfiguration { ReconciliationIntervalHours = SettingLimits.ShortestIntervalHours };
        yield return new PluginConfiguration { ReconciliationIntervalHours = SettingLimits.ShortestIntervalHours - 1 };
        yield return new PluginConfiguration { ReconciliationIntervalHours = SettingLimits.LongestIntervalHours };
        yield return new PluginConfiguration { ReconciliationIntervalHours = SettingLimits.LongestIntervalHours + 1 };
        yield return new PluginConfiguration { ProjectedListName = "A" };
        yield return new PluginConfiguration { ProjectedListName = "A " };
        yield return new PluginConfiguration { ProjectedListName = string.Empty };
        yield return new PluginConfiguration { SharedListName = new string('n', SettingLimits.LongestListNameLength) };
        yield return new PluginConfiguration { SharedListName = new string('n', SettingLimits.LongestListNameLength + 1) };
    }
}
