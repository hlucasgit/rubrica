using System.Net;
using System.Net.Http.Json;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Infrastructure.Clients;

public sealed class DocumentosServiceClient(HttpClient http) : IDocumentosServiceClient
{
    private sealed record DocumentoDetalleResponse(Guid IdDocumento, string NombreArchivo, string TipoContenido, string HashSha256, string Estado, string UrlAlmacenamiento);

    public async Task<DocumentoRemoto?> ObtenerAsync(Guid documentoId, CancellationToken ct = default)
    {
        var respuesta = await http.GetAsync($"/api/documentos/{documentoId}", ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound) return null;
        respuesta.EnsureSuccessStatusCode();

        var detalle = await respuesta.Content.ReadFromJsonAsync<DocumentoDetalleResponse>(cancellationToken: ct);
        return detalle is null ? null : new DocumentoRemoto(detalle.IdDocumento, detalle.HashSha256, detalle.Estado);
    }

    public async Task MarcarFirmadoAsync(Guid documentoId, CancellationToken ct = default)
    {
        var respuesta = await http.PostAsync($"/api/documentos/{documentoId}/marcar-firmado", content: null, ct);
        respuesta.EnsureSuccessStatusCode();
    }

    public async Task<ContenidoDocumentoRemoto?> ObtenerContenidoAsync(Guid documentoId, CancellationToken ct = default)
    {
        var respuesta = await http.GetAsync($"/api/documentos/{documentoId}/contenido", ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound) return null;
        respuesta.EnsureSuccessStatusCode();

        var contenido = await respuesta.Content.ReadAsByteArrayAsync(ct);
        var tipoContenido = respuesta.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var nombreArchivo = respuesta.Content.Headers.ContentDisposition?.FileNameStar
            ?? respuesta.Content.Headers.ContentDisposition?.FileName
            ?? documentoId.ToString();

        return new ContenidoDocumentoRemoto(contenido, tipoContenido, nombreArchivo);
    }

    public async Task GuardarDocumentoFirmadoPadesAsync(Guid documentoId, byte[] contenido, CancellationToken ct = default)
    {
        var cuerpo = new { contenidoBase64 = Convert.ToBase64String(contenido) };
        var respuesta = await http.PutAsJsonAsync($"/api/documentos/{documentoId}/firmado-pades", cuerpo, ct);
        respuesta.EnsureSuccessStatusCode();
    }
}
