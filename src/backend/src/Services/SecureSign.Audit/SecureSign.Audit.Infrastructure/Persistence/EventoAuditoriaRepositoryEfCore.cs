using Microsoft.EntityFrameworkCore;
using SecureSign.Audit.Domain;

namespace SecureSign.Audit.Infrastructure.Persistence;

public sealed class EventoAuditoriaRepositoryEfCore(AuditDbContext db) : IEventoAuditoriaRepository
{
    public async Task AgregarAsync(EventoAuditoria evento, CancellationToken ct = default)
    {
        db.Eventos.Add(evento);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EventoAuditoria>> ConsultarAsync(
        Guid? tenantId, TipoEventoAuditoria? tipoEvento, int limite, CancellationToken ct = default)
    {
        var consulta = db.Eventos.AsQueryable();
        if (tenantId is { } t) consulta = consulta.Where(e => e.TenantId == t);
        if (tipoEvento is { } tipo) consulta = consulta.Where(e => e.TipoEvento == tipo);

        return await consulta
            .OrderByDescending(e => e.OcurridoEn)
            .Take(limite)
            .ToListAsync(ct);
    }
}
