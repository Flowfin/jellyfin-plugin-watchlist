using System;
using System.IO;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The server's path set, pointed at a directory the test owns.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that makes the plugin type reachable from a test. The server's
/// base class does not know where a configuration file lives; it asks
/// <see cref="IApplicationPaths"/> and writes wherever the answer points. So
/// constructing the plugin needs an implementation of this interface and not a server,
/// and every byte it reads or writes lands under <see cref="TemporaryDirectory"/>.
/// </para>
/// <para>
/// Every member is answered from the one directory rather than being left to throw.
/// A member that throws turns "the code under test asked for a path it does not use"
/// into a failure, and the next person to reach a new line of the plugin would meet
/// that instead of their own defect.
/// </para>
/// </remarks>
public sealed class ServerPathsInATemporaryDirectory : IApplicationPaths, IDisposable
{
    private readonly TemporaryDirectory _root;
    private bool _disposed;

    public ServerPathsInATemporaryDirectory()
    {
        _root = new TemporaryDirectory("server-paths");
        Directory.CreateDirectory(PluginConfigurationsPath);
        Directory.CreateDirectory(PluginsPath);
    }

    /// <summary>
    /// Gets the directory every path below sits under.
    /// </summary>
    public string RootPath => _root.FullPath;

    /// <inheritdoc />
    public string ProgramDataPath => Under("program-data");

    /// <inheritdoc />
    public string WebPath => Under("web");

    /// <inheritdoc />
    public string ProgramSystemPath => Under("program-system");

    /// <inheritdoc />
    public string DataPath => Under("data");

    /// <inheritdoc />
    public string ImageCachePath => Under("image-cache");

    /// <inheritdoc />
    public string PluginsPath => Under("plugins");

    /// <inheritdoc />
    public string PluginConfigurationsPath => Under("plugin-configurations");

    /// <inheritdoc />
    public string LogDirectoryPath => Under("log");

    /// <inheritdoc />
    public string ConfigurationDirectoryPath => Under("configuration");

    /// <inheritdoc />
    public string SystemConfigurationFilePath => Path.Combine(ConfigurationDirectoryPath, "system.xml");

    /// <inheritdoc />
    public string CachePath => Under("cache");

    /// <inheritdoc />
    public string TempDirectory => Under("temp");

    /// <inheritdoc />
    public string VirtualDataPath => Under("virtual-data");

    /// <inheritdoc />
    public string TrickplayPath => Under("trickplay");

    /// <inheritdoc />
    public string BackupPath => Under("backup");

    /// <summary>
    /// Where the plugin's own configuration file is written, spelled the way the
    /// server's base class spells it: the assembly file name with an xml extension.
    /// </summary>
    public string PluginConfigurationFilePath => Path.Combine(
        PluginConfigurationsPath,
        Path.ChangeExtension(Path.GetFileName(PluginUnderTest.Assembly.Location), ".xml"));

    /// <inheritdoc />
    public void MakeSanityCheckOrThrow()
    {
        // The directories this suite uses are created in the constructor and the rest
        // are never read, so there is nothing here to check and nothing to throw about.
    }

    /// <inheritdoc />
    public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        Directory.CreateDirectory(path);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _root.Dispose();
    }

    private string Under(string name) => Path.Combine(_root.FullPath, name);
}
