using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SecureSign.UnitTests.Pades;

/// <summary>
/// Certificados/llaves autofirmados de un solo uso para las pruebas de
/// SecureSign.Pades — deliberadamente sin ninguna cadena de confianza real
/// (esas pruebas viven en SecureSign.Trust y necesitan red real; ver
/// RUNBOOK.md 12.9). Aquí solo importa la mecánica criptográfica PAdES/CMS.
/// </summary>
internal static class CertificadoDePruebaHelper
{
    internal sealed record ParFirmante(RSA Rsa, X509Certificate2 Certificado) : IDisposable
    {
        public void Dispose()
        {
            Rsa.Dispose();
            Certificado.Dispose();
        }
    }

    internal static ParFirmante GenerarFirmante(string nombreComun)
    {
        var rsa = RSA.Create(2048);
        var solicitud = new CertificateRequest($"CN={nombreComun}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificadoConLlave = solicitud.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var certificadoPublico = new X509Certificate2(certificadoConLlave.Export(X509ContentType.Cert));
        return new ParFirmante(rsa, certificadoPublico);
    }
}
