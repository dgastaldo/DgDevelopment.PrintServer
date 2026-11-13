using PrintServer.Backends.Ipp;

namespace PrintServer.UnitTests;

public sealed class IppProtocolTests
{
    [Fact]
    public void RequestStartsWithVersionOperationAndRequestId()
    {
        var bytes = IppRequestBuilder.Build(IppOperations.GetPrinterAttributes, 5, [IppAttribute.Charset("attributes-charset", "utf-8")]);

        Assert.Equal([0x02, 0x00, 0x00, 0x0B, 0x00, 0x00, 0x00, 0x05, 0x01], bytes[..9]);
        Assert.Equal(0x03, bytes[^1]);
    }

    [Fact]
    public void AttributeIsEncodedWithTagNameAndValueLengths()
    {
        var bytes = IppRequestBuilder.Build(IppOperations.PrintJob, 1, [IppAttribute.Charset("attributes-charset", "utf-8")]);

        var expected = new List<byte> { 0x47, 0x00, 0x12 };
        expected.AddRange(System.Text.Encoding.ASCII.GetBytes("attributes-charset"));
        expected.AddRange([0x00, 0x05]);
        expected.AddRange(System.Text.Encoding.ASCII.GetBytes("utf-8"));
        Assert.Equal(expected, bytes[9..^1]);
    }

    [Fact]
    public void AdditionalValuesOfAnAttributeUseAnEmptyName()
    {
        var bytes = IppRequestBuilder.Build(IppOperations.GetJobAttributes, 1, [IppAttribute.Keywords("requested-attributes", "a", "b")]);

        var secondValue = new byte[] { 0x44, 0x00, 0x00, 0x00, 0x01, (byte)'b' };
        Assert.True(bytes.AsSpan().IndexOf(secondValue) > 0);
    }

    [Fact]
    public void JobAttributesAreWrittenInTheirOwnGroup()
    {
        var bytes = IppRequestBuilder.Build(
            IppOperations.PrintJob,
            1,
            [IppAttribute.Charset("attributes-charset", "utf-8")],
            [IppAttribute.Integer("copies", 3)]);

        var copies = new byte[] { 0x02, 0x21, 0x00, 0x06 };
        Assert.True(bytes.AsSpan().IndexOf(copies) > 0);
        Assert.Equal([0x00, 0x00, 0x00, 0x03, 0x03], bytes[^5..]);
    }

    [Fact]
    public void ResponseIsParsedIntoGroupsAndTypedValues()
    {
        var data = new IppResponseWriter(0x0001)
            .Group(0x01).Text(0x47, "attributes-charset", "utf-8")
            .Group(0x04).Int(0x23, "printer-state", 3).Bool("printer-is-accepting-jobs", true).Text(0x42, "printer-name", "Office")
            .Build();

        var response = IppResponse.Parse(data);

        Assert.True(response.IsSuccess);
        Assert.Equal(0x0001, response.StatusCode);
        var printer = response.FindGroup(IppTags.PrinterAttributes);
        Assert.NotNull(printer);
        Assert.Equal(3, printer.GetInt32("printer-state"));
        Assert.True(printer.GetBoolean("printer-is-accepting-jobs"));
        Assert.Equal("Office", printer.GetString("printer-name"));
        Assert.Null(printer.GetInt32("missing"));
        Assert.Equal("utf-8", response.FindGroup(IppTags.OperationAttributes)!.GetString("attributes-charset"));
    }

    [Fact]
    public void ClientErrorStatusIsNotASuccess()
    {
        Assert.False(IppResponse.Parse(IppResponseWriter.Status(0x0406)).IsSuccess);
    }

    [Fact]
    public void AdditionalValuesAreAttachedToThePreviousAttribute()
    {
        var data = new List<byte> { 2, 0, 0, 0, 0, 0, 0, 1, 0x04 };
        data.AddRange([0x44, 0x00, 0x01, (byte)'k', 0x00, 0x01, (byte)'a']);
        data.AddRange([0x44, 0x00, 0x00, 0x00, 0x01, (byte)'b']);
        data.Add(0x03);

        var group = IppResponse.Parse(data.ToArray()).FindGroup(IppTags.PrinterAttributes);

        Assert.Equal("a", group!.GetString("k"));
    }

    [Theory]
    [InlineData(new byte[] { 2, 0, 0 })]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 1 })]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 1, 0x04, 0x21, 0x00, 0x05, (byte)'a' })]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 1, 0x21, 0x00, 0x01, (byte)'a', 0x00, 0x00, 0x03 })]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 0, 1, 0x04, 0x44, 0x00, 0x00, 0x00, 0x01, (byte)'a', 0x03 })]
    public void MalformedResponsesAreRejected(byte[] data)
    {
        Assert.Throws<InvalidDataException>(() => IppResponse.Parse(data));
    }

    [Theory]
    [InlineData("ipp://printer.local/ipp/print", "http://printer.local:631/ipp/print", "ipp://printer.local:631/ipp/print")]
    [InlineData("ipps://printer.local:8631/ipp/print", "https://printer.local:8631/ipp/print", "ipps://printer.local:8631/ipp/print")]
    [InlineData("http://cups.local:631/printers/x", "http://cups.local:631/printers/x", "ipp://cups.local:631/printers/x")]
    [InlineData("https://cups.local/printers/x", "https://cups.local:631/printers/x", "ipps://cups.local:631/printers/x")]
    public void UrisAreConvertedBetweenIppAndHttp(string configured, string http, string ipp)
    {
        var uri = new Uri(configured);

        Assert.Equal(http, IppUri.ToHttp(uri).AbsoluteUri);
        Assert.Equal(ipp, IppUri.ToIpp(uri));
    }

    [Theory]
    [InlineData("ftp://printer/x")]
    [InlineData("file:///tmp/x")]
    public void UnsupportedUriSchemesAreRejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() => IppUri.ToHttp(new Uri(value)));
    }

    [Fact]
    public void MissingUriIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => IppUri.ToHttp(null));
    }

    [Theory]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.PDF", "application/pdf")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.txt", "text/plain")]
    [InlineData("a.docx", "application/octet-stream")]
    public void DocumentFormatFollowsTheExtension(string file, string expected)
    {
        Assert.Equal(expected, IppDocumentFormats.ForFile(file));
    }

    [Theory]
    [InlineData((ushort)0x0406, "client-error-not-found (0x0406)")]
    [InlineData((ushort)0x040A, "client-error-document-format-not-supported (0x040A)")]
    [InlineData((ushort)0x0999, "0x0999")]
    public void StatusCodesAreDescribed(ushort code, string expected)
    {
        Assert.Equal(expected, IppStatus.Describe(code));
    }
}
