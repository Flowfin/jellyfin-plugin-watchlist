using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// The one list the whole server can see, as it is written to and read from disk.
/// </summary>
/// <remarks>
/// <para>
/// Not a user's document under another name. It has no user to be keyed by, more
/// than one person writes it, and who may read it and who may change it are two
/// questions rather than one.
/// </para>
/// <para>
/// There is exactly one of these on a server. The record still carries an identity
/// of its own rather than being the file it happens to live in, because everything
/// downstream - a projected playlist, a route, an exported list - has to name the
/// list it is about, and a displayed name is the one thing about a list that is
/// meant to change.
/// </para>
/// <para>
/// The displayed name is not here. The name of a projected list is a configured
/// value for the private lists already, and putting the shared one in the record
/// would give a server two places to answer the same question. What an
/// administrator may set, and where it is stored, is the administrative surface
/// rather than this record.
/// </para>
/// </remarks>
public sealed record SharedWatchlistDocument
{
    /// <summary>
    /// The version this plugin writes. A document carrying a higher number was
    /// written by a newer plugin than the one reading it.
    /// </summary>
    /// <remarks>
    /// Its own number, counted from one, rather than a share of the number a user's
    /// document declares. The two shapes move for different reasons, and a single
    /// counter would make every change to one of them an upgrade step the other has
    /// to carry.
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Gets the schema version the document was written with.
    /// </summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// Gets the identity of the list, which is not its displayed name.
    /// </summary>
    public required Guid ListId { get; init; }

    /// <summary>
    /// Gets the user the list belongs to.
    /// </summary>
    /// <remarks>
    /// A value in the record rather than an implied administrator. A server whose
    /// administrator account is replaced still has the list, and a rule that reads
    /// "whoever is an administrator today" has no answer on a server with several of
    /// them or none.
    /// </remarks>
    public required Guid OwnerUserId { get; init; }

    /// <summary>
    /// Gets the entries, in the order they were written.
    /// </summary>
    public required IReadOnlyList<WatchlistEntry> Entries { get; init; }

    /// <summary>
    /// Gets what this plugin remembers about the playlist this list is projected into,
    /// or null where nothing has been projected for it yet.
    /// </summary>
    /// <remarks>
    /// The same shape a user's document carries, deliberately. What a projection has to
    /// remember does not depend on whose list it is - the playlist, the name last
    /// written, what was last put in it and when - and two records answering that
    /// question differently would be two rules to keep in step.
    ///
    /// Null means no playlist has been made for the shared list, which is every server
    /// where the setting was turned on and no pass has run since. Suppressed when it is
    /// null for the same reason the user document's is, so a server with no projection
    /// carries no such member on disk.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WatchlistProjectionState? Projection { get; init; }
}
