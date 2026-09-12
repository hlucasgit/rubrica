using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Application.RechazarFirma;

public sealed record RechazarFirmaCommand(
    Guid TenantId,
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    string Motivo,
    DatosContextualesRemoto DatosContextuales) : IRequest<Result>;
