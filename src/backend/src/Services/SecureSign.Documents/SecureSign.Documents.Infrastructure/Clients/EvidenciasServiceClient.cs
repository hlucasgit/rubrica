using System.Net.Http.Json;
using SecureSign.Documents.Application.Clients;

namespace SecureSign.Documents.Infrastructure.Clients;

public sealed class EvidenciasServiceClient(HttpClient http) : IEvidenciasServiceClient
{
    public async Task RegistrarAsync(Guid documentoId, string tipoEvidencia, DatosContextualesRemoto datos, CancellationToken ct = default)
    {
        var respuesta = await http.PostAsJsonAsync("/api/evidencias", new
        {
            documentoId,
            tipoEvidencia,
            datos = new { ipOrigen = datos.IpOrigen, userAgent = datos.UserAgent, dispositivo = datos.Dispositivo },
            firmaId = (Guid?)null
        }, ct);

        respuesta.EnsureSuccessStatusCode();
    }
}
