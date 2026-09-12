namespace SecureSign.Signature.Application.Clients;

public sealed record DocumentoRemoto(Guid IdDocumento, string HashSha256, string Estado);

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
}
