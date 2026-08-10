using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.Watchlist.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The configuration is not written by this plugin. A server hands the plugin base an
/// <c>IXmlSerializer</c> and that is what stores the settings, so a property this
/// plugin declares but that serialiser cannot carry is a defect nothing here compiles
/// against. It appears on a server, as a setting that will not save or that reads back
/// as its default after a restart.
/// </summary>
/// <remarks>
/// <para>
/// What the server actually does with the configuration, on the current stable line:
/// </para>
/// <code>
/// git show v10.11.11:MediaBrowser.Common/Plugins/BasePluginOfT.cs | grep -n 'XmlSerializer\|SerializeToFile'
///  41:        protected BasePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
///  79:        protected IXmlSerializer XmlSerializer { get; private set; }
/// 150:                XmlSerializer.SerializeToFile(config, ConfigurationFilePath);
/// git show v10.11.11:Emby.Server.Implementations/Serialization/MyXmlSerializer.cs | grep -n 'new XmlSerializer(t)\|Formatting.Indented\|XmlReader.Create'
///  23:                static (_, t) =&gt; new XmlSerializer(t),
///  63:                textWriter.Formatting = Formatting.Indented;
///  45:            using (var reader = XmlReader.Create(stream))
/// </code>
/// <para>
/// The same three calls are in that file on the next line as well, so both supported
/// lines store the configuration through <c>System.Xml.Serialization.XmlSerializer</c>.
/// </para>
/// <para>
/// The implementation itself is in <c>Emby.Server.Implementations</c>, which is a
/// server assembly and not one this suite may reference under the headless rule. So
/// what runs below is that serialiser's three calls rather than that serialiser's type,
/// and the fidelity of the reproduction is bounded by the reading above rather than
/// asserted by a compiler.
/// </para>
/// </remarks>
public sealed class ConfigurationSerialisationTests
{
    /// <summary>
    /// The type the plugin declares as its configuration, taken off the plugin rather
    /// than named here, so a change of configuration type is covered without an edit
    /// to this file.
    /// </summary>
    private static Type DeclaredConfigurationType =>
        typeof(Plugin).BaseType is { IsGenericType: true } baseType
        && baseType.GetGenericTypeDefinition() == typeof(BasePlugin<>)
            ? baseType.GetGenericArguments()[0]
            : throw new InvalidOperationException(
                $"{typeof(Plugin)} no longer derives from BasePlugin<T>, so this suite cannot read which type the server stores for it.");

    /// <summary>
    /// The settings, in the sense the server's serialiser uses: public, readable and
    /// writable. Read by reflection rather than listed, so the setting somebody adds
    /// tomorrow is covered on the day it is added.
    /// </summary>
    private static IReadOnlyList<PropertyInfo> DeclaredSettings =>
        DeclaredConfigurationType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The plugin declares the type this suite is about, and the set it holds is not
    /// empty. Both are assumptions the two tests below would pass vacuously without.
    /// </summary>
    [Fact]
    public void ThePluginDeclaresAConfigurationTypeWithSettingsInIt()
    {
        Assert.Equal(typeof(PluginConfiguration), DeclaredConfigurationType);
        Assert.NotEmpty(DeclaredSettings);
    }

    /// <summary>
    /// Building the serialiser is where a property it cannot carry is refused, and it
    /// is refused by a throw rather than by a return value. A type with an interface
    /// property, a property whose type has no parameterless constructor, or two members
    /// competing for one element name stops here.
    /// </summary>
    /// <remarks>
    /// On a server this same construction happens inside the plugin manager while the
    /// server is starting, so the failure this catches is a plugin that will not load.
    /// </remarks>
    [Fact]
    public void TheServersSerialiserCanBeBuiltOverTheDeclaredConfigurationType()
    {
        var built = Record.Exception(() => new XmlSerializer(DeclaredConfigurationType));

        Assert.Null(built);
    }

    /// <summary>
    /// Every setting is moved off its default, written and read back the way the server
    /// writes and reads it, and compared. A setting the serialiser silently drops comes
    /// back as its default and fails here, which is the failure a user meets as a page
    /// that will not save.
    /// </summary>
    [Fact]
    public void EverySettingSurvivesARoundTripThroughTheServersSerialisation()
    {
        var written = (BasePluginConfiguration)Activator.CreateInstance(DeclaredConfigurationType)!;
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var setting in DeclaredSettings)
        {
            var value = MovedOffItsDefault(setting, setting.GetValue(written));
            setting.SetValue(written, value);
            expected[setting.Name] = value;
        }

        var read = RoundTrip(written);

        foreach (var setting in DeclaredSettings)
        {
            Assert.Equal(expected[setting.Name], setting.GetValue(read));
        }
    }

    /// <summary>
    /// The server's two calls, in the order it makes them: serialise into a stream
    /// through an indenting writer, then deserialise the same stream back into the
    /// declared type.
    /// </summary>
    /// <param name="configuration">The configuration to write.</param>
    /// <returns>What reading the written bytes back produces.</returns>
    private static object RoundTrip(BasePluginConfiguration configuration)
    {
        var serialiser = new XmlSerializer(DeclaredConfigurationType);

        using var stream = new MemoryStream();
        using (var text = new StreamWriter(stream, leaveOpen: true))
        {
            var writer = new XmlTextWriter(text) { Formatting = Formatting.Indented };
            serialiser.Serialize(writer, configuration);
            writer.Flush();
        }

        stream.Position = 0;
        using var reader = XmlReader.Create(stream);
        return serialiser.Deserialize(reader)
            ?? throw new InvalidOperationException("The serialiser read the configuration back as nothing.");
    }

    /// <summary>
    /// A value of the setting's own type that is not the value it already holds, so a
    /// setting the serialiser drops cannot pass by coming back as what it started as.
    /// </summary>
    /// <param name="setting">The setting being varied, named in the refusal below.</param>
    /// <param name="current">What that setting holds on a fresh configuration.</param>
    /// <returns>A different value of the same type.</returns>
    /// <remarks>
    /// A type this does not know how to vary is refused rather than skipped. Skipping it
    /// would leave the new setting looking covered by a test that never touched it, and
    /// which types this suite can vary is a decision worth meeting when the setting is
    /// added rather than after a user cannot save it.
    /// </remarks>
    private static object? MovedOffItsDefault(PropertyInfo setting, object? current)
    {
        var type = Nullable.GetUnderlyingType(setting.PropertyType) ?? setting.PropertyType;

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToList();
            return values.FirstOrDefault(value => !value.Equals(current)) ?? values[0];
        }

        if (type == typeof(bool))
        {
            return !(current as bool? ?? false);
        }

        if (type == typeof(string))
        {
            return current as string == "a written value" ? "another written value" : "a written value";
        }

        if (type == typeof(Guid))
        {
            return Guid.Parse("2b7f4f0e-1f1e-4a5a-9a3f-6c1f4d2e8b70");
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short)
            || type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            var moved = Convert.ToDecimal(current, CultureInfo.InvariantCulture) + 1;
            return Convert.ChangeType(moved, type, CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException(
            $"{DeclaredConfigurationType.Name}.{setting.Name} is a {setting.PropertyType}, and this suite has no way to give it a value other than the one it starts with. "
            + "Teach MovedOffItsDefault that type, or say in the change why the setting cannot be round tripped.");
    }
}
