using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Application.FirmarDocumento;

public sealed record FirmarDocumentoCommand(
    Guid TenantId,
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    DatosContextualesRemoto DatosContextuales) : IRequest<Result<FirmarDocumentoResponse>>;

public sealed record FirmarDocumentoResponse(string EstadoSolicitud, string AlgoritmoFirma, string FirmaBase64);
