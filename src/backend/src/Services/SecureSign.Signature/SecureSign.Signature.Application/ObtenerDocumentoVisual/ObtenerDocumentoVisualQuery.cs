using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Signature.Application.ObtenerDocumentoVisual;

public sealed record ObtenerDocumentoVisualQuery(Guid TenantId, Guid SolicitudFirmaId) : IRequest<Result<DocumentoVisualResponse>>;

public sealed record DocumentoVisualResponse(byte[] Contenido, string TipoContenido, string NombreArchivo);
