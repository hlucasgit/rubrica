using MediatR;
using SecureSign.Domain.Primitives;
using SecureSign.Signature.Application.Clients;
using SecureSign.Signature.Application.FirmarDocumento;

namespace SecureSign.Signature.Application.FirmarLocal;

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
    string? DocumentoPadesBase64 = null) : IRequest<Result<FirmarDocumentoResponse>>;
