using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Signature.Application.NotificarSolicitud;

/// <summary>Notifica a todos los firmantes actualmente elegibles (respeta orden secuencial).</summary>
public sealed record NotificarSolicitudCommand(Guid TenantId, Guid SolicitudFirmaId) : IRequest<Result>;
