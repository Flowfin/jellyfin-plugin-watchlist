using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.Template.Tests;

/// <summary>
/// Locates the plugin assembly and the type the server would discover in it.
/// One type name is spelled out here and nowhere else, so a rename of the plugin
/// touches a single line of the suite instead of every test in it.
/// </summary>
internal static class PluginUnderTest
{
    public static Assembly Assembly => typeof(Plugin).Assembly;

    /// <summary>
    /// Gets the types in the plugin assembly that a server scanning for plugins would
    /// pick up: public, concrete, and descended from <see cref="BasePlugin"/>.
    /// </summary>
    public static IReadOnlyList<Type> DiscoverableTypes => Assembly
        .GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(BasePlugin).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();
}
