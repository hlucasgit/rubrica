using Microsoft.Extensions.Options;
using SecureSign.Crypto.Domain;
using SecureSign.Tsa;

namespace SecureSign.Crypto.Infrastructure;

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
