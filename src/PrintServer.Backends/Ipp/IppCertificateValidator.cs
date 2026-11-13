using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PrintServer.Backends.Ipp;

internal static class IppCertificateValidator
{
    public static bool IsTrusted(X509Certificate? certificate, SslPolicyErrors errors, IReadOnlyCollection<string> trustedThumbprints)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (certificate is null || (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0 || trustedThumbprints.Count == 0)
        {
            return false;
        }

        var thumbprint = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
        return trustedThumbprints.Any(pin => string.Equals(Normalize(pin), thumbprint, StringComparison.Ordinal));
    }

    private static string Normalize(string thumbprint) =>
        new(thumbprint.Where(Uri.IsHexDigit).ToArray().Select(char.ToUpperInvariant).ToArray());
}
