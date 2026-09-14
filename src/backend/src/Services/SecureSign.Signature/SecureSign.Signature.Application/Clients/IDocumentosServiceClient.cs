namespace SecureSign.Signature.Application.Clients;

public sealed record DocumentoRemoto(Guid IdDocumento, string HashSha256, string Estado, string TipoContenido);

public sealed record ContenidoDocumentoRemoto(byte[] Contenido, string TipoContenido, string NombreArchivo);

/// <summary>
/// Contrato hacia el Servicio Documental, consumido por la orquestación del
/// Servicio de Firma. La implementación HTTP vive en la capa de Infraestructura
/// (ver docs/01-arquitectura/arquitectura-general.md: comunicación síncrona
/// entre servicios para operaciones que requieren respuesta inmediata).
/// </summary>
public interface IDocumentosServiceClient
{
    Task<DocumentoRemoto?> ObtenerAsync(Guid documentoId, CancellationToken ct = default);
    Task MarcarFirmadoAsync(Guid documentoId, CancellationToken ct = default);

    /// <summary>Bytes originales del documento — ver DocumentosController GET /contenido.</summary>
    Task<ContenidoDocumentoRemoto?> ObtenerContenidoAsync(Guid documentoId, CancellationToken ct = default);

    /// <summary>
    /// Guarda el PDF con la firma PAdES real incrustada (ver SecureSign.Pades
    /// y FirmarLocalHandler) — a partir de entonces GET /api/documentos/{id}/firmado
    /// sirve este contenido en vez del original tal cual.
    /// </summary>
    Task GuardarDocumentoFirmadoPadesAsync(Guid documentoId, byte[] contenido, CancellationToken ct = default);
}
