using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What keeps this suite from failing once a week for its own reasons: a directory
/// per test under a root per run, a shuffled order on every run, and no reading of
/// the machine's clock, time zone or locale.
/// </summary>
public class SuiteDeterminismTests
{
    /// <summary>
    /// Two directories asked for under the same name are still two directories, and
    /// both sit under the root this run owns. Sharing one path between two tests is
    /// the failure that shows up as an unrelated test going red once in ten runs.
    /// </summary>
    [Fact]
    public void EveryTemporaryDirectoryIsItsOwnAndSitsUnderThisRunsRoot()
    {
        using var first = new TemporaryDirectory("same-name");
        using var second = new TemporaryDirectory("same-name");

        Assert.NotEqual(first.FullPath, second.FullPath);
        Assert.StartsWith(TemporaryDirectory.Root, first.FullPath, StringComparison.Ordinal);
        Assert.StartsWith(TemporaryDirectory.Root, second.FullPath, StringComparison.Ordinal);
        Assert.True(Directory.Exists(first.FullPath));
        Assert.True(Directory.Exists(second.FullPath));
    }

    /// <summary>
    /// A test that leaves its directory behind turns a run into a slow leak, and the
    /// next reader of the machine finds a temporary folder nobody can attribute.
    /// </summary>
    [Fact]
    public void ATemporaryDirectoryIsGoneOnceTheTestThatOwnedItIsDone()
    {
        string path;

        using (var directory = new TemporaryDirectory("removed-afterwards"))
        {
            path = directory.FullPath;
            File.WriteAllText(Path.Join(path, "a-file"), "content");
            Assert.True(Directory.Exists(path));
        }

        Assert.False(Directory.Exists(path));
    }

    /// <summary>
    /// Disposing twice is what happens when a test disposes explicitly inside a
    /// using block, and it must not throw on the second pass. The using block is
    /// written here rather than described, so the directory is still removed when an
    /// assertion below fails, and the block's own dispose is a third pass.
    /// </summary>
    [Fact]
    public void DisposingATemporaryDirectoryTwiceIsHarmless()
    {
        using var directory = new TemporaryDirectory("disposed-twice");

        directory.Dispose();
        directory.Dispose();

        Assert.False(Directory.Exists(directory.FullPath));
    }

    /// <summary>
    /// The shuffle has to actually reorder. A shuffle that returned its input would
    /// leave every claim about order in this suite standing on nothing.
    /// </summary>
    [Fact]
    public void TheShuffleReordersWhatItIsGiven()
    {
        var input = Enumerable.Range(0, 50).ToList();

        var shuffled = Shuffle.Of(input);

        Assert.Equal(input.Count, shuffled.Count);
        Assert.Equal(input.OrderBy(i => i), shuffled.OrderBy(i => i));
        Assert.NotEqual(input, shuffled);
    }

    /// <summary>
    /// The orderers are named in assembly attributes by string. A rename of either
    /// class leaves the attribute pointing at nothing, xUnit falls back to its
    /// default order without failing, and the suite silently stops shuffling.
    /// </summary>
    [Fact]
    public void TheAssemblyAttributesNameOrderersThatExist()
    {
        AssertNamedTypeExists(typeof(Xunit.TestCaseOrdererAttribute));
        AssertNamedTypeExists(typeof(Xunit.TestCollectionOrdererAttribute));
    }

    private static void AssertNamedTypeExists(Type attributeType)
    {
        var attribute = typeof(SuiteDeterminismTests).Assembly
            .GetCustomAttributesData()
            .SingleOrDefault(a => a.AttributeType == attributeType);

        Assert.True(attribute is not null, $"The suite declares no {attributeType.Name}, so it runs in the default order.");

        var arguments = attribute!.ConstructorArguments
            .Select(a => a.Value as string)
            .ToList();

        Assert.Equal(2, arguments.Count);

        var named = Type.GetType($"{arguments[0]}, {arguments[1]}");

        Assert.True(
            named is not null,
            $"{attributeType.Name} names {arguments[0]} in {arguments[1]}, and no such type exists. The suite would fall back to the default order without failing.");
    }
}
