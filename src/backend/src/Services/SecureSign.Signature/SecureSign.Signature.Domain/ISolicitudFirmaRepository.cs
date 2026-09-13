namespace SecureSign.Signature.Domain;

public interface ISolicitudFirmaRepository
{
    Task AgregarAsync(SolicitudFirma solicitud, CancellationToken ct = default);
    Task<SolicitudFirma?> ObtenerPorIdAsync(Guid tenantId, Guid solicitudId, CancellationToken ct = default);
    Task<SolicitudFirma?> ObtenerPorCodigoVerificacionAsync(string codigo, CancellationToken ct = default);
    Task ActualizarAsync(SolicitudFirma solicitud, CancellationToken ct = default);

    /// <summary>
    /// Solicitudes con al menos un flujo de este firmante todavía pendiente
    /// de firmar (ni Firmado ni Rechazado) — respalda el modo masivo: el
    /// firmante ve su lista de documentos pendientes y elige cuáles firmar
    /// de una vez (ver FirmarLoteHandler).
    /// </summary>
    Task<IReadOnlyList<SolicitudFirma>> ListarPendientesPorFirmanteAsync(Guid tenantId, Guid firmanteUsuarioId, CancellationToken ct = default);
}
