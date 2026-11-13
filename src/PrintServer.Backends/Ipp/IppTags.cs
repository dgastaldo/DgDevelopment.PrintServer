namespace PrintServer.Backends.Ipp;

internal static class IppTags
{
    public const byte OperationAttributes = 0x01;
    public const byte JobAttributes = 0x02;
    public const byte EndOfAttributes = 0x03;
    public const byte PrinterAttributes = 0x04;
    public const byte UnsupportedAttributes = 0x05;

    public const byte Integer = 0x21;
    public const byte Boolean = 0x22;
    public const byte Enum = 0x23;
    public const byte NameWithoutLanguage = 0x42;
    public const byte Keyword = 0x44;
    public const byte Uri = 0x45;
    public const byte Charset = 0x47;
    public const byte NaturalLanguage = 0x48;
    public const byte MimeMediaType = 0x49;
}

internal static class IppOperations
{
    public const ushort PrintJob = 0x0002;
    public const ushort GetJobAttributes = 0x0009;
    public const ushort GetPrinterAttributes = 0x000B;
}
