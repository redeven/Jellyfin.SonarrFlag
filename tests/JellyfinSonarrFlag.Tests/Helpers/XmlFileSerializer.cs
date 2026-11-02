using System.Xml.Serialization;
using MediaBrowser.Model.Serialization;

namespace JellyfinSonarrFlag.Tests.Helpers;

/// <summary>
/// Real XML serializer (like Jellyfin's) so tests catch configuration that would not round-trip through disk.
/// </summary>
public sealed class XmlFileSerializer : IXmlSerializer
{
    public int FilesWritten { get; private set; }

    public object? DeserializeFromStream(Type type, Stream stream) => new XmlSerializer(type).Deserialize(stream);

    public void SerializeToStream(object obj, Stream stream) => new XmlSerializer(obj.GetType()).Serialize(stream, obj);

    public void SerializeToFile(object obj, string file)
    {
        using (var stream = File.Create(file))
        {
            SerializeToStream(obj, stream);
        }

        FilesWritten++;
    }

    public object? DeserializeFromFile(Type type, string file)
    {
        using var stream = File.OpenRead(file);
        return DeserializeFromStream(type, stream);
    }

    public object? DeserializeFromBytes(Type type, byte[] buffer)
    {
        using var stream = new MemoryStream(buffer);
        return DeserializeFromStream(type, stream);
    }
}
