namespace Jellyfin.Plugin.Watchlist.Configuration;

/// <summary>
/// The bounds every setting is judged against, in one place.
/// </summary>
/// <remarks>
/// <para>
/// They are here rather than inside the validator because three surfaces have to
/// agree on them: the validator that refuses a saved value, the controls on the
/// configuration page that stop a browser posting one, and docs/settings.md, which is
/// where an administrator reads what a field will take. A number written separately in
/// three places is the one that drifts.
/// </para>
/// <para>
/// Each bound below carries the reason for its value. None of them is a preference
/// about how a list should be used: a bound this plugin enforces is a bound on what it
/// may cost a server it does not own, and anything narrower than that belongs to
/// whoever curates the list.
/// </para>
/// </remarks>
public static class SettingLimits
{
    /// <summary>
    /// The smallest entry cap that means something.
    /// </summary>
    /// <remarks>
    /// Zero, and it is a legal value rather than nonsense to refuse. Nothing removes
    /// entries when the cap falls under an existing list: that list stops growing and
    /// says why, which is what the configuration and docs/settings.md both state. So
    /// zero means "this list takes nothing further" and one means "it takes one more",
    /// and both are things an administrator can want. What is refused is a negative
    /// number, which means nothing at all.
    /// </remarks>
    public const int SmallestEntryCap = 0;

    /// <summary>
    /// The largest entry cap.
    /// </summary>
    /// <remarks>
    /// A million. The cap exists so a list cannot grow without bound on a server this
    /// plugin does not own, and a cap with no ceiling of its own gives that back the
    /// moment somebody types an extra digit. A document is read whole on every request
    /// that touches it, so the ceiling is set where reading one stops being cheap
    /// rather than where a person stops curating: a hundred times the default, and far
    /// above any library. What it costs a server at that size was not measured, and
    /// nothing here claims the value is a performance finding.
    /// </remarks>
    public const int LargestEntryCap = 1000000;

    /// <summary>
    /// The shortest reconciliation interval.
    /// </summary>
    /// <remarks>
    /// One hour. The scheduled pass converges what the server's own events missed
    /// rather than being how a list is kept current, so a pass more often than hourly
    /// is asking a converging pass to do the job of the events. Zero and negative
    /// values are a trigger that never fires or fires continuously, and neither is a
    /// schedule.
    /// </remarks>
    public const int ShortestIntervalHours = 1;

    /// <summary>
    /// The longest reconciliation interval.
    /// </summary>
    /// <remarks>
    /// A week, in hours. Past that the pass stops being a convergence and becomes a
    /// repair somebody eventually notices, and an administrator who wants it that rare
    /// wants the projection switched off instead, which is its own setting.
    /// </remarks>
    public const int LongestIntervalHours = 168;

    /// <summary>
    /// The greatest length a list name may have.
    /// </summary>
    /// <remarks>
    /// A hundred and twenty-eight characters. This is a display bound and not a
    /// storage one: the name is what a client renders in a list row, and a name longer
    /// than a row is a name nobody reads to the end. No limit imposed by the server's
    /// database or by a filesystem was measured, and none is claimed here; what this
    /// refuses is a paste of a document into the field, which is the way the value
    /// actually goes wrong.
    /// </remarks>
    public const int LongestListNameLength = 128;
}
