using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Application.FirmarDocumento;

/// <param name="PinFirmante">
/// PIN de la tarjeta/token del firmante. Solo necesario cuando el Servicio
/// Criptográfico está configurado con el proveedor PKCS#11 (tarjeta real,
/// p. ej. DNIe); el proveedor de software lo ignora. Nunca se persiste ni
/// se registra en evidencia — viaja solo dentro de esta operación.
/// </param>
public sealed record FirmarDocumentoCommand(
    Guid TenantId,
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    DatosContextualesRemoto DatosContextuales,
    string? PinFirmante = null) : IRequest<Result<FirmarDocumentoResponse>>;

public sealed record FirmarDocumentoResponse(string EstadoSolicitud, string AlgoritmoFirma, string FirmaBase64);
