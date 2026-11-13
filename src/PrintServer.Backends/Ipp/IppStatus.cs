using System.Globalization;

namespace PrintServer.Backends.Ipp;

internal static class IppStatus
{
    public const ushort ClientErrorNotFound = 0x0406;

    public static string Describe(ushort code)
    {
        var name = code switch
        {
            0x0400 => "client-error-bad-request",
            0x0401 => "client-error-forbidden",
            0x0402 => "client-error-not-authenticated",
            0x0403 => "client-error-not-authorized",
            0x0404 => "client-error-not-possible",
            0x0405 => "client-error-timeout",
            0x0406 => "client-error-not-found",
            0x0407 => "client-error-gone",
            0x0408 => "client-error-request-entity-too-large",
            0x040A => "client-error-document-format-not-supported",
            0x040B => "client-error-attributes-or-values-not-supported",
            0x040C => "client-error-uri-scheme-not-supported",
            0x0500 => "server-error-internal-error",
            0x0501 => "server-error-operation-not-supported",
            0x0502 => "server-error-service-unavailable",
            0x0504 => "server-error-device-error",
            0x0505 => "server-error-temporary-error",
            0x0506 => "server-error-not-accepting-jobs",
            0x0507 => "server-error-busy",
            0x0508 => "server-error-job-canceled",
            _ => null,
        };

        var hex = string.Create(CultureInfo.InvariantCulture, $"0x{code:X4}");
        return name is null ? hex : $"{name} ({hex})";
    }
}
