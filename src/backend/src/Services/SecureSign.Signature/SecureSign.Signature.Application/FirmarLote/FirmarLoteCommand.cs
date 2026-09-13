using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;

namespace SecureSign.Signature.Application.FirmarLote;

public sealed record OperacionFirmaLoteDto(Guid SolicitudFirmaId, Guid FlujoFirmaId);

/// <param name="PinFirmante">
/// Ver FirmarDocumentoCommand.PinFirmante — aquí se usa UNA sola vez para
/// todas las <paramref name="Operaciones"/>, que por eso deben pertenecer
/// todas al mismo firmante (ver FirmarLoteHandler).
/// </param>
public sealed record FirmarLoteCommand(
    Guid TenantId,
    IReadOnlyList<OperacionFirmaLoteDto> Operaciones,
    DatosContextualesRemoto DatosContextuales,
    string? PinFirmante) : IRequest<Result<FirmarLoteResponse>>;

public sealed record ResultadoFirmaLoteItem(
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    bool Exitoso,
    string? Error,
    string? EstadoSolicitud,
    string? AlgoritmoFirma,
    string? FirmaBase64);

public sealed record FirmarLoteResponse(IReadOnlyList<ResultadoFirmaLoteItem> Resultados);
