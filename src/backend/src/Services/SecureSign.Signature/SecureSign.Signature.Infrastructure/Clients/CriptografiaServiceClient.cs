using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Infrastructure.Clients;

/// <summary>
/// SIMPLIFICACIÓN: cachea en memoria del proceso la referencia de llave por
/// usuario, ya que el Servicio de Certificados (fuente real de esa
/// referencia, ligada a un certificado emitido) no está implementado en
/// este scaffold. Se pierde al reiniciar el servicio — ver src/backend/README.md.
///
/// El algoritmo a usar es configurable (<c>ServiciosInternos:CriptografiaAlgoritmo</c>,
/// default "EcdsaSha256") porque el proveedor de software firma en ECDSA
/// mientras que el proveedor PKCS#11 (DNIe real) firma en RSA — deben
/// coincidir con lo que exponga el Servicio Criptográfico configurado.
/// </summary>
public sealed class CriptografiaServiceClient(HttpClient http, IConfiguration configuracion) : ICriptografiaServiceClient
{
    private static readonly ConcurrentDictionary<Guid, string> _referenciasPorUsuario = new();

    private string Algoritmo => configuracion["ServiciosInternos:CriptografiaAlgoritmo"] ?? "EcdsaSha256";

    private sealed record GenerarLlaveResponse(string ReferenciaLlave);
    private sealed record FirmarResponse(string FirmaBase64, string Algoritmo, string ReferenciaLlaveUsada);
    private sealed record OperacionFirmarLoteBody(string ReferenciaLlave, string HashDocumentoHex, string Algoritmo);

    public async Task<string> ObtenerOGenerarLlaveAsync(Guid usuarioId, CancellationToken ct = default)
    {
        if (_referenciasPorUsuario.TryGetValue(usuarioId, out var existente))
            return existente;

        var respuesta = await http.PostAsJsonAsync("/api/interno/criptografia/generar-llave",
            new { usuarioId, algoritmo = Algoritmo }, ct);
        respuesta.EnsureSuccessStatusCode();

        var body = await respuesta.Content.ReadFromJsonAsync<GenerarLlaveResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta inválida al generar llave.");

        _referenciasPorUsuario[usuarioId] = body.ReferenciaLlave;
        return body.ReferenciaLlave;
    }

    public async Task<FirmaCriptografica> FirmarAsync(string referenciaLlave, string hashDocumentoHex, string? pin = null, CancellationToken ct = default)
    {
        var respuesta = await http.PostAsJsonAsync("/api/interno/criptografia/firmar",
            new { referenciaLlave, hashDocumentoHex, algoritmo = Algoritmo, pin }, ct);
        respuesta.EnsureSuccessStatusCode();

        var body = await respuesta.Content.ReadFromJsonAsync<FirmarResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta inválida al firmar.");

        return new FirmaCriptografica(body.FirmaBase64, body.Algoritmo, body.ReferenciaLlaveUsada);
    }

    public async Task<IReadOnlyList<FirmaCriptografica>> FirmarLoteAsync(
        IReadOnlyList<(string ReferenciaLlave, string HashDocumentoHex)> operaciones, string? pin = null, CancellationToken ct = default)
    {
        var cuerpo = new
        {
            operaciones = operaciones.Select(o => new OperacionFirmarLoteBody(o.ReferenciaLlave, o.HashDocumentoHex, Algoritmo)),
            pin
        };

        var respuesta = await http.PostAsJsonAsync("/api/interno/criptografia/firmar-lote", cuerpo, ct);
        respuesta.EnsureSuccessStatusCode();

        var body = await respuesta.Content.ReadFromJsonAsync<List<FirmarResponse>>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta inválida al firmar en lote.");

        return body.Select(r => new FirmaCriptografica(r.FirmaBase64, r.Algoritmo, r.ReferenciaLlaveUsada)).ToList();
    }
}
