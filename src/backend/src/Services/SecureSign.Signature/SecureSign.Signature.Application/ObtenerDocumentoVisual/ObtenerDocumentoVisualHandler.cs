using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.Estampado;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.ObtenerDocumentoVisual;

/// <summary>
/// Sirve el documento con el sello visual de cada firmante que ya firmó, en
/// la posición que cada uno eligió (ver IEstampadorVisualDocumento para las
/// limitaciones frente a una firma PAdES real).
/// </summary>
public sealed class ObtenerDocumentoVisualHandler(
    ISolicitudFirmaRepository repositorio,
    IDocumentosServiceClient documentos,
    IEstampadorVisualDocumento estampador)
    : IRequestHandler<ObtenerDocumentoVisualQuery, Result<DocumentoVisualResponse>>
{
    public async Task<Result<DocumentoVisualResponse>> Handle(ObtenerDocumentoVisualQuery request, CancellationToken ct)
    {
        var solicitud = await repositorio.ObtenerPorIdAsync(request.TenantId, request.SolicitudFirmaId, ct);
        if (solicitud is null) return Result.Fallido<DocumentoVisualResponse>("Solicitud de firma no encontrada.");

        var original = await documentos.ObtenerContenidoAsync(solicitud.DocumentoId, ct);
        if (original is null) return Result.Fallido<DocumentoVisualResponse>("El documento asociado ya no existe.");

        var marcas = solicitud.Flujos
            .Where(f => f.Estado == EstadoFlujoFirma.Firmado && f.Posicion is not null)
            .Select(f => new MarcaVisualFirma(
                f.Posicion!.NumeroPagina, f.Posicion.X, f.Posicion.Y, f.Posicion.Ancho, f.Posicion.Alto,
                $"Usuario {f.FirmanteUsuarioId}", f.FirmadoEn ?? DateTimeOffset.UtcNow, solicitud.CodigoVerificacionPublico))
            .ToList();

        var estampado = estampador.Estampar(original.Contenido, original.TipoContenido, marcas);

        return Result.Exitoso(new DocumentoVisualResponse(
            estampado ?? original.Contenido, original.TipoContenido, original.NombreArchivo));
    }
}
