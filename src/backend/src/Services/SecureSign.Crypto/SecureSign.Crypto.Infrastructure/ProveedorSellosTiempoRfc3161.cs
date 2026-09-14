using Microsoft.Extensions.Options;
using SecureSign.Crypto.Domain;
using SecureSign.Tsa;

namespace SecureSign.Crypto.Infrastructure;

/// <summary>
/// URL de la TSA (Autoridad de Sellado de Tiempo) RFC 3161 a usar. Por
/// defecto, la TSA pública y gratuita de DigiCert (no requiere autenticación
/// ni contrato — es la misma que usan innumerables pipelines de firma de
/// código en producción para sellar binarios). Para IOFE real, sustituir
/// por la URL de una TSA acreditada peruana cuando exista — ver informe de
/// preauditoría INDECOPI/IOFE, hallazgo P1 ("TSA / RFC 3161").
/// </summary>
public sealed class OpcionesTsa
{
    public const string SeccionConfiguracion = "Tsa";
    public string UrlTsa { get; set; } = "http://timestamp.digicert.com";
}

/// <summary>
/// Implementación real de <see cref="ISellosTiempoProvider"/> — ver
/// RUNBOOK.md 12.14. Delegación delgada sobre SecureSign.Tsa.ClienteTsaRfc3161
/// (protocolo RFC 3161 puro, sin dependencias de ASP.NET Core) con la URL de
/// la TSA resuelta desde configuración.
/// </summary>
public sealed class ProveedorSellosTiempoRfc3161(ClienteTsaRfc3161 cliente, IOptions<OpcionesTsa> opciones) : ISellosTiempoProvider
{
    public async Task<SelloTiempo> SellarAsync(byte[] hash, CancellationToken ct = default)
    {
        var resultado = await cliente.SellarAsync(hash, opciones.Value.UrlTsa, ct);
        return new SelloTiempo(resultado.TokenDer, resultado.GenTime, resultado.AutoridadEmisora);
    }
}
