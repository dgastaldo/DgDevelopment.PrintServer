using System.Buffers.Binary;

namespace PrintServer.Backends.Ipp;

internal static class IppRequestBuilder
{
    private const byte VersionMajor = 2;
    private const byte VersionMinor = 0;

    public static byte[] Build(
        ushort operation,
        int requestId,
        IReadOnlyList<IppAttribute> operationAttributes,
        IReadOnlyList<IppAttribute>? jobAttributes = null)
    {
        using var stream = new MemoryStream();

        stream.WriteByte(VersionMajor);
        stream.WriteByte(VersionMinor);
        WriteUInt16(stream, operation);
        WriteInt32(stream, requestId);

        stream.WriteByte(IppTags.OperationAttributes);
        foreach (var attribute in operationAttributes)
        {
            Write(stream, attribute);
        }

        if (jobAttributes is { Count: > 0 })
        {
            stream.WriteByte(IppTags.JobAttributes);
            foreach (var attribute in jobAttributes)
            {
                Write(stream, attribute);
            }
        }

        stream.WriteByte(IppTags.EndOfAttributes);
        return stream.ToArray();
    }

    private static void Write(Stream stream, IppAttribute attribute)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(attribute.Name);
        for (var i = 0; i < attribute.Values.Count; i++)
        {
            stream.WriteByte(attribute.Tag);
            if (i == 0)
            {
                WriteUInt16(stream, checked((ushort)name.Length));
                stream.Write(name);
            }
            else
            {
                WriteUInt16(stream, 0);
            }

            var value = attribute.Values[i];
            WriteUInt16(stream, checked((ushort)value.Length));
            stream.Write(value);
        }
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
