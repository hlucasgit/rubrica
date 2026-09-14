using System.Security.Cryptography.X509Certificates;
using SecureSign.Signature.Application.Confianza;
using SecureSign.Trust;

namespace SecureSign.Signature.Infrastructure.Confianza;

/// <summary>Adaptador delgado sobre SecureSign.Trust.ValidadorCertificados — ver IValidadorConfianzaFirmante.</summary>
public sealed class ValidadorConfianzaFirmanteIofe(ValidadorCertificados validador) : IValidadorConfianzaFirmante
{
    public async Task<ResultadoConfianzaFirmante> ValidarAsync(byte[] certificadoDer, DateTimeOffset instante, CancellationToken ct = default)
    {
        using var certificado = new X509Certificate2(certificadoDer);
        var resultado = await validador.ValidarAsync(certificado, instante, ct);
        return new ResultadoConfianzaFirmante(resultado.EstadoFinal, resultado.Evidencia);
    }
}
