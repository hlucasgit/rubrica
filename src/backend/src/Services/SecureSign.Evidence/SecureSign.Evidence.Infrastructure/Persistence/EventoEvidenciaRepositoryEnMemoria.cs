using System.Collections.Concurrent;
using SecureSign.Evidence.Domain;

namespace SecureSign.Evidence.Infrastructure.Persistence;

/// <summary>
/// Implementación en memoria para desarrollo/pruebas. La implementación de
/// producción persiste en la tabla particionada Evidencias (ver
/// src/backend/database/schema.sql) con inserción exclusivamente append-only
/// forzada por permisos de rol de base de datos (ver docs/07-seguridad).
/// </summary>
public sealed class EventoEvidenciaRepositoryEnMemoria : IEventoEvidenciaRepository
{
    private readonly ConcurrentDictionary<Guid, List<EventoEvidencia>> _porTenant = new();
    private readonly object _lock = new();

    public Task AgregarAsync(EventoEvidencia evento, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var lista = _porTenant.GetOrAdd(evento.TenantId, _ => new List<EventoEvidencia>());
            lista.Add(evento);
        }
        return Task.CompletedTask;
    }

    public Task<EventoEvidencia?> ObtenerUltimoAsync(Guid tenantId, CancellationToken ct = default)
    {
        _porTenant.TryGetValue(tenantId, out var lista);
        return Task.FromResult(lista?.LastOrDefault());
    }

    public Task<IReadOnlyList<EventoEvidencia>> ObtenerCadenaCompletaAsync(Guid tenantId, CancellationToken ct = default)
    {
        _porTenant.TryGetValue(tenantId, out var lista);
        return Task.FromResult<IReadOnlyList<EventoEvidencia>>(lista?.ToList() ?? new List<EventoEvidencia>());
    }

    public Task<IReadOnlyList<EventoEvidencia>> ObtenerPorDocumentoAsync(Guid tenantId, Guid documentoId, CancellationToken ct = default)
    {
        _porTenant.TryGetValue(tenantId, out var lista);
        var filtrada = lista?.Where(e => e.DocumentoId == documentoId).ToList() ?? new List<EventoEvidencia>();
        return Task.FromResult<IReadOnlyList<EventoEvidencia>>(filtrada);
    }
}
