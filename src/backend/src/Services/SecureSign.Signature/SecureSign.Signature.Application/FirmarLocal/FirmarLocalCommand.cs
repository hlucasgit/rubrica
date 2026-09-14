using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.FirmarDocumento;

namespace SecureSign.Signature.Application.FirmarLocal;

/// <summary>
/// Claims del ticket de firma de un solo uso (ver
/// SecureSign.Shared.Auth.EmisorTicketFirmaLocal, RUNBOOK.md 12.13) cuando
/// la petición se autenticó con uno — el controlador los extrae de
/// <c>User.Claims</c> (la Aplicación no depende de Shared.Auth) y el
/// handler los liga a la operación exacta que se está completando: rechaza
/// si el ticket era para otra solicitud, otro flujo, u otro documento, o si
/// el documento cambió desde que se emitió (informe de preauditoría
/// INDECOPI/IOFE, sección 12: "ticket repetido"/"documento distinto al hash
/// autorizado" deben quedar bloqueados).
/// </summary>
public sealed record ClaimsTicketFirmaLocal(Guid SolicitudFirmaId, Guid FlujoFirmaId, Guid DocumentoId, string DocumentoHashSha256Hex);

/// <summary>
/// Completa una firma calculada por el Firmador Local (ver RUNBOOK.md
/// sección 12) — la contraparte de FirmarDocumentoCommand para cuando el
/// hash se firmó FUERA de este sistema, con un certificado propio del
/// firmante (DNIe u otro token real) en su propia máquina.
/// </summary>
public sealed record FirmarLocalCommand(
    Guid TenantId,
    Guid SolicitudFirmaId,
    Guid FlujoFirmaId,
    string FirmaBase64,
    string CertificadoBase64,
    string Algoritmo,
    DatosContextualesRemoto DatosContextuales,
    string? DocumentoPadesBase64 = null,
    ClaimsTicketFirmaLocal? Ticket = null) : IRequest<Result<FirmarDocumentoResponse>>;
