using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Application.CrearSolicitudFirma;

public sealed record FirmanteDto(Guid UsuarioId, int Orden);

public sealed record CrearSolicitudFirmaCommand(
    Guid TenantId,
    Guid DocumentoId,
    TipoFirma TipoFirma,
    bool RequiereOrdenSecuencial,
    IReadOnlyList<FirmanteDto> Firmantes,
    Guid CreadoPor,
    DateTimeOffset? FechaLimite,
    Guid? ClienteIntegradorId) : IRequest<Result<CrearSolicitudFirmaResponse>>;

public sealed record CrearSolicitudFirmaResponse(Guid SolicitudFirmaId, string CodigoVerificacionPublico, string UrlFirma);
