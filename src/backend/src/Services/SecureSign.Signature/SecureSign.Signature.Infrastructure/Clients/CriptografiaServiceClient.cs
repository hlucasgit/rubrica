using System.Collections.Concurrent;
using System.Net.Http.Json;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Infrastructure.Clients;

/// <summary>
/// SIMPLIFICACIÓN: cachea en memoria del proceso la referencia de llave por
/// usuario, ya que el Servicio de Certificados (fuente real de esa
/// referencia, ligada a un certificado emitido) no está implementado en
/// este scaffold. Se pierde al reiniciar el servicio — ver src/backend/README.md.
/// </summary>
public sealed class CriptografiaServiceClient(HttpClient http) : ICriptografiaServiceClient
{
    private static readonly ConcurrentDictionary<Guid, string> _referenciasPorUsuario = new();

    private sealed record GenerarLlaveResponse(string ReferenciaLlave);
    private sealed record FirmarResponse(string FirmaBase64, string Algoritmo, string ReferenciaLlaveUsada);

    public async Task<string> ObtenerOGenerarLlaveAsync(Guid usuarioId, CancellationToken ct = default)
    {
        if (_referenciasPorUsuario.TryGetValue(usuarioId, out var existente))
            return existente;

        var respuesta = await http.PostAsJsonAsync("/api/interno/criptografia/generar-llave",
            new { usuarioId, algoritmo = "EcdsaSha256" }, ct);
        respuesta.EnsureSuccessStatusCode();

        var body = await respuesta.Content.ReadFromJsonAsync<GenerarLlaveResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta inválida al generar llave.");

        _referenciasPorUsuario[usuarioId] = body.ReferenciaLlave;
        return body.ReferenciaLlave;
    }

    public async Task<FirmaCriptografica> FirmarAsync(string referenciaLlave, string hashDocumentoHex, CancellationToken ct = default)
    {
        var respuesta = await http.PostAsJsonAsync("/api/interno/criptografia/firmar",
            new { referenciaLlave, hashDocumentoHex, algoritmo = "EcdsaSha256" }, ct);
        respuesta.EnsureSuccessStatusCode();

        var body = await respuesta.Content.ReadFromJsonAsync<FirmarResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta inválida al firmar.");

        return new FirmaCriptografica(body.FirmaBase64, body.Algoritmo, body.ReferenciaLlaveUsada);
    }
}
