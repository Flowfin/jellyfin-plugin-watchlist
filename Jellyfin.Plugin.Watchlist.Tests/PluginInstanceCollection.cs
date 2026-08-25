using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The tests that construct the plugin type, run one collection at a time.
/// </summary>
/// <remarks>
/// <para>
/// Constructing the plugin writes a process-wide static: the server's base class
/// keeps the last instance built and hands it out as <c>Plugin.Instance</c>. Every
/// other test in this suite reads the instance it built through its own local, so the
/// static never mattered. The registration hook does not have that option - its two
/// factories read <c>Plugin.Instance</c>, because the server resolves them long after
/// it constructed the plugin - so a test of those factories asserts against a value
/// another class can overwrite while it runs.
/// </para>
/// <para>
/// The suite states its parallelism in SuiteBehaviour.cs and leaves it on. This does
/// not turn it off; it names the one set of classes that share a static and keeps them
/// out of each other's way, which is narrower than the assembly-wide switch and says
/// at the declaration what the shared thing is.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PluginInstanceCollection
{
    /// <summary>
    /// The name the classes in this collection name.
    /// </summary>
    public const string Name = "the plugin instance static";
}
