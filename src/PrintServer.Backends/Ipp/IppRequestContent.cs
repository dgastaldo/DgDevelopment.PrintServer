using System.Net;
using System.Net.Http.Headers;

namespace PrintServer.Backends.Ipp;

internal sealed class IppRequestContent : HttpContent
{
    private readonly byte[] _header;
    private readonly Stream? _document;

    public IppRequestContent(byte[] header, Stream? document)
    {
        _header = header;
        _document = document;
        Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(_header, cancellationToken).ConfigureAwait(false);
        if (_document is not null)
        {
            await _document.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _document?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override bool TryComputeLength(out long length)
    {
        if (_document is null)
        {
            length = _header.Length;
            return true;
        }

        if (_document.CanSeek)
        {
            length = _header.Length + _document.Length - _document.Position;
            return true;
        }

        length = 0;
        return false;
    }
}
