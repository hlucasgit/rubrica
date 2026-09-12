using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Signature.Application.ConsultarEstado;

// TokenAccesoUnico deliberadamente NO se expone aquí: es el secreto que
// autoriza a firmar (ver FlujoFirma.TokenAccesoUnico). Este DTO respalda
// tanto la consulta autenticada (GET /api/firmas/{id}/estado) como la
// pública (GET /api/validacion/{codigo}) — nunca debe filtrar ese valor.
public sealed record FlujoEstadoDto(Guid FlujoFirmaId, Guid FirmanteUsuarioId, int Orden, string Estado);
public sealed record SolicitudEstadoDto(Guid SolicitudFirmaId, Guid DocumentoId, string Estado, string CodigoVerificacionPublico, IReadOnlyList<FlujoEstadoDto> Firmantes);

public sealed record ConsultarEstadoQuery(Guid TenantId, Guid SolicitudFirmaId) : IRequest<Result<SolicitudEstadoDto>>;

public sealed record ConsultarPorCodigoQuery(string Codigo) : IRequest<Result<SolicitudEstadoDto>>;
