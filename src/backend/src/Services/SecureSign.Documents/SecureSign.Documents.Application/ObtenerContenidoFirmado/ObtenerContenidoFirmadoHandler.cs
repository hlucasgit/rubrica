using MediatR;
using SecureSign.Documents.Application.RegistrarDocumento;
using SecureSign.Documents.Domain;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.ObtenerContenidoFirmado;

/// <summary>
/// GET /api/documentos/{id}/firmado — devuelve <see cref="Documento.ContenidoFirmadoPades"/>
/// cuando existe (el PDF con la firma PAdES real incrustada por el Firmador
/// Local, ver SecureSign.Pades) y, si no, el contenido original tal cual
/// (limitación deliberada de la firma "desacoplada" clásica — ver RUNBOOK.md 12.8).
/// </summary>
public sealed class ObtenerContenidoFirmadoHandler(IDocumentoRepository repositorio, IAlmacenamientoDocumental almacenamiento)
    : IRequestHandler<ObtenerContenidoFirmadoQuery, Result<ContenidoFirmadoResponse>>
{
    public async Task<Result<ContenidoFirmadoResponse>> Handle(ObtenerContenidoFirmadoQuery request, CancellationToken ct)
    {
        var documento = await repositorio.ObtenerPorIdAsync(request.TenantId, request.DocumentoId, ct);
        if (documento is null)
            return Result.Fallido<ContenidoFirmadoResponse>("Documento no encontrado.");

        if (documento.Estado != EstadoDocumento.Firmado)
            return Result.Fallido<ContenidoFirmadoResponse>($"El documento está en estado {documento.Estado}, no Firmado.");

        byte[] contenido = documento.ContenidoFirmadoPades
            ?? await almacenamiento.LeerAsync(documento.UrlAlmacenamiento, ct);

        return Result.Exitoso(new ContenidoFirmadoResponse(
            contenido, documento.TipoContenido, documento.NombreArchivo, documento.Hash.ValorHex));
    }
}
