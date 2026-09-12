using MediatR;
using SecureSign.Domain.Primitives;

namespace SecureSign.Documents.Application.MarcarDocumentoFirmado;

/// <summary>
/// Invocado internamente por el Servicio de Firma cuando una SolicitudFirma
/// transiciona a Firmado (ver SecureSign.Signature.Domain.SolicitudFirma.ConfirmarFirma).
/// </summary>
public sealed record MarcarDocumentoFirmadoCommand(Guid TenantId, Guid DocumentoId) : IRequest<Result>;
