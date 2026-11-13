using System.Buffers.Binary;
using System.Text;

namespace PrintServer.Backends.Ipp;

internal sealed record IppAttribute(byte Tag, string Name, IReadOnlyList<byte[]> Values)
{
    public static IppAttribute Charset(string name, string value) => Text(IppTags.Charset, name, value);

    public static IppAttribute NaturalLanguage(string name, string value) => Text(IppTags.NaturalLanguage, name, value);

    public static IppAttribute Uri(string name, string value) => Text(IppTags.Uri, name, value);

    public static IppAttribute NameWithoutLanguage(string name, string value) => Text(IppTags.NameWithoutLanguage, name, value);

    public static IppAttribute MimeMediaType(string name, string value) => Text(IppTags.MimeMediaType, name, value);

    public static IppAttribute Keywords(string name, params string[] values) =>
        new(IppTags.Keyword, name, values.Select(v => Encoding.UTF8.GetBytes(v)).ToList());

    public static IppAttribute Integer(string name, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return new IppAttribute(IppTags.Integer, name, [bytes]);
    }

    private static IppAttribute Text(byte tag, string name, string value) =>
        new(tag, name, [Encoding.UTF8.GetBytes(value)]);
}
