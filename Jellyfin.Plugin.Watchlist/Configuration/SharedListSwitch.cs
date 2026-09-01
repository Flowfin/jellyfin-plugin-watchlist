namespace Jellyfin.Plugin.Watchlist.Configuration;

/// <summary>
/// What <see cref="PluginConfiguration.SharedListEnabled"/> means to the routes that
/// serve the shared list's CONTENTS: while it says no, this server has no shared list
/// for them to answer from.
/// </summary>
/// <remarks>
/// <para>
/// THE SWITCH IS THE SERVER'S STATEMENT THAT IT OFFERS A SHARED LIST, and until #277
/// only the route that MAKES one read it. Every route over an existing list keyed on
/// whether a document was on disk, so an administrator could turn the switch on, make
/// the list, and turn the switch off, and the configuration page would then say the
/// server offers no shared list while every user of it could still read that list, add
/// to it and take their own entries off it. Nothing refused that sequence and nothing
/// reported it.
/// </para>
/// <para>
/// THE REPAIR TAKEN IS THIS ONE: the contents routes read the setting and answer as
/// though there were no list. Turning the feature off has to mean the list is not
/// readable, and the only surfaces that can promise that are the ones that serve the
/// contents.
/// </para>
/// <para>
/// THE REPAIR REFUSED IS REFUSING THE SAVE. The other way to close the gap was to
/// refuse the configuration page while a list exists, and it is named here rather than
/// left unmentioned: it leaves an administrator unable to turn the feature off at all
/// while a list is there, which is the wrong place for the enforcement, because a
/// setting that cannot be set is not a setting.
/// </para>
/// <para>
/// THE LIST ON DISK IS LEFT ALONE. This governs visibility and never existence, so
/// turning the switch back on restores exactly what was there, and a list is taken
/// away with the removal endpoint rather than by moving a value on a page.
/// </para>
/// <para>
/// ONE PLACE ANSWERS IT, which is the point of this type existing at all rather than
/// the condition being spelled at each route. Three surfaces serve the contents of that
/// list - the two item routes and the read on the watchlist controller, and the export
/// and import pair on the transfer controller - and a condition written at each of them
/// is one question with three answers that drift.
/// </para>
/// </remarks>
internal static class SharedListSwitch
{
    /// <summary>
    /// Whether the routes over the shared list's contents must answer as though this
    /// server had no shared list.
    /// </summary>
    /// <param name="configuration">The server's settings.</param>
    /// <returns>True where the switch says this server offers no shared list.</returns>
    /// <remarks>
    /// It is asked of the CONTENTS routes and never of the administrative pair. The
    /// creation endpoint asks the setting its own question and refuses with a conflict,
    /// which is a different answer for a different reason; and the removal must stay
    /// reachable while the switch is off, because that is how a list made before the
    /// switch moved is taken away.
    /// </remarks>
    internal static bool ClosesTheList(PluginConfiguration configuration) =>
        !configuration.SharedListEnabled;
}
