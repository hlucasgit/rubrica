using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Signature.Application.EstablecerPosicionFirma;

public sealed record EstablecerPosicionFirmaCommand(
    Guid TenantId,
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    int NumeroPagina,
    double X,
    double Y,
    double Ancho,
    double Alto) : IRequest<Result>;
