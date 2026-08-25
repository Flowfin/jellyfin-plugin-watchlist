using System;
using System.IO;
using System.Xml.Serialization;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The server's XML serialiser, over the same framework serialiser the server uses.
/// </summary>
/// <remarks>
/// This is real serialisation rather than a stand-in that hands back an object it was
/// given. The load path this suite exercises is a file somebody edited by hand, so a
/// test that never writes or parses XML would prove nothing about it: the bytes have
/// to go to disk and come back through <see cref="XmlSerializer"/>, which is what the
/// server's own implementation of this interface does.
/// </remarks>
public sealed class PluginConfigurationFile : IXmlSerializer
{
    /// <inheritdoc />
    public object DeserializeFromStream(Type type, Stream stream)
    {
        var serializer = new XmlSerializer(type);
        return serializer.Deserialize(stream)!;
    }

    /// <inheritdoc />
    public void SerializeToStream(object obj, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var serializer = new XmlSerializer(obj.GetType());
        serializer.Serialize(stream, obj);
    }

    /// <inheritdoc />
    public void SerializeToFile(object obj, string file)
    {
        using var stream = File.Create(file);
        SerializeToStream(obj, stream);
    }

    /// <inheritdoc />
    public object DeserializeFromFile(Type type, string file)
    {
        using var stream = File.OpenRead(file);
        return DeserializeFromStream(type, stream);
    }

    /// <inheritdoc />
    public object DeserializeFromBytes(Type type, byte[] buffer)
    {
        using var stream = new MemoryStream(buffer);
        return DeserializeFromStream(type, stream);
    }
}
