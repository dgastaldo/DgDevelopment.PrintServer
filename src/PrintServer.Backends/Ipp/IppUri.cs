namespace PrintServer.Backends.Ipp;

internal static class IppUri
{
    private const int DefaultIppPort = 631;

    public static Uri ToHttp(Uri? printerUri)
    {
        var uri = Validate(printerUri);
        var secure = uri.Scheme is "ipps" or "https";
        var builder = new UriBuilder(uri)
        {
            Scheme = secure ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
            Port = uri.IsDefaultPort || uri.Port == -1 ? DefaultIppPort : uri.Port,
        };
        return builder.Uri;
    }

    public static string ToIpp(Uri? printerUri)
    {
        var uri = Validate(printerUri);
        var secure = uri.Scheme is "ipps" or "https";
        var builder = new UriBuilder(uri)
        {
            Scheme = secure ? "ipps" : "ipp",
            Port = uri.IsDefaultPort || uri.Port == -1 ? DefaultIppPort : uri.Port,
        };
        return builder.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    private static Uri Validate(Uri? printerUri)
    {
        if (printerUri is null || !printerUri.IsAbsoluteUri || printerUri.Scheme is not ("ipp" or "ipps" or "http" or "https"))
        {
            throw new InvalidOperationException($"'{printerUri}' is not a valid ipp, ipps, http or https printer URI.");
        }

        return printerUri;
    }
}
