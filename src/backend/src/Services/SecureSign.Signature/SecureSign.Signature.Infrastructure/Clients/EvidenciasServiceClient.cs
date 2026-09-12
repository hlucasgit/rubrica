using System.Net.Http.Json;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Infrastructure.Clients;

public sealed class EvidenciasServiceClient(HttpClient http) : IEvidenciasServiceClient
{
    public async Task RegistrarAsync(Guid documentoId, string tipoEvidencia, DatosContextualesRemoto datos, Guid? firmaId = null, CancellationToken ct = default)
    {
        var respuesta = await http.PostAsJsonAsync("/api/evidencias", new
        {
            documentoId,
            tipoEvidencia,
            datos = new { ipOrigen = datos.IpOrigen, userAgent = datos.UserAgent, dispositivo = datos.Dispositivo },
            firmaId
        }, ct);

        respuesta.EnsureSuccessStatusCode();
    }
}
