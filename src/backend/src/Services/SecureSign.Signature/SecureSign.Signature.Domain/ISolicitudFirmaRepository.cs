namespace SecureSign.Signature.Domain;

public interface ISolicitudFirmaRepository
{
    Task AgregarAsync(SolicitudFirma solicitud, CancellationToken ct = default);
    Task<SolicitudFirma?> ObtenerPorIdAsync(Guid tenantId, Guid solicitudId, CancellationToken ct = default);
    Task<SolicitudFirma?> ObtenerPorCodigoVerificacionAsync(string codigo, CancellationToken ct = default);
    Task ActualizarAsync(SolicitudFirma solicitud, CancellationToken ct = default);
}
