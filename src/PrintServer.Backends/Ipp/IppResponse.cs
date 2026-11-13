using System.Buffers.Binary;
using System.Text;

namespace PrintServer.Backends.Ipp;

internal sealed class IppAttributeGroup(byte tag)
{
    private readonly Dictionary<string, List<byte[]>> _attributes = new(StringComparer.Ordinal);

    public byte Tag { get; } = tag;

    public void Add(string name, byte[] value)
    {
        if (!_attributes.TryGetValue(name, out var values))
        {
            values = [];
            _attributes[name] = values;
        }

        values.Add(value);
    }

    public int? GetInt32(string name) =>
        _attributes.TryGetValue(name, out var values) && values[0].Length == 4
            ? BinaryPrimitives.ReadInt32BigEndian(values[0])
            : null;

    public bool? GetBoolean(string name) =>
        _attributes.TryGetValue(name, out var values) && values[0].Length == 1 ? values[0][0] != 0 : null;

    public string? GetString(string name) =>
        _attributes.TryGetValue(name, out var values) ? Encoding.UTF8.GetString(values[0]) : null;
}

internal sealed class IppResponse
{
    private const ushort SuccessLimit = 0x0100;

    private IppResponse(ushort statusCode, IReadOnlyList<IppAttributeGroup> groups)
    {
        StatusCode = statusCode;
        Groups = groups;
    }

    public ushort StatusCode { get; }

    public IReadOnlyList<IppAttributeGroup> Groups { get; }

    public bool IsSuccess => StatusCode < SuccessLimit;

    public IppAttributeGroup? FindGroup(byte tag) => Groups.FirstOrDefault(g => g.Tag == tag);

    public static IppResponse Parse(ReadOnlySpan<byte> data)
    {
        var position = 0;
        if (data.Length < 8)
        {
            throw new InvalidDataException("IPP response is shorter than its header.");
        }

        var statusCode = BinaryPrimitives.ReadUInt16BigEndian(data[2..4]);
        position = 8;

        var groups = new List<IppAttributeGroup>();
        IppAttributeGroup? current = null;
        string? lastName = null;

        while (true)
        {
            if (position >= data.Length)
            {
                throw new InvalidDataException("IPP response ended before the end-of-attributes tag.");
            }

            var tag = data[position++];
            if (tag == IppTags.EndOfAttributes)
            {
                break;
            }

            if (tag <= IppTags.UnsupportedAttributes)
            {
                current = new IppAttributeGroup(tag);
                groups.Add(current);
                lastName = null;
                continue;
            }

            if (current is null)
            {
                throw new InvalidDataException("IPP attribute found outside an attribute group.");
            }

            var name = ReadBlock(data, ref position);
            var value = ReadBlock(data, ref position);
            var nameText = name.Length == 0 ? lastName : Encoding.UTF8.GetString(name);
            if (nameText is null)
            {
                throw new InvalidDataException("IPP additional value without a preceding attribute.");
            }

            current.Add(nameText, value.ToArray());
            lastName = nameText;
        }

        return new IppResponse(statusCode, groups);
    }

    private static ReadOnlySpan<byte> ReadBlock(ReadOnlySpan<byte> data, ref int position)
    {
        if (position + 2 > data.Length)
        {
            throw new InvalidDataException("IPP response is truncated.");
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(data[position..(position + 2)]);
        position += 2;
        if (position + length > data.Length)
        {
            throw new InvalidDataException("IPP response is truncated.");
        }

        var block = data.Slice(position, length);
        position += length;
        return block;
    }
}
