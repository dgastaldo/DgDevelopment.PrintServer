using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PrintServer.Backends.Ipp;

namespace PrintServer.UnitTests;

public sealed class IppCertificateValidatorTests : IDisposable
{
    private readonly X509Certificate2 _certificate;
    private readonly string _thumbprint;

    public IppCertificateValidatorTests()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=printer.local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        _thumbprint = Convert.ToHexString(SHA256.HashData(_certificate.RawData));
    }

    public void Dispose() => _certificate.Dispose();

    [Fact]
    public void ValidChainsAreAlwaysTrusted()
    {
        Assert.True(IppCertificateValidator.IsTrusted(_certificate, SslPolicyErrors.None, []));
    }

    [Fact]
    public void InvalidChainsAreRejectedWithoutPins()
    {
        Assert.False(IppCertificateValidator.IsTrusted(_certificate, SslPolicyErrors.RemoteCertificateChainErrors, []));
    }

    [Fact]
    public void InvalidChainsAreAcceptedWhenTheCertificateIsPinned()
    {
        Assert.True(IppCertificateValidator.IsTrusted(_certificate, SslPolicyErrors.RemoteCertificateChainErrors, [_thumbprint]));
    }

    [Fact]
    public void PinsMayUseLowerCaseAndSeparators()
    {
        var lower = Convert.ToHexStringLower(SHA256.HashData(_certificate.RawData));
        var formatted = string.Join(':', Enumerable.Range(0, lower.Length / 2).Select(i => lower.Substring(i * 2, 2)));

        Assert.True(IppCertificateValidator.IsTrusted(
            _certificate,
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch,
            [formatted]));
    }

    [Fact]
    public void AnotherCertificatesPinDoesNotMatch()
    {
        Assert.False(IppCertificateValidator.IsTrusted(
            _certificate,
            SslPolicyErrors.RemoteCertificateChainErrors,
            [new string('A', 64)]));
    }

    [Fact]
    public void MissingCertificatesAreRejected()
    {
        Assert.False(IppCertificateValidator.IsTrusted(null, SslPolicyErrors.RemoteCertificateNotAvailable, [_thumbprint]));
        Assert.False(IppCertificateValidator.IsTrusted(_certificate, SslPolicyErrors.RemoteCertificateNotAvailable, [_thumbprint]));
    }
}
