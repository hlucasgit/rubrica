namespace SecureSign.Audit.Domain;

public interface IEventoAuditoriaRepository
{
    Task AgregarAsync(EventoAuditoria evento, CancellationToken ct = default);

    /// <summary>Más recientes primero — para un tenant (o todos, si tenantId es null), opcionalmente filtrados por tipo.</summary>
    Task<IReadOnlyList<EventoAuditoria>> ConsultarAsync(
        Guid? tenantId, TipoEventoAuditoria? tipoEvento, int limite, CancellationToken ct = default);
}
