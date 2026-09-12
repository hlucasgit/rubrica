using System.Collections.Concurrent;
using SecureSign.Signature.Domain;

namespace SecureSign.Signature.Infrastructure.Persistence;

/// <summary>
/// Implementación en memoria para desarrollo/pruebas. La implementación de
/// producción persiste en PostgreSQL (tablas SolicitudesFirma / FlujosFirma,
/// ver src/backend/database/schema.sql) y no está incluida en este scaffold.
/// </summary>
public sealed class SolicitudFirmaRepositoryEnMemoria : ISolicitudFirmaRepository
{
    private readonly ConcurrentDictionary<Guid, SolicitudFirma> _solicitudes = new();

    public Task AgregarAsync(SolicitudFirma solicitud, CancellationToken ct = default)
    {
        _solicitudes[solicitud.Id] = solicitud;
        return Task.CompletedTask;
    }

    public Task<SolicitudFirma?> ObtenerPorIdAsync(Guid tenantId, Guid solicitudId, CancellationToken ct = default)
    {
        _solicitudes.TryGetValue(solicitudId, out var solicitud);
        return Task.FromResult(solicitud?.TenantId == tenantId ? solicitud : null);
    }

    public Task<SolicitudFirma?> ObtenerPorCodigoVerificacionAsync(string codigo, CancellationToken ct = default)
    {
        var solicitud = _solicitudes.Values.FirstOrDefault(s => s.CodigoVerificacionPublico == codigo);
        return Task.FromResult(solicitud);
    }

    public Task ActualizarAsync(SolicitudFirma solicitud, CancellationToken ct = default)
    {
        _solicitudes[solicitud.Id] = solicitud;
        return Task.CompletedTask;
    }
}
