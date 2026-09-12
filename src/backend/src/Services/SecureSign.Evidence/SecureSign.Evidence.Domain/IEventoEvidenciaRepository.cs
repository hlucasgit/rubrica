namespace SecureSign.Evidence.Domain;

public interface IEventoEvidenciaRepository
{
    Task AgregarAsync(EventoEvidencia evento, CancellationToken ct = default);

    /// <summary>Último evento registrado para el tenant, para encadenar el siguiente.</summary>
    Task<EventoEvidencia?> ObtenerUltimoAsync(Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<EventoEvidencia>> ObtenerCadenaCompletaAsync(Guid tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<EventoEvidencia>> ObtenerPorDocumentoAsync(Guid tenantId, Guid documentoId, CancellationToken ct = default);
}
