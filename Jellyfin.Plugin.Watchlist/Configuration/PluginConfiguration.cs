using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Watchlist.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// <para>
/// The template's four demonstration settings were removed in #90. What is here is the
/// set #32 fixes and nothing else: the projection's switch and the name it writes, the
/// watched rule's switch, the reconciliation interval, the shared list's switch, name
/// and cap, and the per-user cap the store cannot do without. Anything beyond that
/// needs its own issue and a reason.
/// </para>
/// <para>
/// Every default here has to work on a server where nobody opened the page, because
/// that is most of them. The projection is on, both lists have names, and both caps are
/// set. The two things that are off by default are the two a server should not gain
/// without an administrator asking: a list every user can see, and a rule that takes
/// entries off a list by itself.
/// </para>
/// <para>
/// Nothing here validates a value. Refusing a bad one at save and repairing one that
/// was hand-edited into the configuration file is #34, and the bounds it enforces are
/// stated at each setting below rather than invented there.
/// </para>
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// The number of entries one user's list may hold when nothing says otherwise.
    /// </summary>
    /// <remarks>
    /// Ten thousand, which is the number the upstream attempt at a native watchlist
    /// chose for the same reason, in its MaxItemsPerUserList. Matching it means a user
    /// who ever moves between the two meets one bound rather than two, and it means
    /// the number is not one this board invented. It sits far above any list a person
    /// curates by hand and far below the size at which reading the document costs a
    /// server anything noticeable.
    /// </remarks>
    public const int DefaultMaxEntriesPerUser = 10000;

    /// <summary>
    /// The number of entries the shared list may hold when nothing says otherwise.
    /// </summary>
    /// <remarks>
    /// The same number as the per-user bound, for the same reason and not for a
    /// different one: it is the size at which a list stops being something a person
    /// reads. The shared list has one copy on the server rather than one per user, so
    /// it costs less than the per-user bound does, and a smaller number here would be a
    /// rule about how a list should be used rather than a bound on what a server can
    /// carry.
    /// </remarks>
    public const int DefaultMaxEntriesInSharedList = 10000;

    /// <summary>
    /// The name the projected private list carries when nothing says otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE RULE, and it is the reason this is not the word "Watchlist" on its own. The
    /// projected playlist takes a name a server's own list would not take, and the
    /// default says which plugin made it rather than claiming the generic word. Two
    /// lists both called "Watchlist" in one client is the failure this exists against,
    /// and it is one a user cannot resolve by looking, because a playlist does not say
    /// what created it.
    /// </para>
    /// <para>
    /// A server growing a watchlist of its own is not hypothetical; the upstream
    /// attempt is open and measured in docs/coexistence.md, which is where this rule
    /// was written before this setting existed and where the position it serves is
    /// argued. Somebody moving this value off its default is choosing to take that
    /// risk, which is a choice they may make; somebody landing a new default without
    /// reading this is not, which is why the sentence is here rather than only there.
    /// </para>
    /// </remarks>
    public const string DefaultProjectedListName = "Watchlist (plugin)";

    /// <summary>
    /// The name the projected shared list carries when nothing says otherwise.
    /// </summary>
    /// <remarks>
    /// The same rule as <see cref="DefaultProjectedListName"/>, and it applies harder
    /// here: one shared list is visible to every user on the server, so a collision is
    /// met by everybody rather than by one person.
    /// </remarks>
    public const string DefaultSharedListName = "Shared Watchlist (plugin)";

    /// <summary>
    /// The number of hours between reconciliation runs when nothing says otherwise.
    /// </summary>
    /// <remarks>
    /// Six hours. The scheduled pass exists to converge what the events missed, not to
    /// be how the projection is kept current, so the interval is chosen against the
    /// cost of a run rather than against how fresh a list feels: a run over a server
    /// whose projections are already correct issues no write, which #24 asks for and
    /// proves, and a run that finds nothing is close to free. Four runs a day puts a
    /// missed event right within one working day without a person noticing, and leaves
    /// the value far enough from the bottom of #34's range that lowering it is a
    /// deliberate act.
    /// </remarks>
    public const int DefaultReconciliationIntervalHours = 6;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        MaxEntriesPerUser = DefaultMaxEntriesPerUser;
        ProjectionEnabled = true;
        ProjectedListName = DefaultProjectedListName;
        RemoveWhenWatched = false;
        ReconciliationIntervalHours = DefaultReconciliationIntervalHours;
        SharedListEnabled = false;
        SharedListName = DefaultSharedListName;
        MaxEntriesInSharedList = DefaultMaxEntriesInSharedList;
    }

    /// <summary>
    /// Gets or sets the greatest number of entries one user's list may hold.
    /// </summary>
    /// <remarks>
    /// An add that would take a list past this is refused and nothing is written.
    /// Lowering it under an existing list removes nothing: that list stops growing and
    /// says why. Validating the value on save and on load is #34.
    /// </remarks>
    public int MaxEntriesPerUser { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the projection runs at all.
    /// </summary>
    /// <remarks>
    /// On by default, because a plugin whose whole client surface is the projection
    /// shows a user nothing until somebody finds this. Turning it off stops the
    /// projection and the scheduled pass and changes no stored document, which is the
    /// promise #38 makes about a disabled plugin and is the same promise made one
    /// setting smaller. The projection itself is M3 and is not built yet, so today this
    /// value is read by nothing.
    /// </remarks>
    public bool ProjectionEnabled { get; set; }

    /// <summary>
    /// Gets or sets the displayed name of each user's projected private list.
    /// </summary>
    /// <remarks>
    /// The rule the default follows, and the reason it is not the generic word, is at
    /// <see cref="DefaultProjectedListName"/>. Changing this renames the projected
    /// playlist rather than making a second one, which is #35.
    /// </remarks>
    public string ProjectedListName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an entry leaves a private list once the
    /// user has watched it.
    /// </summary>
    /// <remarks>
    /// Off by default. A list somebody curated losing entries without being asked is a
    /// surprise rather than a feature, and it is not one a user can undo: nothing
    /// records what was taken off. On, the rule is series-aware and is #21. It never
    /// touches the shared list, by answer 9 on #1, because watched is individual and
    /// what one person finished is not what another has.
    /// </remarks>
    public bool RemoveWhenWatched { get; set; }

    /// <summary>
    /// Gets or sets the number of hours between scheduled reconciliation runs.
    /// </summary>
    /// <remarks>
    /// The default and the reason for that number are at
    /// <see cref="DefaultReconciliationIntervalHours"/>. The task this drives is #24
    /// and is not built yet, so today this value is read by nothing.
    /// </remarks>
    public int ReconciliationIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this server offers a shared list.
    /// </summary>
    /// <remarks>
    /// Off by default, and that is a privacy default rather than a convenience one. A
    /// server that gains a list every user can see without an administrator asking for
    /// one is a surprise, and by answer 8 on #1 an entry on that list carries and shows
    /// who put it there, so what a user adds to it is attributable to them in front of
    /// everybody. Answer 6 on #1 is that there is exactly one, which is why its name
    /// and its cap are settings here rather than fields on a record.
    /// </remarks>
    public bool SharedListEnabled { get; set; }

    /// <summary>
    /// Gets or sets the displayed name of the shared list.
    /// </summary>
    /// <remarks>
    /// The rule the default follows is at <see cref="DefaultSharedListName"/>. The
    /// value is kept whether or not the list is switched on, so turning the list off
    /// and on again does not lose the name an administrator chose.
    /// </remarks>
    public string SharedListName { get; set; }

    /// <summary>
    /// Gets or sets the greatest number of entries the shared list may hold.
    /// </summary>
    /// <remarks>
    /// The shared list is bounded separately from a private one because it is written
    /// by everybody, by answer 7 on #1, so the number of people who can grow it is the
    /// number of people on the server. It behaves the same way at the bound: an add
    /// past it is refused and nothing is written, and lowering it under a list that is
    /// already larger removes nothing.
    /// </remarks>
    public int MaxEntriesInSharedList { get; set; }
}
