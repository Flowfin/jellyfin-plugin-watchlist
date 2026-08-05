using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// Shuffles the test cases inside a class. A suite that only ever runs in one order
/// hides every dependency between its tests until the day something reorders them,
/// and that day is usually somebody else's machine.
/// </summary>
public class RandomisedTestCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
        => Shuffle.Of(testCases);
}

/// <summary>
/// Shuffles the collections, which is the order the classes themselves run in.
/// </summary>
public class RandomisedTestCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
        => Shuffle.Of(testCollections);
}

internal static class Shuffle
{
    public static IReadOnlyList<T> Of<T>(IEnumerable<T> items)
    {
        var ordered = items.ToArray();

        for (var i = ordered.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
        }

        return ordered;
    }
}
