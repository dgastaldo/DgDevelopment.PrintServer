using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace PrintServer.UnitTests;

internal sealed class IppResponseWriter
{
    private readonly List<byte> _bytes = [];

    public IppResponseWriter(ushort status)
    {
        _bytes.AddRange([2, 0]);
        WriteUInt16(status);
        _bytes.AddRange([0, 0, 0, 1]);
    }

    public IppResponseWriter Group(byte tag)
    {
        _bytes.Add(tag);
        return this;
    }

    public IppResponseWriter Int(byte tag, string name, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return Attribute(tag, name, bytes);
    }

    public IppResponseWriter Bool(string name, bool value) => Attribute(0x22, name, [value ? (byte)1 : (byte)0]);

    public IppResponseWriter Text(byte tag, string name, string value) => Attribute(tag, name, Encoding.UTF8.GetBytes(value));

    public IppResponseWriter Attribute(byte tag, string name, byte[] value)
    {
        _bytes.Add(tag);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        WriteUInt16((ushort)nameBytes.Length);
        _bytes.AddRange(nameBytes);
        WriteUInt16((ushort)value.Length);
        _bytes.AddRange(value);
        return this;
    }

    public byte[] Build()
    {
        _bytes.Add(0x03);
        return _bytes.ToArray();
    }

    private void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        _bytes.AddRange(buffer.ToArray());
    }

    public static byte[] Printer(int state, bool acceptingJobs) =>
        new IppResponseWriter(0).Group(0x04).Int(0x23, "printer-state", state).Bool("printer-is-accepting-jobs", acceptingJobs).Build();

    public static byte[] JobCreated(int jobId) =>
        new IppResponseWriter(0).Group(0x02).Int(0x21, "job-id", jobId).Build();

    public static byte[] JobState(int state) =>
        new IppResponseWriter(0).Group(0x02).Int(0x23, "job-state", state).Build();

    public static byte[] Status(ushort status) => new IppResponseWriter(status).Build();
}

internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<(Uri? Uri, string? ContentType, byte[] Body)> Requests { get; } = [];

    public ScriptedHttpHandler Respond(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses.Enqueue(() => new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        return this;
    }

    public ScriptedHttpHandler Throw(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add((request.RequestUri, request.Content?.Headers.ContentType?.MediaType, body));
        return _responses.Dequeue()();
    }
}
