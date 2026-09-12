using Microsoft.EntityFrameworkCore;
using SecureSign.Evidence.Domain;

namespace SecureSign.Evidence.Infrastructure.Persistence;

public sealed class EventoEvidenciaRepositoryEfCore(EvidenceDbContext db) : IEventoEvidenciaRepository
{
    public async Task AgregarAsync(EventoEvidencia evento, CancellationToken ct = default)
    {
        db.Eventos.Add(evento);
        await db.SaveChangesAsync(ct);
    }

    public Task<EventoEvidencia?> ObtenerUltimoAsync(Guid tenantId, CancellationToken ct = default)
        => db.Eventos
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.RegistradoEn)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<EventoEvidencia>> ObtenerCadenaCompletaAsync(Guid tenantId, CancellationToken ct = default)
        => await db.Eventos
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.RegistradoEn)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EventoEvidencia>> ObtenerPorDocumentoAsync(Guid tenantId, Guid documentoId, CancellationToken ct = default)
        => await db.Eventos
            .Where(e => e.TenantId == tenantId && e.DocumentoId == documentoId)
            .OrderBy(e => e.RegistradoEn)
            .ToListAsync(ct);
}
