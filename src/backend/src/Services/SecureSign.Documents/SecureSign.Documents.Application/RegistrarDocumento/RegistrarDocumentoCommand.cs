using MediatR;
using SecureSign.Documents.Application.Clients;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.RegistrarDocumento;

public sealed record RegistrarDocumentoCommand(
    Guid TenantId,
    string NombreArchivo,
    string TipoContenido,
    byte[] Contenido,
    Guid CreadoPor,
    string? CodigoExterno,
    DatosContextualesRemoto DatosContextuales) : IRequest<Result<RegistrarDocumentoResponse>>;

public sealed record RegistrarDocumentoResponse(Guid IdDocumento, string HashDocumento, string Estado);
