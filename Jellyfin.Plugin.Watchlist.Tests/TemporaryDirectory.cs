using System;
using System.Globalization;
using System.IO;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// A directory a single test owns, under a root unique to the run it belongs to.
/// Nothing in this suite writes to a shared location, because two tests sharing a
/// path fail each other roughly one run in ten and the failure never names the
/// test that caused it.
///
/// The root carries a random component rather than a timestamp. The headless rule
/// refuses a clock read, and two runs starting in the same second would collide on
/// a timestamp anyway.
/// </summary>
public sealed class TemporaryDirectory : IDisposable
{
    private static readonly string RunRoot = Path.Combine(
        Path.GetTempPath(),
        "jellyfin-plugin-watchlist-tests",
        Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));

    private bool _disposed;

    static TemporaryDirectory()
    {
        // Each instance removes its own directory, which leaves the run root behind
        // as an empty folder. Removing it when the process ends keeps a machine that
        // has run the suite a thousand times from carrying a thousand empty folders.
        // Best effort: a run killed outright never reaches this, and a leftover root
        // holds nothing.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                if (Directory.Exists(RunRoot))
                {
                    Directory.Delete(RunRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        };
    }

    public TemporaryDirectory(string name)
    {
        FullPath = Path.Combine(RunRoot, name, Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(FullPath);
    }

    /// <summary>
    /// Gets the directory this instance owns. It exists as soon as the instance does.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// Gets the root every directory of this run sits under. A test asserts its own
    /// directory is inside it, which is what makes the rule readable rather than
    /// only intended.
    /// </summary>
    public static string Root => RunRoot;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Directory.Exists(FullPath))
        {
            Directory.Delete(FullPath, recursive: true);
        }
    }
}
