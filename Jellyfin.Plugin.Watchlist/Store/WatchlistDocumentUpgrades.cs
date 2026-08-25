using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.Watchlist.Store;

/// <summary>
/// Brings a stored document from the version it declares up to the version this
/// plugin writes, one step at a time.
/// </summary>
/// <remarks>
/// The steps work on the stored JSON rather than on <see cref="WatchlistDocument"/>,
/// because that type is the current shape by definition. A document written under an
/// older shape cannot be deserialised into it at all: the format refuses an unmapped
/// member and requires every member the current shape declares, so a member that was
/// added, removed or renamed makes the read throw before any upgrade could run.
///
/// A version with no step to the current one is refused rather than relabelled.
/// Relabelling is what the store did before this existed, and it is the failure this
/// exists to prevent: a document keeping its old content while declaring the new
/// version reads as current from then on, and nothing later can tell it apart from
/// one that was really upgraded.
/// </remarks>
public static class WatchlistDocumentUpgrades
{
    /// <summary>
    /// The oldest version a stored document may declare and still be read. Below it
    /// there is no step, so a document declaring less than this is refused.
    /// </summary>
    public const int OldestReadableSchemaVersion = 0;

    /// <summary>
    /// One step per version this plugin can upgrade from, keyed by the version it
    /// upgrades away from. A key of N produces the shape of version N plus one.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> ShippedSteps =
        new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            [0] = FromVersionZeroToVersionOne,
            [1] = FromVersionOneToVersionTwo,
        };

    /// <summary>
    /// Gets the steps this plugin ships, for a test that reads the real chain rather
    /// than a fixture one.
    /// </summary>
    internal static IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> Steps => ShippedSteps;

    /// <summary>
    /// Whether a document declaring this version can be brought to the version this
    /// plugin writes.
    /// </summary>
    /// <param name="storedSchemaVersion">The version the document declares.</param>
    /// <returns>True when every step between the two exists.</returns>
    public static bool CanBringForward(int storedSchemaVersion) =>
        Covers(storedSchemaVersion, WatchlistDocument.CurrentSchemaVersion, ShippedSteps);

    /// <summary>
    /// Brings a stored document up to the version this plugin writes.
    /// </summary>
    /// <param name="document">The document as it was stored.</param>
    /// <param name="storedSchemaVersion">The version it declares.</param>
    /// <returns>The document at the current version.</returns>
    /// <exception cref="InvalidOperationException">
    /// A step between the two versions is missing. Ask <see cref="CanBringForward"/>
    /// first; a caller that did not is a caller that would have relabelled.
    /// </exception>
    public static JsonObject BringForward(JsonObject document, int storedSchemaVersion) =>
        Apply(document, storedSchemaVersion, WatchlistDocument.CurrentSchemaVersion, ShippedSteps);

    /// <summary>
    /// Whether a chain of steps reaches from one version to another.
    /// </summary>
    /// <param name="from">The version a document declares.</param>
    /// <param name="to">The version it has to reach.</param>
    /// <param name="steps">The steps available, keyed by the version each upgrades from.</param>
    /// <returns>True when every version in between has a step.</returns>
    /// <remarks>
    /// Takes the steps as a parameter so the suite can drive it with a chain of its
    /// own. A test that judged only the chain this plugin happens to ship today would
    /// prove the state of the tree rather than the rule.
    /// </remarks>
    internal static bool Covers(int from, int to, IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (from > to)
        {
            return false;
        }

        for (var version = from; version < to; version++)
        {
            if (!steps.ContainsKey(version))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Runs the steps from one version to another, in order.
    /// </summary>
    /// <param name="document">The document as it was stored.</param>
    /// <param name="from">The version it declares.</param>
    /// <param name="to">The version it has to reach.</param>
    /// <param name="steps">The steps available, keyed by the version each upgrades from.</param>
    /// <returns>The document at the target version.</returns>
    /// <exception cref="InvalidOperationException">A step in the chain is missing.</exception>
    /// <remarks>
    /// The version number is stamped here rather than inside each step, so a step
    /// author cannot forget it and a step that did it twice cannot skip a version.
    /// What a step is responsible for is the shape and nothing else.
    /// </remarks>
    internal static JsonObject Apply(
        JsonObject document,
        int from,
        int to,
        IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> steps)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(steps);

        var upgraded = document;

        for (var version = from; version < to; version++)
        {
            if (!steps.TryGetValue(version, out var step))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "There is no upgrade step from watchlist schema version {0}, so a document at version {1} cannot reach version {2}.",
                    version,
                    from,
                    to));
            }

            upgraded = step(upgraded)
                ?? throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "The upgrade step from watchlist schema version {0} returned nothing.",
                    version));

            upgraded[nameof(WatchlistDocument.SchemaVersion)] = version + 1;
        }

        return upgraded;
    }

    /// <summary>
    /// Version 0 to version 1, which changes no member.
    /// </summary>
    /// <param name="document">The document at version 0.</param>
    /// <returns>The same members, which are already the version 1 shape.</returns>
    /// <remarks>
    /// Version 1 is the first shape this plugin defined, and a version 0 document
    /// carries the same members: the fixtures for the two versions are asserted to
    /// differ in the number they declare and in nothing else. So this step is a
    /// statement that the shape did not move, not a step nobody wrote. It still has to
    /// exist, because the chain is what decides a version is readable and a version
    /// with no step is refused.
    /// </remarks>
    private static JsonObject FromVersionZeroToVersionOne(JsonObject document) => document;

    /// <summary>
    /// Version 1 to version 2, which adds no member to a stored document.
    /// </summary>
    /// <param name="document">The document at version 1.</param>
    /// <returns>The same members, which are already the version 2 shape.</returns>
    /// <remarks>
    /// Version 2 added the per-user preferences block, and the block is written only
    /// for a user who answered something. A version 1 document was written before any
    /// user could answer, so every one of them is a user who answered nothing, and the
    /// version 2 shape of that is the block being absent. Writing an empty block here
    /// would put a block on disk for a user who never set anything, which is the state
    /// the member is suppressed to avoid.
    /// </remarks>
    private static JsonObject FromVersionOneToVersionTwo(JsonObject document) => document;
}
