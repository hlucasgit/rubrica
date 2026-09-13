using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Signature.Application.ListarPendientes;

public sealed record ListarPendientesQuery(Guid TenantId, Guid FirmanteUsuarioId) : IRequest<Result<IReadOnlyList<PendienteDto>>>;

public sealed record PendienteDto(
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    Guid DocumentoId,
    string TipoFirma,
    string EstadoFlujo,
    string EstadoSolicitud,
    DateTimeOffset? NotificadoEn);
