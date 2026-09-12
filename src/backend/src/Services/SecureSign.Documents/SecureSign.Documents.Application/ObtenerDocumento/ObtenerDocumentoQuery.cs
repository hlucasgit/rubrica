using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.ObtenerDocumento;

public sealed record ObtenerDocumentoQuery(Guid TenantId, Guid DocumentoId) : IRequest<Result<DocumentoDetalleResponse>>;

public sealed record DocumentoDetalleResponse(
    Guid IdDocumento,
    string NombreArchivo,
    string TipoContenido,
    string HashSha256,
    string Estado,
    string UrlAlmacenamiento);
