namespace SecureSign.Signature.Application.Clients;

/// <summary>
/// Contrato hacia el Servicio de Auditoría técnica — distinto de
/// IEvidenciasServiceClient (evidencia de negocio, con cadena de hashes,
/// siempre ligada a un documento). Ver SecureSign.Audit.Domain.TipoEventoAuditoria
/// y RUNBOOK.md 12.16.
/// </summary>
public interface IAuditoriaServiceClient
{
    Task RegistrarAsync(string tipoEvento, string detalle, Guid? tenantId, CancellationToken ct = default);
}
