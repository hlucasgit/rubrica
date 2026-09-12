using MediatR;
using SecureSign.Documents.Domain;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.ObtenerDocumento;

public sealed class ObtenerDocumentoHandler(IDocumentoRepository repositorio)
    : IRequestHandler<ObtenerDocumentoQuery, Result<DocumentoDetalleResponse>>
{
    public async Task<Result<DocumentoDetalleResponse>> Handle(ObtenerDocumentoQuery request, CancellationToken ct)
    {
        var documento = await repositorio.ObtenerPorIdAsync(request.TenantId, request.DocumentoId, ct);
        if (documento is null)
            return Result.Fallido<DocumentoDetalleResponse>("Documento no encontrado.");

        return Result.Exitoso(new DocumentoDetalleResponse(
            documento.Id, documento.NombreArchivo, documento.TipoContenido,
            documento.Hash.ValorHex, documento.Estado.ToString(), documento.UrlAlmacenamiento));
    }
}
