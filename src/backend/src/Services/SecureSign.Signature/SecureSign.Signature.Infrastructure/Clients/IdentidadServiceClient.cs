using System.Net.Http.Json;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Infrastructure.Clients;

public sealed class IdentidadServiceClient(HttpClient http) : IIdentidadServiceClient
{
    private sealed record IndiceConfianzaResponse(Guid UsuarioId, int Indice, bool TieneCertificadoVigente, bool EsCuentaInstitucional, bool TieneValidacionExitosa);

    public async Task<IndiceConfianzaRemoto> ObtenerIndiceConfianzaAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var respuesta = await http.GetAsync($"/api/interno/identidad/{usuarioId}/indice-confianza", ct);
        respuesta.EnsureSuccessStatusCode();

        var body = await respuesta.Content.ReadFromJsonAsync<IndiceConfianzaResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta inválida al consultar el índice de confianza.");

        return new IndiceConfianzaRemoto(body.Indice);
    }
}
