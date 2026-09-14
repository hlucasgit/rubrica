using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.ObtenerContenidoFirmado;

public sealed record ObtenerContenidoFirmadoQuery(Guid TenantId, Guid DocumentoId) : IRequest<Result<ContenidoFirmadoResponse>>;

public sealed record ContenidoFirmadoResponse(byte[] Contenido, string TipoContenido, string NombreArchivo, string HashSha256);
