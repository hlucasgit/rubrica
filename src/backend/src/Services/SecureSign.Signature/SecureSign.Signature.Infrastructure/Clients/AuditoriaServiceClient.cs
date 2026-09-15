using System.Net.Http.Json;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Infrastructure.Clients;

public sealed class AuditoriaServiceClient(HttpClient http) : IAuditoriaServiceClient
{
    public async Task RegistrarAsync(string tipoEvento, string detalle, Guid? tenantId, CancellationToken ct = default)
    {
        var respuesta = await http.PostAsJsonAsync("/api/auditoria", new { tipoEvento, detalle, tenantId }, ct);
        respuesta.EnsureSuccessStatusCode();
    }
}
