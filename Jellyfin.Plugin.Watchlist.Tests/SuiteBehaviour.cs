using Xunit;

// Classes run in a shuffled order and the tests inside each class run in a shuffled
// order, on every run rather than on a special one. An order-dependent test is
// therefore found by whoever introduces it and not by whoever changes the order
// months later.
[assembly: TestCaseOrderer(
    "Jellyfin.Plugin.Watchlist.Tests.RandomisedTestCaseOrderer",
    "Jellyfin.Plugin.Watchlist.Tests")]
[assembly: TestCollectionOrderer(
    "Jellyfin.Plugin.Watchlist.Tests.RandomisedTestCollectionOrderer",
    "Jellyfin.Plugin.Watchlist.Tests")]

// Classes run in parallel with each other, which is the default and is stated here
// so that turning it off is a visible edit rather than a quiet one.
[assembly: CollectionBehavior(DisableTestParallelization = false)]
